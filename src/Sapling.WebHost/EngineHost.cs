using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using Sapling.Engine;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Pgn;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling.WebHost;

/// <summary>
/// Managed boundary between the browser worker and engine. Methods return JSON strings;
/// search runs in bounded slices so the worker can process cancellation between calls.
/// </summary>
public static partial class EngineHost
{
    private static ParallelSearcher? _ps;
    private static GameState? _game;
    private static long _ttEntries;
    private static string _startFen = Constants.InitialState;

    // Session / request state. A single authoritative request id makes obsolete
    // progress, slices and stops inert against a later search.
    private static int _currentRequestId;
    private static int _sliceNodes;
    private static long _totalNodeBudget;
    private static long _nodesCumulative;
    private static long _deadlineTimestamp;
    private static int _maxDepth;
    private static Stopwatch? _clock;

    // Best completed result across the slices of the current request.
    private static int _bestDepth;
    private static int _bestScore;
    private static uint _bestMove;
    private static List<uint> _bestPv = new();

    // Human-like difficulty (continuous Elo). Default off (Elo 0); the override path is inert
    // unless a positive Elo is set, so normal play is unchanged.
    private static readonly Random _humanRng = new();
    private static bool _humanApplied;

    // Explicit host lifecycle (4.2). Every JS entry point checks the state, so obsolete
    // requests, double stops, and use-after-dispose are rejected deterministically rather
    // than relying on scattered null checks.
    private enum HostState { Uninitialized, Initializing, Idle, Searching, Stopping, Faulted, Disposed }
    private static HostState _state = HostState.Uninitialized;

    [JSExport]
    public static string Initialize(int ttMb)
    {
        if (_state == HostState.Disposed) return Fail("disposed", "Host has been disposed.");
        if (_state == HostState.Searching) return Busy();
        _state = HostState.Initializing;
        try
        {
            ttMb = Math.Clamp(ttMb, 1, 64);
            _ttEntries = TranspositionTableExtensions.CalculateTranspositionTableSize(ttMb);
            _ps?.Dispose(); // replace policy: release the previous owner before allocating a new one
            _ps = new ParallelSearcher(_ttEntries);
            _ps.SetThreads(1);
            _game = GameState.InitialState();
            _state = HostState.Idle;

            var j = new Json();
            j.Start();
            j.Bool("ok", true);
            j.Str("engine", "Sapling");
            j.Num("ttMb", ttMb);
            j.Num("ttEntries", _ttEntries);
            j.Num("ttBytes", _ttEntries * 20);
            j.Bool("singleWorker", true);
            j.Bool("resumable", true);
            j.End();
            return j.Done();
        }
        catch (Exception ex)
        {
            _state = HostState.Faulted;
            return Error(ex);
        }
    }

    [JSExport]
    public static string SetHumanElo(int elo)
    {
        if (_state == HostState.Disposed) return Fail("disposed", "Host has been disposed.");
        HumanPlay.SetElo(Math.Clamp(elo, 0, 3000));
        var j = new Json();
        j.Start();
        j.Bool("ok", true);
        j.Num("humanElo", HumanPlay.Elo);
        j.Bool("enabled", HumanPlay.Enabled);
        j.End();
        return j.Done();
    }

    [JSExport]
    public static string SetPosition(string fen)
    {
        if (_state != HostState.Idle) return Busy();
        if (_game is null) return NotReady();
        if (FenValidator.Validate(fen) is { } fenError)
            return Fail("invalid-fen", fenError);
        try
        {
            // Commit the start FEN only after the position actually resets, so a rejected FEN
            // leaves the board, history and exported PGN unchanged.
            _game.ResetToFen(fen);
            _startFen = fen;
            return Position();
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [JSExport]
    public static string ApplyMove(string uci)
    {
        if (_state != HostState.Idle) return Busy();
        if (_game is null) return NotReady();
        try
        {
            var move = MatchLegalMove(_game, uci);
            if (move == 0) return Fail("illegal", $"Move '{uci}' is not legal in the current position.");
            _game.Apply(move);
            return Position();
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [JSExport]
    public static string StartSearch(int requestId, int maxDepth, int sliceNodes, int totalNodes, int timeMs)
    {
        if (_state is HostState.Uninitialized or HostState.Initializing or HostState.Disposed) return NotReady();
        if (_state == HostState.Searching) return Busy();
        if (_ps is null || _game is null) return NotReady();
        if (maxDepth < 1 || sliceNodes < 1 || totalNodes < 1)
            return Fail("invalid-limits", "maxDepth, sliceNodes and totalNodes must be positive.");

        _currentRequestId = requestId;
        _maxDepth = Math.Min(maxDepth, Constants.MaxSearchDepth);
        _sliceNodes = sliceNodes;
        _totalNodeBudget = totalNodes;
        _nodesCumulative = 0;
        _bestDepth = 0;
        _bestScore = 0;
        _bestMove = 0;
        _bestPv = new List<uint>();
        _humanApplied = false;
        _clock = Stopwatch.StartNew();
        _deadlineTimestamp = timeMs > 0
            ? Stopwatch.GetTimestamp() + (long)(timeMs * (double)Stopwatch.Frequency / 1000.0)
            : 0;

        try
        {
            _ps.BeginResumableSearch(_game);
            _state = HostState.Searching;
        }
        catch (Exception ex)
        {
            _state = HostState.Faulted;
            return Error(ex);
        }

        var j = new Json();
        j.Start();
        j.Bool("ok", true);
        j.Num("requestId", requestId);
        j.Num("maxDepth", _maxDepth);
        j.Num("sliceNodes", _sliceNodes);
        j.Num("totalNodes", _totalNodeBudget);
        j.Num("timeMs", timeMs);
        j.End();
        return j.Done();
    }

    [JSExport]
    public static string SearchSlice(int requestId)
    {
        if (_state is HostState.Uninitialized or HostState.Initializing or HostState.Disposed) return NotReady();
        if (requestId != _currentRequestId) return Obsolete(requestId);
        if (_state == HostState.Idle) return Stopped(requestId); // already finalised; idempotent terminal
        if (_ps is null) return NotReady();

        try
        {
            var elapsedMs = (int)_clock!.Elapsed.TotalMilliseconds;

            // Finalize without starting new work once the deadline or the total-node budget is
            // already exhausted, provided a legal fallback move has been harvested.
            var deadlineHit = _deadlineTimestamp != 0 && Stopwatch.GetTimestamp() >= _deadlineTimestamp;
            if ((deadlineHit || _nodesCumulative >= _totalNodeBudget) && _bestMove != 0)
            {
                _ps.EndResumableSearch();
                _state = HostState.Idle;
                ApplyHumanOverride();
                return Progress(requestId, _bestDepth, _bestScore, _nodesCumulative, elapsedMs, true, false);
            }

            // Cap the slice to the remaining total-node budget and remaining wall-clock. Always run
            // at least one slice so a legal fallback move is harvested from the initialization
            // search, and never truncate a live deadline into the unlimited (0) sentinel.
            var sliceNodes = (int)Math.Max(1, Math.Min(_sliceNodes, _totalNodeBudget - _nodesCumulative));
            var sliceMs = 0;
            if (_deadlineTimestamp != 0)
            {
                var remainingMs = (long)((_deadlineTimestamp - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency);
                sliceMs = (int)Math.Max(1, Math.Min(remainingMs, 1000));
            }

            var r = _ps.ResumableSlice(_maxDepth, sliceNodes, sliceMs);
            _nodesCumulative = r.TotalNodes;

            if (r.Depth > _bestDepth || _bestMove == 0)
            {
                _bestDepth = r.Depth;
                _bestScore = r.Score;
                _bestPv = r.Pv;
                _bestMove = r.BestMove;
            }

            deadlineHit = _deadlineTimestamp != 0 && Stopwatch.GetTimestamp() >= _deadlineTimestamp;
            var done = r.Done || deadlineHit || _nodesCumulative >= _totalNodeBudget;
            var stopped = r.Stopped;

            if (done || stopped)
            {
                _ps.EndResumableSearch();
                _state = HostState.Idle;
            }

            // Preserve a legal fallback: if the budget/deadline was too small to complete any
            // iteration, emit a legal move rather than an empty bestMove.
            if (_bestMove == 0 && _game!.LegalMoves.Count > 0)
                _bestMove = _game.LegalMoves[0];

            if (done)
                ApplyHumanOverride();

            return Progress(requestId, r.Depth, r.Score, r.TotalNodes, elapsedMs, done, stopped);
        }
        catch (Exception ex)
        {
            _state = HostState.Faulted;
            return Error(ex);
        }
    }

    [JSExport]
    public static string Stop(int requestId)
    {
        if (_state is HostState.Uninitialized or HostState.Initializing or HostState.Disposed) return NotReady();
        if (requestId != _currentRequestId) return Obsolete(requestId);
        if (_state == HostState.Idle) return Stopped(requestId); // already finalised; idempotent
        if (_ps is null) return NotReady();

        _state = HostState.Stopping;
        try
        {
            _ps.Stop();
            _ps.EndResumableSearch();
        }
        catch (Exception ex)
        {
            _state = HostState.Faulted;
            return Error(ex);
        }
        _state = HostState.Idle;
        ApplyHumanOverride();
        return Stopped(requestId);
    }

    [JSExport]
    public static string NewGame()
    {
        if (_state is HostState.Uninitialized or HostState.Initializing or HostState.Disposed) return NotReady();
        if (_ps is null || _game is null) return NotReady();
        try
        {
            if (_state == HostState.Searching || _state == HostState.Stopping)
            {
                _ps.Stop();
                _ps.EndResumableSearch();
            }
            _ps.NewGame();
            _game.Reset();
            _startFen = Constants.InitialState;
            _currentRequestId = 0;
            _state = HostState.Idle;
            return Position();
        }
        catch (Exception ex)
        {
            _state = HostState.Faulted;
            return Error(ex);
        }
    }

    [JSExport]
    public static string Dispose()
    {
        try
        {
            _ps?.Dispose();
            _game?.Dispose();
            _ps = null;
            _game = null;
            _state = HostState.Disposed;
            var j = new Json();
            j.Start();
            j.Bool("ok", true);
            j.End();
            return j.Done();
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    [JSExport]
    public static string Pgn()
    {
        if (_state is HostState.Uninitialized or HostState.Initializing or HostState.Disposed) return NotReady();
        if (_game is null) return NotReady();
        try
        {
            var (pgn, result) = BuildPgn(_startFen, _game.History);
            var j = new Json();
            j.Start();
            j.Bool("ok", true);
            j.Str("pgn", pgn);
            j.Str("result", result);
            j.End();
            return j.Done();
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    // Authoritative SAN/PGN built by replaying the committed move list through the engine.
    // Single source of truth: no JavaScript re-implementation of chess rules.
    private static (string pgn, string result) BuildPgn(string startFen, List<uint> history)
    {
        using var g = GameState.InitialState();
        g.ResetToFen(startFen);

        var parts = startFen.Split(' ');
        var whiteStart = parts.Length > 1 && parts[1] == "w";
        var moveNumber = parts.Length > 5 && int.TryParse(parts[5], out var n) ? n : 1;
        var sanMoves = new List<string>();
        foreach (var m in history)
        {
            var san = SanFor(g, m);
            g.Apply(m);
            if (g.LegalMoves.Count == 0) { if (g.Board.InCheck) san += "#"; }
            else if (g.Board.InCheck) san += "+";
            sanMoves.Add(san);
        }

        var result = Result(g);
        var sb = new StringBuilder();
        sb.Append("[Event \"Sapling browser game\"]\n");
        sb.Append("[Site \"Sapling webapp\"]\n");
        sb.Append("[Date \"").Append(DateTime.UtcNow.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)).Append("\"]\n");
        sb.Append("[Round \"1\"]\n[White \"Sapling\"]\n[Black \"Sapling\"]\n");
        sb.Append("[Result \"").Append(result).Append("\"]\n");
        if (startFen != Constants.InitialState)
            sb.Append("[SetUp \"1\"]\n[FEN \"").Append(startFen).Append("\"]\n");
        sb.Append('\n');

        var whiteToMove = whiteStart;
        for (var i = 0; i < sanMoves.Count; i++)
        {
            if (whiteToMove)
                sb.Append(moveNumber).Append(". ");
            else if (i == 0)
                sb.Append(moveNumber).Append("... "); // black moves first (from a SetUp FEN)
            sb.Append(sanMoves[i]);
            if (i + 1 < sanMoves.Count) sb.Append(' ');
            if (!whiteToMove) moveNumber++;
            whiteToMove = !whiteToMove;
        }
        sb.Append('\n').Append(result).Append('\n');
        return (sb.ToString(), result);
    }

    private static string SanFor(GameState g, uint m)
    {
        if (m.IsCastle())
            return m.GetToSquare() % 8 == 2 ? "O-O-O" : "O-O";

        var moveType = m.GetMoveType();
        var from = m.GetFromSquare();
        var to = m.GetToSquare();
        var piece = m.GetMovedPiece();
        var pawn = piece == Constants.WhitePawn || piece == Constants.BlackPawn;
        var capture = m.IsCapture();
        var dest = to.ConvertPosition();
        var sb = new StringBuilder();

        if (pawn)
        {
            if (capture) sb.Append((char)('a' + from % 8)).Append('x');
            sb.Append(dest);
            sb.Append(moveType switch
            {
                Constants.PawnQueenPromotion => "=Q",
                Constants.PawnRookPromotion => "=R",
                Constants.PawnBishopPromotion => "=B",
                Constants.PawnKnightPromotion => "=N",
                _ => "",
            });
        }
        else
        {
            sb.Append(((Piece)piece).PieceToUpperChar());
            var others = g.LegalMoves.Where(x => x != m && x.GetToSquare() == to && x.GetMovedPiece() == piece).ToList();
            if (others.Count > 0)
            {
                var sameFile = others.Any(x => x.GetFromSquare() % 8 == from % 8);
                var sameRank = others.Any(x => x.GetFromSquare() / 8 == from / 8);
                if (!sameFile) sb.Append((char)('a' + from % 8));
                else if (!sameRank) sb.Append((char)('1' + from / 8));
                else sb.Append((char)('a' + from % 8)).Append((char)('1' + from / 8));
            }
            if (capture) sb.Append('x');
            sb.Append(dest);
        }
        return sb.ToString();
    }

    private static uint MatchLegalMove(GameState game, string uci)
    {
        foreach (var move in game.LegalMoves)
            if (move.ToUciMoveName() == uci)
                return move;
        return 0;
    }

    private static string Position()
    {
        var game = _game!;
        var j = new Json();
        j.Start();
        j.Bool("ok", true);
        j.Str("fen", game.Board.ToFen());
        j.Str("sideToMove", game.Board.WhiteToMove ? "w" : "b");
        j.ArrStart("legalMoves");
        foreach (var move in game.LegalMoves) j.StrItem(move.ToUciMoveName());
        j.ArrEnd();
        j.Str("lastMove", game.History.Count > 0 ? game.History[^1].ToUciMoveName() : "");
        j.Bool("inCheck", game.Board.InCheck);
        j.Bool("gameOver", game.GameOver());
        j.Str("result", Result(game));
        j.End();
        return j.Done();
    }

    private static string Result(GameState game)
    {
        if (game.LegalMoves.Count == 0)
        {
            if (game.Board.InCheck)
                return game.Board.WhiteToMove ? "0-1" : "1-0";
            return "1/2-1/2";
        }
        if (game.Board.HalfMoveClock >= 100 || game.Board.InsufficientMatingMaterial())
            return "1/2-1/2";
        return "*";
    }

    // Human-like difficulty override: rank the root moves and sample which rank to play,
    // replacing the engine's best move. Inert unless HumanPlay is enabled; runs once per request.
    private static void ApplyHumanOverride()
    {
        if (!HumanPlay.Enabled || _humanApplied || _bestMove == 0 || _ps is null || _game is null)
            return;
        _humanApplied = true;

        var ranked = _ps.RankRootMoves(_game, HumanPlay.RankDepth);
        if (ranked.Count == 0)
            return;

        var scores = new int[ranked.Count];
        var feats = new int[ranked.Count];
        for (var i = 0; i < ranked.Count; i++)
        {
            scores[i] = ranked[i].Score;
            feats[i] = ranked[i].Feat;
        }
        var rank = HumanPlay.PickRank(_humanRng, scores, feats);
        if (rank <= 0 || rank >= ranked.Count)
            return;

        var chosen = ranked[rank].Move;
        if (chosen == _bestMove)
            return;

        _bestMove = chosen;
        _bestScore = ranked[rank].Score;
        _bestPv = new List<uint> { chosen };
    }

    private static string Progress(int requestId, int depth, int score, long nodesSlice, int elapsedMs, bool done, bool stopped)
    {
        var j = new Json();
        j.Start();
        j.Bool("ok", true);
        j.Num("requestId", requestId);
        j.Num("depth", depth);
        j.Num("bestDepth", _bestDepth);
        if (MoveScoring.IsMateScore(_bestScore))
        {
            j.Str("scoreType", "mate");
            j.Num("score", Math.Sign(_bestScore) * MoveScoring.GetMateDistance(_bestScore));
        }
        else
        {
            j.Str("scoreType", "cp");
            j.Num("score", _bestScore);
        }
        j.Num("nodesSlice", nodesSlice);
        j.Num("nodesTotal", _nodesCumulative);
        j.Num("timeMs", elapsedMs);
        j.Num("nps", elapsedMs > 0 ? (long)(_nodesCumulative / (elapsedMs / 1000.0)) : 0);
        j.Str("bestMove", _bestMove != 0 ? _bestMove.ToUciMoveName() : "");
        j.ArrStart("pv");
        foreach (var move in _bestPv) j.StrItem(move.ToUciMoveName());
        j.ArrEnd();
        j.Bool("done", done);
        j.Bool("stopped", stopped);
        j.End();
        return j.Done();
    }

    private static string Stopped(int requestId)
    {
        var j = new Json();
        j.Start();
        j.Bool("ok", true);
        j.Num("requestId", requestId);
        j.Str("state", "stopped");
        j.Num("bestDepth", _bestDepth);
        if (MoveScoring.IsMateScore(_bestScore))
        {
            j.Str("scoreType", "mate");
            j.Num("score", Math.Sign(_bestScore) * MoveScoring.GetMateDistance(_bestScore));
        }
        else
        {
            j.Str("scoreType", "cp");
            j.Num("score", _bestScore);
        }
        j.Num("nodesTotal", _nodesCumulative);
        j.Str("bestMove", _bestMove != 0 ? _bestMove.ToUciMoveName() : "");
        j.ArrStart("pv");
        foreach (var move in _bestPv) j.StrItem(move.ToUciMoveName());
        j.ArrEnd();
        j.End();
        return j.Done();
    }

    private static string Obsolete(int requestId)
    {
        var j = new Json();
        j.Start();
        j.Bool("ok", false);
        j.Str("state", "obsolete");
        j.Num("requestId", requestId);
        j.End();
        return j.Done();
    }

    private static string NotReady() => Fail("not-ready", "Engine is not initialized.");

    private static string Busy() => Fail("busy", "Engine is busy; stop the current search first.");

    private static string Fail(string code, string message)
    {
        var j = new Json();
        j.Start();
        j.Bool("ok", false);
        j.Str("error", code);
        j.Str("message", message);
        j.End();
        return j.Done();
    }

    private static string Error(Exception ex) => Fail("engine-error", ex.Message);

    /// <summary>Minimal allocation-light JSON writer for the small host payloads.</summary>
    private sealed class Json
    {
        private readonly StringBuilder _sb = new();
        private bool _needComma;

        public void Start() { _sb.Append('{'); _needComma = false; }
        public void End() { _sb.Append('}'); }
        public string Done() => _sb.ToString();

        private void Key(string name)
        {
            if (_needComma) _sb.Append(',');
            _sb.Append('"').Append(name).Append("\":");
            _needComma = true;
        }

        public void Bool(string name, bool value) { Key(name); _sb.Append(value ? "true" : "false"); }
        public void Num(string name, long value) { Key(name); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); }
        public void Str(string name, string value) { Key(name); WriteString(value); }

        public void ArrStart(string name) { Key(name); _sb.Append('['); _needComma = false; }
        public void ArrEnd() { _sb.Append(']'); _needComma = true; }
        public void StrItem(string value)
        {
            if (_sb[^1] != '[') _sb.Append(',');
            WriteString(value);
        }

        private void WriteString(string value)
        {
            _sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
