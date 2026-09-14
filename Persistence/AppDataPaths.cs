namespace MediaWorkflowOrchestrator.Persistence
{
    public static class AppDataPaths
    {
        public static string RootDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaWorkflowOrchestrator");

        public static string SettingsPath => Path.Combine(RootDirectory, "settings.json");
        public static string WorkflowsDirectory => Path.Combine(RootDirectory, "workflows");
        public static string LogsDirectory => Path.Combine(RootDirectory, "logs");
        public static string PublishingDirectory => Path.Combine(RootDirectory, "publishing");
        public static string PublicationRoutesPath => Path.Combine(PublishingDirectory, "routes.json");
        public static string PublicationManifestsDirectory => Path.Combine(PublishingDirectory, "manifests");
        public static string CleanReloadMarkerPath => Path.Combine(RootDirectory, "clean-reload.flag");

        public static void EnsureAll()
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(WorkflowsDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(PublishingDirectory);
            Directory.CreateDirectory(PublicationManifestsDirectory);
        }
    }
}
