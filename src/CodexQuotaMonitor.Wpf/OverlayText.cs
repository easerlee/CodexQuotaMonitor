using System.Globalization;

namespace CodexQuotaMonitor.Wpf;

// Shared text helpers so every row centres and measures glyphs the same way.
public static class OverlayText
{
    public static System.Windows.Media.FormattedText Make(
        string text,
        double size,
        System.Windows.FontWeight weight,
        System.Windows.Media.Brush brush,
        double dpi)
    {
        return new System.Windows.Media.FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(
                new System.Windows.Media.FontFamily(Constants.UiFont),
                System.Windows.FontStyles.Normal,
                weight,
                System.Windows.FontStretches.Normal),
            size,
            brush,
            dpi);
    }

    // Centre the glyphs themselves (not the line box) in the row and left-align the ink.
    public static void DrawInkCentered(
        System.Windows.Media.DrawingContext dc,
        System.Windows.Media.FormattedText text,
        double x,
        double rowHeight)
    {
        var ink = text.BuildGeometry(new System.Windows.Point(0, 0)).Bounds;
        dc.DrawText(text, new System.Windows.Point(x - ink.Left, (rowHeight - ink.Height) / 2 - ink.Top));
    }
}
