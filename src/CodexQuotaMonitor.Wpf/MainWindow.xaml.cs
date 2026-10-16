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
    private readonly string _codexHome;
    private readonly AccountHistoryStore _accountHistory;
    private readonly DispatcherTimer _tickTimer = new();
    private readonly DispatcherTimer _topmostTimer = new();
    private readonly DispatcherTimer _placementTimer = new();
    private readonly DispatcherTimer _menuDismissTimer = new();
    private readonly MetricGaugeBlock _quota5h;
    private readonly MetricGaugeBlock _quotaWeek;
    private readonly TextRowBlock _resetText;
    private readonly Border _statusLight;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly List<Forms.ToolStripMenuItem> _quotaIntervalItems = new();
    private Forms.ToolStripMenuItem? _autostartItem;
    private readonly List<Forms.ToolStripItem> _accountRows = new();
    private Forms.ToolStripItem? _accountBlockAnchor;
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
    private bool _hiddenForFullscreen;
    private bool _isExiting;

    public MainWindow(AppPaths paths, CliOptions options, AppSettings settings, SimpleLogger logger)
    {
        _paths = paths;
        _settings = settings;
        _logger = logger;
        _codexHome = paths.ResolveCodexHome(options.CodexHome);
        _quotaReader = new QuotaReader(_codexHome, options.CodexExe, logger);
        _accountHistory = new AccountHistoryStore(Path.Combine(paths.RootDirectory, "accounts.json"));

        InitializeComponent();
        Width = Constants.DefaultWidth;
        Height = Constants.DefaultHeight;

        for (var row = 0; row < 3; row++)
        {
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        _quota5h = AddGaugeBlock("5H", 0);
        _quotaWeek = AddGaugeBlock("WK", 1);

        _resetText = new TextRowBlock();
        Grid.SetRow(_resetText, 2);
        RootGrid.Children.Add(_resetText);

        _statusLight = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
            Background = new SolidColorBrush(Formatting.Idle),
            IsHitTestVisible = false
        };
        Grid.SetRow(_statusLight, 2);
        RootGrid.Children.Add(_statusLight);

        BuildMenu();
        ConfigureTimers();
        RefreshNow();
    }

    private MetricGaugeBlock AddGaugeBlock(string title, int row)
    {
        var block = new MetricGaugeBlock(title, _settings);
        Grid.SetRow(block, row);
        RootGrid.Children.Add(block);
        return block;
    }

    private void ConfigureTimers()
    {
        _tickTimer.Interval = TimeSpan.FromSeconds(1);
        _tickTimer.Tick += (_, _) => Tick();
        _tickTimer.Start();

        _topmostTimer.Interval = TimeSpan.FromMilliseconds(500);
        _topmostTimer.Tick += (_, _) => UpdateOverlayVisibility();
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
            RebuildAccountViews();
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

        // Everything except 退出 lives in 设置, so the first level stays about the account.
        // It opens leftwards: the card sits at the screen's right edge under the tray.
        var settingsMenu = new Forms.ToolStripMenuItem("设置")
        {
            DropDownDirection = Forms.ToolStripDropDownDirection.Left
        };
        settingsMenu.DropDownItems.Add("立即刷新", null, (_, _) => RefreshNow());
        settingsMenu.DropDownItems.Add("重新贴靠到托盘左侧", null, (_, _) => SnapToTaskbar());
        _autostartItem = new Forms.ToolStripMenuItem("开机自动启动") { CheckOnClick = false };
        _autostartItem.Click += (_, _) => ToggleAutostart();
        settingsMenu.DropDownItems.Add(_autostartItem);
        settingsMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        settingsMenu.DropDownItems.Add(AccountLabel("刷新间隔"));
        foreach (var (label, seconds) in new[] { ("1 分钟", 60), ("3 分钟", 180), ("5 分钟", 300), ("10 分钟", 600), ("15 分钟", 900) })
        {
            var item = new Forms.ToolStripMenuItem(label) { Tag = seconds, CheckOnClick = false };
            item.Click += (_, _) => SetQuotaInterval(seconds);
            settingsMenu.DropDownItems.Add(item);
            _quotaIntervalItems.Add(item);
        }

        // Account blocks are inserted in front of this separator, so it marks where they end
        // and keeps a line between the last account and 设置.
        _accountBlockAnchor = new Forms.ToolStripSeparator();
        _menu.Items.Add(_accountBlockAnchor);
        _menu.Items.Add(settingsMenu);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => RequestExit());
        UpdateMenuChecks();
        RebuildAccountViews();
    }

    // The account blocks lead the menu and are rebuilt whenever it opens, so the list is
    // never stale. They sit in front of the trailing 设置/退出 pair.
    private void RebuildAccountViews()
    {
        var identity = AccountIdentity.Read(_codexHome);

        foreach (var row in _accountRows)
        {
            _menu.Items.Remove(row);
            row.Dispose();
        }

        _accountRows.Clear();

        var records = _accountHistory.Load()
            .OrderByDescending(record => record.LastSeenAt)
            .ToList();
        var insertAt = _accountBlockAnchor is null ? 0 : _menu.Items.IndexOf(_accountBlockAnchor);
        foreach (var row in BuildAccountRows(records, identity))
        {
            _menu.Items.Insert(insertAt++, row);
            _accountRows.Add(row);
        }

        // Labels are added while the menu is opening, so re-measure or the widest row gets
        // clipped by the menu edge.
        _menu.PerformLayout();
    }

    private static IEnumerable<Forms.ToolStripItem> BuildAccountRows(
        IReadOnlyList<AccountRecord> records,
        AccountIdentity? identity)
    {
        if (records.Count == 0)
        {
            yield return AccountLabel("（取数成功后自动记录）");
            yield break;
        }

        var now = DateTimeOffset.Now;
        var first = true;
        foreach (var record in records)
        {
            if (!first)
            {
                yield return new Forms.ToolStripSeparator();
            }

            first = false;
            var current = identity is not null &&
                string.Equals(record.AccountId, identity.Id, StringComparison.OrdinalIgnoreCase);
            foreach (var line in AccountRow.Lines(record, now, current))
            {
                yield return AccountLabel(line);
            }
        }
    }

    private static Forms.ToolStripLabel AccountLabel(string text)
    {
        return new Forms.ToolStripLabel(text)
        {
            AutoSize = true,
            Font = new System.Drawing.Font("Consolas", 9F)
        };
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

    // Left click refreshes on the spot. While the menu is open the click only dismisses it.
    private void OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_menuVisible)
        {
            return;
        }

        ForceTopmost();
        RefreshNow();
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
            RenderStatusLight();
            return;
        }

        _quotaInFlight = true;
        UpdateTitle();
        RenderStatusLight();
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
            RecordAccount(value);
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
        RenderStatusLight();
        UpdateTitle();
    }

    // File this reading under whoever is logged in now. Switching accounts later leaves the
    // record untouched, so it keeps the last values seen for that account.
    private void RecordAccount(QuotaSnapshot snapshot)
    {
        var identity = AccountIdentity.Read(_codexHome);
        if (identity is null)
        {
            return;
        }

        _accountHistory.Upsert(new AccountRecord(
            identity.Id,
            identity.Email,
            snapshot.PlanType ?? identity.PlanType,
            DateTimeOffset.Now,
            snapshot.FiveHour?.RemainingPercent,
            snapshot.FiveHour?.ResetsAt,
            snapshot.Weekly?.RemainingPercent,
            snapshot.Weekly?.ResetsAt));
    }

    private void RenderQuota()
    {
        var fiveHour = _lastQuota?.FiveHour;
        var weekly = _lastQuota?.Weekly;
        _quota5h.SetMetric(fiveHour?.RemainingPercent, _settings);
        _quotaWeek.SetMetric(weekly?.RemainingPercent, _settings);

        // Bottom line: the 5h reset clock and the weekly countdown side by side. A window that
        // is full or already past its reset reads 已重置 instead of a meaningless time.
        var now = DateTimeOffset.Now;
        var rowHeight = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight / 3.0 : Constants.DefaultHeight / 3.0;
        _resetText.SetText(
            $"{Formatting.WindowReset(fiveHour?.RemainingPercent, fiveHour?.ResetsAt, now, weekly: false)} / " +
            $"{Formatting.WindowReset(weekly?.RemainingPercent, weekly?.ResetsAt, now, weekly: true)}",
            Formatting.TextSize(rowHeight));
    }

    private void RenderStatusLight()
    {
        var state = _quotaInFlight || IsStale(_quotaLastSuccessAt, _settings.QuotaInterval)
            ? Formatting.Warn
            : _lastQuota is null
                ? Formatting.Idle
                : _quotaLastError is not null && _quotaLastSuccessAt.HasValue
                    ? Formatting.Warn
                    : _lastQuota.Error is not null && !_quotaLastSuccessAt.HasValue
                        ? Formatting.Fail
                        : Formatting.Ok;
        _statusLight.Background = new SolidColorBrush(state);
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

    // The overlay belongs to the taskbar strip: it stays with the taskbar and gets out of
    // the way while a fullscreen application owns the screen.
    private void UpdateOverlayVisibility()
    {
        if (IsFullscreenForeground())
        {
            if (!_hiddenForFullscreen)
            {
                _hiddenForFullscreen = true;
                Hide();
            }
            return;
        }

        if (_hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            Show();
            SnapToTaskbar();
        }

        ForceTopmost();
    }

    private bool IsFullscreenForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var root = NativeMethods.RootWindow(foreground);
        if (root == _hwnd || !NativeMethods.GetWindowRect(root, out var rect))
        {
            return false;
        }

        return FullscreenDetector.IsFullscreen(
            NativeMethods.WindowClassName(root),
            rect,
            Forms.Screen.FromHandle(root).Bounds);
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
        // While parked for a fullscreen app the placement timer must not move or re-show
        // the window, not even when the taskbar itself disappears.
        if (_hiddenForFullscreen)
        {
            return;
        }

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
