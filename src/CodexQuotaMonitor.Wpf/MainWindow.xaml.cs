using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexQuotaMonitor.Wpf;

public partial class MainWindow : Window
{
    private static readonly int[] MouseVirtualKeys = [0x01, 0x02, 0x04];

    private readonly AppPaths _paths;
    private readonly SimpleLogger _logger;
    private readonly QuotaReader _quotaReader;
    private readonly DispatcherTimer _tickTimer = new();
    private readonly DispatcherTimer _topmostTimer = new();
    private readonly DispatcherTimer _placementTimer = new();
    private readonly DispatcherTimer _menuDismissTimer = new();
    private readonly MetricGaugeBlock _quota5h;
    private readonly MetricGaugeBlock _quotaWeek;
    private readonly Border _separator;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly List<Forms.ToolStripMenuItem> _quotaIntervalItems = new();
    private Forms.ToolStripMenuItem? _autostartItem;
    private AppSettings _settings;
    private IntPtr _hwnd;
    private QuotaSnapshot? _lastQuota;
    private string? _quotaLastError;
    private DateTimeOffset? _quotaLastSuccessAt;
    private DateTimeOffset _nextQuotaAt;
    private bool _quotaInFlight;
    private bool _quotaPendingRefresh;
    private bool _menuVisible;
    private bool _mouseButtonWasDown;
    private bool _isExiting;

    public MainWindow(AppPaths paths, CliOptions options, AppSettings settings, SimpleLogger logger)
    {
        _paths = paths;
        _settings = settings;
        _logger = logger;
        _quotaReader = new QuotaReader(paths.ResolveCodexHome(options.CodexHome), options.CodexExe, logger);

        InitializeComponent();
        Width = Constants.DefaultWidth;
        Height = Constants.DefaultHeight;

        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _quota5h = AddGaugeBlock("5H", 0);
        _quotaWeek = AddGaugeBlock("WK", 1);
        _separator = AddSeparator(0);

        BuildMenu();
        ConfigureTimers();
        RefreshNow();
    }

    private MetricGaugeBlock AddGaugeBlock(string title, int column)
    {
        var block = new MetricGaugeBlock(title, _settings)
        {
            Margin = new Thickness(column == 0 ? 0 : 3, 0, 2, 0)
        };
        Grid.SetColumn(block, column);
        RootGrid.Children.Add(block);
        return block;
    }

    // The separator doubles as the reading-state indicator: its colour reports connectivity.
    private Border AddSeparator(int column)
    {
        var separator = new Border
        {
            Width = 2,
            Margin = new Thickness(0, 5, 0, 5),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Formatting.ColorFromHex("#8794A2")),
            IsHitTestVisible = false
        };
        Grid.SetColumn(separator, column);
        RootGrid.Children.Add(separator);
        return separator;
    }

    private void ConfigureTimers()
    {
        _tickTimer.Interval = TimeSpan.FromSeconds(1);
        _tickTimer.Tick += (_, _) => Tick();
        _tickTimer.Start();

        _topmostTimer.Interval = TimeSpan.FromMilliseconds(500);
        _topmostTimer.Tick += (_, _) => ForceTopmost();
        _topmostTimer.Start();

        _placementTimer.Interval = TimeSpan.FromSeconds(1);
        _placementTimer.Tick += (_, _) => SnapToTaskbar();
        _placementTimer.Start();

        _menuDismissTimer.Interval = TimeSpan.FromMilliseconds(15);
        _menuDismissTimer.Tick += (_, _) => DismissMenuAfterOutsideClick();
    }

    private void BuildMenu()
    {
        _menu.Opening += (_, _) =>
        {
            _menuVisible = true;
            UpdateMenuChecks();
        };
        _menu.Opened += (_, _) =>
        {
            ResetMouseClickState();
            _menuDismissTimer.Start();
        };
        _menu.Closed += (_, _) =>
        {
            _menuDismissTimer.Stop();
            _menuVisible = false;
            ForceTopmost();
        };
        _menu.AutoClose = true;
        _menu.Items.Add("立即刷新", null, (_, _) => RefreshNow());
        _menu.Items.Add("重新贴靠到托盘左侧", null, (_, _) => SnapToTaskbar());
        _autostartItem = new Forms.ToolStripMenuItem("开机自动启动") { CheckOnClick = false };
        _autostartItem.Click += (_, _) => ToggleAutostart();
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());

        var quotaMenu = new Forms.ToolStripMenuItem("刷新间隔");
        foreach (var (label, seconds) in new[] { ("1 分钟", 60), ("3 分钟", 180), ("5 分钟", 300), ("10 分钟", 600), ("15 分钟", 900) })
        {
            var item = new Forms.ToolStripMenuItem(label) { Tag = seconds, CheckOnClick = false };
            item.Click += (_, _) => SetQuotaInterval(seconds);
            quotaMenu.DropDownItems.Add(item);
            _quotaIntervalItems.Add(item);
        }
        _menu.Items.Add(quotaMenu);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => RequestExit());
        UpdateMenuChecks();
    }

    private void ToggleAutostart()
    {
        if (StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled(), _logger))
        {
            UpdateMenuChecks();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(SnapToTaskbar, DispatcherPriority.ApplicationIdle);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ApplyOverlayStyles(_hwnd);
        NativeMethods.AttachToTaskbar(_hwnd);
        if (!NativeMethods.EnableFrostedBackdrop(_hwnd))
        {
            _logger.Warning("native frosted backdrop is unavailable; using translucent WPF fallback");
        }
        SnapToTaskbar();
        ForceTopmost();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _tickTimer.Stop();
        _topmostTimer.Stop();
        _placementTimer.Stop();
        _menuDismissTimer.Stop();
        _menu.Dispose();

        if (!_isExiting)
        {
            _isExiting = true;
            Dispatcher.BeginInvoke(() => System.Windows.Application.Current.Shutdown(), DispatcherPriority.ApplicationIdle);
        }
    }

    private void RequestExit()
    {
        _isExiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void OnMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ForceTopmost();
        _menu.Show(Forms.Control.MousePosition);
    }

    private void ResetMouseClickState()
    {
        ReadMouseState(out _, out _mouseButtonWasDown);
    }

    private void DismissMenuAfterOutsideClick()
    {
        if (!_menu.Visible)
        {
            return;
        }

        ReadMouseState(out var clickedSinceLastTick, out var anyButtonDown);
        var newPress = clickedSinceLastTick || (anyButtonDown && !_mouseButtonWasDown);
        _mouseButtonWasDown = anyButtonDown;
        if (!newPress)
        {
            return;
        }

        var cursor = Forms.Control.MousePosition;
        if (!IsPointInsideOpenMenu(_menu, cursor))
        {
            _menu.Close(Forms.ToolStripDropDownCloseReason.AppClicked);
        }
    }

    private static void ReadMouseState(out bool clickedSinceLastTick, out bool anyButtonDown)
    {
        clickedSinceLastTick = false;
        anyButtonDown = false;
        foreach (var virtualKey in MouseVirtualKeys)
        {
            var state = NativeMethods.GetAsyncKeyState(virtualKey);
            clickedSinceLastTick |= (state & 0x0001) != 0;
            anyButtonDown |= (state & 0x8000) != 0;
        }
    }

    private static bool IsPointInsideOpenMenu(Forms.ToolStripDropDown menu, System.Drawing.Point point)
    {
        if (menu.Visible && menu.Bounds.Contains(point))
        {
            return true;
        }

        foreach (Forms.ToolStripItem item in menu.Items)
        {
            if (item is Forms.ToolStripDropDownItem dropDownItem &&
                dropDownItem.HasDropDownItems &&
                dropDownItem.DropDown.Visible &&
                IsPointInsideOpenMenu(dropDownItem.DropDown, point))
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshNow()
    {
        _nextQuotaAt = DateTimeOffset.Now.AddSeconds(_settings.QuotaInterval);
        StartQuotaRefresh();
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        if (now >= _nextQuotaAt)
        {
            _nextQuotaAt = now.AddSeconds(_settings.QuotaInterval);
            StartQuotaRefresh();
        }
        Render();
    }

    private void StartQuotaRefresh(bool pendingIfBusy = true)
    {
        if (_quotaInFlight)
        {
            if (pendingIfBusy)
            {
                _quotaPendingRefresh = true;
            }
            UpdateTitle();
            RenderStatusLine();
            return;
        }

        _quotaInFlight = true;
        UpdateTitle();
        RenderStatusLine();
        _ = Task.Run(async () => await _quotaReader.ReadAsync())
            .ContinueWith(task => Dispatcher.Invoke(() => HandleQuotaResult(task)));
    }

    private void HandleQuotaResult(Task<QuotaSnapshot> task)
    {
        _quotaInFlight = false;
        var value = task.IsCompletedSuccessfully
            ? task.Result
            : new QuotaSnapshot(Error: task.Exception?.GetBaseException().Message ?? "quota worker failed", UpdatedAt: DateTimeOffset.Now);
        if (value.Error is not null)
        {
            _quotaLastError = value.Error;
            if (_lastQuota is null || _lastQuota.Error is not null)
            {
                _lastQuota = value;
            }
        }
        else
        {
            _lastQuota = value;
            _quotaLastError = null;
            _quotaLastSuccessAt = value.UpdatedAt ?? DateTimeOffset.Now;
        }

        if (_quotaPendingRefresh)
        {
            _quotaPendingRefresh = false;
            _nextQuotaAt = DateTimeOffset.Now.AddSeconds(_settings.QuotaInterval);
            StartQuotaRefresh(false);
        }
        Render();
    }

    private void Render()
    {
        RenderQuota();
        RenderStatusLine();
        UpdateTitle();
    }

    private void RenderQuota()
    {
        if (_lastQuota is null)
        {
            _quota5h.SetMetric(null, "wait", _settings);
            _quotaWeek.SetMetric(null, "wait", _settings);
            return;
        }
        if (_lastQuota.Error is not null && _lastQuota.FiveHour is null && _lastQuota.Weekly is null)
        {
            _quota5h.SetMetric(null, "unavail", _settings);
            _quotaWeek.SetMetric(null, "refresh", _settings);
            return;
        }

        var fiveHour = _lastQuota.FiveHour;
        var weekly = _lastQuota.Weekly;
        _quota5h.SetMetric(fiveHour?.RemainingPercent, fiveHour is null ? "inactive" : Formatting.Countdown(fiveHour.ResetsAt), _settings);
        _quotaWeek.SetMetric(weekly?.RemainingPercent, weekly is null ? "unavail" : Formatting.Countdown(weekly.ResetsAt), _settings);
    }

    private void RenderStatusLine()
    {
        var accent = _quotaInFlight || IsStale(_quotaLastSuccessAt, _settings.QuotaInterval)
            ? "#FFC857"
            : _lastQuota is null
                ? "#8794A2"
                : _quotaLastError is not null && _quotaLastSuccessAt.HasValue
                    ? "#FFC857"
                    : _lastQuota.Error is not null && !_quotaLastSuccessAt.HasValue
                        ? "#FF6B81"
                        : "#2DD4A8";
        _separator.Background = new SolidColorBrush(Formatting.ColorFromHex(accent));
    }

    private void UpdateTitle()
    {
        var stamp = _quotaLastSuccessAt.HasValue ? _quotaLastSuccessAt.Value.ToString("HH:mm") : "--:--";
        var parts = new List<string>();
        if (_quotaInFlight) parts.Add("quota reading");
        if (_quotaPendingRefresh) parts.Add("quota pending");
        if (IsStale(_quotaLastSuccessAt, _settings.QuotaInterval)) parts.Add("quota stale");
        if (_quotaLastError is not null) parts.Add("quota last error");
        if (_lastQuota?.Error is null && _lastQuota?.FiveHour is null) parts.Add("5h unavailable");
        if (parts.Count == 0) parts.Add("quota ok");

        Title = $"{Constants.WindowTitlePrefix} | updated {stamp} | quota {_settings.QuotaInterval}s | {string.Join(" | ", parts)}";
    }

    private static bool IsStale(DateTimeOffset? timestamp, int intervalSeconds)
    {
        if (!timestamp.HasValue)
        {
            return false;
        }
        var age = DateTimeOffset.Now - timestamp.Value;
        return age.TotalSeconds > Math.Max(intervalSeconds * 2.0, intervalSeconds + 5.0);
    }

    private void SetQuotaInterval(int seconds)
    {
        _settings.QuotaInterval = seconds;
        _settings.Normalize();
        SettingsStore.Save(_paths.SettingsPath, _settings, _logger);
        _nextQuotaAt = DateTimeOffset.Now.AddSeconds(_settings.QuotaInterval);
        UpdateMenuChecks();
        UpdateTitle();
    }

    private void UpdateMenuChecks()
    {
        foreach (var item in _quotaIntervalItems)
        {
            item.Checked = item.Tag is int seconds && seconds == _settings.QuotaInterval;
        }

        if (_autostartItem is not null)
        {
            _autostartItem.Checked = StartupRegistration.IsEnabled();
        }
    }

    private void ForceTopmost()
    {
        if (_menuVisible)
        {
            return;
        }

        Topmost = true;
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.ApplyOverlayStyles(_hwnd);
            NativeMethods.SetTopmostNoActivate(_hwnd);
        }
    }

    private void SnapToTaskbar()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.AttachToTaskbar(_hwnd);
        }

        var placement = ResolveTaskbarPlacement(Constants.DefaultWidth);
        if (_hwnd == IntPtr.Zero)
        {
            Left = placement.X;
            Top = placement.Y;
            return;
        }

        if (NativeMethods.GetWindowRect(_hwnd, out var current) &&
            current.Left == placement.X && current.Top == placement.Y &&
            current.Width == placement.Width && current.Height == placement.Height)
        {
            return;
        }

        NativeMethods.SetTopmostPosition(_hwnd, placement.X, placement.Y, placement.Width, placement.Height);
    }

    public static TaskbarPlacement ResolveTaskbarPlacement(int preferredWidth)
    {
        var bounds = Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(
            0,
            0,
            (int)SystemParameters.PrimaryScreenWidth,
            (int)SystemParameters.PrimaryScreenHeight);
        if (NativeMethods.TryGetTaskbarRect(out var edge, out var rect))
        {
            var trayLeft = NativeMethods.TryGetTrayLeftEdge(out var left) ? left : 0;
            return TaskbarPlacementCalculator.Compute(
                edge,
                rect,
                preferredWidth,
                Constants.DefaultHeight,
                bounds.Width,
                bounds.Height,
                trayLeft);
        }

        return TaskbarPlacementCalculator.Fallback(preferredWidth, Constants.DefaultHeight, bounds.Width, bounds.Height);
    }
}
