using System.Text.Json;

namespace MediaWorkflowOrchestrator.Services
{
    public sealed class SubtitleInspectorService : ISubtitleInspectorService
    {
        private static readonly string[] SpanishNameHints =
        {
            "spanish",
            "español",
            "espanol",
            "latino",
            "latam",
            "castellano",
            "es-419",
        };

        private readonly IProcessRunnerService processRunnerService;

        public SubtitleInspectorService(IProcessRunnerService processRunnerService)
        {
            this.processRunnerService = processRunnerService;
        }

        public async Task<SubtitleInspectionResult> InspectAsync(string path, AppSettings settings, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new SubtitleInspectionResult
                {
                    Availability = SubtitleSpanishAvailability.Unknown,
                    Message = "No se encontró el archivo principal para inspección."
                };
            }

            if (!File.Exists(settings.MkvmergePath))
            {
                return new SubtitleInspectionResult
                {
                    Availability = SubtitleSpanishAvailability.Unknown,
                    Message = "mkvmerge no está disponible; se requiere decisión manual."
                };
            }

            var result = await processRunnerService.RunAsync(
                new ProcessExecutionRequest
                {
                    FileName = settings.MkvmergePath,
                    Arguments = new[] { "-J", path },
                    WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
                },
                null,
                cancellationToken);

            if (!result.Success)
            {
                return new SubtitleInspectionResult
                {
                    Availability = SubtitleSpanishAvailability.Unknown,
                    Message = "La inspección con mkvmerge falló; se requiere decisión manual."
                };
            }

            try
            {
                using var doc = JsonDocument.Parse(result.StandardOutput);
                if (!doc.RootElement.TryGetProperty("tracks", out var tracks))
                {
                    return new SubtitleInspectionResult
                    {
                        Availability = SubtitleSpanishAvailability.Unknown,
                        Message = "La salida de mkvmerge no expone pistas."
                    };
                }

                var matches = new List<string>();
                foreach (var track in tracks.EnumerateArray())
                {
                    if (!track.TryGetProperty("type", out var typeElement) || typeElement.GetString() != "subtitles")
                    {
                        continue;
                    }

                    var properties = track.TryGetProperty("properties", out var propElement) ? propElement : default;
                    var language = properties.ValueKind != JsonValueKind.Undefined && properties.TryGetProperty("language", out var lang)
                        ? lang.GetString() ?? string.Empty
                        : string.Empty;
                    var name = properties.ValueKind != JsonValueKind.Undefined && properties.TryGetProperty("track_name", out var nameElement)
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty;

                    var normalizedLanguage = language.ToLowerInvariant();
                    var normalizedName = name.ToLowerInvariant();
                    if (normalizedLanguage is "spa" or "es" or "es-419" || SpanishNameHints.Any(hint => normalizedName.Contains(hint)))
                    {
                        matches.Add($"{language} {name}".Trim());
                    }
                }

                if (matches.Count > 0)
                {
                    return new SubtitleInspectionResult
                    {
                        Availability = SubtitleSpanishAvailability.Present,
                        Message = "Se detectaron subtítulos en español.",
                        Matches = matches,
                    };
                }

                return new SubtitleInspectionResult
                {
                    Availability = SubtitleSpanishAvailability.Missing,
                    Message = "No se detectaron subtítulos en español."
                };
            }
            catch (JsonException)
            {
                return new SubtitleInspectionResult
                {
                    Availability = SubtitleSpanishAvailability.Unknown,
                    Message = "No se pudo interpretar la salida de mkvmerge."
                };
            }
        }
    }
}
