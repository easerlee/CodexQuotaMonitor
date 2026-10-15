using System.Windows;
using System.Windows.Media;

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

        // Fixed label column: both rows start their bar at exactly the same x, no matter
        // how wide the glyphs or the fallback font happen to measure.
        var textSize = Formatting.TextSize(height);
        var label = OverlayText.Make(_title, textSize, FontWeights.Normal, primary, dpi);
        OverlayText.DrawInkCentered(dc, label, 4, height);

        var (barLeft, barRight) = BarGeometry(width, height);
        var barWidth = barRight - barLeft;
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

    // Bar extents for one row, derived from the row geometry alone: the label never enters
    // the calculation, so 5H and WK always get an identical bar. The right edge matches the
    // state light's (MainWindow gives it a 2px right margin).
    public static (double Left, double Right) BarGeometry(double rowWidth, double rowHeight)
    {
        return (4 + Math.Ceiling(Formatting.TextSize(rowHeight) * 1.2) + 5, rowWidth - 2);
    }
}
