using System.Windows;
using System.Windows.Media;

namespace PhasmaStrap.UI
{
    public static class CutCorners
    {
        public static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached(
            "Size", typeof(double), typeof(CutCorners), new PropertyMetadata(0.0, OnSizeChanged));

        public static double GetSize(DependencyObject d) => (double)d.GetValue(SizeProperty);

        public static void SetSize(DependencyObject d, double value) => d.SetValue(SizeProperty, value);

        private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            element.SizeChanged -= Element_SizeChanged;

            if ((double)e.NewValue > 0)
            {
                element.SizeChanged += Element_SizeChanged;
                Update(element);
            }
            else
            {
                element.ClearValue(UIElement.ClipProperty);
            }
        }

        private static void Element_SizeChanged(object sender, SizeChangedEventArgs e) => Update((FrameworkElement)sender);

        private static void Update(FrameworkElement element)
        {
            double w = element.ActualWidth, h = element.ActualHeight;

            if (w <= 0 || h <= 0)
                return;

            double c = Math.Min(GetSize(element), Math.Min(w, h) / 2);

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(c, 0), true, true);
                ctx.PolyLineTo(new[]
                {
                    new Point(w, 0),
                    new Point(w, h - c),
                    new Point(w - c, h),
                    new Point(0, h),
                    new Point(0, c)
                }, true, false);
            }
            geometry.Freeze();
            element.Clip = geometry;
        }
    }
}
