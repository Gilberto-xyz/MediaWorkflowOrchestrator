namespace MediaWorkflowOrchestrator.Models
{
    public sealed class PublicationWorkspace
    {
        public string WorkflowId { get; set; } = string.Empty;
        public string CanonicalTitle { get; set; } = string.Empty;
        public PublicationContentKind ContentKind { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public string SourceRootPath { get; set; } = string.Empty;
        public string DriveFolderUrl { get; set; } = string.Empty;
        public string MediaFireFolderUrl { get; set; } = string.Empty;
        public string MediaFireAccountProfile { get; set; } = string.Empty;
        public string TransferItStartUrl { get; set; } = "https://transfer.it/start";
        public string SheetUrl { get; set; } = string.Empty;
        public string DriveLinksText { get; set; } = string.Empty;
        public string MediaFireLinksText { get; set; } = string.Empty;
        public string TransferLink { get; set; } = string.Empty;
        public DateTimeOffset? TransferExpiresAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<PublicationFileRecord> Files { get; set; } = new();

        public string RouteKey => PublicationRouteRecord.BuildKey(ContentKind, CanonicalTitle);
    }

    public sealed class PublicationFileRecord
    {
        public string LocalPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTimeOffset LastWriteTimeUtc { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public string OneFichierUrl { get; set; } = string.Empty;
        public string DriveUrl { get; set; } = string.Empty;
        public string MediaFireUrl { get; set; } = string.Empty;
        public string TransferUrl { get; set; } = string.Empty;
    }

    public sealed class PublicationRouteRecord
    {
        public string Key { get; set; } = string.Empty;
        public string CanonicalTitle { get; set; } = string.Empty;
        public PublicationContentKind ContentKind { get; set; }
        public string DriveFolderUrl { get; set; } = string.Empty;
        public string MediaFireFolderUrl { get; set; } = string.Empty;
        public string MediaFireAccountProfile { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        public static string BuildKey(PublicationContentKind kind, string title)
        {
            var family = kind is PublicationContentKind.SeriesEpisode or PublicationContentKind.SeriesBatch
                ? "series"
                : kind == PublicationContentKind.Movie ? "movie" : "collection";
            var normalized = new string(title.Trim().ToLowerInvariant()
                .Normalize(System.Text.NormalizationForm.FormD)
                .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
                .ToArray());
            normalized = string.Join('-', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return $"{family}:{normalized}";
        }
    }
}
