namespace MediaWorkflowOrchestrator.Views
{
    public sealed partial class HistoryPage : Page
    {
        public HistoryPage()
        {
            InitializeComponent();
        }

        public HistoryViewModel ViewModel { get; } = new();
    }
}
