using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PhasmaStrap.UI
{
    public static class WheelRouter
    {
        public static readonly DependencyProperty OptOutProperty = DependencyProperty.RegisterAttached(
            "OptOut", typeof(bool), typeof(WheelRouter), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

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

                if (node is ComboBox || node is Slider || node is RangeBase)
                    nearest = false;
            }

            if (target is null)
                return;

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
