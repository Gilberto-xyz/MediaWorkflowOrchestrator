namespace MediaWorkflowOrchestrator.Models
{
    public sealed class AppSettings
    {
        public string BrandName { get; set; } = "GDriveLatinoHD";
        public string PythonPath { get; set; } = "python";
        public string DownloaderScriptPath { get; set; } = @"C:\Users\gilbe\Downloads\Nyaa-autoDownload\auto_backup_nyaa.py";
        public string DownloaderLinkScriptPath { get; set; } = @"C:\Users\gilbe\Downloads\Nyaa-autoDownload\source_from_link.py";
        public string DownloaderConfigPath { get; set; } = @"C:\Users\gilbe\Downloads\Nyaa-autoDownload\watchlist.json";
        public string DownloaderStatePath { get; set; } = @"C:\Users\gilbe\Downloads\Nyaa-autoDownload\state.json";
        public bool DownloaderDryRun { get; set; }
        public bool DownloaderForceLatest { get; set; }
        public string TrackCleanupScriptPath { get; set; } = @"C:\Users\gilbe\OneDrive\Documentos\SubForge\limpiar_tracks.py";
        public bool TrackCleanupCloseQbittorrent { get; set; } = true;
        public bool TrackCleanupDeleteOriginals { get; set; }
        public bool TagAndRenameAttachCover { get; set; } = true;
        public string TagAndRenameScriptPath { get; set; } = @"C:\Users\gilbe\OneDrive\Documentos\ENCODER_INMORTUS\1 Subs\ETIQUETAS_GDRIVELATINO.py";
        public string RarPackagingScriptPath { get; set; } = @"C:\Users\gilbe\OneDrive\Documentos\WorkflowRAR_IMG\rar_folder_image_info.py";
        public string MkvmergePath { get; set; } = @"C:\Program Files\MKVToolNix\mkvmerge.exe";
        public string MkvpropeditPath { get; set; } = @"C:\Program Files\MKVToolNix\mkvpropedit.exe";
        public string RarExePath { get; set; } = @"C:\Program Files\WinRAR\rar.exe";
        public bool RarSkipImages { get; set; }
        public bool RarNoCompress { get; set; }
        public bool RarUseCompressionNormal { get; set; }
        public string RarCaptureCount { get; set; } = string.Empty;
        public string RarImageFormat { get; set; } = "jpg";
        public bool RarVerbose { get; set; }
        public string EncryptedRarPassword { get; set; } = string.Empty;
        public string DownloadWorkingDirectory { get; set; } = @"C:\Users\gilbe\Downloads\Nyaa-autoDownload";
        public string TagAndRenameWorkingDirectory { get; set; } = @"C:\Users\gilbe\OneDrive\Documentos\ENCODER_INMORTUS\1 Subs";
        public bool PreferSkipTranslationWhenSpanishExists { get; set; } = true;
        public string FileUploaderExePath { get; set; } = @"C:\Users\gilbe\OneDrive\Documentos\ENCODER_INMORTUS\3 Upload 1Ficher\FileUploader.exe";
        public string FileUploaderLogPath { get; set; } = @"C:\Users\gilbe\OneDrive\Documentos\ENCODER_INMORTUS\3 Upload 1Ficher\FileUploader.log";
        public string PublicationDriveRootUrl { get; set; } = "https://drive.google.com/drive/my-drive";
        public string PublicationMediaFireRootUrl { get; set; } = "https://app.mediafire.com/folder/myfiles";
        public string PublicationTransferItUrl { get; set; } = "https://transfer.it/start";
        public string PublicationSheetUrl { get; set; } = string.Empty;
        public string MediaFireAccountProfile { get; set; } = DateTime.Now.ToString("yyyy-MM");

        public static AppSettings CreateDefault() => new();
    }
}
