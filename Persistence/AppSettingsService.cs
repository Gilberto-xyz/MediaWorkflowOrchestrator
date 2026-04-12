using System.Text.Json;
using MediaWorkflowOrchestrator.Models;

namespace MediaWorkflowOrchestrator.Persistence
{
    public sealed class AppSettingsService : IAppSettingsService
    {
        public async Task<AppSettings> LoadAsync()
        {
            AppDataPaths.EnsureAll();
            if (!File.Exists(AppDataPaths.SettingsPath))
            {
                var settings = AppSettings.CreateDefault();
                await SaveAsync(settings);
                return settings;
            }

            await using var stream = File.OpenRead(AppDataPaths.SettingsPath);
            var settingsFromDisk = await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonSerializerContext.Default.AppSettings);
            return settingsFromDisk ?? AppSettings.CreateDefault();
        }

        public async Task SaveAsync(AppSettings settings)
        {
            AppDataPaths.EnsureAll();
            await using var stream = File.Create(AppDataPaths.SettingsPath);
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                AppJsonSerializerContext.Default.AppSettings);
        }

        public async Task<AppSettings> RestoreDefaultsAsync()
        {
            var defaults = AppSettings.CreateDefault();
            await SaveAsync(defaults);
            return defaults;
        }
    }
}
