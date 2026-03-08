using System.Runtime.InteropServices;
using MediaWorkflowOrchestrator.Persistence;

namespace MediaWorkflowOrchestrator
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public const string CleanReloadArgument = "--clean-reload";
        private Window? window;

        public static AppHost Host { get; } = new();
        public static MainWindow? MainWindowInstance { get; private set; }
        public static bool StartWithCleanReloadState { get; private set; }

        public static IntPtr MainWindowHandle
        {
            get
            {
                if (MainWindowInstance is null)
                {
                    return IntPtr.Zero;
                }

                return WinRT.Interop.WindowNative.GetWindowHandle(MainWindowInstance);
            }
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            UnhandledException += OnUnhandledException;
            DiagnosticsTrace.Write("App initialized.");
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            DiagnosticsTrace.Write($"OnLaunched args: '{e.Arguments}'.");
            AppDataPaths.EnsureAll();
            StartWithCleanReloadState = ConsumeCleanReloadMarker()
                || e.Arguments?
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(CleanReloadArgument, StringComparer.OrdinalIgnoreCase) == true;
            DiagnosticsTrace.Write($"StartWithCleanReloadState: {StartWithCleanReloadState}.");
            MainWindowInstance ??= new MainWindow();
            window = MainWindowInstance;
            window.Activate();
            DiagnosticsTrace.Write("Main window activated.");
        }

        public static void MarkNextLaunchAsCleanReload()
        {
            AppDataPaths.EnsureAll();
            File.WriteAllText(AppDataPaths.CleanReloadMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
        }

        public static void ClearCleanReloadMarker()
        {
            if (File.Exists(AppDataPaths.CleanReloadMarkerPath))
            {
                File.Delete(AppDataPaths.CleanReloadMarkerPath);
            }
        }

        private static bool ConsumeCleanReloadMarker()
        {
            if (!File.Exists(AppDataPaths.CleanReloadMarkerPath))
            {
                return false;
            }

            File.Delete(AppDataPaths.CleanReloadMarkerPath);
            return true;
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            DiagnosticsTrace.Write($"UnhandledException: {e.Message}");
        }
    }
}
