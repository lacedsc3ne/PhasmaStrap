using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PhasmaStrap.UI
{
    /// <summary>
    /// Makes the mouse wheel always scroll something sensible, whatever is under the pointer.
    ///
    /// WPF hands the wheel to the innermost control first. A list that is already at its end, a closed
    /// combo box, a number box or a horizontal strip will swallow the wheel even though it cannot use it,
    /// and the page behind it stops scrolling. This router looks at the window level, before any control
    /// sees the event, finds the nearest scroll viewer under the pointer that can actually move in the
    /// wheel's direction, and scrolls that one directly.
    ///
    /// Hold Ctrl, Shift or Alt to give the wheel back to the control under the pointer (zooming,
    /// horizontal scrolling, changing a value on purpose).
    /// </summary>
    public static class WheelRouter
    {
        public static readonly DependencyProperty OptOutProperty = DependencyProperty.RegisterAttached(
            "OptOut", typeof(bool), typeof(WheelRouter), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>Set on an element whose subtree should keep its own wheel handling (canvases, editors).</summary>
        public static bool GetOptOut(DependencyObject obj) => (bool)obj.GetValue(OptOutProperty);

        public static void SetOptOut(DependencyObject obj, bool value) => obj.SetValue(OptOutProperty, value);

        private const double LinePixels = 48;

        public static void Attach(Window window)
        {
            window.PreviewMouseWheel -= OnPreviewMouseWheel;
            window.PreviewMouseWheel += OnPreviewMouseWheel;
        }

        public static void Detach(Window window) => window.PreviewMouseWheel -= OnPreviewMouseWheel;

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (e.OriginalSource is not DependencyObject source)
                return;

            if (source is FrameworkElement fe && GetOptOut(fe))
                return;

            bool up = e.Delta > 0;
            ScrollViewer? target = null;
            bool nearest = true;

            int guard = 0;
            for (DependencyObject? node = source; node is not null && guard++ < 400; node = Parent(node))
            {
                // Inside an open drop down or flyout with nothing left to scroll: leave the page alone.
                if (node is Popup)
                    return;

                if (node is ScrollViewer viewer)
                {
                    if (CanScroll(viewer, up))
                    {
                        target = viewer;
                        break;
                    }

                    nearest = false;
                    continue;
                }

                // A control that changes its value on the wheel (closed combo box, slider, number box)
                // must never eat a wheel meant for the page.
                if (node is ComboBox || node is Slider || node is RangeBase)
                    nearest = false;
            }

            if (target is null)
                return;

            // The innermost scroller can move: let it do so with its own smooth handling.
            if (nearest)
                return;

            e.Handled = true;

            double lines = e.Delta / 120.0;
            double offset = target.VerticalOffset - lines * (target.CanContentScroll ? 3 : LinePixels);
            target.ScrollToVerticalOffset(Math.Clamp(offset, 0, target.ScrollableHeight));
        }

        private static bool CanScroll(ScrollViewer viewer, bool up)
        {
            if (viewer.ScrollableHeight <= 0.5 || viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
                return false;

            return up ? viewer.VerticalOffset > 0.5 : viewer.VerticalOffset < viewer.ScrollableHeight - 0.5;
        }

        private static DependencyObject? Parent(DependencyObject node)
        {
            if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            {
                DependencyObject? visualParent = VisualTreeHelper.GetParent(node);
                if (visualParent is not null)
                    return visualParent;
            }

            // Popups (open combo box lists, flyouts) start a new visual tree; hop back to the owner.
            if (node is FrameworkElement element && element.Parent is not null)
                return element.Parent;

            if (node is FrameworkElement root && root.TemplatedParent is not null)
                return root.TemplatedParent;

            if (node is FrameworkElement popupRoot && popupRoot.Parent is null && LogicalTreeHelper.GetParent(popupRoot) is DependencyObject logical)
                return logical;

            return node is FrameworkContentElement content ? content.Parent : null;
        }
    }
}
