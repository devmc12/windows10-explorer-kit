// Keeps related action groups at opposite edges without overflowing narrow windows
using System;
using System.Windows;
using System.Windows.Controls;

namespace Win10ExplorerKit.UI.Services
{
    internal sealed class SeparatedActionsPanel : Panel
    {
        // Minimum horizontal separation between action groups in device-independent pixels
        private const double HorizontalGap = 16;
        // Vertical separation when a narrow window requires two rows
        private const double VerticalGap = 8;

        /// <summary>Measures both groups at their natural width before deciding whether to wrap</summary>
        /// <param name="availableSize">Available layout area</param><returns>Required panel size</returns>
        protected override Size MeasureOverride(Size availableSize)
        {
            if (InternalChildren.Count != 2) return new Size();
            foreach (UIElement child in InternalChildren) child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            Size left = InternalChildren[0].DesiredSize, right = InternalChildren[1].DesiredSize;
            bool wrap = left.Width + HorizontalGap + right.Width > availableSize.Width;
            return new Size(Math.Min(availableSize.Width, left.Width + HorizontalGap + right.Width),
                wrap ? left.Height + VerticalGap + right.Height : Math.Max(left.Height, right.Height));
        }

        /// <summary>Places the primary group left and utility group right, wrapping only when necessary</summary>
        /// <param name="finalSize">Assigned panel size</param><returns>Arranged size</returns>
        protected override Size ArrangeOverride(Size finalSize)
        {
            if (InternalChildren.Count != 2) return finalSize;
            Size left = InternalChildren[0].DesiredSize, right = InternalChildren[1].DesiredSize;
            bool wrap = left.Width + HorizontalGap + right.Width > finalSize.Width;
            double firstRowHeight = wrap ? left.Height : finalSize.Height;
            InternalChildren[0].Arrange(new Rect(0, (firstRowHeight - left.Height) / 2, left.Width, left.Height));
            InternalChildren[1].Arrange(new Rect(Math.Max(0, finalSize.Width - right.Width),
                wrap ? left.Height + VerticalGap : (finalSize.Height - right.Height) / 2, right.Width, right.Height));
            return finalSize;
        }
    }
}
