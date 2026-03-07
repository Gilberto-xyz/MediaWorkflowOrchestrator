using MediaWorkflowOrchestrator.Models;

namespace MediaWorkflowOrchestrator.Persistence
{
    public interface IAppSettingsService
    {
        Task<AppSettings> LoadAsync();
        Task SaveAsync(AppSettings settings);
        Task<AppSettings> RestoreDefaultsAsync();
    }
}
