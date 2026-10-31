using System.Diagnostics;
using System.Globalization;
using Sapling.Engine;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling.Mobile;

/// <summary>Native MAUI game screen. The C# engine runs in-process; no web view or server is used.</summary>
public sealed class MainPage : ContentPage
{
    private static readonly Color Canvas = Color.FromArgb("#0B0B0F");
    private static readonly Color Surface = Color.FromArgb("#1C1C1E");
    private static readonly Color Surface2 = Color.FromArgb("#2C2C2E");
    private static readonly Color PrimaryText = Color.FromArgb("#F2F2F7");
    private static readonly Color SecondaryText = Color.FromArgb("#98989F");
    private static readonly Color Separator = Color.FromArgb("#38383A");
    private static readonly Color Accent = Color.FromArgb("#0A84FF");
    private static readonly Color LightSquare = Color.FromArgb("#F0D9B5");
    private static readonly Color DarkSquare = Color.FromArgb("#B58863");
    private static readonly string InitialFen = Constants.InitialState;

    private readonly Grid _board = new() { RowSpacing = 0, ColumnSpacing = 0 };
    private readonly AbsoluteLayout _animationOverlay = new() { InputTransparent = true };
    private readonly Button[] _squares = new Button[64];
    private readonly Image[] _pieces = new Image[64];
    private readonly Label _status = new() { FontSize = 15, FontAttributes = FontAttributes.Bold };
    private readonly Label _moveCount = new() { FontSize = 13, TextColor = SecondaryText };
    private readonly Label _depth = new() { Text = "–" };
    private readonly Label _evaluation = new() { Text = "–" };
    private readonly Label _elapsed = new() { Text = "–" };
    private readonly Label _nodes = new() { Text = "–" };
    private readonly Label _nps = new() { Text = "–" };
    private readonly Label _pv = new() { Text = "–", LineBreakMode = LineBreakMode.WordWrap, FontSize = 12 };
    private readonly VerticalStackLayout _moveRows = new() { Spacing = 4 };
    private readonly Label _material = new() { FontSize = 13, TextColor = SecondaryText };
    private readonly Label _eloValue = new() { FontSize = 13, TextColor = SecondaryText };
    private readonly Slider _eloSlider = new() { Minimum = 0, Maximum = 171, Value = 171 };
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly Button _hintButton;
    private readonly Button _searchButton;
    private readonly Button _whiteButton;
    private readonly Button _blackButton;
    private readonly Border _root;

    private ParallelSearcher? _engine;
    private GameState? _game;
    private Task? _searchTask;
    private int? _selectedSquare;
    private HashSet<int> _targets = [];
    private readonly List<string> _history = [];
    private readonly Stack<string[]> _redo = new();
    private readonly List<(int Value, bool WhiteCaptured)> _captured = [];
    private readonly List<string> _moveLabels = [];
    private Dictionary<string, int> _hintScores = new(StringComparer.Ordinal);
    private bool _humanWhite = true;
    private bool _flipped;
    private bool _thinking;
    private bool _hinting;
    private bool _hintMode;
    private bool _restoring;
    private bool _disposed;
    private volatile bool _cancelSearch;
    private int _positionVersion;
    private int _boardRenderVersion;
    private readonly Stopwatch _searchClock = new();
    private readonly Random _humanRng = new();

    public MainPage()
    {
        Title = "Sapling";
        BackgroundColor = Canvas;
        Padding = new Thickness(12, 4);
        _undoButton = ActionButton("Undo", "arrow.uturn.backward", UndoAsync);
        _redoButton = ActionButton("Redo", "arrow.uturn.forward", RedoAsync);
        _hintButton = ActionButton("Hints Off", "sparkles", ToggleHintsAsync);
        _searchButton = new Button
        {
            Text = "New game", FontSize = 12, Padding = new Thickness(6, 10), CornerRadius = 14,
            BackgroundColor = Accent, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center, MinimumHeightRequest = 46
        };
        _searchButton.Clicked += SearchButtonNewGameClicked;
        _whiteButton = ActionButton("White", "", () => ChangeSideAsync(true));
        _blackButton = ActionButton("Black", "", () => ChangeSideAsync(false));
        _whiteButton.BackgroundColor = Accent;
        _whiteButton.TextColor = Colors.White;
        _root = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 28 },
            BackgroundColor = Canvas,
            Content = BuildPageContent()
        };
        Content = _root;
        BuildBoard();
        ApplyTheme();
        Application.Current!.RequestedThemeChanged += (_, _) => ApplyTheme();
        UpdateBoard();
        SetStatus("Preparing local engine…");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private View BuildPageContent()
    {
        var scroll = new ScrollView { VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        var content = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(0, 4, 0, 20) };
        content.Children.Add(BuildHeader());
        content.Children.Add(BuildBoardCard());
        content.Children.Add(BuildActionRow());
        content.Children.Add(BuildSettingsCard());
        content.Children.Add(BuildAnalysisCard());
        content.Children.Add(BuildMovesCard());
        content.Children.Add(BuildExportRow());
        scroll.Content = content;
        return scroll;
    }

    private View BuildHeader()
    {
        var title = new Label
        {
            Text = "Sapling", FontSize = 28, FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center
        };
        var subtitle = new Label { Text = "CHESS ENGINE", FontSize = 10, CharacterSpacing = 1.5, TextColor = SecondaryText };
        var brandText = new VerticalStackLayout { Spacing = 1, Children = { title, subtitle } };
        var brand = new HorizontalStackLayout
        {
            Spacing = 9,
            VerticalOptions = LayoutOptions.Center,
            Children = { new Image { Source = "sapling_logo.png", WidthRequest = 28, HeightRequest = 28, Aspect = Aspect.AspectFit }, brandText }
        };
        var github = new Button
        {
            Text = "GitHub", FontSize = 13, Padding = new Thickness(13, 8),
            CornerRadius = 18, BackgroundColor = Surface2, TextColor = PrimaryText,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center
        };
        github.Clicked += async (_, _) => await Launcher.Default.OpenAsync("https://github.com/Timmoth/Sapling");
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(brand, 0, 0);
        row.Add(github, 1, 0);
        return row;
    }

    private View BuildBoardCard()
    {
        var statusRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { _status, _moveCount }
        };
        Grid.SetColumn(_moveCount, 1);
        var boardFrame = new Border
        {
            Stroke = Color.FromArgb("#333333"),
            StrokeThickness = 2,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 13 },
            Padding = 0,
            Content = BuildBoardSurface(),
            HorizontalOptions = LayoutOptions.Fill,
            MaximumWidthRequest = 640
        };
        boardFrame.SizeChanged += (_, _) =>
        {
            var width = boardFrame.Width - boardFrame.Padding.HorizontalThickness;
            if (width > 0) boardFrame.HeightRequest = width;
        };
        var boardStack = new VerticalStackLayout { Spacing = 9, Children = { statusRow, boardFrame, _material } };
        var card = Card(boardStack);
        return card;
    }

    private View BuildBoardSurface()
    {
        var surface = new Grid();
        surface.Add(_board);
        surface.Add(_animationOverlay);
        return surface;
    }

    private View BuildActionRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        grid.Add(_searchButton, 0, 0);
        grid.Add(_undoButton, 1, 0);
        grid.Add(_redoButton, 2, 0);
        grid.Add(_hintButton, 3, 0);
        return grid;
    }

    private View BuildSettingsCard()
    {
        var heading = SectionHeading("Game settings", "person.fill");
        var sideCaption = new Label { Text = "Play as", FontSize = 14, VerticalTextAlignment = TextAlignment.Center };
        var sideButtons = new HorizontalStackLayout { Spacing = 8, Children = { _whiteButton, _blackButton } };
        var sideRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { sideCaption, sideButtons }
        };
        Grid.SetColumn(sideButtons, 1);
        var flip = ActionButton("Flip board", "arrow.2.squarepath", () => { _flipped = !_flipped; UpdateBoard(); return Task.CompletedTask; });
        flip.HorizontalOptions = LayoutOptions.Fill;
        var eloRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { new Label { Text = "Human-style level", FontSize = 14 }, _eloValue }
        };
        Grid.SetColumn(_eloValue, 1);
        _eloSlider.MinimumTrackColor = Accent;
        _eloSlider.ValueChanged += (_, e) =>
        {
            var step = (int)Math.Round(e.NewValue);
            var elo = step == 171 ? 0 : 500 + step * 10;
            _eloValue.Text = elo == 0 ? "Full strength" : $"≈{elo} Elo";
            HumanPlay.SetElo(elo);
        };
        var stack = new VerticalStackLayout
        {
            Spacing = 13,
            Children = { heading, sideRow, flip, new BoxView { HeightRequest = 1, Color = Separator }, eloRow, _eloSlider }
        };
        var card = Card(stack);
        return card;
    }

    private View BuildAnalysisCard()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 8, RowSpacing = 12
        };
        AddStat(grid, "DEPTH", _depth, 0, 0);
        AddStat(grid, "EVALUATION", _evaluation, 1, 0);
        AddStat(grid, "TIME", _elapsed, 2, 0);
        AddStat(grid, "NODES", _nodes, 0, 1);
        AddStat(grid, "NODES / SEC", _nps, 1, 1);
        var stack = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                SectionHeading("Analysis", "waveform.path"), grid,
                new BoxView { HeightRequest = 1, Color = Separator },
                new Label { Text = "PRINCIPAL VARIATION", FontSize = 10, CharacterSpacing = 1.2, TextColor = SecondaryText },
                _pv
            }
        };
        return Card(stack);
    }

    private View BuildMovesCard()
    {
        var header = SectionHeading("Moves", "list.number");
        var moveScroll = new ScrollView { HeightRequest = 156, Content = _moveRows };
        return Card(new VerticalStackLayout { Spacing = 12, Children = { header, moveScroll } });
    }

    private View BuildExportRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8
        };
        var fen = ActionButton("Copy FEN", "doc.on.doc", CopyFenAsync);
        var pgn = ActionButton("Copy PGN", "doc.on.doc", CopyPgnAsync);
        var share = ActionButton("Share PGN", "square.and.arrow.up", SharePgnAsync);
        grid.Add(fen, 0, 0); grid.Add(pgn, 1, 0); grid.Add(share, 2, 0);
        return grid;
    }

    private void BuildBoard()
    {
        for (var row = 0; row < 8; row++) _board.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        for (var col = 0; col < 8; col++) _board.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var viewRow = 0; viewRow < 8; viewRow++)
        for (var viewCol = 0; viewCol < 8; viewCol++)
        {
            var row = viewRow;
            var col = viewCol;
            var button = new Button
            {
                Padding = 0, Margin = 0, CornerRadius = 0, BorderWidth = 0,
                FontSize = 34,
                MinimumHeightRequest = 0, MinimumWidthRequest = 0,
                HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill
            };
            SemanticProperties.SetHint(button, "Double tap to select or move a piece");
            button.Clicked += async (_, _) => await OnSquareTappedAsync(SquareAt(row, col));
            _squares[SquareAt(row, col)] = button;
            _board.Add(button, col, row);
            var piece = new Image
            {
                Aspect = Aspect.AspectFit,
                Margin = 4,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                InputTransparent = true
            };
            _pieces[SquareAt(row, col)] = piece;
            _board.Add(piece, col, row);
        }
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (_engine is not null || _disposed) return;
        try
        {
            await Task.Run(() =>
            {
                var entries = TranspositionTableExtensions.CalculateTranspositionTableSize(16);
                var engine = new ParallelSearcher(entries);
                engine.SetThreads(1);
                var game = GameState.InitialState();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_disposed) { engine.Dispose(); game.Dispose(); return; }
                    _engine = engine;
                    _game = game;
                    UpdateBoard();
                    SetStatus(_humanWhite ? "Your move" : "Sapling to move");
                    UpdateControls();
                    if (!_humanWhite) _ = StartAiMoveAsync();
                });
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Engine unavailable: {ex.Message}");
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _disposed = true;
        _engine?.Stop();
        _engine?.Dispose();
        _game?.Dispose();
        _engine = null;
        _game = null;
    }

    private async Task OnSquareTappedAsync(int square)
    {
        if (_game is null || _thinking || _hinting || _restoring || IsGameOver() || IsEngineTurn()) return;
        var legal = _game.LegalMoves;
        if (_selectedSquare is null)
        {
            SelectIfMovable(square, legal);
            return;
        }
        if (_selectedSquare == square) { ClearSelection(); return; }
        var candidates = legal.Where(m => m.GetFromSquare() == _selectedSquare && m.GetToSquare() == square).ToArray();
        if (candidates.Length == 0)
        {
            SelectIfMovable(square, legal);
            return;
        }
        uint move = candidates[0];
        if (candidates.Length > 1)
        {
            var choice = await DisplayActionSheetAsync("Promote pawn to", "Cancel", null, "Queen", "Rook", "Bishop", "Knight");
            if (choice == "Cancel" || choice is null) return;
            var suffix = choice switch { "Rook" => "r", "Bishop" => "b", "Knight" => "n", _ => "q" };
            move = candidates.First(m => m.ToUciMoveName().EndsWith(suffix, StringComparison.Ordinal));
        }
        await PlayMoveAsync(move, isHuman: true);
    }

    private void SelectIfMovable(int square, List<uint> legal)
    {
        if (_game is null) return;
        var ownColor = _game.Board.WhiteToMove == _humanWhite;
        if (!ownColor) return;
        if (legal.Any(m => m.GetFromSquare() == square))
        {
            _selectedSquare = square;
            _targets = legal.Where(m => m.GetFromSquare() == square).Select(m => (int)m.GetToSquare()).ToHashSet();
            UpdateBoard();
            if (_hintMode) _ = ScoreHintsAsync();
        }
    }

    private async Task PlayMoveAsync(uint move, bool isHuman)
    {
        if (_game is null) return;
        var from = (int)move.GetFromSquare();
        var to = (int)move.GetToSquare();
        var isCastle = move.GetMoveType() == (byte)MoveType.Castle;
        var captured = move.GetCapturedPiece();
        _history.Add(move.ToUciMoveName());
        _moveLabels.Add(move.ToPgnMoveName());
        if (captured != Constants.None)
            _captured.Add((PieceValueType(captured), _game.Board.WhiteToMove));
        _redo.Clear();
        _game.Apply(move);
        ClearSelection(false);
        UpdateBoard();
        await AnimateBoardMoveAsync(from, to, isCastle);
        UpdateMoveList();
        UpdateControls();
        UpdateTerminalStatus();
        if (!_game.GameOver() && isHuman) await StartAiMoveAsync();
    }

    private async Task AnimateBoardMoveAsync(int from, int to, bool isCastle)
    {
#if IOS
        if (UIKit.UIAccessibility.IsReduceMotionEnabled) return;
#endif
        var distance = _board.Width / 8;
        if (distance <= 0) return;
        var renderVersion = _boardRenderVersion;
        var moves = new List<Task> { AnimatePieceAsync(from, to, distance, renderVersion) };
        if (isCastle)
        {
            var rank = from / 8;
            var kingSide = to > from;
            var rookFrom = rank * 8 + (kingSide ? 7 : 0);
            var rookTo = rank * 8 + (kingSide ? 5 : 3);
            moves.Add(AnimatePieceAsync(rookFrom, rookTo, distance, renderVersion));
        }
        await Task.WhenAll(moves);
    }

    private async Task AnimatePieceAsync(int from, int destination, double cellSize, int renderVersion)
    {
        var destinationPiece = _pieces[destination];
        if (!destinationPiece.IsVisible || destinationPiece.Source is null) return;
        var (fromRow, fromCol) = ViewPosition(from);
        var (toRow, toCol) = ViewPosition(destination);
        var deltaX = (toCol - fromCol) * cellSize;
        var deltaY = (toRow - fromRow) * cellSize;
        var imageSize = cellSize * 0.8;
        var movingPiece = new Image
        {
            Source = destinationPiece.Source,
            Aspect = Aspect.AspectFit,
            InputTransparent = true
        };
        AbsoluteLayout.SetLayoutBounds(movingPiece, new Rect(
            fromCol * cellSize + cellSize * 0.1,
            fromRow * cellSize + cellSize * 0.1,
            imageSize,
            imageSize));
        _animationOverlay.Add(movingPiece);
        destinationPiece.IsVisible = false;
        var fileChange = Math.Abs(from % 8 - destination % 8);
        var rankChange = Math.Abs(from / 8 - destination / 8);
        var isKnightMove = (fileChange == 1 && rankChange == 2) || (fileChange == 2 && rankChange == 1);
        try
        {
            if (isKnightMove)
            {
                await movingPiece.TranslateToAsync(0, deltaY, 240, Easing.CubicInOut);
                await movingPiece.TranslateToAsync(deltaX, deltaY, 120, Easing.CubicInOut);
            }
            else
            {
                await movingPiece.TranslateToAsync(deltaX, deltaY, 360, Easing.CubicInOut);
            }
        }
        finally
        {
            movingPiece.CancelAnimations();
            _animationOverlay.Remove(movingPiece);
            if (renderVersion == _boardRenderVersion) destinationPiece.IsVisible = true;
        }
    }

    private (int Row, int Col) ViewPosition(int square)
    {
        for (var row = 0; row < 8; row++)
        for (var col = 0; col < 8; col++)
            if (SquareAt(row, col) == square) return (row, col);
        return (0, 0);
    }

    private async Task StartAiMoveAsync()
    {
        var session = _engine;
        var game = _game;
        if (game is null || session is null || _thinking || IsGameOver()) return;
        _thinking = true;
        _searchButton.Text = "Stop";
        _searchButton.Clicked -= SearchButtonNewGameClicked;
        _searchButton.Clicked += SearchButtonStopClicked;
        SetStatus("Sapling is thinking…");
        UpdateControls();
        _searchClock.Restart();
        _depth.Text = "0"; _evaluation.Text = "–"; _elapsed.Text = "0.0s"; _nodes.Text = "0"; _nps.Text = "0"; _pv.Text = "–";
        var positionVersion = _positionVersion;
        _cancelSearch = false;
        var searchWork = Task.Run(() =>
        {
            Searcher.ResumableResult result = default;
            try
            {
                session.BeginResumableSearch(game);
                var total = Stopwatch.StartNew();
                while (!result.Done && !_cancelSearch && total.ElapsedMilliseconds < 1500 && result.TotalNodes < 4_000_000)
                {
                    result = session.ResumableSlice(40, 12_000, 45);
                    var progress = result;
                    MainThread.BeginInvokeOnMainThread(() => UpdateSearchStats(progress, game.Board.WhiteToMove));
                    if (!result.Done) Thread.Yield();
                }
                if (!result.Done) { session.Stop(); result = session.ResumableSlice(40, 1, 10); }
            }
            finally
            {
                session.EndResumableSearch();
            }

            uint selectedMove = 0;
            if (!_cancelSearch && result.BestMove != 0)
            {
                if (HumanPlay.Enabled)
                {
                    var ranked = session.RankRootMoves(game, HumanPlay.RankDepth);
                    if (ranked.Count > 0)
                    {
                        var board = game.Board;
                        var selection = HumanPlay.SelectMove(_humanRng, ref board, ranked);
                        selectedMove = selection.Move;
                        result = result with
                        {
                            Depth = HumanPlay.RankDepth,
                            Score = ranked[selection.Rank].Score,
                            BestMove = selection.Move,
                            Pv = [selection.Move]
                        };
                    }
                }
                else
                {
                    selectedMove = result.BestMove;
                }
            }
            return (Result: result, SelectedMove: selectedMove);
        });
        _searchTask = searchWork;
        Searcher.ResumableResult final;
        uint selectedMove;
        try
        {
            var completed = await searchWork;
            final = completed.Result;
            selectedMove = completed.SelectedMove;
        }
        catch (Exception ex)
        {
            SetStatus($"Search failed: {ex.Message}");
            final = default;
            selectedMove = 0;
        }
        _searchTask = null;
        _searchClock.Stop();
        if (_disposed || positionVersion != _positionVersion)
        {
            _thinking = false;
            ResetSearchButton();
            return;
        }
        UpdateSearchStats(final, game.Board.WhiteToMove);
        _thinking = false;
        ResetSearchButton();
        if (!_cancelSearch && selectedMove != 0)
            await PlayMoveAsync(selectedMove, isHuman: false);
        else
        {
            SetStatus(IsGameOver() ? TerminalStatus() : "Search stopped");
            UpdateControls();
        }
    }

    private void UpdateSearchStats(Searcher.ResumableResult result, bool whiteToMove)
    {
        if (_disposed || !_thinking) return;
        _depth.Text = result.Depth.ToString(CultureInfo.InvariantCulture);
        _evaluation.Text = FormatEvaluation(result.Score, whiteToMove);
        _elapsed.Text = _searchClock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s";
        _nodes.Text = result.TotalNodes.ToString("N0", CultureInfo.CurrentCulture);
        _nps.Text = (_searchClock.Elapsed.TotalSeconds > 0 ? result.TotalNodes / _searchClock.Elapsed.TotalSeconds : 0).ToString("N0", CultureInfo.CurrentCulture);
        _pv.Text = result.Pv.Count == 0 ? "–" : string.Join("  ", result.Pv.Select(m => m.ToUciMoveName()));
    }

    private async Task NewGameAsync()
    {
        _positionVersion++;
        await StopSearchAsync();
        if (_game is null) return;
        _game.ResetToFen(InitialFen);
        _history.Clear(); _moveLabels.Clear(); _captured.Clear(); _redo.Clear();
        ClearSelection(false);
        ResetStats();
        UpdateBoard(); UpdateMoveList(); UpdateControls();
        SetStatus(_humanWhite ? "Your move" : "Sapling to move");
        if (!_humanWhite) await StartAiMoveAsync();
    }

    private async Task UndoAsync()
    {
        if (_game is null || _history.Count == 0 || _restoring) return;
        if (_thinking) await StopSearchAsync();
        var count = _history.Count > 1 && !IsEngineTurn() ? 2 : 1;
        count = Math.Min(count, _history.Count);
        var removed = _history.TakeLast(count).ToArray();
        _redo.Push(removed);
        _history.RemoveRange(_history.Count - count, count);
        _moveLabels.RemoveRange(_moveLabels.Count - count, count);
        await RestoreHistoryAsync();
    }

    private async Task RedoAsync()
    {
        if (_game is null || _redo.Count == 0 || _restoring) return;
        _history.AddRange(_redo.Pop());
        await RestoreHistoryAsync();
    }

    private async Task RestoreHistoryAsync()
    {
        var game = _game;
        if (game is null) return;
        _restoring = true;
        SetStatus("Restoring position…");
        game.ResetToFen(InitialFen);
        _captured.Clear();
        _moveLabels.Clear();
        foreach (var uci in _history)
        {
            var move = game.LegalMoves.FirstOrDefault(m => m.ToUciMoveName() == uci);
            if (move == 0) break;
            _moveLabels.Add(move.ToPgnMoveName());
            var captured = move.GetCapturedPiece();
            if (captured != Constants.None) _captured.Add((PieceValueType(captured), game.Board.WhiteToMove));
            game.Apply(move);
        }
        _restoring = false;
        ClearSelection(false);
        UpdateBoard(); UpdateMoveList(); UpdateControls(); UpdateTerminalStatus();
        if (!game.GameOver() && IsEngineTurn()) await StartAiMoveAsync();
    }

    private async Task StopSearchAsync()
    {
        if (!_thinking || _engine is null) return;
        _cancelSearch = true;
        _engine.Stop();
        var pending = _searchTask;
        if (pending is not null)
        {
            try { await pending; }
            catch { }
            await Task.Delay(30);
        }
    }

    private async Task ChangeSideAsync(bool white)
    {
        _humanWhite = white;
        _whiteButton.BackgroundColor = white ? Accent : Surface2;
        _whiteButton.TextColor = PrimaryText;
        _blackButton.BackgroundColor = white ? Surface2 : Accent;
        _blackButton.TextColor = PrimaryText;
        await NewGameAsync();
    }

    private async Task ToggleHintsAsync()
    {
        _hintMode = !_hintMode;
        _hintButton.Text = _hintMode ? "Hints On" : "Hints Off";
        _hintButton.BackgroundColor = _hintMode ? Accent : Surface2;
        _hintButton.TextColor = PrimaryText;
        _hintScores.Clear();
        UpdateBoard();
        if (_hintMode && _selectedSquare.HasValue) await ScoreHintsAsync();
    }

    private async Task ScoreHintsAsync()
    {
        var game = _game;
        var engine = _engine;
        if (game is null || engine is null || !_selectedSquare.HasValue || _thinking || _hinting || _restoring) return;
        var legal = game.LegalMoves.Where(m => m.GetFromSquare() == _selectedSquare.Value).ToArray();
        if (legal.Length == 0) return;
        _hinting = true;
        UpdateControls();
        SetStatus("Scoring candidate moves…");
        try
        {
            var scores = await Task.Run(() => engine.RankRootMoves(game, 4));
            _hintScores = scores.ToDictionary(s => s.Move.ToUciMoveName(), s => s.Score, StringComparer.Ordinal);
            UpdateBoard();
        }
        catch (Exception ex) { SetStatus($"Hint search failed: {ex.Message}"); }
        finally
        {
            _hinting = false;
            UpdateControls();
            if (!_thinking) SetStatus(game.Board.InCheck ? "Your move · check" : "Your move");
        }
    }

    private async Task CopyFenAsync()
    {
        if (_game is null) return;
        await Clipboard.Default.SetTextAsync(_game.Board.ToFen());
        SetStatus("FEN copied");
    }

    private async Task CopyPgnAsync()
    {
        if (_game is null) return;
        await Clipboard.Default.SetTextAsync(_game.ToPgn());
        SetStatus("PGN copied");
    }

    private async Task SharePgnAsync()
    {
        if (_game is null) return;
        await Share.Default.RequestAsync(new ShareTextRequest { Title = "Sapling game", Text = _game.ToPgn() });
    }

    private void UpdateBoard()
    {
        _boardRenderVersion++;
        foreach (var movingPiece in _animationOverlay.Children.OfType<View>()) movingPiece.CancelAnimations();
        _animationOverlay.Children.Clear();
        var fen = _game is null ? InitialFen : _game.Board.ToFen();
        var placement = fen.Split(' ')[0].Split('/');
        var pieces = new char[64];
        for (var fenRow = 0; fenRow < 8; fenRow++)
        {
            var file = 0;
            foreach (var c in placement[fenRow])
            {
                if (char.IsDigit(c)) file += c - '0';
                else pieces[(7 - fenRow) * 8 + file++] = c;
            }
        }
        var legal = _game?.LegalMoves ?? [];
        var lastMove = _history.Count == 0 ? null : _history[^1];
        var checkedSquare = _game is { } game && game.Board.InCheck
            ? (game.Board.WhiteToMove ? game.Board.WhiteKingSquare : game.Board.BlackKingSquare)
            : -1;
        for (var row = 0; row < 8; row++)
        for (var col = 0; col < 8; col++)
        {
            var square = SquareAt(row, col);
            var button = _squares[square];
            var piece = pieces[square];
            button.Text = "";
            button.TextColor = Color.FromArgb("#1C1C1E");
            button.FontSize = Math.Max(10, Math.Min(18, Width / 24));
            var pieceImage = _pieces[square];
            pieceImage.Source = PieceImage(piece);
            pieceImage.IsVisible = piece != '\0';
            var light = ((square % 8) + (square / 8)) % 2 == 1;
            var color = light ? LightSquare : DarkSquare;
            if (_targets.Contains(square)) color = Color.FromArgb("#397548");
            if (_selectedSquare == square) color = Color.FromArgb("#8C811A");
            if (lastMove is { Length: >= 4 } && (SquareFrom(lastMove[..2]) == square || SquareFrom(lastMove.Substring(2, 2)) == square))
                color = light ? Color.FromArgb("#B6C8A5") : Color.FromArgb("#789273");
            if (checkedSquare == square) color = Color.FromArgb("#A93D3A");
            button.BackgroundColor = color;
            var label = SquareName(square);
            var candidate = legal.FirstOrDefault(m => m.GetToSquare() == square && _selectedSquare.HasValue && m.GetFromSquare() == _selectedSquare.Value);
            button.Text = piece == '\0' && _targets.Contains(square) ? "·" : "";
            if (_hintMode && candidate != 0 && _hintScores.TryGetValue(candidate.ToUciMoveName(), out var hintScore))
                button.Text = piece == '\0' ? FormatHintScore(hintScore) : "";
            SemanticProperties.SetDescription(button, $"{label}, {(piece == '\0' ? "empty" : PieceName(piece))}");
            SemanticProperties.SetHint(button, row == 7 ? $"File {label[0]}. Double tap to select or move." : "Double tap to select or move.");
        }
        _moveCount.Text = $"{_history.Count} {(_history.Count == 1 ? "move" : "moves")}";
        _material.Text = MaterialSummary();
        UpdateControls();
    }

    private void UpdateMoveList()
    {
        _moveRows.Children.Clear();
        for (var index = 0; index < _history.Count; index += 2)
        {
            var white = _moveLabels[index];
            var black = index + 1 < _moveLabels.Count ? _moveLabels[index + 1] : "";
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(new GridLength(40)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, Padding = new Thickness(2, 3) };
            row.Add(new Label { Text = $"{index / 2 + 1}.", TextColor = SecondaryText, FontSize = 13 }, 0, 0);
            row.Add(new Label { Text = white, FontSize = 14 }, 1, 0);
            row.Add(new Label { Text = black, FontSize = 14 }, 2, 0);
            _moveRows.Children.Add(row);
        }
    }

    private string MaterialSummary()
    {
        var balance = _captured.Sum(c => c.WhiteCaptured ? c.Value : -c.Value);
        return balance == 0 ? "Material even" : balance > 0 ? $"White +{balance}" : $"Black +{Math.Abs(balance)}";
    }

    private void UpdateControls()
    {
        var canEdit = _game is not null && !_thinking && !_hinting && !_restoring;
        _undoButton.IsEnabled = canEdit && _history.Count > 0;
        _redoButton.IsEnabled = canEdit && _redo.Count > 0;
        _hintButton.IsEnabled = canEdit && !IsGameOver();
        _whiteButton.IsEnabled = canEdit;
        _blackButton.IsEnabled = canEdit;
        _eloSlider.IsEnabled = canEdit;
        _searchButton.IsEnabled = _game is not null && !_restoring && !_hinting;
    }

    private void UpdateTerminalStatus()
    {
        if (_game?.GameOver() == true) SetStatus(TerminalStatus());
        else SetStatus(_game?.Board.InCheck == true ? (IsEngineTurn() ? "Sapling to move · check" : "Your move · check") : IsEngineTurn() ? "Sapling to move" : "Your move");
    }

    private string TerminalStatus()
    {
        if (_game is null) return "Game over";
        if (_game.LegalMoves.Count == 0) return _game.Board.WhiteToMove ? "Checkmate · Black wins" : "Checkmate · White wins";
        return "Draw";
    }

    private bool IsGameOver() => _game?.GameOver() ?? false;
    private bool IsEngineTurn() => _game is not null && _game.Board.WhiteToMove != _humanWhite;

    private void ResetSearchButton()
    {
        _searchButton.Clicked -= SearchButtonStopClicked;
        _searchButton.Clicked -= SearchButtonNewGameClicked;
        _searchButton.Text = "New game";
        _searchButton.Clicked += SearchButtonNewGameClicked;
        UpdateControls();
    }

    private async void SearchButtonStopClicked(object? sender, EventArgs e)
    {
        if (_engine is null) return;
        _engine.Stop();
        if (_searchTask is not null) { try { await _searchTask; } catch { } }
    }
    private async void SearchButtonNewGameClicked(object? sender, EventArgs e) => await NewGameAsync();

    private void ResetStats()
    {
        _depth.Text = _evaluation.Text = _elapsed.Text = _nodes.Text = _nps.Text = "–";
        _pv.Text = "–";
    }

    private void ClearSelection(bool refresh = true)
    {
        _selectedSquare = null;
        _targets.Clear();
        _hintScores.Clear();
        if (refresh) UpdateBoard();
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        SemanticScreenReader.Announce(text);
    }

    private void ApplyTheme()
    {
        BackgroundColor = Canvas;
        _root.BackgroundColor = BackgroundColor;
        _status.TextColor = PrimaryText;
        if (_game is not null) UpdateBoard();
    }

    private View Card(View content)
    {
        var card = new Border
        {
            Padding = 12,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Content = content
        };
        card.BackgroundColor = Surface;
        return card;
    }

    private Label SectionHeading(string text, string symbol)
    {
        var label = new Label { Text = text, FontSize = 17, FontAttributes = FontAttributes.Bold };
        SemanticProperties.SetHeadingLevel(label, SemanticHeadingLevel.Level2);
        return label;
    }

    private Button ActionButton(string text, string symbol, Func<Task> action, bool primary = false)
    {
        _ = symbol;
        var button = new Button
        {
            Text = text, FontSize = 12, Padding = new Thickness(5, 8), CornerRadius = 12,
            BackgroundColor = primary ? Accent : Surface2,
            TextColor = primary ? Colors.White : PrimaryText,
            HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation, MinimumHeightRequest = 46
        };
        button.Clicked += async (_, _) => await action();
        return button;
    }

    private static string? PieceImage(char piece) => piece switch
    {
        'K' => "white_king.svg", 'Q' => "white_queen.svg", 'R' => "white_rook.svg",
        'B' => "white_bishop.svg", 'N' => "white_knight.svg", 'P' => "white_pawn.svg",
        'k' => "black_king.svg", 'q' => "black_queen.svg", 'r' => "black_rook.svg",
        'b' => "black_bishop.svg", 'n' => "black_knight.svg", 'p' => "black_pawn.svg",
        _ => null
    };

    private static void AddStat(Grid grid, string caption, Label value, int column, int row)
    {
        var stack = new VerticalStackLayout { Spacing = 3 };
        stack.Children.Add(new Label { Text = caption, FontSize = 9, CharacterSpacing = 1, TextColor = SecondaryText });
        value.FontSize = 16;
        value.FontAttributes = FontAttributes.Bold;
        stack.Children.Add(value);
        grid.Add(stack, column, row);
    }

    private int SquareAt(int viewRow, int viewCol) => _flipped
        ? viewRow * 8 + (7 - viewCol)
        : (7 - viewRow) * 8 + viewCol;

    private static string SquareName(int square) => $"{(char)('a' + square % 8)}{square / 8 + 1}";
    private static int SquareFrom(string square) => (square[1] - '1') * 8 + square[0] - 'a';

    private static string PieceName(char piece) => char.ToLowerInvariant(piece) switch
    {
        'k' => "king", 'q' => "queen", 'r' => "rook", 'b' => "bishop", 'n' => "knight", 'p' => "pawn", _ => "piece"
    };

    private static int PieceValueType(byte piece) => (piece & 0x0F) switch
    {
        Constants.WhitePawn or Constants.BlackPawn => 1,
        Constants.WhiteKnight or Constants.BlackKnight => 3,
        Constants.WhiteBishop or Constants.BlackBishop => 3,
        Constants.WhiteRook or Constants.BlackRook => 5,
        Constants.WhiteQueen or Constants.BlackQueen => 9,
        _ => 0
    };

    private static string FormatEvaluation(int score, bool whiteToMove)
    {
        var signed = whiteToMove ? score : -score;
        if (MoveScoring.IsMateScore(signed))
        {
            var mate = MoveScoring.GetMateDistance(signed);
            return signed > 0 ? $"M{mate}" : $"−M{mate}";
        }
        return (signed / 100.0).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatHintScore(int score)
    {
        if (MoveScoring.IsMateScore(score))
        {
            var mate = MoveScoring.GetMateDistance(score);
            return score > 0 ? $"M{mate}" : $"−M{mate}";
        }
        return (score / 100.0).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
    }
}
