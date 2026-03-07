using System.Text.Json;
using MediaWorkflowOrchestrator.Models;

namespace MediaWorkflowOrchestrator.Persistence
{
    public sealed class AppSettingsService : IAppSettingsService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

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
            var settingsFromDisk = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions);
            return settingsFromDisk ?? AppSettings.CreateDefault();
        }

        public async Task SaveAsync(AppSettings settings)
        {
            AppDataPaths.EnsureAll();
            await using var stream = File.Create(AppDataPaths.SettingsPath);
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions);
        }

        public async Task<AppSettings> RestoreDefaultsAsync()
        {
            var defaults = AppSettings.CreateDefault();
            await SaveAsync(defaults);
            return defaults;
        }
    }
}
