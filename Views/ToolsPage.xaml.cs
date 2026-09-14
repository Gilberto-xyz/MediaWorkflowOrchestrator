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
            ToolsLayoutRoot.Padding = ResponsiveLayout.PagePadding(width);

            ApplyResponsiveGrid(ToolsHeaderGrid, width < WideLayoutBreakpoint, new[] { Star(), GridLength.Auto }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsBaseEnvironmentGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsScriptPathsGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1), (1, 0), (1, 1), (2, 0));
            ApplyResponsiveGrid(ToolsBinaryPathsGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsRarFlagsGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsRarCaptureGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsPublicationAppsGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1));
            ApplyResponsiveGrid(ToolsPublicationDestinationsGrid, width < WideLayoutBreakpoint, new[] { Star(), Star() }, (0, 0), (0, 1), (1, 0), (1, 1));
            ApplyResponsiveGrid(ToolsValidationHeaderGrid, width < WideLayoutBreakpoint, new[] { Star(), GridLength.Auto }, (0, 0), (0, 1));
        }

        private static void ApplyResponsiveGrid(Grid grid, bool stacked, GridLength[] wideColumnWidths, params (int row, int column)[] widePositions)
            => ResponsiveLayout.ApplyGrid(grid, stacked, wideColumnWidths, widePositions);

        private static GridLength Star(double value = 1) => new(value, GridUnitType.Star);
    }
}
