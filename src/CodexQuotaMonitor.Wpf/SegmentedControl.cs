using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace CodexQuotaMonitor.Wpf;

// The panel's sliding options. Both use the panel's own chart colours - the plate is the bar's
// track colour, the moving part is the bar's green - so the settings read like the bars above
// them instead of like a second visual language.
// The refresh interval, the display side and the autostart switch all use one of these.
public sealed class SegmentedControl : Border
{
    // One option wide; the control is a fixed size so the sliding block can be placed without
    // measuring anything, which also keeps the settings rows from widening the panel.
    private const double OptionWidth = 25;
    private const double ControlHeight = 22;
    private const double Inset = 2;
    private const int SlideMilliseconds = 150;

    private readonly TranslateTransform _thumbShift = new();
    private readonly List<TextBlock> _options = [];
    private readonly Action<int> _pick;
    private int _selected;

    public SegmentedControl(IReadOnlyList<string> labels, int selected, Action<int> pick)
    {
        _pick = pick;
        _selected = Math.Clamp(selected, 0, Math.Max(0, labels.Count - 1));

        Width = OptionWidth * Math.Max(1, labels.Count);
        Height = ControlHeight;
        CornerRadius = new CornerRadius(ControlHeight / 2);
        Background = new SolidColorBrush(Formatting.Popup.Track);
        Padding = new Thickness(Inset);

        var grid = new Grid();
        for (var index = 0; index < labels.Count; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        // The block fills the first column, so sliding it by exactly one column width lands it
        // on the next option: no arithmetic against the panel's width, and no drift if the panel
        // is resized later.
        var thumb = new Border
        {
            CornerRadius = new CornerRadius((ControlHeight - 2 * Inset) / 2),
            Background = new SolidColorBrush(Formatting.Popup.Ok),
            RenderTransform = _thumbShift
        };
        grid.Children.Add(thumb);

        for (var index = 0; index < labels.Count; index++)
        {
            var label = new TextBlock
            {
                Text = labels[index],
                FontFamily = new FontFamily(Constants.UiFont),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(index == _selected ? Formatting.Popup.Background : Formatting.Popup.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _options.Add(label);

            var segment = new Border
            {
                // Transparent still hit-tests, which is what makes the whole segment clickable
                // rather than only the glyphs - the plate under it is only a few pixels tall.
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = label
            };
            var captured = index;
            segment.MouseLeftButtonUp += (_, _) => Select(captured);
            Grid.SetColumn(segment, index);
            grid.Children.Add(segment);
        }

        Child = grid;
        // The width is known from here on; the first layout pass places the block.
        SizeChanged += (_, _) => MoveThumb(animate: false);
    }

    private double ColumnWidth => Math.Max(1, (ActualWidth - 2 * Inset) / Math.Max(1, _options.Count));

    private void Select(int index)
    {
        if (index == _selected)
        {
            return;
        }

        _selected = index;
        MoveThumb(animate: true);
        for (var option = 0; option < _options.Count; option++)
        {
            _options[option].Foreground = new SolidColorBrush(
                option == _selected ? Formatting.Popup.Background : Formatting.Popup.Text);
        }

        _pick(index);
    }

    private void MoveThumb(bool animate)
    {
        var target = _selected * ColumnWidth;
        if (!animate)
        {
            _thumbShift.BeginAnimation(TranslateTransform.XProperty, null);
            _thumbShift.X = target;
            return;
        }

        _thumbShift.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(SlideMilliseconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }
}

// The autostart switch: same plate colour, green knob as the moving option, so on/off reads as
// position plus colour instead of a check mark tacked onto the label.
public sealed class ToggleSwitch : Border
{
    private const double TrackWidth = 34;
    private const double TrackHeight = 18;
    private const double KnobSize = 14;
    private const double Inset = 2;
    private const int SlideMilliseconds = 150;

    private readonly TranslateTransform _knobShift = new();
    private readonly Ellipse _knob = new()
    {
        Width = KnobSize,
        Height = KnobSize,
        Margin = new Thickness(Inset, 0, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center
    };

    public ToggleSwitch(bool on)
    {
        Width = TrackWidth;
        Height = TrackHeight;
        CornerRadius = new CornerRadius(TrackHeight / 2);
        Background = new SolidColorBrush(Formatting.Popup.Track);
        _knob.RenderTransform = _knobShift;
        Child = _knob;
        Set(on, animate: false);
    }

    public void Set(bool on, bool animate = true)
    {
        _knob.Fill = new SolidColorBrush(on ? Formatting.Popup.Ok : Formatting.Popup.Idle);

        var target = on ? TrackWidth - KnobSize - 2 * Inset : 0;
        if (!animate)
        {
            _knobShift.BeginAnimation(TranslateTransform.XProperty, null);
            _knobShift.X = target;
            return;
        }

        _knobShift.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(SlideMilliseconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }
}
