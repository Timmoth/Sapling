using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace Sapling.Engine.MoveGen;

public static class AttackTables
{
    private static readonly ulong[] RookAttackMasks = new ulong[64];
    public static readonly MagicBitBoard[] RookMagics = new MagicBitBoard[64];
    private static readonly ulong[] BishopAttackMasks = new ulong[64];
    public static readonly MagicBitBoard[] BishopMagics = new MagicBitBoard[64];

    private static readonly int[] WhitePawnOffsets = { 7, 9 };
    private static readonly int[] BlackPawnOffsets = { -7, -9 };
    private static readonly int[] KnightOffsets = { 17, 15, 10, 6, -6, -10, -15, -17 };
    private static readonly int[] KingOffsets = { 1, -1, 8, -8, 9, -9, 7, -7 };
    public static readonly ulong[] KnightAttackTable = new ulong[64];
    public static readonly ulong[] KingAttackTable = new ulong[64];
    public static readonly ulong[] WhitePawnAttackTable = new ulong[64];
    public static readonly ulong[] BlackPawnAttackTable = new ulong[64];

    private static readonly ulong[] PextAttacks = new ulong[5248 + 102400];
    private static readonly ulong[] BishopPextOffset = new ulong[64];
    private static readonly ulong[] RookPextOffset = new ulong[64];

    static AttackTables()
    {
        var rand = Random.Shared;
        for (var i = 0; i < 64; i++)
        {
            RookAttackMasks[i] = RookAttackMask(i);
            BishopAttackMasks[i] = BishopAttackMask(i);
        }

        for (var i = 0; i < 64; i++)
        {
            RookMagics[i] = GetRookMagicNumbers(i, rand);
            BishopMagics[i] = GetBishopMagicNumbers(i, rand);
        }

        // Initialize the knight and king attack tables
        for (var square = 0; square < 64; square++)
        {
            var knightAttacks = 0UL;
            var kingAttacks = 0UL;
            var whitePawnAttacks = 0UL;
            var blackPawnAttacks = 0UL;
            var rank = square.GetRankIndex();
            var file = square.GetFileIndex();

            foreach (var offset in WhitePawnOffsets)
            {
                var targetSquare = square + offset;
                var targetRank = targetSquare.GetRankIndex();
                var targetFile = targetSquare.GetFileIndex();

                if (targetSquare is >= 0 and < 64 && Math.Abs(rank - targetRank) <= 2 &&
                    Math.Abs(file - targetFile) <= 2)
                {
                    whitePawnAttacks |= 1UL << targetSquare;
                }
            }

            foreach (var offset in BlackPawnOffsets)
            {
                var targetSquare = square + offset;
                var targetRank = targetSquare.GetRankIndex();
                var targetFile = targetSquare.GetFileIndex();

                if (targetSquare is >= 0 and < 64 && Math.Abs(rank - targetRank) <= 2 &&
                    Math.Abs(file - targetFile) <= 2)
                {
                    blackPawnAttacks |= 1UL << targetSquare;
                }
            }

            foreach (var offset in KnightOffsets)
            {
                var targetSquare = square + offset;
                var targetRank = targetSquare.GetRankIndex();
                var targetFile = targetSquare.GetFileIndex();

                if (targetSquare is >= 0 and < 64 && Math.Abs(rank - targetRank) <= 2 &&
                    Math.Abs(file - targetFile) <= 2)
                {
                    knightAttacks |= 1UL << targetSquare;
                }
            }

            foreach (var offset in KingOffsets)
            {
                var targetSquare = square + offset;
                var targetRank = targetSquare.GetRankIndex();
                var targetFile = targetSquare.GetFileIndex();

                if (targetSquare is >= 0 and < 64 && Math.Abs(rank - targetRank) <= 1 &&
                    Math.Abs(file - targetFile) <= 1)
                {
                    kingAttacks |= 1UL << targetSquare;
                }
            }

            WhitePawnAttackTable[square] = whitePawnAttacks;
            BlackPawnAttackTable[square] = blackPawnAttacks;
            KnightAttackTable[square] = knightAttacks;
            KingAttackTable[square] = kingAttacks;
        }

        ulong pextAttackIndex = 0;
        for (var square = 0; square < 64; square++)
        {
            BishopPextOffset[square] = pextAttackIndex;
            var bishopAttackMask = BishopAttackMasks[square];
            var patterns = 1UL << BitboardHelpers.PopCount(bishopAttackMask);
            for (ulong i = 0; i < patterns; i++)
            {
                var blockers = Bmi2.X64.ParallelBitDeposit(i, bishopAttackMask);
                PextAttacks[pextAttackIndex++] = BishopMagics[square].GetMoves(blockers);
            }
        }

        for (var square = 0; square < 64; square++)
        {
            RookPextOffset[square] = pextAttackIndex;
            var rookAttackMask = RookAttackMasks[square];
            var patterns = 1UL << BitboardHelpers.PopCount(rookAttackMask);
            for (ulong i = 0; i < patterns; i++)
            {
                var blockers = Bmi2.X64.ParallelBitDeposit(i, rookAttackMask);
                PextAttacks[pextAttackIndex++] = RookMagics[square].GetMoves(blockers);
            }
        }
    }

    public static ulong RookAttackMask(int square)
    {
        var attackMask = 0UL;
        var rank = square / 8;
        var file = square % 8;

        // Horizontal (rank) moves
        for (var f = file + 1; f < 7; f++) // Start from the next square and end before the edge
        {
            attackMask |= 1UL << (rank * 8 + f);
        }

        for (var f = file - 1; f > 0; f--) // Start from the previous square and end before the edge
        {
            attackMask |= 1UL << (rank * 8 + f);
        }

        // Vertical (file) moves
        for (var r = rank + 1; r < 7; r++) // Start from the next square and end before the edge
        {
            attackMask |= 1UL << (r * 8 + file);
        }

        for (var r = rank - 1; r > 0; r--) // Start from the previous square and end before the edge
        {
            attackMask |= 1UL << (r * 8 + file);
        }

        return attackMask;
    }

    public static ulong BishopAttackMask(int square)
    {
        var attackMask = 0UL;
        var rank = square / 8;
        var file = square % 8;

        // Northeast direction (increasing rank and file)
        for (int r = rank + 1, f = file + 1; r < 7 && f < 7; r++, f++)
        {
            attackMask |= 1UL << (r * 8 + f);
        }

        // Northwest direction (increasing rank, decreasing file)
        for (int r = rank + 1, f = file - 1; r < 7 && f > 0; r++, f--)
        {
            attackMask |= 1UL << (r * 8 + f);
        }

        // Southeast direction (decreasing rank, increasing file)
        for (int r = rank - 1, f = file + 1; r > 0 && f < 7; r--, f++)
        {
            attackMask |= 1UL << (r * 8 + f);
        }

        // Southwest direction (decreasing rank and file)
        for (int r = rank - 1, f = file - 1; r > 0 && f > 0; r--, f--)
        {
            attackMask |= 1UL << (r * 8 + f);
        }

        return attackMask;
    }


    private static ulong[] CreateAllBlockerBitBoards(ulong movementMask)
    {
        var indicesCount = BitboardHelpers.PopCount(movementMask);
        var numPatterns = 1 << indicesCount;
        Span<int> indices = stackalloc int[indicesCount];

        var index = 0;
        for (var i = 0; i < 64; i++)
        {
            if (((movementMask >> i) & 1) == 1)
            {
                indices[index++] = i;
            }
        }

        var blockerBitBoards = new ulong[numPatterns];

        for (var patternIndex = 0; patternIndex < numPatterns; patternIndex++)
        {
            for (var bitIndex = 0; bitIndex < indicesCount; bitIndex++)
            {
                var bit = (patternIndex >> bitIndex) & 1;
                blockerBitBoards[patternIndex] |= (ulong)bit << indices[bitIndex];
            }
        }

        return blockerBitBoards;
    }

    private static ulong CalculateRookLegalMoveBitBoard(ulong position, ulong blockers)
    {
        var legalMoves = 0UL;

        // Calculate moves in each direction and stop at the first blocker
        legalMoves |= CalculateUpRayMoves(position, blockers);
        legalMoves |= CalculateDownRayMoves(position, blockers);
        legalMoves |= CalculateLeftRayMoves(position, blockers);
        legalMoves |= CalculateRightRayMoves(position, blockers);

        return legalMoves;
    }

    private static ulong CalculateUpRayMoves(ulong rookPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = rookPosition;
        while ((currentPos & 0xFF00000000000000UL) == 0) // Ensure not beyond the top row
        {
            currentPos <<= 8;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0)
            {
                break;
            }
        }

        return rayMoves;
    }

    private static ulong CalculateDownRayMoves(ulong rookPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = rookPosition;
        while ((currentPos & 0x00000000000000FFUL) == 0) // Ensure not beyond the bottom row
        {
            currentPos >>= 8;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0)
            {
                break;
            }
        }

        return rayMoves;
    }

    private static ulong CalculateLeftRayMoves(ulong rookPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = rookPosition;
        while ((currentPos & 0x0101010101010101UL) == 0) // Ensure not beyond the left column
        {
            currentPos >>= 1;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0)
            {
                break;
            }
        }

        return rayMoves;
    }

    private static ulong CalculateRightRayMoves(ulong rookPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = rookPosition;
        while ((currentPos & 0x8080808080808080UL) == 0) // Ensure not beyond the right column
        {
            currentPos <<= 1;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0)
            {
                break;
            }
        }

        return rayMoves;
    }

    private static ulong CalculateBishopLegalMoveBitBoard(ulong position, ulong blockers)
    {
        var legalMoves = 0UL;

        // Calculate moves in each diagonal direction and stop at the first blocker
        legalMoves |= CalculateNortheastRayMoves(position, blockers);
        legalMoves |= CalculateNorthwestRayMoves(position, blockers);
        legalMoves |= CalculateSoutheastRayMoves(position, blockers);
        legalMoves |= CalculateSouthwestRayMoves(position, blockers);

        return legalMoves;
    }

    private static ulong CalculateNortheastRayMoves(ulong bishopPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = bishopPosition;
        while ((currentPos & 0x8080808080808080UL) == 0 &&
               (currentPos & 0xFF00000000000000UL) == 0) // Ensure not beyond the top row and right column
        {
            currentPos <<= 9;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0)
            {
                break;
            }
        }

        return rayMoves;
    }

    private static ulong CalculateNorthwestRayMoves(ulong bishopPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = bishopPosition;
        while ((currentPos & 0x0101010101010101UL) == 0 &&
               (currentPos & 0xFF00000000000000UL) == 0) // Ensure not beyond the top row and left column
        {
            currentPos <<= 7;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0)
            {
                break;
            }
        }

        return rayMoves;
    }

    private static ulong CalculateSoutheastRayMoves(ulong bishopPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = bishopPosition;
        while ((currentPos & 0x8080808080808080UL) == 0 &&
               (currentPos & 0x00000000000000FFUL) == 0) // Ensure not beyond the bottom row and right column
        {
            currentPos >>= 7;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0)
            {
                break;
            }
        }

        return rayMoves;
    }

    private static ulong CalculateSouthwestRayMoves(ulong bishopPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = bishopPosition;
        while ((currentPos & 0x0101010101010101UL) == 0 &&
               (currentPos & 0x00000000000000FFUL) == 0) // Ensure not beyond the bottom row and left column
        {
            currentPos >>= 9;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0)
            {
                break;
            }
        }

        return rayMoves;
    }

    private static MagicBitBoard GetBishopMagicNumbers(int square, Random rand)
    {
        var movementMask = BishopAttackMasks[square];

        // Calculate all possible blocker configurations for movement mask
        var blockers = CreateAllBlockerBitBoards(movementMask);

        // Calculate all possible legal moves for each blocker arrangement
        Span<ulong> legalMoves = stackalloc ulong[blockers.Length];
        var position = 1UL << square;
        for (var j = 0; j < blockers.Length; j++)
        {
            legalMoves[j] = CalculateBishopLegalMoveBitBoard(position, blockers[j]);
        }

        var relevantBits = BitboardHelpers.PopCount(movementMask);
        Span<ulong> usedAttacks = stackalloc ulong[1 << relevantBits];
        var indexBits = 64 - relevantBits;
        ulong magic = 0;

        while (true)
        {
            var u1 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            var u2 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            var u3 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            magic = u1 & u2 & u3;
            var isMagic = true;
            usedAttacks.Clear();
            for (var i = 0; i < legalMoves.Length; i++)
            {
                var occupancy = blockers[i];
                var index = (int)((occupancy * magic) >> indexBits);
                if (usedAttacks[index] == 0)
                {
                    usedAttacks[index] = legalMoves[i];
                }
                else if (usedAttacks[index] != legalMoves[i])
                {
                    isMagic = false;
                    break;
                }
            }

            if (isMagic)
            {
                break;
            }
        }

        return new MagicBitBoard(magic, movementMask, indexBits, usedAttacks.ToArray());
    }

    private static MagicBitBoard GetRookMagicNumbers(int square, Random rand)
    {
        var movementMask = RookAttackMasks[square];

        // Calculate all possible blocker configurations for movement mask
        var blockers = CreateAllBlockerBitBoards(movementMask);

        // Calculate all possible legal moves for each blocker arrangement
        Span<ulong> legalMoves = stackalloc ulong[blockers.Length];
        var position = 1UL << square;
        for (var j = 0; j < blockers.Length; j++)
        {
            legalMoves[j] = CalculateRookLegalMoveBitBoard(position, blockers[j]);
        }

        var relevantBits = BitboardHelpers.PopCount(movementMask);
        Span<ulong> usedAttacks = stackalloc ulong[1 << relevantBits];
        var indexBits = 64 - relevantBits;
        ulong magic = 0;

        while (true)
        {
            var u1 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            var u2 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            var u3 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            magic = u1 & u2 & u3;
            var isMagic = true;
            usedAttacks.Clear();
            for (var i = 0; i < legalMoves.Length; i++)
            {
                var occupancy = blockers[i];
                var index = (int)((occupancy * magic) >> indexBits);
                if (usedAttacks[index] == 0)
                {
                    usedAttacks[index] = legalMoves[i];
                }
                else if (usedAttacks[index] != legalMoves[i])
                {
                    isMagic = false;
                    break;
                }
            }

            if (isMagic)
            {
                break;
            }
        }

        return new MagicBitBoard(magic, movementMask, indexBits, usedAttacks.ToArray());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PextRookAttacks(ulong occupation, int square)
    {
        return PextAttacks[RookPextOffset[square] + Bmi2.X64.ParallelBitExtract(occupation, RookAttackMasks[square])];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PextBishopAttacks(ulong occupation, int square)
    {
        return PextAttacks[
            BishopPextOffset[square] + Bmi2.X64.ParallelBitExtract(occupation, BishopAttackMasks[square])];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAttackedByWhite(this BoardState pieces, int index)
    {
        return (PextBishopAttacks(pieces.Occupancy, index) &
                (pieces.WhiteBishops | pieces.WhiteQueens)) != 0 ||
               (PextRookAttacks(pieces.Occupancy, index) & (pieces.WhiteRooks | pieces.WhiteQueens)) !=
               0 ||
               (KnightAttackTable[index] & pieces.WhiteKnights) != 0 ||
               (BlackPawnAttackTable[index] & pieces.WhitePawns) != 0 ||
               (KingAttackTable[index] & pieces.WhiteKings) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAttackedByBlack(this BoardState pieces, int index)
    {
        return (PextBishopAttacks(pieces.Occupancy, index) &
                (pieces.BlackBishops | pieces.BlackQueens)) != 0 ||
               (PextRookAttacks(pieces.Occupancy, index) & (pieces.BlackRooks | pieces.BlackQueens)) !=
               0 ||
               (KnightAttackTable[index] & pieces.BlackKnights) != 0 ||
               (WhitePawnAttackTable[index] & pieces.BlackPawns) != 0 ||
               (KingAttackTable[index] & pieces.BlackKings) != 0;
    }
}