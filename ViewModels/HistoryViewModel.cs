using System.Collections.ObjectModel;
using MediaWorkflowOrchestrator.Messages;

namespace MediaWorkflowOrchestrator.ViewModels
{
    public partial class HistoryViewModel : BaseViewModel
    {
        private readonly IWorkflowExecutionService workflowExecutionService = App.Host.WorkflowExecutionService;

        public HistoryViewModel()
        {
            Title = "Historial";
            WorkflowHistory = new ObservableCollection<WorkflowInstance>();
            _ = RefreshAsync();
        }

        public ObservableCollection<WorkflowInstance> WorkflowHistory { get; }

        [ObservableProperty]
        private WorkflowInstance? _selectedWorkflow;

        public string SelectedDisplayName => SelectedWorkflow?.DisplayName ?? "Sin selección";
        public string SelectedRootPath => SelectedWorkflow?.RootPath ?? string.Empty;
        public string SelectedSummary => SelectedWorkflow?.LastExecutionSummary ?? "Selecciona un workflow para ver su resumen.";

        partial void OnSelectedWorkflowChanged(WorkflowInstance? value)
        {
            OnPropertyChanged(nameof(SelectedDisplayName));
            OnPropertyChanged(nameof(SelectedRootPath));
            OnPropertyChanged(nameof(SelectedSummary));
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            WorkflowHistory.Clear();
            var history = await workflowExecutionService.LoadHistoryAsync();
            foreach (var workflow in history)
            {
                WorkflowHistory.Add(workflow);
            }

            SelectedWorkflow ??= WorkflowHistory.FirstOrDefault();
        }

        [RelayCommand]
        private void ContinueWorkflow()
        {
            if (SelectedWorkflow is null)
            {
                return;
            }

            WeakReferenceMessenger.Default.Send(new WorkflowSelectedMessage(SelectedWorkflow.Id));
        }
    }
}
