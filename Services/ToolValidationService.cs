namespace MediaWorkflowOrchestrator.Services
{
    public sealed class ToolValidationService : IToolValidationService
    {
        public Task<IReadOnlyList<ToolValidationResult>> ValidateAllAsync(AppSettings settings)
        {
            var results = new List<ToolValidationResult>
            {
                ValidatePath("python", "Python", settings.PythonPath, allowCommandAlias: true),
                ValidatePath("downloader", "Script Nyaa", settings.DownloaderScriptPath),
                ValidatePath("downloaderlink", "Script Nyaa por link", settings.DownloaderLinkScriptPath),
                ValidatePath("cleanup", "Script limpiar tracks", settings.TrackCleanupScriptPath),
                ValidatePath("tagrename", "Script etiquetas", settings.TagAndRenameScriptPath),
                ValidatePath("rarpack", "Script RAR", settings.RarPackagingScriptPath),
                ValidatePath("mkvmerge", "mkvmerge", settings.MkvmergePath),
                ValidatePath("mkvpropedit", "mkvpropedit", settings.MkvpropeditPath),
                ValidatePath("rar", "rar.exe", settings.RarExePath),
            };

            return Task.FromResult<IReadOnlyList<ToolValidationResult>>(results);
        }

        private static ToolValidationResult ValidatePath(string key, string displayName, string path, bool allowCommandAlias = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ToolValidationResult
                {
                    ToolKey = key,
                    DisplayName = displayName,
                    State = ToolValidationState.Incomplete,
                    Message = "No configurado.",
                };
            }

            if (allowCommandAlias && !path.Contains(Path.DirectorySeparatorChar) && !path.Contains(Path.AltDirectorySeparatorChar))
            {
                return new ToolValidationResult
                {
                    ToolKey = key,
                    DisplayName = displayName,
                    State = ToolValidationState.Available,
                    Message = $"Se usará el alias de comando '{path}'.",
                    Path = path,
                };
            }

            return File.Exists(path)
                ? new ToolValidationResult
                {
                    ToolKey = key,
                    DisplayName = displayName,
                    State = ToolValidationState.Available,
                    Message = "Ruta válida.",
                    Path = path,
                }
                : new ToolValidationResult
                {
                    ToolKey = key,
                    DisplayName = displayName,
                    State = ToolValidationState.Missing,
                    Message = "No se encontró el archivo configurado.",
                    Path = path,
                };
        }

    }
}
