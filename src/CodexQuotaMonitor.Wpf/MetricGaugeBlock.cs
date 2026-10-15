using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace CodexQuotaMonitor.Wpf;

// One row of the card: the 5H/WK label and a bar filled to the remaining percentage.
public sealed class MetricGaugeBlock : FrameworkElement
{
    private readonly string _title;
    private double? _remaining;
    private AppSettings _settings;

    public MetricGaugeBlock(string title, AppSettings settings)
    {
        _title = title;
        _settings = settings;
        SnapsToDevicePixels = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    public void SetMetric(double? remaining, AppSettings settings)
    {
        _remaining = remaining;
        _settings = settings;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = Math.Max(28.0, ActualWidth);
        var height = Math.Max(12.0, ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var primary = new SolidColorBrush(Formatting.PrimaryText);
        var track = new SolidColorBrush(Formatting.GaugeTrack);
        var accent = new SolidColorBrush(Formatting.ColorForRemaining(_remaining, _settings));

        // Centre the glyphs themselves (not the line box) on the bar, and left-align the
        // ink so every row starts at the same x.
        var label = MakeText(_title, Formatting.TextSize(height), FontWeights.Bold, primary, dpi);
        var ink = label.BuildGeometry(new Point(0, 0)).Bounds;
        dc.DrawText(label, new Point(4 - ink.Left, (height - ink.Height) / 2 - ink.Top));

        // Both rows measure the same advance, so the bars share one left edge and length.
        var barLeft = 4 + Math.Ceiling(label.Width) + 5;
        var barWidth = width - 2 - barLeft;
        if (barWidth < 6)
        {
            return;
        }

        var barHeight = Math.Clamp(height * 0.42, 4.0, 7.0);
        var barTop = (height - barHeight) / 2;
        const double radius = 3.0;
        dc.DrawRoundedRectangle(track, null, new Rect(barLeft, barTop, barWidth, barHeight), radius, radius);

        if (_remaining.HasValue)
        {
            var filled = barWidth * Math.Clamp(_remaining.Value, 0.0, 100.0) / 100.0;
            dc.DrawRoundedRectangle(
                accent,
                null,
                new Rect(barLeft, barTop, Math.Clamp(filled, radius * 2, barWidth), barHeight),
                radius,
                radius);
        }
    }

    private static FormattedText MakeText(string text, double size, FontWeight weight, Brush brush, double dpi)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily(Constants.UiFont), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            dpi);
    }
}
