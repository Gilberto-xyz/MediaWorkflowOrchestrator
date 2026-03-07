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
                ValidatePath("translator", "Script traducción", settings.SubtitleTranslatorScriptPath),
                ValidatePath("cleanup", "Script limpiar tracks", settings.TrackCleanupScriptPath),
                ValidatePath("tagrename", "Script etiquetas", settings.TagAndRenameScriptPath),
                ValidatePath("rarpack", "Script RAR", settings.RarPackagingScriptPath),
                ValidatePath("mkvmerge", "mkvmerge", settings.MkvmergePath),
                ValidatePath("mkvpropedit", "mkvpropedit", settings.MkvpropeditPath),
                ValidatePath("rar", "rar.exe", settings.RarExePath),
                ValidatePath("ollamaexe", "Ollama", settings.OllamaExePath),
                ValidateUrl("ollamahost", "Ollama host", settings.OllamaHost),
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

        private static ToolValidationResult ValidateUrl(string key, string displayName, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return new ToolValidationResult
                {
                    ToolKey = key,
                    DisplayName = displayName,
                    State = ToolValidationState.Available,
                    Message = "Host válido.",
                    Path = url,
                };
            }

            return new ToolValidationResult
            {
                ToolKey = key,
                DisplayName = displayName,
                State = ToolValidationState.Incomplete,
                Message = "La URL no es válida.",
                Path = url,
            };
        }
    }
}
