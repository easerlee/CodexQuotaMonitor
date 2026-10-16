using System.Text.Json;
using CodexQuotaMonitor.Wpf;

var tests = new (string Name, Action Body)[]
{
    ("quota JSON-RPC response parsing", TestQuotaParsing),
    ("weekly-only quota is not mislabeled as 5H", TestWeeklyOnlyQuotaParsing),
    ("settings defaults, JSON load, CLI override, corrupt fallback", TestSettings),
    ("formatting helpers", TestFormatting),
    ("taskbar overlay placement", TestTaskbarPlacement),
    ("bar geometry ignores the label", TestBarGeometry),
    ("account identity from auth.json", TestAccountIdentity),
    ("account history store and rows", TestAccountHistory),
    ("stale codex.exe path is re-resolved", TestStaleCodexPath),
    ("autostart registry Run entry", TestAutostartRegistration),
    ("fullscreen detection", TestFullscreenDetector),
    ("argument handling", TestArguments)
};

var passed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
        return 1;
    }
}

Console.WriteLine($"{passed}/{tests.Length} tests passed");
return 0;

static void TestQuotaParsing()
{
    using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "limitId": "test-limit",
            "limitName": "Test Limit",
            "planType": "plus",
            "primary": {
              "usedPercent": 57.25,
              "windowDurationMins": 300,
              "resetsAt": 4102444800
            },
            "secondary": {
              "usedPercent": 21,
              "windowDurationMins": 10080,
              "resetsAt": 4102448400
            }
          }
        }
        """);

    var snapshot = QuotaReader.ParseRateLimitResult(document.RootElement);
    Equal(null, snapshot.Error, "quota error");
    Equal("test-limit", snapshot.LimitId, "limit id");
    Near(42.75, snapshot.FiveHour!.RemainingPercent!.Value, 0.001, "5h remaining");
    Near(79.0, snapshot.Weekly!.RemainingPercent!.Value, 0.001, "weekly remaining");
}

static void TestWeeklyOnlyQuotaParsing()
{
    using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "limitId": "codex",
            "primary": {
              "usedPercent": 25,
              "windowDurationMins": 10080,
              "resetsAt": 4102448400
            },
            "secondary": null
          }
        }
        """);

    var snapshot = QuotaReader.ParseRateLimitResult(document.RootElement);
    Equal<LimitWindow?>(null, snapshot.FiveHour, "disabled 5h window");
    Near(75.0, snapshot.Weekly!.RemainingPercent!.Value, 0.001, "weekly-only remaining");
    Equal(10080, snapshot.Weekly.WindowMins, "weekly-only duration");
}

static void TestSettings()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "codex-quota-native-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    var path = Path.Combine(tempDir, "settings.json");
    try
    {
        var defaults = SettingsStore.Load(path);
        Equal(180, defaults.QuotaInterval, "default quota interval");

        File.WriteAllText(path, """
            {
              "quota_interval": 60,
              "red_threshold": 10,
              "amber_threshold": 25
            }
            """);
        var loaded = SettingsStore.Load(path);
        Equal(60, loaded.QuotaInterval, "loaded quota interval");
        Equal(25.0, loaded.AmberThreshold, "loaded amber threshold");

        var cli = CliOptions.Parse(["--quota-interval", "300"]);
        var merged = SettingsStore.ApplyCliOverrides(loaded, cli);
        Equal(300, merged.QuotaInterval, "cli quota override");

        File.WriteAllText(path, "{ broken json");
        var fallback = SettingsStore.Load(path);
        Equal(180, fallback.QuotaInterval, "corrupt JSON fallback");
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}

static void TestFormatting()
{
    Equal("abc", Formatting.Truncate("abc", 10), "truncate short");
    Equal("abcdefg...", Formatting.Truncate("abcdefghijk", 10), "truncate long");
    Equal("--", Formatting.RemainingText(null), "remaining missing");
    Equal("43", Formatting.RemainingText(42.75), "remaining percent");

    Equal("--:--", Formatting.ResetClock(null), "reset clock missing");
    var epoch = 1_800_000_000L;
    Equal(
        DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime().ToString("HH:mm"),
        Formatting.ResetClock(epoch),
        "reset clock format");

    // The 5h window only starts counting from first use, so a full window has no trustworthy
    // reset time and must read 已重置 rather than 100% plus a bogus clock.
    var now = DateTimeOffset.Now;
    var future = now.AddHours(3).ToUnixTimeSeconds();
    var past = now.AddMinutes(-5).ToUnixTimeSeconds();
    Equal("已重置", Formatting.WindowReset(100, future, now, weekly: false), "full window reads 已重置");
    Equal("已重置", Formatting.WindowReset(0, past, now, weekly: false), "expired window reads 已重置");
    Equal(
        Formatting.ResetClock(future),
        Formatting.WindowReset(19, future, now, weekly: false),
        "an active window still shows its clock");
    Equal(
        Formatting.Countdown(future),
        Formatting.WindowReset(40, future, now, weekly: true),
        "an active weekly window still counts down");
}

static void TestTaskbarPlacement()
{
    var bottomRect = new NativeMethods.RECT { Left = 0, Top = 1032, Right = 1920, Bottom = 1080 };
    var bottom = TaskbarPlacementCalculator.Compute(3, bottomRect, 260, 48, 1920, 1080);
    Equal(new TaskbarPlacement(0, 1032, 260, 48), bottom, "bottom taskbar placement");

    var topRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 40 };
    var top = TaskbarPlacementCalculator.Compute(1, topRect, 260, 48, 1920, 1080);
    Equal(new TaskbarPlacement(0, 0, 260, 40), top, "top taskbar height");

    var verticalRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 48, Bottom = 1080 };
    var vertical = TaskbarPlacementCalculator.Compute(0, verticalRect, 260, 48, 1920, 1080);
    Equal(new TaskbarPlacement(0, 1032, 260, 48), vertical, "vertical taskbar fallback");

    var trayAnchored = TaskbarPlacementCalculator.Compute(3, bottomRect, 260, 48, 1920, 1080, 1500);
    Equal(new TaskbarPlacement(1232, 1032, 260, 48), trayAnchored, "bottom taskbar anchored left of tray");

    var noRoomForTrayAnchor = TaskbarPlacementCalculator.Compute(3, bottomRect, 260, 48, 1920, 1080, 100);
    Equal(new TaskbarPlacement(0, 1032, 260, 48), noRoomForTrayAnchor, "tray anchor falls back to taskbar left");
}

static void TestAutostartRegistration()
{
    const string scratch = "CodexQuotaMonitorNativeSelfTest";
    var logger = new SimpleLogger(Path.Combine(Path.GetTempPath(), "codex-quota-autostart-test.log"));
    try
    {
        StartupRegistration.SetEnabled(false, logger, scratch);
        Equal(false, StartupRegistration.IsEnabled(scratch), "autostart starts disabled");

        Equal(true, StartupRegistration.SetEnabled(true, logger, scratch), "enable autostart");
        Equal(true, StartupRegistration.IsEnabled(scratch), "autostart reports enabled");

        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            var value = key?.GetValue(scratch) as string;
            if (value is null || !value.StartsWith('"') || !value.EndsWith('"'))
            {
                throw new InvalidOperationException($"autostart command must be a quoted path, got: {value}");
            }
        }

        Equal(true, StartupRegistration.SetEnabled(false, logger, scratch), "disable autostart");
        Equal(false, StartupRegistration.IsEnabled(scratch), "autostart reports disabled");
    }
    finally
    {
        StartupRegistration.SetEnabled(false, logger, scratch);
    }
}

static void TestFullscreenDetector()
{
    var screen = new System.Drawing.Rectangle(0, 0, 3440, 1440);
    var coversMonitor = new NativeMethods.RECT { Left = 0, Top = 0, Right = 3440, Bottom = 1440 };
    var stopsAboveTaskbar = new NativeMethods.RECT { Left = 0, Top = 0, Right = 3440, Bottom = 1392 };

    Equal(true, FullscreenDetector.IsFullscreen("HwndWrapper[Game.exe;;guid]", coversMonitor, screen), "fullscreen app");
    Equal(false, FullscreenDetector.IsFullscreen("HwndWrapper[App.exe;;guid]", stopsAboveTaskbar, screen), "maximised window");
    Equal(false, FullscreenDetector.IsFullscreen("Progman", coversMonitor, screen), "desktop");
    Equal(false, FullscreenDetector.IsFullscreen("WorkerW", coversMonitor, screen), "desktop worker");
    Equal(false, FullscreenDetector.IsFullscreen("Shell_TrayWnd", coversMonitor, screen), "taskbar");
    Equal(false, FullscreenDetector.IsFullscreen("HwndWrapper[Game.exe;;guid]", new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }, screen), "window on one monitor only");
}

static void TestBarGeometry()
{
    // Card is 116px wide; its inner grid is 116 - 2 (border/margin) - 6 (grid margin) = 106.
    const double rowWidth = 106;
    const double rowHeight = (48 - 2 - 2 - 2) / 3.0; // 48px card minus border/margins -> 14px rows

    var (left, right) = MetricGaugeBlock.BarGeometry(rowWidth, rowHeight);
    Equal(106.0 - 2, right, "bar right edge matches the state light");
    if (right - left < 40)
    {
        throw new InvalidOperationException($"bar too short: {right - left:0.#}px");
    }

    // The fixed label column must fit two monospace characters (advance is 0.55em).
    var textSize = Formatting.TextSize(rowHeight);
    if (left - 4 < 2 * 0.55 * textSize)
    {
        throw new InvalidOperationException(
            $"label column {left - 4:0.#}px cannot hold two {textSize:0.#}px monospace characters");
    }
}

static void TestAccountIdentity()
{
    static string Jwt(object payload)
    {
        static string B64(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return B64("{\"alg\":\"none\"}") + "." + B64(JsonSerializer.Serialize(payload)) + ".sig";
    }

    var json = JsonSerializer.Serialize(new
    {
        auth_mode = "chatgpt",
        tokens = new
        {
            account_id = "9bd8c1e2-aaaa-bbbb-cccc-1234abcd99fc",
            id_token = Jwt(new Dictionary<string, object?>
            {
                ["email"] = "es@example.com",
                ["name"] = "E S",
                ["https://api.openai.com/auth"] = new Dictionary<string, object?>
                {
                    ["chatgpt_plan_type"] = "plus"
                }
            })
        }
    });
    var identity = AccountIdentity.Parse(json);
    if (identity is null)
    {
        throw new InvalidOperationException("identity was not parsed");
    }

    Equal("9bd8c1e2-aaaa-bbbb-cccc-1234abcd99fc", identity.Id, "account id");
    Equal("es@example.com", identity.Email, "email from id_token");
    Equal("es", identity.Display, "display keeps only the part before the @");
    Equal("plus", identity.PlanType, "plan from id_token");

    Equal<AccountIdentity?>(null, AccountIdentity.Parse("{\"tokens\":null}"), "tokens not an object");
    Equal<AccountIdentity?>(null, AccountIdentity.Parse("not json"), "garbage auth file");

    // Fall back to the account id when the token carries no email.
    var idOnly = AccountIdentity.Parse(
        JsonSerializer.Serialize(new { tokens = new { account_id = "abc123" } }));
    Equal("abc123", idOnly?.Display, "display falls back to the id");
}

static void TestAccountHistory()
{
    var directory = Path.Combine(Path.GetTempPath(), "cqm-accounts-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new AccountHistoryStore(Path.Combine(directory, "accounts.json"));
        Equal(0, store.Load().Count, "empty store");

        var now = DateTimeOffset.Now;
        store.Upsert(new AccountRecord("acct-1", "one@example.com", "plus", now.AddMinutes(-10), 68, now.AddHours(2).ToUnixTimeSeconds(), 55, now.AddDays(4).ToUnixTimeSeconds()));
        store.Upsert(new AccountRecord("acct-2", "two@example.com", "pro", now, 12, now.AddHours(1).ToUnixTimeSeconds(), 40, now.AddDays(2).ToUnixTimeSeconds()));
        // Re-reading the second account must overwrite its row, not append a duplicate.
        store.Upsert(new AccountRecord("acct-2", "two@example.com", "pro", now, 41, now.AddHours(1).ToUnixTimeSeconds(), 40, now.AddDays(2).ToUnixTimeSeconds()));

        var records = store.Load();
        Equal(2, records.Count, "one row per account");
        Equal(41.0, records.Single(r => r.AccountId == "acct-2").FiveHourPercent, "overwritten value");
        Equal(68.0, records.Single(r => r.AccountId == "acct-1").FiveHourPercent, "other account untouched");

        var acct1 = records.Single(r => r.AccountId == "acct-1");
        var live = AccountRow.Lines(acct1, now, current: true);
        Equal(3, live.Count, "account block is the name plus the two quota lines");
        if (!live[0].Contains("one") || !live[0].Contains("当前") || live[0].Contains("@"))
        {
            throw new InvalidOperationException($"name line looks wrong: {live[0]}");
        }

        if (!live[1].Contains("68%") || live[1].Contains("已重置"))
        {
            throw new InvalidOperationException($"5H line looks wrong: {live[1]}");
        }

        if (!live[2].Contains("55%"))
        {
            throw new InvalidOperationException($"weekly line looks wrong: {live[2]}");
        }

        // A window whose reset time has passed must read 已重置, and the block must keep the
        // same width so the columns line up across accounts.
        var expired = records.Single(r => r.AccountId == "acct-1") with
        {
            FiveHourResetsAt = now.AddMinutes(-5).ToUnixTimeSeconds()
        };
        var expiredLines = AccountRow.Lines(expired, now, current: false);
        if (!expiredLines[1].Contains("已重置") || expiredLines[1].Contains("68%"))
        {
            throw new InvalidOperationException($"expired line looks wrong: {expiredLines[1]}");
        }

        static int DisplayWidth(string text) => text.Sum(c => c > 0x2E80 ? 2 : 1);
        Equal(DisplayWidth(live[1]), DisplayWidth(expiredLines[1]), "5H line keeps its width");
        Equal(DisplayWidth(live[2]), DisplayWidth(expiredLines[2]), "weekly line keeps its width");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}

static void TestStaleCodexPath()
{
    // The CLI moves itself into a new bin folder on update; the monitor must notice that the
    // path it remembered is gone instead of failing every read until it is restarted.
    var stale = Path.Combine(Path.GetTempPath(), "cqm-codex-" + Guid.NewGuid().ToString("N") + ".exe");
    File.WriteAllText(stale, "placeholder");
    const string fresh = @"C:\Users\example\AppData\Local\OpenAI\Codex\bin\e78bad9887fa50cf\codex.exe";
    try
    {
        Equal(stale, QuotaReader.ResolveExePath(stale, null, _ => fresh), "a path that still exists is kept");
        File.Delete(stale);
        Equal(fresh, QuotaReader.ResolveExePath(stale, null, _ => fresh), "a vanished path is re-resolved");
        Equal<string?>(null, QuotaReader.ResolveExePath(null, null, _ => null), "no candidate stays null");
        Equal(fresh, QuotaReader.ResolveExePath(null, "configured", _ => fresh), "first read resolves too");
    }
    finally
    {
        if (File.Exists(stale))
        {
            File.Delete(stale);
        }
    }
}

static void TestArguments(){
    var options = CliOptions.Parse([
        "--check",
        "--codex-home", "C:\\CodexHome\\.codex",
        "--codex-exe", "C:\\Tools\\codex.exe",
        "--quota-interval", "600"
    ]);
    Equal(true, options.Check, "check flag");
    Equal(false, options.Once, "once flag");
    Equal("C:\\CodexHome\\.codex", options.CodexHome, "codex home");
    Equal("C:\\Tools\\codex.exe", options.CodexExe, "codex exe");
    Equal(600, options.QuotaInterval, "quota interval");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}

static void Near(double expected, double actual, double tolerance, string label)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
