using Avalonia.Animation;
using Avalonia.Animation.Easings;

namespace v2rayN.Desktop.Satra.Controls;

/// <summary>
/// iOS-style segmented control: a raised thumb slides under the chosen label (spring-ish ease), the label turns
/// bold immediately, and the control's own state is the source of truth — so the selection is always visible
/// even if the page doesn't rebuild.
/// </summary>
public class SlideSegmented : Border
{
    private readonly Grid _grid = new();
    private readonly Border _thumb;
    private readonly List<TextBlock> _labels = [];
    private readonly Action<int> _pick;
    public int Selected { get; private set; }

    public SlideSegmented(string[] labels, int selected, Action<int> pick)
    {
        _pick = pick;
        Selected = selected;
        Background = P.B(P.Fill);
        CornerRadius = Ui.RCtl;
        Padding = new Thickness(3);
        _thumb = new Border
        {
            CornerRadius = new CornerRadius(9), Background = P.B(P.Raised), HorizontalAlignment = HorizontalAlignment.Left,
            BoxShadow = BoxShadows.Parse("0 2 8 0 #40000000"),
            RenderTransform = TransformOperations.Parse("translateX(0px)"),
            Transitions = new Transitions { new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new CubicEaseOut() } },
        };
        var row = new UniformGrid { Rows = 1 };
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var tb = new TextBlock { Text = labels[i], FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _labels.Add(tb);
            var cell = new Border { Padding = new Thickness(10, 7), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            cell.PointerReleased += (_, _) => Select(idx, true);
            row.Children.Add(cell);
        }
        _grid.Children.Add(_thumb);
        _grid.Children.Add(row);
        Child = _grid;
        _grid.SizeChanged += (_, _) => Place(false);
        Paint();
    }

    public void Select(int i, bool notify)
    {
        if (i == Selected && notify) return;
        Selected = i;
        Paint();
        Place(true);
        if (notify) _pick(i);
    }

    private void Paint()
    {
        for (var i = 0; i < _labels.Count; i++)
        {
            var on = i == Selected;
            _labels[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Medium;
            _labels[i].Foreground = P.B(on ? P.Text : P.Text2);
        }
    }

    private void Place(bool animate)
    {
        var w = _grid.Bounds.Width / Math.Max(1, _labels.Count);
        if (w <= 0) return;
        _thumb.Width = w;
        var x = (FlowDirection == FlowDirection.RightToLeft ? -1 : 1) * 0 + w * Selected;
        if (!animate)
        {
            var t = _thumb.Transitions;
            _thumb.Transitions = null;
            _thumb.RenderTransform = TransformOperations.Parse($"translateX({x:0.##}px)");
            _thumb.Transitions = t;
        }
        else _thumb.RenderTransform = TransformOperations.Parse($"translateX({x:0.##}px)");
    }
}
