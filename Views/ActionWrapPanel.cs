using Windows.Foundation;

namespace MediaWorkflowOrchestrator.Views
{
    // CommandBar provides overflow, but not multiple rows of variable-width controls.
    // This panel preserves native Button/ToggleButton behavior while wrapping their natural sizes.
    public sealed class ActionWrapPanel : Panel
    {
        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
            nameof(Spacing), typeof(double), typeof(ActionWrapPanel), new PropertyMetadata(8d, OnSpacingChanged));

        public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
            nameof(RowSpacing), typeof(double), typeof(ActionWrapPanel), new PropertyMetadata(8d, OnSpacingChanged));

        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        public double RowSpacing
        {
            get => (double)GetValue(RowSpacingProperty);
            set => SetValue(RowSpacingProperty, value);
        }

        private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
            => ((ActionWrapPanel)sender).InvalidateMeasure();

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                {
                    child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
                }
            }
            return Layout(availableSize.Width, arrange: false);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Layout(finalSize.Width, arrange: true);
            return finalSize;
        }

        private Size Layout(double availableWidth, bool arrange)
        {
            double x = 0, y = 0, rowHeight = 0, widestRow = 0;
            var hasItem = false;
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var width = Math.Min(child.DesiredSize.Width, availableWidth);
                var height = child.DesiredSize.Height;
                var gap = hasItem ? Math.Max(0, Spacing) : 0;
                if (hasItem && x + gap + width > availableWidth)
                {
                    widestRow = Math.Max(widestRow, x);
                    y += rowHeight + Math.Max(0, RowSpacing);
                    x = rowHeight = gap = 0;
                }

                x += gap;
                if (arrange)
                {
                    child.Arrange(new Rect(x, y, width, height));
                }
                x += width;
                rowHeight = Math.Max(rowHeight, height);
                hasItem = true;
            }
            return new Size(Math.Max(widestRow, x), y + rowHeight);
        }
    }
}
