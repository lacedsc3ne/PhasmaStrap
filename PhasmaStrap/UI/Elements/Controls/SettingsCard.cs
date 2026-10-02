using System.Windows;
using System.Windows.Controls;

namespace PhasmaStrap.UI.Elements.Controls
{
    /// <summary>
    /// A titled group of settings drawn as one card. Rows inside (OptionControl) turn flat and get a line between them.
    /// The look lives in UI/Style/Phasma.xaml.
    /// </summary>
    public class SettingsCard : ItemsControl
    {
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

        /// <summary>Optional element shown on the right of the card title, like a status tag or a button.</summary>
        public static readonly DependencyProperty HeaderContentProperty =
            DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(SettingsCard));

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public object? HeaderContent
        {
            get => GetValue(HeaderContentProperty);
            set => SetValue(HeaderContentProperty, value);
        }

        public SettingsCard()
        {
            SetResourceReference(StyleProperty, "PhasmaSettingsCard");
        }

        // Rows are real elements, not data, so they are their own containers.
        protected override bool IsItemItsOwnContainerOverride(object item) => item is UIElement;
    }

    /// <summary>Lays cards out two to a row when there is room, and one per row when the window is narrow.</summary>
    public class CardColumns : Panel
    {
        public static readonly DependencyProperty ColumnsProperty =
            DependencyProperty.Register(nameof(Columns), typeof(int), typeof(CardColumns), new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty MinColumnWidthProperty =
            DependencyProperty.Register(nameof(MinColumnWidth), typeof(double), typeof(CardColumns), new FrameworkPropertyMetadata(340.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty SpacingProperty =
            DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(CardColumns), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>Set on a child to make it take the whole row.</summary>
        public static readonly DependencyProperty FullRowProperty =
            DependencyProperty.RegisterAttached("FullRow", typeof(bool), typeof(CardColumns), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

        public static bool GetFullRow(DependencyObject element) => (bool)element.GetValue(FullRowProperty);

        public static void SetFullRow(DependencyObject element, bool value) => element.SetValue(FullRowProperty, value);

        public int Columns
        {
            get => (int)GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        public double MinColumnWidth
        {
            get => (double)GetValue(MinColumnWidthProperty);
            set => SetValue(MinColumnWidthProperty, value);
        }

        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        private int ColumnsFor(double width)
        {
            if (double.IsInfinity(width) || double.IsNaN(width))
                return 1;

            int columns = Math.Max(1, Columns);
            while (columns > 1 && (width - Spacing * (columns - 1)) / columns < MinColumnWidth)
                columns--;

            return columns;
        }

        private IEnumerable<List<UIElement>> Rows(int columns)
        {
            var row = new List<UIElement>();

            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;

                if (GetFullRow(child) && row.Count > 0)
                {
                    yield return row;
                    row = new List<UIElement>();
                }

                row.Add(child);

                if (GetFullRow(child) || row.Count == columns)
                {
                    yield return row;
                    row = new List<UIElement>();
                }
            }

            if (row.Count > 0)
                yield return row;
        }

        private double CellWidth(double width, int columns, List<UIElement> row) =>
            row.Count == 1 && GetFullRow(row[0]) ? width : (width - Spacing * (columns - 1)) / columns;

        protected override Size MeasureOverride(Size available)
        {
            int columns = ColumnsFor(available.Width);
            double width = double.IsInfinity(available.Width) ? 0 : available.Width;
            double height = 0;
            double widest = 0;
            bool first = true;

            foreach (List<UIElement> row in Rows(columns))
            {
                double cell = width > 0 ? CellWidth(width, columns, row) : double.PositiveInfinity;
                double tallest = 0;

                foreach (UIElement child in row)
                {
                    child.Measure(new Size(cell, double.PositiveInfinity));
                    tallest = Math.Max(tallest, child.DesiredSize.Height);
                    widest = Math.Max(widest, child.DesiredSize.Width);
                }

                height += tallest + (first ? 0 : Spacing);
                first = false;
            }

            return new Size(width > 0 ? width : widest, height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            int columns = ColumnsFor(final.Width);
            double y = 0;

            foreach (List<UIElement> row in Rows(columns))
            {
                double cell = CellWidth(final.Width, columns, row);
                double tallest = row.Max(c => c.DesiredSize.Height);
                double x = 0;

                foreach (UIElement child in row)
                {
                    // Cards in the same row share a height so their edges line up.
                    child.Arrange(new Rect(x, y, cell, tallest));
                    x += cell + Spacing;
                }

                y += tallest + Spacing;
            }

            return final;
        }
    }
}
