using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

public enum SnakeGameStatus { Ready, Running, Paused, GameOver, Won }
public readonly record struct SnakeCell(int X, int Y);

/// <summary>确定且有界的原生游戏规则，不执行插件代码，也不访问外部资源。</summary>
public sealed class SnakeGameEngine
{
    private readonly SnakeGameOptions _options;
    private readonly Random _random;
    private readonly List<SnakeCell> _cells = [];
    private SnakeCell _direction = new(1, 0), _pending = new(1, 0);
    public IReadOnlyList<SnakeCell> Cells => _cells;
    public SnakeCell Food { get; private set; }
    public SnakeGameStatus Status { get; private set; }
    public int Score { get; private set; }
    public SnakeGameEngine(SnakeGameOptions options, int? seed = null)
    {
        if (options.Columns is < 12 or > 40 || options.Rows is < 10 or > 30 || options.TickMilliseconds is < 80 or > 600)
            throw new ArgumentOutOfRangeException(nameof(options));
        _options = options; _random = seed is { } value ? new Random(value) : new Random(); Reset();
    }
    public void Reset()
    {
        var x = _options.Columns / 2; var y = _options.Rows / 2;
        _cells.Clear(); _cells.AddRange([new(x, y), new(x - 1, y), new(x - 2, y)]);
        _direction = _pending = new(1, 0); Score = 0; Status = SnakeGameStatus.Ready; PlaceFood();
    }
    public void Start() { if (Status is SnakeGameStatus.Ready or SnakeGameStatus.Paused) Status = SnakeGameStatus.Running; }
    public void Pause() { if (Status == SnakeGameStatus.Running) Status = SnakeGameStatus.Paused; }
    public void Turn(int x, int y)
    {
        if (Math.Abs(x) + Math.Abs(y) != 1 || x == -_direction.X && y == -_direction.Y) return;
        _pending = new(x, y);
    }
    public void Advance()
    {
        if (Status != SnakeGameStatus.Running) return;
        _direction = _pending;
        var next = new SnakeCell(_cells[0].X + _direction.X, _cells[0].Y + _direction.Y);
        if (_options.WrapWalls) next = new((next.X + _options.Columns) % _options.Columns, (next.Y + _options.Rows) % _options.Rows);
        if (next.X < 0 || next.Y < 0 || next.X >= _options.Columns || next.Y >= _options.Rows)
        { Status = SnakeGameStatus.GameOver; return; }
        var grows = next == Food;
        if (_cells.Take(_cells.Count - (grows ? 0 : 1)).Contains(next)) { Status = SnakeGameStatus.GameOver; return; }
        _cells.Insert(0, next);
        if (grows) { Score += 10; PlaceFood(); }
        else _cells.RemoveAt(_cells.Count - 1);
    }
    private void PlaceFood()
    {
        var free = new List<SnakeCell>();
        var occupied = _cells.ToHashSet();
        for (var y = 0; y < _options.Rows; y++) for (var x = 0; x < _options.Columns; x++)
            if (!occupied.Contains(new SnakeCell(x, y))) free.Add(new(x, y));
        if (free.Count == 0) { Status = SnakeGameStatus.Won; return; }
        Food = free[_random.Next(free.Count)];
    }
}

public sealed class SnakeGameControl : UserControl, IDisposable, IPluginKeyboardScope
{
    private readonly DispatcherTimer _timer;
    private readonly SnakeBoard _board;
    private readonly TextBlock _status;
    private readonly Button _toggle;
    private bool _disposed;
    public SnakeGameEngine Game { get; }
    public bool IsDisposed => _disposed;
    public bool IsTimerRunning => _timer.IsEnabled;
    public Control KeyboardTarget => _board;
    public SnakeGameControl(SnakeGameOptions options)
    {
        Game = new(options);
        _timer = new() { Interval = TimeSpan.FromMilliseconds(options.TickMilliseconds) };
        _board = new(Game, options) { Height = 330, MinHeight = 240, Focusable = true, ClipToBounds = true };
        AutomationProperties.SetName(_board, L10n.T("Common.SnakeBoardArrowsOrWASDToTurnSpaceTo"));
        _status = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        _toggle = new Button { Name = "SnakeToggle", Content = L10n.T("Common.StartGame"), MinWidth = 110, MinHeight = 42 };
        _toggle.Click += (_, _) => { Toggle(); _board.Focus(); };
        var restart = new Button { Name = "SnakeRestart", Content = L10n.T("Common.Restart"), MinWidth = 110, MinHeight = 42 };
        restart.Click += (_, _) => { Restart(); _board.Focus(); };
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[] { _toggle, restart, _status }) { control.Margin = new Thickness(0, 0, 12, 8); controls.Children.Add(control); }
        var frame = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), BorderBrush = Brushes.SlateGray, ClipToBounds = true, Child = _board };
        _board.GotFocus += (_, _) => frame.BorderBrush = new SolidColorBrush(Color.Parse(options.Snake));
        _board.LostFocus += (_, _) => frame.BorderBrush = Brushes.SlateGray;
        Content = new StackPanel { Spacing = 12, Children =
        {
            controls, frame,
            new TextBlock { Text = L10n.T("Common.ArrowsWASDToTurnSpaceToPauseResumeR"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .75 }
        } };
        _timer.Tick += TimerTick;
        AddHandler(KeyDownEvent, GameKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
        _board.PointerPressed += (_, e) => { _board.Focus(); e.Handled = true; };
        Update();
    }
    private void GameKeyDown(object? sender, KeyEventArgs e)
    {
        if (_disposed) return;
        switch (e.Key)
        {
            case Key.Left: case Key.A: Game.Turn(-1, 0); break;
            case Key.Right: case Key.D: Game.Turn(1, 0); break;
            case Key.Up: case Key.W: Game.Turn(0, -1); break;
            case Key.Down: case Key.S: Game.Turn(0, 1); break;
            case Key.Space: Toggle(); e.Handled = true; return;
            case Key.R: Restart(); e.Handled = true; return;
            default: return;
        }
        e.Handled = true;
    }
    public void Toggle()
    {
        if (_disposed) return;
        if (Game.Status == SnakeGameStatus.Running) Game.Pause();
        else if (Game.Status is SnakeGameStatus.GameOver or SnakeGameStatus.Won) { Game.Reset(); Game.Start(); }
        else Game.Start();
        SyncTimer(); Update();
    }
    public void Restart()
    {
        if (_disposed) return;
        Game.Reset(); Game.Start(); SyncTimer(); Update();
    }
    private void TimerTick(object? sender, EventArgs e) { Game.Advance(); SyncTimer(); Update(); }
    private void SyncTimer() { if (Game.Status == SnakeGameStatus.Running) _timer.Start(); else _timer.Stop(); }
    private void Update()
    {
        var label = Game.Status switch { SnakeGameStatus.Ready => L10n.T("Common.Ready"), SnakeGameStatus.Running => L10n.T("Common.Playing"), SnakeGameStatus.Paused => L10n.T("Common.Paused"), SnakeGameStatus.Won => L10n.T("Common.BoardCompleted"), _ => L10n.T("Common.GameOverRestartToPlayAgain") };
        _status.Text = L10n.Format("Common.Score", Game.Score, L10n.T(label));
        _toggle.Content = L10n.T(Game.Status == SnakeGameStatus.Running ? L10n.T("Common.PauseGame") : Game.Status == SnakeGameStatus.Paused ? L10n.T("Common.ResumeGame") : L10n.T("Common.StartGame"));
        _board.InvalidateVisual();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Dispose(); base.OnDetachedFromVisualTree(e); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _timer.Stop(); _timer.Tick -= TimerTick; Game.Pause(); IsEnabled = false;
    }
    private sealed class SnakeBoard(SnakeGameEngine game, SnakeGameOptions options) : Control
    {
        private readonly IBrush _background = new SolidColorBrush(Color.Parse(options.Background));
        private readonly IBrush _snake = new SolidColorBrush(Color.Parse(options.Snake));
        private readonly IBrush _food = new SolidColorBrush(Color.Parse(options.Food));
        public override void Render(DrawingContext context)
        {
            base.Render(context); context.DrawRectangle(_background, null, new Rect(Bounds.Size));
            var cell = Math.Min((Bounds.Width - 24) / options.Columns, (Bounds.Height - 24) / options.Rows);
            if (cell <= 0) return;
            var left = (Bounds.Width - cell * options.Columns) / 2; var top = (Bounds.Height - cell * options.Rows) / 2;
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), 1);
            for (var x = 0; x <= options.Columns; x++) context.DrawLine(grid, new(left + x * cell, top), new(left + x * cell, top + options.Rows * cell));
            for (var y = 0; y <= options.Rows; y++) context.DrawLine(grid, new(left, top + y * cell), new(left + options.Columns * cell, top + y * cell));
            foreach (var item in game.Cells) DrawCell(item, _snake);
            if (game.Status != SnakeGameStatus.Won) DrawCell(game.Food, _food);
            void DrawCell(SnakeCell position, IBrush brush) => context.DrawRectangle(brush, null,
                new Rect(left + position.X * cell + 1.5, top + position.Y * cell + 1.5, Math.Max(1, cell - 3), Math.Max(1, cell - 3)), 3, 3);
        }
    }
}
