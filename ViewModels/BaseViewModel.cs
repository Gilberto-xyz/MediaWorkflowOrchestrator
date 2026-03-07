namespace MediaWorkflowOrchestrator.ViewModels
{
    public partial class BaseViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = string.Empty;
    }
}
