using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaWorkflowOrchestrator.Services
{
    public sealed class PublicationService : IPublicationService
    {
        private static readonly Regex EpisodeRegex = new(
            @"(?<title>.+?)(?:\s+-\s+|\s+)S(?<season>\d{1,2})E(?<episode>\d{1,3})(?:\b|\s+-)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex YearRegex = new(
            @"(?<title>.+?)\s*\((?<year>(?:19|20)\d{2})\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex FileUploaderLineRegex = new(
            @"^(?<date>[^|]+)\|(?<host>[^|]+)\|(?<url>https?://[^|]+)\|+(?<file>[^|]+)\|?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> PublishableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".m4v", ".avi", ".webm", ".rar", ".zip", ".jpg", ".jpeg", ".png", ".webp"
        };
        private static readonly HashSet<string> GenericFolderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Completado", "Completed", "Output", "Final", "Temp", "TEMP%"
        };
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

        private readonly IAppSettingsService appSettingsService;

        public PublicationService(IAppSettingsService appSettingsService)
        {
            this.appSettingsService = appSettingsService;
        }

        public async Task<PublicationWorkspace> PrepareAsync(WorkflowInstance workflow, CancellationToken cancellationToken)
        {
            AppDataPaths.EnsureAll();
            var settings = await appSettingsService.LoadAsync();
            var existing = await LoadAsync(workflow.Id, cancellationToken);
            var analysis = Analyze(workflow);
            var routes = await LoadRoutesAsync(cancellationToken);
            routes.TryGetValue(analysis.RouteKey, out var route);

            var workspace = existing ?? new PublicationWorkspace { WorkflowId = workflow.Id };
            workspace.CanonicalTitle = analysis.CanonicalTitle;
            workspace.ContentKind = analysis.ContentKind;
            workspace.SeasonNumber = analysis.SeasonNumber;
            workspace.EpisodeNumber = analysis.EpisodeNumber;
            workspace.SourceRootPath = analysis.SourceRootPath;
            workspace.DriveFolderUrl = FirstNotBlank(workspace.DriveFolderUrl, route?.DriveFolderUrl, settings.PublicationDriveRootUrl);
            workspace.MediaFireFolderUrl = FirstNotBlank(workspace.MediaFireFolderUrl, route?.MediaFireFolderUrl, settings.PublicationMediaFireRootUrl);
            workspace.MediaFireAccountProfile = FirstNotBlank(workspace.MediaFireAccountProfile, route?.MediaFireAccountProfile, settings.MediaFireAccountProfile);
            workspace.TransferItStartUrl = FirstNotBlank(workspace.TransferItStartUrl, settings.PublicationTransferItUrl, "https://transfer.it/start");
            workspace.SheetUrl = FirstNotBlank(workspace.SheetUrl, settings.PublicationSheetUrl);
            workspace.Files = MergeFileRecords(workspace.Files, DiscoverFiles(workflow, analysis));
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
            workspace = await RefreshLinksAsync(workspace, cancellationToken);
            await SaveAsync(workspace, cancellationToken);
            return workspace;
        }

        public async Task<PublicationWorkspace?> LoadAsync(string workflowId, CancellationToken cancellationToken)
        {
            AppDataPaths.EnsureAll();
            var path = GetManifestPath(workflowId);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<PublicationWorkspace>(stream, JsonOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"Publication manifest could not be loaded: {path}. {ex}");
                return null;
            }
        }

        public async Task SaveAsync(PublicationWorkspace workspace, CancellationToken cancellationToken)
        {
            AppDataPaths.EnsureAll();
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
            var path = GetManifestPath(workspace.WorkflowId);
            var tempPath = $"{path}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, workspace, JsonOptions, cancellationToken);
            }
            File.Move(tempPath, path, overwrite: true);

            var routes = await LoadRoutesAsync(cancellationToken);
            routes[workspace.RouteKey] = new PublicationRouteRecord
            {
                Key = workspace.RouteKey,
                CanonicalTitle = workspace.CanonicalTitle,
                ContentKind = workspace.ContentKind,
                DriveFolderUrl = workspace.DriveFolderUrl.Trim(),
                MediaFireFolderUrl = workspace.MediaFireFolderUrl.Trim(),
                MediaFireAccountProfile = workspace.MediaFireAccountProfile.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await SaveRoutesAsync(routes, cancellationToken);
        }

        public async Task<PublicationWorkspace> RefreshLinksAsync(PublicationWorkspace workspace, CancellationToken cancellationToken)
        {
            var settings = await appSettingsService.LoadAsync();
            var oneFichierLinks = await ReadFileUploaderLinksAsync(settings.FileUploaderLogPath, cancellationToken);
            foreach (var file in workspace.Files)
            {
                if (oneFichierLinks.TryGetValue(file.FileName, out var url))
                {
                    file.OneFichierUrl = url;
                }
            }
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
            return workspace;
        }

        public string BuildSheetRows(PublicationWorkspace workspace)
        {
            var driveLinks = SplitLinks(workspace.DriveLinksText);
            var mediaFireLinks = SplitLinks(workspace.MediaFireLinksText);
            var rows = new List<string>();
            for (var index = 0; index < workspace.Files.Count; index++)
            {
                var file = workspace.Files[index];
                var drive = FirstNotBlank(file.DriveUrl, ElementAtOrEmpty(driveLinks, index), workspace.DriveFolderUrl);
                var mediaFire = FirstNotBlank(file.MediaFireUrl, ElementAtOrEmpty(mediaFireLinks, index), workspace.MediaFireFolderUrl);
                var transfer = FirstNotBlank(file.TransferUrl, workspace.TransferLink);
                rows.Add(string.Join('\t', new[]
                {
                    workspace.CanonicalTitle,
                    GetKindLabel(workspace.ContentKind),
                    BuildEpisodeLabel(workspace),
                    file.FileName,
                    drive,
                    mediaFire,
                    file.OneFichierUrl,
                    transfer,
                    workspace.MediaFireAccountProfile,
                    workspace.TransferExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                }));
            }

            if (rows.Count == 0)
            {
                rows.Add(string.Join('\t', new[]
                {
                    workspace.CanonicalTitle,
                    GetKindLabel(workspace.ContentKind),
                    BuildEpisodeLabel(workspace),
                    string.Empty,
                    workspace.DriveFolderUrl,
                    workspace.MediaFireFolderUrl,
                    string.Empty,
                    workspace.TransferLink,
                    workspace.MediaFireAccountProfile,
                    workspace.TransferExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                }));
            }

            return string.Join(Environment.NewLine, rows);
        }

        public string BuildSummary(PublicationWorkspace workspace)
        {
            var oneFichierCount = workspace.Files.Count(file => !string.IsNullOrWhiteSpace(file.OneFichierUrl));
            var totalSize = workspace.Files.Sum(file => file.SizeBytes);
            var expiry = workspace.TransferExpiresAt is null
                ? "sin caducidad registrada"
                : $"caduca {workspace.TransferExpiresAt.Value.ToLocalTime():dd/MM/yyyy}";
            return $"{GetKindLabel(workspace.ContentKind)} · {workspace.Files.Count} archivo(s) · {FormatBytes(totalSize)} · " +
                   $"1fichier {oneFichierCount}/{workspace.Files.Count} · Transfer.it {expiry}";
        }

        private static PublicationAnalysis Analyze(WorkflowInstance workflow)
        {
            var sourcePath = FirstNotBlank(workflow.SourcePrimaryVideoPath, workflow.PrimaryVideoPath, workflow.RootPath);
            var sourceName = File.Exists(sourcePath)
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : workflow.DisplayName;
            var episodeMatch = EpisodeRegex.Match(sourceName);
            var sourceRoot = ResolveSourceRoot(workflow, sourcePath);
            var discoveredEpisodeTokens = Directory.Exists(sourceRoot)
                ? Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.TopDirectoryOnly)
                    .Select(path => EpisodeRegex.Match(Path.GetFileNameWithoutExtension(path)))
                    .Where(match => match.Success)
                    .Select(match => $"S{match.Groups["season"].Value}E{match.Groups["episode"].Value}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .Count()
                : 0;

            PublicationContentKind kind;
            string canonicalTitle;
            int? season = null;
            int? episode = null;
            if (episodeMatch.Success)
            {
                kind = discoveredEpisodeTokens > 1 && workflow.SourceSelectionIsFile is false
                    ? PublicationContentKind.SeriesBatch
                    : PublicationContentKind.SeriesEpisode;
                canonicalTitle = CleanTitle(episodeMatch.Groups["title"].Value);
                season = int.Parse(episodeMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
                episode = int.Parse(episodeMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                var yearMatch = YearRegex.Match(sourceName);
                if (yearMatch.Success)
                {
                    kind = PublicationContentKind.Movie;
                    canonicalTitle = $"{CleanTitle(yearMatch.Groups["title"].Value)} ({yearMatch.Groups["year"].Value})";
                }
                else if (workflow.SourceSelectionIsFile is false)
                {
                    kind = PublicationContentKind.Collection;
                    canonicalTitle = CleanTitle(GenericFolderNames.Contains(workflow.DisplayName) ? sourceName : workflow.DisplayName);
                }
                else
                {
                    kind = PublicationContentKind.Unknown;
                    canonicalTitle = CleanTitle(sourceName);
                }
            }

            return new PublicationAnalysis(kind, canonicalTitle, season, episode, sourceRoot);
        }

        private static List<PublicationFileRecord> DiscoverFiles(WorkflowInstance workflow, PublicationAnalysis analysis)
        {
            if (!Directory.Exists(analysis.SourceRootPath))
            {
                return new List<PublicationFileRecord>();
            }

            var searchOption = workflow.SourceSelectionIsFile is false && !GenericFolderNames.Contains(Path.GetFileName(analysis.SourceRootPath))
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            var candidates = Directory.EnumerateFiles(analysis.SourceRootPath, "*.*", searchOption)
                .Where(path => PublishableExtensions.Contains(Path.GetExtension(path)))
                .Where(path => ShouldIncludeFile(path, workflow, analysis))
                .Select(CreateFileRecord)
                .OrderBy(record => record.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var primaryPath = FirstNotBlank(workflow.PrimaryVideoPath, workflow.SourcePrimaryVideoPath);
            if (File.Exists(primaryPath) && candidates.All(item => !PathsEqual(item.LocalPath, primaryPath)))
            {
                candidates.Insert(0, CreateFileRecord(primaryPath));
            }
            return candidates;
        }

        private static bool ShouldIncludeFile(string path, WorkflowInstance workflow, PublicationAnalysis analysis)
        {
            if (workflow.SourceSelectionIsFile is false && !GenericFolderNames.Contains(Path.GetFileName(analysis.SourceRootPath)))
            {
                return true;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            if (analysis.ContentKind == PublicationContentKind.SeriesEpisode && analysis.SeasonNumber is not null && analysis.EpisodeNumber is not null)
            {
                return Regex.IsMatch(name, $@"\bS0*{analysis.SeasonNumber}E0*{analysis.EpisodeNumber}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            if (analysis.ContentKind == PublicationContentKind.Movie)
            {
                var titleWithoutYear = YearRegex.Replace(analysis.CanonicalTitle, match => match.Groups["title"].Value).Trim();
                return NormalizeForMatch(name).Contains(NormalizeForMatch(titleWithoutYear), StringComparison.OrdinalIgnoreCase);
            }
            return PathsEqual(path, FirstNotBlank(workflow.PrimaryVideoPath, workflow.SourcePrimaryVideoPath));
        }

        private static PublicationFileRecord CreateFileRecord(string path)
        {
            var info = new FileInfo(path);
            return new PublicationFileRecord
            {
                LocalPath = info.FullName,
                FileName = info.Name,
                SizeBytes = info.Exists ? info.Length : 0,
                LastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTimeOffset.MinValue,
                Fingerprint = info.Exists ? $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}-{NormalizeForMatch(info.Name)}" : NormalizeForMatch(info.Name),
            };
        }

        private static List<PublicationFileRecord> MergeFileRecords(
            IReadOnlyCollection<PublicationFileRecord> existing,
            IReadOnlyCollection<PublicationFileRecord> discovered)
        {
            var byFingerprint = existing
                .GroupBy(item => item.Fingerprint, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            var merged = new List<PublicationFileRecord>();
            foreach (var item in discovered)
            {
                if (byFingerprint.TryGetValue(item.Fingerprint, out var previous))
                {
                    item.OneFichierUrl = previous.OneFichierUrl;
                    item.DriveUrl = previous.DriveUrl;
                    item.MediaFireUrl = previous.MediaFireUrl;
                    item.TransferUrl = previous.TransferUrl;
                }
                merged.Add(item);
            }
            return merged;
        }

        private static async Task<Dictionary<string, string>> ReadFileUploaderLinksAsync(string logPath, CancellationToken cancellationToken)
        {
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            {
                return results;
            }

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var match = FileUploaderLineRegex.Match(line);
                if (!match.Success || !match.Groups["host"].Value.Contains("1fichier", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                results[match.Groups["file"].Value.Trim()] = match.Groups["url"].Value.Trim();
            }
            return results;
        }

        private static string ResolveSourceRoot(WorkflowInstance workflow, string sourcePath)
        {
            if (workflow.SourceSelectionIsFile is false && Directory.Exists(workflow.SourceRootPath))
            {
                return workflow.SourceRootPath;
            }
            if (File.Exists(sourcePath))
            {
                return Path.GetDirectoryName(sourcePath) ?? workflow.RootPath;
            }
            return Directory.Exists(workflow.RootPath) ? workflow.RootPath : string.Empty;
        }

        private static string CleanTitle(string value)
        {
            var cleaned = Regex.Replace(value, @"[_\.]+", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', '_');
            return string.IsNullOrWhiteSpace(cleaned) ? "Publicación sin título" : cleaned;
        }

        private static string NormalizeForMatch(string value)
        {
            var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            return new string(decomposed.Where(char.IsLetterOrDigit).ToArray());
        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetManifestPath(string workflowId) =>
            Path.Combine(AppDataPaths.PublicationManifestsDirectory, $"{workflowId}.json");

        private static async Task<Dictionary<string, PublicationRouteRecord>> LoadRoutesAsync(CancellationToken cancellationToken)
        {
            AppDataPaths.EnsureAll();
            if (!File.Exists(AppDataPaths.PublicationRoutesPath))
            {
                return new Dictionary<string, PublicationRouteRecord>(StringComparer.OrdinalIgnoreCase);
            }
            try
            {
                await using var stream = File.OpenRead(AppDataPaths.PublicationRoutesPath);
                var routes = await JsonSerializer.DeserializeAsync<Dictionary<string, PublicationRouteRecord>>(stream, JsonOptions, cancellationToken);
                return routes is null
                    ? new Dictionary<string, PublicationRouteRecord>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, PublicationRouteRecord>(routes, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"Publication routes could not be loaded: {ex}");
                return new Dictionary<string, PublicationRouteRecord>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static async Task SaveRoutesAsync(Dictionary<string, PublicationRouteRecord> routes, CancellationToken cancellationToken)
        {
            var tempPath = $"{AppDataPaths.PublicationRoutesPath}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, routes, JsonOptions, cancellationToken);
            }
            File.Move(tempPath, AppDataPaths.PublicationRoutesPath, overwrite: true);
        }

        private static IReadOnlyList<string> SplitLinks(string value) => value
            .Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => Uri.TryCreate(item, UriKind.Absolute, out _))
            .ToList();

        private static string ElementAtOrEmpty(IReadOnlyList<string> values, int index) => index < values.Count ? values[index] : string.Empty;

        private static string FirstNotBlank(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

        private static string GetKindLabel(PublicationContentKind kind) => kind switch
        {
            PublicationContentKind.SeriesEpisode => "Episodio individual",
            PublicationContentKind.SeriesBatch => "Serie o temporada",
            PublicationContentKind.Movie => "Película",
            PublicationContentKind.Collection => "Colección",
            _ => "Sin clasificar",
        };

        private static string BuildEpisodeLabel(PublicationWorkspace workspace) =>
            workspace.SeasonNumber is not null && workspace.EpisodeNumber is not null
                ? $"S{workspace.SeasonNumber:00}E{workspace.EpisodeNumber:00}"
                : string.Empty;

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var value = (double)Math.Max(0, bytes);
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.##} {units[unit]}";
        }

        private readonly record struct PublicationAnalysis(
            PublicationContentKind ContentKind,
            string CanonicalTitle,
            int? SeasonNumber,
            int? EpisodeNumber,
            string SourceRootPath)
        {
            public string RouteKey => PublicationRouteRecord.BuildKey(ContentKind, CanonicalTitle);
        }
    }
}
