using System.Collections.ObjectModel;

namespace MediaWorkflowOrchestrator.ViewModels
{
    public partial class ToolsViewModel : BaseViewModel
    {
        private readonly IWorkflowExecutionService workflowExecutionService = App.Host.WorkflowExecutionService;

        public ToolsViewModel()
        {
            Title = "Herramientas";
            ValidationResults = new ObservableCollection<ToolValidationResult>();
            _ = LoadAsync();
        }

        public ObservableCollection<ToolValidationResult> ValidationResults { get; }

        [ObservableProperty]
        private AppSettings _settings = AppSettings.CreateDefault();

        [ObservableProperty]
        private string _rarPassword = string.Empty;

        [RelayCommand]
        private async Task SaveSettingsAsync()
        {
            await workflowExecutionService.SaveSettingsAsync(Settings, RarPassword);
            await ValidateAllToolsAsync();
        }

        [RelayCommand]
        private async Task RestoreDefaultsAsync()
        {
            Settings = await workflowExecutionService.RestoreDefaultSettingsAsync();
            RarPassword = workflowExecutionService.GetDecryptedRarPassword(Settings);
            await ValidateAllToolsAsync();
        }

        [RelayCommand]
        private async Task ValidateAllToolsAsync()
        {
            ValidationResults.Clear();
            var results = await workflowExecutionService.ValidateToolsAsync();
            foreach (var item in results)
            {
                ValidationResults.Add(item);
            }
        }

        private async Task LoadAsync()
        {
            Settings = await workflowExecutionService.GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(Settings.EncryptedRarPassword))
            {
                Settings.EncryptedRarPassword = App.Host.SecretProtector.Protect("GDRIVELatinoHD.NET");
                await workflowExecutionService.SaveSettingsAsync(Settings, "GDRIVELatinoHD.NET");
            }

            RarPassword = workflowExecutionService.GetDecryptedRarPassword(Settings);
            await ValidateAllToolsAsync();
        }
    }
}
