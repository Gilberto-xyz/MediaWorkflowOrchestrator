namespace MediaWorkflowOrchestrator.Views
{
    public sealed partial class HistoryPage : Page
    {
        private const double WideLayoutBreakpoint = 1040;
        private const double NarrowLayoutBreakpoint = 760;

        public HistoryPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnPageSizeChanged;
        }

        public HistoryViewModel ViewModel { get; } = new();

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
            HistoryLayoutRoot.Padding = ResponsiveLayout.PagePadding(width);

            ApplyResponsiveGrid(HistoryHeaderGrid, width < WideLayoutBreakpoint, new[] { Star(), GridLength.Auto }, (0, 0), (0, 1));
            ApplyResponsiveGrid(
                HistoryContentGrid,
                width < WideLayoutBreakpoint,
                new[] { Star(1.15), Star(0.85) },
                (0, 0),
                (0, 1));
        }

        private static void ApplyResponsiveGrid(Grid grid, bool stacked, GridLength[] wideColumnWidths, params (int row, int column)[] widePositions)
            => ResponsiveLayout.ApplyGrid(grid, stacked, wideColumnWidths, widePositions);

        private static GridLength Star(double value = 1) => new(value, GridUnitType.Star);
    }
}
