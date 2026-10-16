using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace CodexQuotaMonitor.Wpf;

public partial class MainWindow : Window
{
    private readonly AppPaths _paths;
    private readonly SimpleLogger _logger;
    private readonly QuotaReader _quotaReader;
    private readonly string _codexHome;
    private readonly AccountHistoryStore _accountHistory;
    private readonly DispatcherTimer _tickTimer = new();
    private readonly DispatcherTimer _topmostTimer = new();
    private readonly DispatcherTimer _placementTimer = new();
    private readonly MetricGaugeBlock _quota5h;
    private readonly MetricGaugeBlock _quotaWeek;
    private readonly TextRowBlock _resetText;
    private readonly Border _statusLight;
    private readonly Popup _popup = new();
    private readonly DispatcherTimer _menuGuardTimer = new();
    private QuotaMenu? _menu;
    private AppSettings _settings;
    private IntPtr _hwnd;
    private QuotaSnapshot? _lastQuota;
    private string? _quotaLastError;
    private DateTimeOffset? _quotaLastSuccessAt;
    private DateTimeOffset _nextQuotaAt;
    private bool _quotaInFlight;
    private bool _quotaPendingRefresh;
    private bool _menuVisible;
    private bool _menuButtonWasUp = true;
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

        // Only runs while the panel is open. See MenuDismiss for why the panel watches the mouse
        // itself instead of relying on the popup's own click-outside dismissal.
        _menuGuardTimer.Interval = TimeSpan.FromMilliseconds(80);
        _menuGuardTimer.Tick += (_, _) => CheckMenuDismiss();
    }

    // The panel is a WPF popup rather than a system menu so it can be the dark rounded card
    // the design calls for. Only the command rows close it: the three settings rows answer with
    // a sliding control, so the panel has to stay open for that movement to be seen.
    private void BuildMenu()
    {
        _menu = new QuotaMenu(
            () => RunFromMenu(RefreshNow),
            ToggleAutostart,
            SetQuotaInterval,
            SetDisplaySide,
            RequestExit);
        _popup.PlacementTarget = this;
        _popup.Placement = PlacementMode.Custom;
        _popup.StaysOpen = false;
        _popup.AllowsTransparency = true;
        _popup.PopupAnimation = PopupAnimation.Fade;
        _popup.Child = _menu;
        _popup.CustomPopupPlacementCallback = PlaceMenu;
        _popup.Opened += (_, _) =>
        {
            _menuVisible = true;
            // The panel is opened by a button release, so the next press is a fresh one.
            _menuButtonWasUp = true;
            _menuGuardTimer.Start();
            ForceTopmost();
        };
        _popup.Closed += (_, _) =>
        {
            _menuVisible = false;
            _menuGuardTimer.Stop();
            ForceTopmost();
        };
    }

    private void CheckMenuDismiss()
    {
        var pressed = NativeMethods.MouseButtonDown();
        var wasUp = _menuButtonWasUp;
        _menuButtonWasUp = !pressed;
        if (_menu is null || !MenuDismiss.ShouldClose(pressed, wasUp, CursorInsideMenu()))
        {
            return;
        }

        _popup.IsOpen = false;
    }

    // The panel is a real window of its own, so its own rectangle and the cursor are both read in
    // raw screen pixels - no DPI conversion between the two.
    private bool CursorInsideMenu()
    {
        if (_menu is null || PresentationSource.FromVisual(_menu) is not HwndSource source)
        {
            return false;
        }

        if (!NativeMethods.GetCursorPos(out var cursor) || !NativeMethods.GetWindowRect(source.Handle, out var rect))
        {
            return true;
        }

        return cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
    }

    // The card sits under the tray in the screen's bottom-right corner, so the panel opens up
    // and to the left of the cursor. These offsets are relative to the card, so the panel
    // legitimately lands at negative coordinates.
    private CustomPopupPlacement[] PlaceMenu(System.Windows.Size popup, System.Windows.Size target, System.Windows.Point offset)
    {
        var cursor = System.Windows.Input.Mouse.GetPosition(this);
        var position = new Point(cursor.X - popup.Width + 10, cursor.Y - popup.Height - 10);
        return [new CustomPopupPlacement(position, PopupPrimaryAxis.Horizontal)];
    }

    // Picking something closes the panel first, the way a menu behaves.
    private void RunFromMenu(Action action)
    {
        _popup.IsOpen = false;
        action();
    }

    private void ShowMenu()
    {
        if (_menu is null)
        {
            return;
        }

        _menu.Populate(
            _accountHistory.Load(),
            AccountIdentity.Read(_codexHome)?.Id,
            _settings,
            StartupRegistration.IsEnabled());
        ForceTopmost();
        _popup.IsOpen = true;
    }

    // Returns the state the registry ended up in, so the switch shows the truth rather than
    // what the click intended.
    private bool ToggleAutostart()
    {
        StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled(), _logger);
        return StartupRegistration.IsEnabled();
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
        _menuGuardTimer.Stop();
        _popup.IsOpen = false;

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
        ShowMenu();
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
        UpdateTitle();
    }

    // Moving the card is the whole point of this setting, so apply it on the spot instead of
    // waiting for the next placement tick to notice the new value.
    private void SetDisplaySide(string side)
    {
        _settings.DisplaySide = side;
        _settings.Normalize();
        SettingsStore.Save(_paths.SettingsPath, _settings, _logger);
        SnapToTaskbar();
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

        var placement = ResolveTaskbarPlacement(Constants.DefaultWidth, _settings.DisplayLeft);
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

    public static TaskbarPlacement ResolveTaskbarPlacement(int preferredWidth, bool pinLeft = false)
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
                trayLeft,
                pinLeft);
        }

        return TaskbarPlacementCalculator.Fallback(preferredWidth, Constants.DefaultHeight, bounds.Width, bounds.Height);
    }
}
