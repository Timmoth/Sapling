using System.Buffers.Binary;
using System.Text;
using Sapling.Engine;
using Sapling.Engine.MoveGen;

namespace Sapling.Utils
{
    internal class Program
    {
        public static string PolyGlotToUciMove(ushort move)
        {
            // Extract components using bit-shifting and masking

            // To file (0-2 bits)
            int toFile = (move & 0b0000000000000111);

            // To rank (3-5 bits)
            int toRank = (move >> 3) & 0b0000000000000111;

            // From file (6-8 bits)
            int fromFile = (move >> 6) & 0b0000000000000111;

            // From rank (9-11 bits)
            int fromRank = (move >> 9) & 0b0000000000000111;

            // Promotion piece (12-14 bits)
            int promotionPiece = (move >> 12) & 0b0000000000000111;

            // Convert the file (0-7) to a letter ('a' = 0, 'b' = 1, ..., 'h' = 7)
            char fromFileChar = (char)('a' + fromFile);
            char toFileChar = (char)('a' + toFile);

            // Convert the rank (0-7) to a number ('1' = 0, '2' = 1, ..., '8' = 7)
            char fromRankChar = (char)('1' + fromRank);
            char toRankChar = (char)('1' + toRank);

            // Create the basic move string (like "e2e4")
            string moveStr = $"{fromFileChar}{fromRankChar}{toFileChar}{toRankChar}";

            // Handle promotion if present (promotionPiece > 0 means promotion)
            if (promotionPiece > 0)
            {
                // Convert promotion piece (1=q, 2=r, 3=b, 4=n)
                char promotionChar = promotionPiece switch
                {
                    1 => 'q',  // Queen
                    2 => 'r',  // Rook
                    3 => 'b',  // Bishop
                    4 => 'n',  // Knight
                    _ => throw new InvalidOperationException("Invalid promotion piece")
                };

                // Append the promotion character to the move string
                moveStr += promotionChar;
            }

            return moveStr;
        }

        public static ushort ToOpeningMove(uint move)
        {
            var moveType = move.GetMoveType();

            var promotion = 0;
            if (moveType >= Constants.PawnKnightPromotion)
            {
                promotion = moveType - 3;
            }

            var from = move.GetFromSquare();
            var to = move.GetToSquare();

            return (ushort)(to.GetFileIndex() |
                   to.GetRankIndex() << 3 |
                   from.GetFileIndex() << 6 |
                   from.GetRankIndex() << 9 |
                   promotion << 12
                   );
        }

        static void Main(string[] args)
        {
            var fileName = "./Human.bin";
            var entrySize = sizeof(ulong) + sizeof(ushort) + sizeof(ushort) + sizeof(uint);
            var entryCount = (int)new FileInfo(fileName).Length / entrySize;

            var openingMoves = new Dictionary<ulong, List<ushort>>();
            using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                using var reader = new BinaryReader(fs, Encoding.UTF8, false);
                for (var i = 0; i < entryCount; i++)
                {
                    ulong hash;
                    ushort move;

                    if (BitConverter.IsLittleEndian)
                    {
                        hash = BinaryPrimitives.ReverseEndianness(reader.ReadUInt64());
                        move = BinaryPrimitives.ReverseEndianness(reader.ReadUInt16());
                    }
                    else
                    {
                        hash = reader.ReadUInt64();
                        move = reader.ReadUInt16();
                    }

                    // Skip weight & learn
                    reader.ReadUInt16();
                    reader.ReadUInt32();

                    if (!openingMoves.TryGetValue(hash, out var moveList))
                    {
                        openingMoves[hash] = moveList = new List<ushort>();
                    }

                    moveList.Add(move);
                }
            }

            Console.WriteLine("Size: " + entryCount);

            var gameHashes = new HashSet<ulong>();
            var openingBookBulder = new StringBuilder();

            var validFirstMoves = new Dictionary<string, int>
            {
                // Pawn moves
                {"a2a3", 0}, {"a2a4", 0},
                {"b2b3", 0}, {"b2b4", 0},
                {"c2c3", 0}, {"c2c4", 0},
                {"d2d3", 0}, {"d2d4", 0},
                {"e2e3", 0}, {"e2e4", 0},
                {"f2f3", 0}, {"f2f4", 0},
                {"g2g3", 0}, {"g2g4", 0},
                {"h2h3", 0}, {"h2h4", 0},

                // Knight moves
                {"b1a3", 0}, {"b1c3", 0},
                {"g1f3", 0}, {"g1h3", 0}
            };

            var initialGameState = new GameState(BoardStateExtensions.CreateBoardFromArray(Constants.InitialState));
            var gameState = new GameState(BoardStateExtensions.CreateBoardFromArray(Constants.InitialState));
            for (var i = 0; i < 5000000; i++)
            {
                    gameState.ResetTo(initialGameState.Board);
                    var firstMove = gameState.Moves[Random.Shared.Next(0, gameState.Moves.Count)];
                    gameState.Apply(firstMove);

                    var openingMovesBuilder = new StringBuilder();
                    openingMovesBuilder.Append(firstMove.ToUciMoveName());
                    var gameOk = true;
                    var j = 1;

                    for (j = 1; j < 12; j++)
                    {
                        var hash = Zobrist.CalculatePolyGlotKey(ref gameState.Board.Data);
                        if (!openingMoves.TryGetValue(hash, out var openingMoveList))
                        {
                            gameOk = false;
                            break;
                        }

                        var randomOpeningMove = openingMoveList[Random.Shared.Next(0, openingMoveList.Count)];
                        var mv = gameState.Moves.FirstOrDefault(m => ToOpeningMove(m) == randomOpeningMove);
                        if (mv == default)
                        {
                            var uciMove = PolyGlotToUciMove(randomOpeningMove);
                            if (uciMove == "e1h1")
                            {
                                mv = gameState.Moves.FirstOrDefault(m => m.ToUciMoveName() == "e1g1");
                            }
                            else if (uciMove == "e1a1")
                            {
                                mv = gameState.Moves.FirstOrDefault(m => m.ToUciMoveName() == "e1b1");
                            }
                            else if (uciMove == "e8h8")
                            {
                                mv = gameState.Moves.FirstOrDefault(m => m.ToUciMoveName() == "e8g8");
                            }
                            else if (uciMove == "e8a8")
                            {
                                mv = gameState.Moves.FirstOrDefault(m => m.ToUciMoveName() == "e8b8");
                            }
                        }

                        if (mv == default)
                        {
                            if (j < 8)
                            {
                                gameOk = false;
                            }

                            break;
                        }

                        openingMovesBuilder.Append(" ");
                        openingMovesBuilder.Append(mv.ToUciMoveName());
                        gameState.Apply(mv);
                    }

                    if (!gameHashes.Contains(gameState.Board.Data.Hash) && gameOk)
                    {
                        gameHashes.Add(gameState.Board.Data.Hash);
                        validFirstMoves[firstMove.ToUciMoveName()]++;
                        openingBookBulder.AppendLine(openingMovesBuilder.ToString());
                    }
                }

            var total = 0;
            foreach (var (move, count) in validFirstMoves)
            {
                total += count;
                Console.WriteLine($"move: {move} count: {count}");
            }

            Console.WriteLine($"Total games: {total}");

            File.WriteAllText("./book.csv",openingBookBulder.ToString());
            Console.WriteLine("Fin");
        }
    }
}
