namespace MediaWorkflowOrchestrator.Views
{
    internal static class ResponsiveLayout
    {
        internal static Thickness PagePadding(double width) => (Thickness)Application.Current.Resources[
            width < 760 ? "PagePaddingNarrow" : "PagePaddingDefault"];

        internal static void ApplyGrid(Grid grid, bool stacked, GridLength[] columnWidths,
            params (int row, int column)[] positions)
        {
            if (grid.Children.Count < positions.Length || positions.Length == 0)
            {
                return;
            }

            // Empty Auto rows still contribute RowSpacing. Keep only occupied rows,
            // recreating them when a toolbar wraps after a window resize.
            var rowCount = stacked ? positions.Length : positions.Max(position => position.row) + 1;
            while (grid.RowDefinitions.Count > rowCount)
            {
                grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
            }
            while (grid.RowDefinitions.Count < rowCount)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (var column = 0; column < grid.ColumnDefinitions.Count; column++)
            {
                grid.ColumnDefinitions[column].Width = stacked
                    ? column == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0)
                    : column < columnWidths.Length ? columnWidths[column] : new GridLength(0);
            }

            for (var index = 0; index < positions.Length; index++)
            {
                if (grid.Children[index] is FrameworkElement child)
                {
                    Grid.SetRow(child, stacked ? index : positions[index].row);
                    Grid.SetColumn(child, stacked ? 0 : positions[index].column);
                }
            }
        }
    }
}
