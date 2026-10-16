using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace CodexQuotaMonitor.Wpf;

// The right-click panel: a dark rounded card that shows every account as a small chart
// (label, bar, percentage, reset time) plus the handful of settings the monitor has.
public sealed class QuotaMenu : Border
{
    private const double LabelWidth = 26;
    private const double BarWidth = 66;
    private const double ValueWidth = 42;
    private const double ResetWidth = 58;

    private static readonly FontFamily UiFont = new(Constants.UiFont);

    private readonly StackPanel _content = new();
    private readonly Action _refresh;
    private readonly Action _resnap;
    private readonly Action _toggleAutostart;
    private readonly Action<int> _setInterval;
    private readonly Action _exit;

    public QuotaMenu(
        Action refresh,
        Action resnap,
        Action toggleAutostart,
        Action<int> setInterval,
        Action exit)
    {
        _refresh = refresh;
        _resnap = resnap;
        _toggleAutostart = toggleAutostart;
        _setInterval = setInterval;
        _exit = exit;

        Background = new SolidColorBrush(Formatting.Popup.Background);
        BorderBrush = new SolidColorBrush(Formatting.Popup.Outline);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(16, 12, 16, 12);
        Effect = new DropShadowEffect
        {
            BlurRadius = 22,
            ShadowDepth = 5,
            Direction = 270,
            Opacity = 0.55,
            Color = Colors.Black
        };
        Child = _content;
    }

    // Rebuilt on every open, so the numbers and check marks are never stale.
    public void Populate(
        IReadOnlyList<AccountRecord> records,
        string? currentAccountId,
        AppSettings settings,
        bool autostart)
    {
        _content.Children.Clear();

        if (records.Count == 0)
        {
            _content.Children.Add(Text("还没有账号记录，取数成功后自动记录", Formatting.Popup.Dim, 12));
        }

        var now = DateTimeOffset.Now;
        var first = true;
        foreach (var record in records)
        {
            if (!first)
            {
                _content.Children.Add(Divider());
            }

            first = false;
            var current = currentAccountId is not null &&
                string.Equals(record.AccountId, currentAccountId, StringComparison.OrdinalIgnoreCase);
            _content.Children.Add(AccountHeader(AccountIdentity.ShortName(record.Email, record.AccountId), current));
            _content.Children.Add(MetricRow("5H", record.FiveHourPercent, record.FiveHourResetsAt, now, settings, weekly: false));
            _content.Children.Add(MetricRow("WK", record.WeeklyPercent, record.WeeklyResetsAt, now, settings, weekly: true));
        }

        _content.Children.Add(Divider());
        _content.Children.Add(ActionRow("立即刷新", _refresh));
        _content.Children.Add(ActionRow("重新贴靠到托盘左侧", _resnap));
        _content.Children.Add(ActionRow(autostart ? "开机自动启动   ✓" : "开机自动启动", _toggleAutostart, dim: !autostart));
        _content.Children.Add(IntervalRow(settings.QuotaInterval));
        _content.Children.Add(Divider());
        _content.Children.Add(ActionRow("退出", _exit, danger: true));
    }

    private static UIElement AccountHeader(string name, bool current)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 4)
        };
        row.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Fill = new SolidColorBrush(current ? Formatting.Popup.Accent : Formatting.Popup.Idle)
        });
        row.Children.Add(new TextBlock
        {
            Text = name,
            FontFamily = UiFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Formatting.Popup.Text),
            VerticalAlignment = VerticalAlignment.Center
        });

        if (current)
        {
            row.Children.Add(new TextBlock
            {
                Text = "← 当前",
                FontFamily = UiFont,
                FontSize = 11,
                Margin = new Thickness(10, 0, 0, 0),
                Foreground = new SolidColorBrush(Formatting.Popup.Accent),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return row;
    }

    private static UIElement MetricRow(
        string label,
        double? percent,
        long? resetsAt,
        DateTimeOffset now,
        AppSettings settings,
        bool weekly)
    {
        var fresh = Formatting.WindowIsFresh(percent, resetsAt, now);
        var color = Formatting.Popup.ColorForRemaining(percent, settings);

        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ValueWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ResetWidth) });

        var labelText = Text(label, Formatting.Popup.Dim, 12);
        labelText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labelText, 0);
        row.Children.Add(labelText);

        // A full window has no chart to draw: the bar stays empty and the row reads 已重置.
        var bar = Bar(fresh ? 0 : percent ?? 0, color);
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);

        var valueText = Text(
            fresh ? "已重置" : percent.HasValue ? $"{Math.Round(percent.Value):0}%" : "--",
            fresh ? Formatting.Popup.Dim : color,
            13);
        valueText.VerticalAlignment = VerticalAlignment.Center;
        valueText.HorizontalAlignment = HorizontalAlignment.Right;
        valueText.FontWeight = FontWeights.SemiBold;
        valueText.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(valueText, 2);
        row.Children.Add(valueText);

        var resetText = Text(
            fresh ? string.Empty : Formatting.WindowReset(percent, resetsAt, now, weekly),
            Formatting.Popup.Dim,
            12);
        resetText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(resetText, 3);
        row.Children.Add(resetText);

        return row;
    }

    private static UIElement Bar(double percent, Color color)
    {
        var track = new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Formatting.Popup.Track)
        };

        // Star-sized columns give an exact proportional fill without any measuring.
        var fillHost = new Grid();
        fillHost.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Clamp(percent, 0, 100), GridUnitType.Star)
        });
        fillHost.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Clamp(100 - percent, 0, 100), GridUnitType.Star)
        });
        var fill = new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(color)
        };
        Grid.SetColumn(fill, 0);
        fillHost.Children.Add(fill);

        var host = new Grid
        {
            Width = BarWidth - 10,
            Height = 7,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        host.Children.Add(track);
        host.Children.Add(fillHost);
        return host;
    }

    private static UIElement ActionRow(string text, Action action, bool danger = false, bool dim = false)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5, 8, 5),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = Text(text, dim ? Formatting.Popup.Dim : danger ? Formatting.Popup.Fail : Formatting.Popup.Text, 12.5)
        };
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Formatting.Popup.Hover);
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => action();
        return row;
    }

    private UIElement IntervalRow(int currentSeconds)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth + BarWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = Text("刷新间隔", Formatting.Popup.Dim, 12);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var pills = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var seconds in OfferedIntervals(currentSeconds))
        {
            var active = seconds == currentSeconds;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(active ? Formatting.Popup.Accent : Formatting.Popup.Track),
                Child = Text(seconds / 60 + "", active ? Formatting.Popup.Background : Formatting.Popup.Text, 11.5)
            };
            var captured = seconds;
            pill.MouseLeftButtonUp += (_, _) => _setInterval(captured);
            pills.Children.Add(pill);
        }

        Grid.SetColumn(pills, 1);
        row.Children.Add(pills);
        return row;
    }

    // 1/5/15 minutes, plus whatever is active right now if it came from elsewhere (a value
    // saved by an older build or --quota-interval), so a choice is always highlighted.
    private static IReadOnlyList<int> OfferedIntervals(int currentSeconds)
    {
        var offered = new List<int> { 60, 300, 900 };
        if (currentSeconds > 0 && !offered.Contains(currentSeconds))
        {
            offered.Add(currentSeconds);
            offered.Sort();
        }

        return offered;
    }

    private static TextBlock Text(string text, Color color, double size)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = UiFont,
            FontSize = size,
            Foreground = new SolidColorBrush(color)
        };
    }

    private static UIElement Divider()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8, 0, 8),
            Background = new SolidColorBrush(Formatting.Popup.Outline)
        };
    }
}
