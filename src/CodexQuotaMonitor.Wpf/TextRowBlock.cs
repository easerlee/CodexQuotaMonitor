namespace CodexQuotaMonitor.Wpf;

// Bottom row: the reset times, drawn with the same ink-centring as the labels above.
public sealed class TextRowBlock : System.Windows.FrameworkElement
{
    private const double RightGuard = 16; // room for the state light

    private string _text = string.Empty;
    private double _size;

    public TextRowBlock()
    {
        SnapsToDevicePixels = true;
        System.Windows.Media.TextOptions.SetTextRenderingMode(this, System.Windows.Media.TextRenderingMode.ClearType);
        System.Windows.Media.TextOptions.SetTextFormattingMode(this, System.Windows.Media.TextFormattingMode.Ideal);
    }

    public void SetText(string text, double size)
    {
        _text = text;
        _size = size;
        InvalidateVisual();
    }

    protected override void OnRender(System.Windows.Media.DrawingContext dc)
    {
        base.OnRender(dc);
        if (string.IsNullOrEmpty(_text) || _size <= 0)
        {
            return;
        }

        var text = OverlayText.Make(
            _text,
            _size,
            System.Windows.FontWeights.Normal,
            new System.Windows.Media.SolidColorBrush(Formatting.PrimaryText),
            System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);
        text.MaxTextWidth = Math.Max(1.0, ActualWidth - 4 - RightGuard);
        text.Trimming = System.Windows.TextTrimming.CharacterEllipsis;
        OverlayText.DrawInkCentered(dc, text, 4, Math.Max(12.0, ActualHeight));
    }
}
