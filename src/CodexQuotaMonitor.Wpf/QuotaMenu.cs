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
    // The label and bar columns are fixed; the value and reset columns are measured from the
    // widest text this panel will show, so the percentage and its reset time sit close
    // together while both stay left-aligned. The reset column is a star column with that
    // measured width as its floor, so its right edge always lands on the panel's own edge -
    // the same edge the interval pills align to.
    private const double LabelWidth = 26;
    private const double BarWidth = 66;
    private const double MinValueWidth = 30;
    private const double MinResetWidth = 40;

    private static readonly FontFamily UiFont = new(Constants.UiFont);

    private readonly StackPanel _content = new();
    private readonly Action _refresh;
    private readonly Func<bool> _toggleAutostart;
    private readonly Action<int> _setInterval;
    private readonly Action<string> _setDisplaySide;
    private readonly Action _exit;

    public QuotaMenu(
        Action refresh,
        Func<bool> toggleAutostart,
        Action<int> setInterval,
        Action<string> setDisplaySide,
        Action exit)
    {
        _refresh = refresh;
        _toggleAutostart = toggleAutostart;
        _setInterval = setInterval;
        _setDisplaySide = setDisplaySide;
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
        // The panel grows with the number of accounts; keep it inside the screen and let it
        // scroll instead of running off the top edge.
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 80);
        Child = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    // Rebuilt on every open, so the numbers and switch states are never stale. The logged-in
    // account leads the list and is the only one with a green light, matching the overlay's
    // own status semantics.
    public void Populate(
        IReadOnlyList<AccountRecord> records,
        string? currentAccountId,
        AppSettings settings,
        bool autostart)
    {
        _content.Children.Clear();

        var now = DateTimeOffset.Now;
        var ordered = AccountOrder.Sort(records, currentAccountId, now);
        if (ordered.Count == 0)
        {
            _content.Children.Add(Text("还没有账号记录，取数成功后自动记录", Formatting.Popup.Dim, 12));
        }

        var valueWidth = MeasureColumn(ordered, now, MinValueWidth, 13, ValueText);
        var resetWidth = MeasureColumn(ordered, now, MinResetWidth, 12, ResetText);
        var first = true;
        foreach (var record in ordered)
        {
            if (!first)
            {
                _content.Children.Add(Divider());
            }

            first = false;
            var current = AccountOrder.IsCurrent(record, currentAccountId);
            _content.Children.Add(AccountHeader(AccountIdentity.ShortName(record.Email, record.AccountId), current));
            _content.Children.Add(MetricRow("5H", record.FiveHourPercent, record.FiveHourResetsAt, now, settings, weekly: false, valueWidth, resetWidth));
            _content.Children.Add(MetricRow("WK", record.WeeklyPercent, record.WeeklyResetsAt, now, settings, weekly: true, valueWidth, resetWidth));
        }

        _content.Children.Add(Divider());
        _content.Children.Add(IntervalRow(settings.QuotaInterval));
        _content.Children.Add(ActionRow("立即刷新", _refresh));
        _content.Children.Add(Divider());
        _content.Children.Add(DisplaySideRow(settings.DisplayLeft));
        _content.Children.Add(AutostartRow(autostart));
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
            // Same reading as the overlay's status light: green means it is the live account,
            // grey means the reading is frozen because it is not logged in any more.
            Fill = new SolidColorBrush(current ? Formatting.Popup.Ok : Formatting.Popup.Idle)
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

        return row;
    }

    private static UIElement MetricRow(
        string label,
        double? percent,
        long? resetsAt,
        DateTimeOffset now,
        AppSettings settings,
        bool weekly,
        double valueWidth,
        double resetWidth)
    {
        var fresh = Formatting.WindowIsFresh(percent, resetsAt, now);
        var color = Formatting.Popup.ColorForRemaining(percent, settings);

        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valueWidth) });
        // Star: the reset column then ends exactly on the panel's own right edge, which is the
        // edge the interval pills line up with. ResetWidth below keeps it wide enough.
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = Text(label, Formatting.Popup.Dim, 12);
        labelText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labelText, 0);
        row.Children.Add(labelText);

        // A full window has no chart to draw: the bar stays empty and the row reads 已重置.
        var bar = Bar(fresh ? 0 : percent ?? 0, color);
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);

        var valueText = Text(ValueText(percent, resetsAt, now, weekly), fresh ? Formatting.Popup.Dim : color, 13);
        valueText.VerticalAlignment = VerticalAlignment.Center;
        valueText.HorizontalAlignment = HorizontalAlignment.Left;
        valueText.TextAlignment = TextAlignment.Left;
        valueText.TextTrimming = TextTrimming.CharacterEllipsis;
        valueText.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(valueText, 2);
        row.Children.Add(valueText);

        var resetText = Text(ResetText(percent, resetsAt, now, weekly), Formatting.Popup.Dim, 12);
        resetText.VerticalAlignment = VerticalAlignment.Center;
        // Right-aligned so every reset time ends on the same edge as the interval pills to the
        // right of the panel's content; the column is measured, so the gap after the value
        // stays tight either way.
        resetText.HorizontalAlignment = HorizontalAlignment.Right;
        resetText.TextAlignment = TextAlignment.Right;
        resetText.TextTrimming = TextTrimming.CharacterEllipsis;
        resetText.MinWidth = resetWidth;
        Grid.SetColumn(resetText, 3);
        row.Children.Add(resetText);

        return row;
    }

    private static string ValueText(double? percent, long? resetsAt, DateTimeOffset now, bool weekly)
    {
        if (Formatting.WindowIsFresh(percent, resetsAt, now))
        {
            return "已重置";
        }

        return percent.HasValue ? $"{Math.Round(percent.Value):0}%" : "--";
    }

    private static string ResetText(double? percent, long? resetsAt, DateTimeOffset now, bool weekly)
    {
        return Formatting.WindowIsFresh(percent, resetsAt, now)
            ? string.Empty
            : Formatting.WindowReset(percent, resetsAt, now, weekly);
    }

    // Widest text this panel will show in a column, so the column hugs its content instead of
    // leaving a gap. The same picker feeds the renderer, so the two can never disagree.
    private static double MeasureColumn(
        IReadOnlyList<AccountRecord> records,
        DateTimeOffset now,
        double minimum,
        double size,
        Func<double?, long?, DateTimeOffset, bool, string> pick)
    {
        var brush = new SolidColorBrush(Formatting.Popup.Text);
        var widest = minimum;
        foreach (var record in records)
        {
            foreach (var text in new[]
                     {
                         pick(record.FiveHourPercent, record.FiveHourResetsAt, now, false),
                         pick(record.WeeklyPercent, record.WeeklyResetsAt, now, true)
                     })
            {
                if (text.Length > 0)
                {
                    widest = Math.Max(widest, OverlayText.Make(text, size, FontWeights.SemiBold, brush, 1.0).Width + 2);
                }
            }
        }

        return Math.Ceiling(widest);
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

    // Rows line up with the metric labels: the negative margin pulls the hover highlight out
    // past the content edge while the padding pushes the text back to the same x as 5H, so the
    // text keeps its column and the highlight still has a margin around it.
    private static UIElement ActionRow(string text, Action action, bool danger = false, bool dim = false)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 8, 8, 8),
            Margin = new Thickness(-8, 0, -8, 0),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = Text(text, dim ? Formatting.Popup.Dim : danger ? Formatting.Popup.Fail : Formatting.Popup.Text, 12.5)
        };
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Formatting.Popup.Hover);
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => action();
        return row;
    }

    // 刷新间隔 and 显示位置 share one shape: the label sits in the 5H column and the sliding
    // control is flush right, on the same edge as the reset time column above it.
    private static UIElement SettingRow(string label, FrameworkElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 9, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth + BarWidth) });
        // Star so the control can be pushed flush against the panel's right edge.
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = Text(label, Formatting.Popup.Text, 12.5);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 0);
        row.Children.Add(text);

        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private UIElement IntervalRow(int currentSeconds)
    {
        var offered = OfferedIntervals(currentSeconds);
        // The slide is the feedback, so this row must not close the panel the way a command row
        // does: the callback only saves the choice, and the panel is rebuilt on its next open.
        return SettingRow(
            "刷新间隔",
            new SegmentedControl(
                offered.Select(seconds => (seconds / 60).ToString()).ToList(),
                offered.IndexOf(currentSeconds),
                index => _setInterval(offered[index])));
    }

    private UIElement DisplaySideRow(bool left)
    {
        return SettingRow(
            "显示位置",
            new SegmentedControl(["左", "右"], left ? 0 : 1, index => _setDisplaySide(index == 0 ? "left" : "right")));
    }

    // The autostart row keeps the hover plate and the label column of the rows above, but ends
    // in a switch instead of a check mark. The switch takes the state the registry actually
    // ended up in rather than assuming the write worked, and the panel stays open so the knob
    // is seen moving.
    private UIElement AutostartRow(bool autostart)
    {
        var toggle = new ToggleSwitch(autostart);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth + BarWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = Text("开机自动启动", Formatting.Popup.Text, 12.5);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);

        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 8, 8, 8),
            Margin = new Thickness(-8, 0, -8, 0),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = grid
        };
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Formatting.Popup.Hover);
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => toggle.Set(_toggleAutostart());
        return row;
    }

    // 1/5/15 minutes, plus whatever is active right now if it came from elsewhere (a value
    // saved by an older build or --quota-interval), so a choice is always highlighted.
    private static List<int> OfferedIntervals(int currentSeconds)
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
