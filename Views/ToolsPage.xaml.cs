namespace MediaWorkflowOrchestrator.Views
{
    public sealed partial class ToolsPage : Page
    {
        public ToolsPage()
        {
            InitializeComponent();
        }

        public ToolsViewModel ViewModel { get; } = new();
    }
}
