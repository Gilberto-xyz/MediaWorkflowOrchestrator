namespace MediaWorkflowOrchestrator.ViewModels
{
    public partial class MainWindowViewModel : BaseViewModel
    {
        [ObservableProperty]
        private string _windowTitle = "Media Workflow Orchestrator";

        [ObservableProperty]
        private string _selectedNavigationTag = "dashboard";
    }
}
