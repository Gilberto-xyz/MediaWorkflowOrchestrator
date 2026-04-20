namespace MediaWorkflowOrchestrator.Views
{
    public sealed partial class ToolsPage : Page
    {
        private const double WideLayoutBreakpoint = 980;
        private const double NarrowLayoutBreakpoint = 760;

        public ToolsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnPageSizeChanged;
        }

        public ToolsViewModel ViewModel { get; } = new();

        private void OnLoaded(object sender, RoutedEventArgs e) => UpdateResponsiveLayout(ActualWidth);

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
            SizeChanged -= OnPageSizeChanged;
        }

        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout(e.NewSize.Width);

        private void UpdateResponsiveLayout(double width)
        {
            ToolsLayoutRoot.Padding = width < NarrowLayoutBreakpoint
                ? new Thickness(12, 10, 12, 16)
                : width < WideLayoutBreakpoint
                    ? new Thickness(16, 14, 16, 20)
                    : new Thickness(24, 18, 24, 28);

            ApplyResponsiveGrid(ToolsHeaderGrid, width < WideLayoutBreakpoint, new[] { Star(), GridLength.Auto }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsBaseEnvironmentGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsBinaryPathsGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsTranslationGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsRarFlagsGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsRarCaptureGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsValidationHeaderGrid, width < WideLayoutBreakpoint, new[] { Star(), GridLength.Auto }, (0, 0), (0, 1));
        }

        private static void ApplyResponsiveGrid(Grid grid, bool stacked, GridLength[] wideColumnWidths, params (int row, int column)[] widePositions)
        {
            if (grid.Children.Count < widePositions.Length)
            {
                return;
            }

            if (stacked)
            {
                grid.ColumnDefinitions[0].Width = Star();
                for (var column = 1; column < grid.ColumnDefinitions.Count; column++)
                {
                    grid.ColumnDefinitions[column].Width = new GridLength(0);
                }

                for (var index = 0; index < widePositions.Length; index++)
                {
                    if (grid.Children[index] is FrameworkElement child)
                    {
                        Grid.SetColumn(child, 0);
                        Grid.SetRow(child, index);
                    }
                }

                return;
            }

            for (var column = 0; column < grid.ColumnDefinitions.Count; column++)
            {
                grid.ColumnDefinitions[column].Width = column < wideColumnWidths.Length
                    ? wideColumnWidths[column]
                    : GridLength.Auto;
            }

            for (var index = 0; index < widePositions.Length; index++)
            {
                if (grid.Children[index] is FrameworkElement child)
                {
                    Grid.SetRow(child, widePositions[index].row);
                    Grid.SetColumn(child, widePositions[index].column);
                }
            }
        }

        private static GridLength Star(double value = 1) => new(value, GridUnitType.Star);
    }
}
