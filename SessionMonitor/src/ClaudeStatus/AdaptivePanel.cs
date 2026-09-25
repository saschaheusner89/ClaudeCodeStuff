using System.Windows;
using System.Windows.Controls;
using ClaudeStatus.Core;

namespace ClaudeStatus;

/// <summary>
/// Fills its whole area with its children. The grid adapts to the panel's shape:
/// a wide strip gives one row, a tall strip one column, everything else a balanced grid.
/// </summary>
public sealed class AdaptivePanel : Panel
{
    public double PreferredAspect { get; set; } = 1.6;
    public double Gap { get; set; } = 6;

    protected override Size MeasureOverride(Size available)
    {
        var size = new Size(
            double.IsInfinity(available.Width) ? 400 : available.Width,
            double.IsInfinity(available.Height) ? 300 : available.Height);
        var visible = VisibleChildren();
        var cells = GridLayout.Arrange(visible.Count, size.Width, size.Height, PreferredAspect, Gap);
        for (int i = 0; i < visible.Count; i++)
            visible[i].Measure(new Size(cells[i].Width, cells[i].Height));
        return size;
    }

    protected override Size ArrangeOverride(Size final)
    {
        var visible = VisibleChildren();
        var cells = GridLayout.Arrange(visible.Count, final.Width, final.Height, PreferredAspect, Gap);
        for (int i = 0; i < visible.Count; i++)
            visible[i].Arrange(new Rect(cells[i].X, cells[i].Y, cells[i].Width, cells[i].Height));
        return final;
    }

    private List<UIElement> VisibleChildren() =>
        InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
}
