namespace MediaWorkflowOrchestrator
{
    public static class DiagnosticsTrace
    {
        private static readonly object Gate = new();

        public static string LogPath
        {
            get
            {
                AppDataPaths.EnsureAll();
                return Path.Combine(AppDataPaths.RootDirectory, "startup.log");
            }
        }

        public static void Write(string message)
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
        }
    }
}
