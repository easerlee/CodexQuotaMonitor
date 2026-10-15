using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace CodexQuotaMonitor.Wpf;

// Third block: countdown to the 5-hour quota reset, plus the quota read status.
public sealed class RefreshStatusBlock : FrameworkElement
{
    private long? _resetsAt;
    private string _status = "WAIT";
    private Color _accentColor = Formatting.ColorFromHex("#8794A2");

    public RefreshStatusBlock()
    {
        SnapsToDevicePixels = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    public void SetStatus(long? resetsAt, string status, string accentHex)
    {
        _resetsAt = resetsAt;
        _status = status;
        _accentColor = Formatting.ColorFromHex(accentHex);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = Math.Max(34.0, ActualWidth);
        var height = Math.Max(22.0, ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var accent = new SolidColorBrush(_accentColor);
        var current = new SolidColorBrush(Formatting.ColorFromHex("#F4F6F8"));
        var labelSize = Math.Clamp(height * 0.21, 7.0, 9.2);

        DrawText(dc, "重置", 4, 1, labelSize, FontWeights.Bold, current, dpi);
        dc.DrawEllipse(accent, null, new Point(width - 5, 5), 2.2, 2.2);

        var countdownSize = Math.Clamp(height * 0.285, 8.8, 12.5);
        DrawCenteredText(
            dc,
            Formatting.Countdown(_resetsAt),
            new Rect(0, 2, width, height * 0.64),
            countdownSize,
            FontWeights.SemiBold,
            current,
            dpi);

        if (!string.IsNullOrWhiteSpace(_status))
        {
            var statusSize = Math.Clamp(height * 0.18, 7.0, 8.0);
            DrawCenteredText(dc, _status, new Rect(0, height - statusSize - 2, width, statusSize + 2), statusSize, FontWeights.Bold, accent, dpi);
        }
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, FontWeight weight, Brush brush, double dpi)
    {
        dc.DrawText(MakeText(text, size, weight, brush, dpi), new Point(x, y));
    }

    private static void DrawCenteredText(DrawingContext dc, string text, Rect bounds, double size, FontWeight weight, Brush brush, double dpi)
    {
        var formatted = MakeText(text, size, weight, brush, dpi);
        var textBounds = formatted.BuildGeometry(new Point(0, 0)).Bounds;
        var x = bounds.Left + (bounds.Width - textBounds.Width) / 2.0 - textBounds.Left;
        var y = bounds.Top + (bounds.Height - textBounds.Height) / 2.0 - textBounds.Top;
        dc.DrawText(formatted, new Point(x, y));
    }

    private static FormattedText MakeText(string text, double size, FontWeight weight, Brush brush, double dpi)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            dpi);
    }
}
