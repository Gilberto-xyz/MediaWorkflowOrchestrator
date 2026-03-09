using System.Collections.ObjectModel;
using System.Diagnostics;
using MediaWorkflowOrchestrator.Messages;
using Windows.ApplicationModel.DataTransfer;

namespace MediaWorkflowOrchestrator.ViewModels
{
    public partial class DashboardViewModel : BaseViewModel, IRecipient<WorkflowSelectedMessage>
    {
        private const double MinDetailOutputHeight = 220;
        private const double MaxDetailOutputHeight = 1200;

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush PendingButtonBackgroundBrush = CreateBrush(0x26, 0x14, 0x18, 0x22);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush PendingButtonBorderBrush = CreateBrush(0x66, 0xF8, 0xFA, 0xFF);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush CopiedButtonBackgroundBrush = CreateBrush(0xCC, 0x0F, 0x76, 0x6E);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush CopiedButtonBorderBrush = CreateBrush(0xFF, 0x2A, 0xF5, 0x98);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ButtonForegroundBrush = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);

        private readonly IWorkflowExecutionService workflowExecutionService = App.Host.WorkflowExecutionService;
        private readonly IWorkflowStore workflowStore = App.Host.WorkflowStore;
        private CancellationTokenSource? executionCancellationTokenSource;
        private WorkflowInstance? currentWorkflow;
        private WorkflowStepKey? activeOutputStepKey;
        private bool utilityOutputActive;
        private AppSettings quickSettings = AppSettings.CreateDefault();

        public DashboardViewModel()
        {
            Title = "Dashboard";
            StepItems = new ObservableCollection<WorkflowStepState>();
            ResetWorkflowState("La descarga Nyaa se ejecuta como utilidad global; el workflow real empieza cuando eliges el archivo o carpeta.");
            WeakReferenceMessenger.Default.Register(this);
            _ = LoadQuickSettingsAsync();
            if (!App.StartWithCleanReloadState)
            {
                _ = LoadLatestWorkflowAsync();
            }
        }

        public ObservableCollection<WorkflowStepState> StepItems { get; }

        [ObservableProperty]
        private WorkflowStepState? _selectedStep;

        [ObservableProperty]
        private string _displayName = "Selecciona un archivo o carpeta para comenzar.";

        [ObservableProperty]
        private string _rootPath = "Sin workflow activo";

        [ObservableProperty]
        private string _nextStepLabel = "Sin pasos listos";

        [ObservableProperty]
        private string _globalStatus = "Esperando selección";

        [ObservableProperty]
        private string _statusMessage = "La descarga Nyaa se ejecuta como utilidad global; el workflow real empieza cuando eliges el archivo o carpeta.";

        [ObservableProperty]
        private bool _isStatusInfoOpen = true;

        [ObservableProperty]
        private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

        [ObservableProperty]
        private string _liveOutput = string.Empty;

        [ObservableProperty]
        private string _detailOutput = "Selecciona un paso para ver detalle y salida.";

        [ObservableProperty]
        private double _detailOutputHeight = 420;

        [ObservableProperty]
        private bool _detailOutputHeightWasResized;

        [ObservableProperty]
        private string _selectedStepTitle = "Sin paso seleccionado";

        [ObservableProperty]
        private string _selectedStepDescription = "Selecciona un paso para ver detalle y salida.";

        [ObservableProperty]
        private string _packageRarDetailSummary = "Ejecuta Empaquetar RAR para habilitar la copia rápida.";

        [ObservableProperty]
        private bool _showTranslationDecisionActions;

        [ObservableProperty]
        private bool _showQuickActionOptions;

        [ObservableProperty]
        private bool _showDownloadQuickOptions;

        [ObservableProperty]
        private bool _showTranslateQuickOptions;

        [ObservableProperty]
        private bool _showCleanTracksQuickOptions;

        [ObservableProperty]
        private bool _showPackageRarQuickOptions;

        [ObservableProperty]
        private bool _showPackageRarDetailActions;

        [ObservableProperty]
        private bool _showSkipAheadActions;

        [ObservableProperty]
        private bool _hasExplicitStepSelection;

        [ObservableProperty]
        private bool _packageRarRawDataCopied;

        [ObservableProperty]
        private bool _packageRarWeightSummaryCopied;

        [ObservableProperty]
        private bool _packageRarCleanNameCopied;

        [ObservableProperty]
        private string _quickOptionsTitle = "Opciones rápidas";

        [ObservableProperty]
        private string _quickOptionsDescription = "Selecciona un paso para ajustar flags rápidos y acciones de avance.";

        [ObservableProperty]
        private bool _downloadDryRunEnabled;

        [ObservableProperty]
        private bool _downloadForceLatestEnabled;

        [ObservableProperty]
        private bool _translateFastModeEnabled = true;

        [ObservableProperty]
        private bool _translateSkipSummaryEnabled = true;

        [ObservableProperty]
        private bool _cleanupCloseQbittorrentEnabled = true;

        [ObservableProperty]
        private bool _cleanupDeleteOriginalsEnabled;

        [ObservableProperty]
        private bool _rarSkipImagesEnabled;

        [ObservableProperty]
        private bool _rarNoCompressEnabled;

        [ObservableProperty]
        private bool _rarUseCompressionNormalEnabled;

        [ObservableProperty]
        private bool _rarVerboseEnabled;

        [ObservableProperty]
        private string _rarImageFormatQuick = "jpg";

        partial void OnSelectedStepChanged(WorkflowStepState? value)
        {
            foreach (var step in StepItems)
            {
                step.IsSelected = ReferenceEquals(step, value);
            }

            SelectedStepTitle = value?.DisplayName ?? "Sin paso seleccionado";
            SelectedStepDescription = value?.StatusReason ?? "Selecciona un paso para ver detalle y salida.";
            UpdateQuickOptionsVisibility();
            UpdatePackageRarDetailActions();
            RefreshSelectedStepOutput();
        }

        public string DownloadDryRunButtonLabel => $"Dry-run: {(DownloadDryRunEnabled ? "ON" : "OFF")}";
        public string DownloadForceLatestButtonLabel => $"Force latest: {(DownloadForceLatestEnabled ? "ON" : "OFF")}";
        public string TranslateFastModeButtonLabel => $"Modo rápido: {(TranslateFastModeEnabled ? "ON" : "OFF")}";
        public string TranslateSkipSummaryButtonLabel => $"Omitir resumen: {(TranslateSkipSummaryEnabled ? "ON" : "OFF")}";
        public string CleanupCloseQbittorrentButtonLabel => $"Cerrar qBittorrent: {(CleanupCloseQbittorrentEnabled ? "ON" : "OFF")}";
        public string CleanupDeleteOriginalsButtonLabel => $"Eliminar originales: {(CleanupDeleteOriginalsEnabled ? "ON" : "OFF")}";
        public string PackageRarRawDataButtonLabel => "Raw Data";
        public string PackageRarWeightSummaryButtonLabel => "Peso completo";
        public string PackageRarCleanNameButtonLabel => "Nombre limpio";
        public string RarSkipImagesButtonLabel => $"Sin imágenes: {(RarSkipImagesEnabled ? "ON" : "OFF")}";
        public string RarNoCompressButtonLabel => $"Solo info: {(RarNoCompressEnabled ? "ON" : "OFF")}";
        public string RarCompressionModeButtonLabel => $"Modo RAR: {(RarUseCompressionNormalEnabled ? "Comprimir" : "Contenedor fast")}";
        public string RarVerboseButtonLabel => $"Verbose: {(RarVerboseEnabled ? "ON" : "OFF")}";
        public string RarImageFormatButtonLabel => $"Formato imagen: {RarImageFormatQuick.ToUpperInvariant()}";
        public Microsoft.UI.Xaml.Media.Brush PackageRarRawDataButtonBackground => PackageRarRawDataCopied ? CopiedButtonBackgroundBrush : PendingButtonBackgroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarWeightSummaryButtonBackground => PackageRarWeightSummaryCopied ? CopiedButtonBackgroundBrush : PendingButtonBackgroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarCleanNameButtonBackground => PackageRarCleanNameCopied ? CopiedButtonBackgroundBrush : PendingButtonBackgroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarRawDataButtonBorderBrush => PackageRarRawDataCopied ? CopiedButtonBorderBrush : PendingButtonBorderBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarWeightSummaryButtonBorderBrush => PackageRarWeightSummaryCopied ? CopiedButtonBorderBrush : PendingButtonBorderBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarCleanNameButtonBorderBrush => PackageRarCleanNameCopied ? CopiedButtonBorderBrush : PendingButtonBorderBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarRawDataButtonForeground => ButtonForegroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarWeightSummaryButtonForeground => ButtonForegroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarCleanNameButtonForeground => ButtonForegroundBrush;

        public void EnsureDetailOutputFitsViewport(double viewportHeight)
        {
            if (DetailOutputHeightWasResized || viewportHeight <= 0)
            {
                return;
            }

            var targetHeight = Math.Clamp((viewportHeight * 0.48) - 60, MinDetailOutputHeight, 760);
            if (Math.Abs(targetHeight - DetailOutputHeight) > 1)
            {
                DetailOutputHeight = targetHeight;
            }
        }

        public void ResizeDetailOutput(double delta)
        {
            DetailOutputHeightWasResized = true;
            DetailOutputHeight = Math.Clamp(DetailOutputHeight + delta, MinDetailOutputHeight, MaxDetailOutputHeight);
        }

        public void ResetDetailOutputAutoSize(double viewportHeight)
        {
            DetailOutputHeightWasResized = false;
            EnsureDetailOutputFitsViewport(viewportHeight);
        }

        [RelayCommand]
        private async Task RunNextAsync()
        {
            if (currentWorkflow is null)
            {
                return;
            }

            var nextStep = App.Host.WorkflowEngine.GetNextReadyStep(currentWorkflow);
            if (nextStep is null)
            {
                return;
            }

            SelectStep(nextStep.StepKey);
            await ExecuteAsync(
                () => workflowExecutionService.ExecuteStepAsync(currentWorkflow, nextStep.StepKey, AppendOutput, CancellationToken),
                nextStep.StepKey);
        }

        [RelayCommand]
        private async Task RunSelectedStepAsync()
        {
            if (currentWorkflow is null || SelectedStep is null)
            {
                return;
            }

            SelectStep(SelectedStep.StepKey);
            await ExecuteAsync(
                () => workflowExecutionService.ExecuteStepAsync(currentWorkflow, SelectedStep.StepKey, AppendOutput, CancellationToken, forceExecution: true),
                SelectedStep.StepKey);
        }

        [RelayCommand]
        private async Task RetrySelectedStepAsync() => await RunSelectedStepAsync();

        [RelayCommand]
        private async Task SkipSelectedStepAsync()
        {
            if (currentWorkflow is null || SelectedStep is null)
            {
                StatusSeverity = InfoBarSeverity.Warning;
                StatusMessage = "No hay un paso seleccionado para omitir.";
                IsStatusInfoOpen = true;
                return;
            }

            if (SelectedStep.StepKey == WorkflowStepKey.TranslateSubs)
            {
                await ChangeTranslationDecisionAsync(translateRequired: false);
            }
            else
            {
                SelectedStep.Status = WorkflowStepStatus.Skipped;
                SelectedStep.StatusReason = "Paso omitido manualmente desde la interfaz.";
                SelectedStep.FinishedAt = DateTimeOffset.UtcNow;
                currentWorkflow.LastExecutionSummary = $"{SelectedStep.DisplayName}: omitido manualmente.";
                App.Host.WorkflowEngine.RefreshStatuses(currentWorkflow);
                await workflowStore.SaveAsync(currentWorkflow);
                StatusSeverity = InfoBarSeverity.Informational;
                StatusMessage = currentWorkflow.LastExecutionSummary;
                IsStatusInfoOpen = true;
                DiagnosticsTrace.Write($"Step manually skipped: {SelectedStep.StepKey}.");
                RefreshFromWorkflow(currentWorkflow);
            }
        }

        [RelayCommand]
        private async Task PrepareSelectedStepAsync()
        {
            await SkipAheadToSelectedStepAsync(runAfterPreparing: false);
        }

        [RelayCommand]
        private async Task PrepareAndRunSelectedStepAsync()
        {
            await SkipAheadToSelectedStepAsync(runAfterPreparing: true);
        }

        [RelayCommand]
        private async Task ToggleDownloadDryRunAsync()
        {
            DownloadDryRunEnabled = !DownloadDryRunEnabled;
            OnPropertyChanged(nameof(DownloadDryRunButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleDownloadForceLatestAsync()
        {
            DownloadForceLatestEnabled = !DownloadForceLatestEnabled;
            OnPropertyChanged(nameof(DownloadForceLatestButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleTranslateFastModeAsync()
        {
            TranslateFastModeEnabled = !TranslateFastModeEnabled;
            OnPropertyChanged(nameof(TranslateFastModeButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleTranslateSkipSummaryAsync()
        {
            TranslateSkipSummaryEnabled = !TranslateSkipSummaryEnabled;
            OnPropertyChanged(nameof(TranslateSkipSummaryButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleCleanupCloseQbittorrentAsync()
        {
            CleanupCloseQbittorrentEnabled = !CleanupCloseQbittorrentEnabled;
            OnPropertyChanged(nameof(CleanupCloseQbittorrentButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleCleanupDeleteOriginalsAsync()
        {
            CleanupDeleteOriginalsEnabled = !CleanupDeleteOriginalsEnabled;
            OnPropertyChanged(nameof(CleanupDeleteOriginalsButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleRarSkipImagesAsync()
        {
            RarSkipImagesEnabled = !RarSkipImagesEnabled;
            OnPropertyChanged(nameof(RarSkipImagesButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleRarNoCompressAsync()
        {
            RarNoCompressEnabled = !RarNoCompressEnabled;
            OnPropertyChanged(nameof(RarNoCompressButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleRarCompressionModeAsync()
        {
            RarUseCompressionNormalEnabled = !RarUseCompressionNormalEnabled;
            OnPropertyChanged(nameof(RarCompressionModeButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleRarVerboseAsync()
        {
            RarVerboseEnabled = !RarVerboseEnabled;
            OnPropertyChanged(nameof(RarVerboseButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task CycleRarImageFormatAsync()
        {
            RarImageFormatQuick = string.Equals(RarImageFormatQuick, "png", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
            OnPropertyChanged(nameof(RarImageFormatButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task MarkTranslateRequiredAsync()
        {
            await ChangeTranslationDecisionAsync(translateRequired: true);
        }

        [RelayCommand]
        private async Task MarkTranslateSkippedAsync()
        {
            await ChangeTranslationDecisionAsync(translateRequired: false);
        }

        [RelayCommand]
        private async Task RunDownloadAsync()
        {
            try
            {
                var settings = await workflowExecutionService.GetSettingsAsync();
                BeginUtilityOutput(
                    "Descarga semanal de Nyaa",
                    "Salida en vivo del script global de descarga semanal.",
                    "Ejecutando descarga semanal. Cuando termine, elige el archivo o carpeta descargados para iniciar un nuevo workflow.");
                var result = await App.Host.ProcessRunnerService.RunAsync(
                    new ProcessExecutionRequest
                    {
                        FileName = settings.PythonPath,
                        Arguments = BuildDownloadArgs(settings),
                        WorkingDirectory = settings.DownloadWorkingDirectory,
                    },
                    AppendOutput,
                    CancellationToken.None);

                StatusSeverity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
                StatusMessage = result.Success
                    ? "La descarga semanal terminó. Selecciona manualmente el archivo o carpeta descargados para iniciar el workflow."
                    : "La descarga semanal falló. Revisa la salida en vivo.";
                IsStatusInfoOpen = true;
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"RunDownloadAsync failed: {ex}");
                ShowStatus(InfoBarSeverity.Error, $"No se pudo ejecutar la descarga semanal: {ex.Message}");
            }
        }

        public async Task RunDownloadFromLinkAsync(string link, string mode)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                ShowStatus(InfoBarSeverity.Warning, "Pega un link de Nyaa antes de lanzar la descarga por link.");
                return;
            }

            try
            {
                var settings = await workflowExecutionService.GetSettingsAsync();
                if (string.IsNullOrWhiteSpace(settings.DownloaderLinkScriptPath) || !File.Exists(settings.DownloaderLinkScriptPath))
                {
                    ShowStatus(InfoBarSeverity.Error, "No se encontró configurado el script de descarga por link de Nyaa.");
                    return;
                }

                BeginUtilityOutput(
                    "Descarga directa desde link de Nyaa",
                    "Salida en vivo del script de descarga por link.",
                    "Ejecutando descarga por link. Cuando termine, selecciona manualmente el archivo o carpeta resultantes para iniciar un nuevo workflow.");
                var result = await App.Host.ProcessRunnerService.RunAsync(
                    new ProcessExecutionRequest
                    {
                        FileName = settings.PythonPath,
                        Arguments = BuildDownloadFromLinkArgs(settings, link, mode),
                        WorkingDirectory = settings.DownloadWorkingDirectory,
                    },
                    AppendOutput,
                    CancellationToken.None);

                StatusSeverity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
                StatusMessage = result.Success
                    ? "La descarga por link terminó. Revisa Nyaa/qBittorrent y luego selecciona manualmente el archivo o carpeta resultantes."
                    : "La descarga por link falló. Revisa la salida en vivo.";
                IsStatusInfoOpen = true;
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"RunDownloadFromLinkAsync failed: {ex}");
                ShowStatus(InfoBarSeverity.Error, $"No se pudo ejecutar la descarga por link: {ex.Message}");
            }
        }

        [RelayCommand]
        private void OpenRootFolder()
        {
            if (currentWorkflow is null || !Directory.Exists(currentWorkflow.RootPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = currentWorkflow.RootPath,
                UseShellExecute = true,
            });
        }

        [RelayCommand]
        private void OpenSelectedLog()
        {
            var logPath = SelectedStep?.StdoutLogPath;
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true,
            });
        }

        [RelayCommand]
        private void CancelExecution()
        {
            executionCancellationTokenSource?.Cancel();
        }

        [RelayCommand]
        private void CopyPackageRarRawData()
        {
            CopyPackageRarHint(
                WorkflowExecutionService.PackageRarRawDataHintKey,
                "No hay datos RAW del empaquetado para copiar.",
                "Se copiaron los datos RAW del empaquetado.");
        }

        [RelayCommand]
        private void CopyPackageRarWeightSummary()
        {
            CopyPackageRarHint(
                WorkflowExecutionService.PackageRarWeightSummaryHintKey,
                "No hay resumen de pesos disponible para copiar.",
                "Se copió el resumen completo de pesos.");
        }

        [RelayCommand]
        private void CopyPackageRarCleanName()
        {
            CopyPackageRarHint(
                WorkflowExecutionService.PackageRarCleanNameHintKey,
                "No hay nombre limpio disponible para copiar.",
                "Se copió el nombre limpio.");
        }

        public async Task CreateWorkflowFromPathAsync(string path, bool isFile)
        {
            ResetWorkflowState("Cargando un nuevo workflow desde la ruta seleccionada...");
            currentWorkflow = await workflowExecutionService.CreateWorkflowAsync(path, isFile, CancellationToken.None);
            utilityOutputActive = false;
            RefreshFromWorkflow(currentWorkflow);
        }

        public void SelectStepFromUser(WorkflowStepState step)
        {
            HasExplicitStepSelection = true;
            SelectedStep = step;
        }

        public void BeginWorkflowSelection(string message)
        {
            ResetWorkflowState(message);
            utilityOutputActive = false;
            SelectedStepTitle = "Sin paso seleccionado";
            SelectedStepDescription = "Selecciona un paso para ver detalle y salida.";
            DetailOutput = "Esperando que elijas el archivo o carpeta base.";
        }

        public async Task PrepareCurrentWorkflowForCleanReloadAsync()
        {
            if (currentWorkflow is null
                || !TryResolveWorkflowReloadSource(currentWorkflow, out var sourcePath, out var isFile))
            {
                return;
            }

            var cleanWorkflow = App.Host.WorkflowEngine.CreateWorkflow(sourcePath, isFile);
            cleanWorkflow.Id = currentWorkflow.Id;
            cleanWorkflow.CreatedAt = currentWorkflow.CreatedAt;
            cleanWorkflow.LastExecutionSummary = "Sin ejecuciones todavía.";

            await workflowStore.SaveAsync(cleanWorkflow);

            currentWorkflow = cleanWorkflow;
            utilityOutputActive = false;
            RefreshFromWorkflow(cleanWorkflow);
        }

        public async void Receive(WorkflowSelectedMessage message)
        {
            var workflow = await workflowExecutionService.LoadWorkflowAsync(message.Value);
            if (workflow is null)
            {
                return;
            }

            currentWorkflow = workflow;
            RefreshFromWorkflow(workflow);
        }

        private CancellationToken CancellationToken => executionCancellationTokenSource?.Token ?? CancellationToken.None;

        private async Task LoadLatestWorkflowAsync()
        {
            currentWorkflow = await workflowExecutionService.LoadLatestWorkflowAsync();
            if (currentWorkflow is not null)
            {
                RefreshFromWorkflow(currentWorkflow);
            }
        }

        private async Task LoadQuickSettingsAsync()
        {
            quickSettings = await workflowExecutionService.GetSettingsAsync();
            SyncQuickOptionsFromSettings();
        }

        private static bool TryResolveWorkflowReloadSource(WorkflowInstance workflow, out string sourcePath, out bool isFile)
        {
            var sourceSelectionIsFile = workflow.SourceSelectionIsFile == true;
            var candidatePath = sourceSelectionIsFile
                ? workflow.PrimaryVideoPath
                : workflow.RootPath;

            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                candidatePath = !string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath)
                    ? workflow.PrimaryVideoPath
                    : workflow.RootPath;
            }

            sourcePath = candidatePath ?? string.Empty;
            isFile = sourceSelectionIsFile;
            return !string.IsNullOrWhiteSpace(sourcePath);
        }

        private void SyncQuickOptionsFromSettings()
        {
            DownloadDryRunEnabled = quickSettings.DownloaderDryRun;
            DownloadForceLatestEnabled = quickSettings.DownloaderForceLatest;
            TranslateFastModeEnabled = quickSettings.SubtitleFastMode;
            TranslateSkipSummaryEnabled = quickSettings.SubtitleSkipSummary;
            CleanupCloseQbittorrentEnabled = quickSettings.TrackCleanupCloseQbittorrent;
            CleanupDeleteOriginalsEnabled = quickSettings.TrackCleanupDeleteOriginals;
            RarSkipImagesEnabled = quickSettings.RarSkipImages;
            RarNoCompressEnabled = quickSettings.RarNoCompress;
            RarUseCompressionNormalEnabled = quickSettings.RarUseCompressionNormal;
            RarVerboseEnabled = quickSettings.RarVerbose;
            RarImageFormatQuick = string.Equals(quickSettings.RarImageFormat, "png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
            NotifyQuickOptionLabelsChanged();
        }

        private void ApplyQuickOptionsToSettings()
        {
            quickSettings.DownloaderDryRun = DownloadDryRunEnabled;
            quickSettings.DownloaderForceLatest = DownloadForceLatestEnabled;
            quickSettings.SubtitleFastMode = TranslateFastModeEnabled;
            quickSettings.SubtitleSkipSummary = TranslateSkipSummaryEnabled;
            quickSettings.TrackCleanupCloseQbittorrent = CleanupCloseQbittorrentEnabled;
            quickSettings.TrackCleanupDeleteOriginals = CleanupDeleteOriginalsEnabled;
            quickSettings.RarSkipImages = RarSkipImagesEnabled;
            quickSettings.RarNoCompress = RarNoCompressEnabled;
            quickSettings.RarUseCompressionNormal = RarUseCompressionNormalEnabled;
            quickSettings.RarVerbose = RarVerboseEnabled;
            quickSettings.RarImageFormat = string.Equals(RarImageFormatQuick, "png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
        }

        private async Task PersistQuickSettingsAsync()
        {
            try
            {
                ApplyQuickOptionsToSettings();
                await workflowExecutionService.SaveSettingsAsync(
                    quickSettings,
                    workflowExecutionService.GetDecryptedRarPassword(quickSettings));
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"PersistQuickSettingsAsync failed: {ex}");
                ShowStatus(InfoBarSeverity.Error, $"No se pudieron guardar las opciones rápidas: {ex.Message}");
            }
        }

        private void NotifyQuickOptionLabelsChanged()
        {
            OnPropertyChanged(nameof(DownloadDryRunButtonLabel));
            OnPropertyChanged(nameof(DownloadForceLatestButtonLabel));
            OnPropertyChanged(nameof(TranslateFastModeButtonLabel));
            OnPropertyChanged(nameof(TranslateSkipSummaryButtonLabel));
            OnPropertyChanged(nameof(CleanupCloseQbittorrentButtonLabel));
            OnPropertyChanged(nameof(CleanupDeleteOriginalsButtonLabel));
            OnPropertyChanged(nameof(RarSkipImagesButtonLabel));
            OnPropertyChanged(nameof(RarNoCompressButtonLabel));
            OnPropertyChanged(nameof(RarCompressionModeButtonLabel));
            OnPropertyChanged(nameof(RarVerboseButtonLabel));
            OnPropertyChanged(nameof(RarImageFormatButtonLabel));
        }

        private async Task ChangeTranslationDecisionAsync(bool translateRequired)
        {
            if (currentWorkflow is null)
            {
                StatusSeverity = InfoBarSeverity.Warning;
                StatusMessage = "No hay workflow activo para cambiar la decisión de traducción.";
                IsStatusInfoOpen = true;
                DiagnosticsTrace.Write("Translation decision ignored because there is no active workflow.");
                return;
            }

            var translateStep = currentWorkflow.FindStep(WorkflowStepKey.TranslateSubs);
            if (translateStep is null)
            {
                StatusSeverity = InfoBarSeverity.Error;
                StatusMessage = "El workflow actual no contiene el paso de traducción.";
                IsStatusInfoOpen = true;
                DiagnosticsTrace.Write("Translation decision failed because TranslateSubs step was not found.");
                return;
            }

            try
            {
                DiagnosticsTrace.Write($"Translation decision requested. translateRequired={translateRequired}.");
                currentWorkflow = await workflowExecutionService.DecideTranslationAsync(currentWorkflow, translateRequired);
                RefreshFromWorkflow(currentWorkflow);
                StatusSeverity = InfoBarSeverity.Informational;
                StatusMessage = translateRequired
                    ? "Se marcó la traducción de subtítulos como requerida."
                    : "Se omitió la traducción de subtítulos y el flujo avanzó al siguiente paso disponible.";
                IsStatusInfoOpen = true;
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"Translation decision crashed: {ex}");
                StatusSeverity = InfoBarSeverity.Error;
                StatusMessage = $"No se pudo actualizar la decisión de traducción: {ex.Message}";
                IsStatusInfoOpen = true;
            }
        }

        private async Task ExecuteAsync(Func<Task<ExecutionRecord?>> operation, WorkflowStepKey outputStepKey)
        {
            executionCancellationTokenSource?.Dispose();
            executionCancellationTokenSource = new CancellationTokenSource();
            activeOutputStepKey = outputStepKey;
            utilityOutputActive = false;
            LiveOutput = string.Empty;
            DetailOutput = "Esperando salida del proceso...";

            try
            {
                var record = await operation();
                if (currentWorkflow is not null)
                {
                    currentWorkflow = await workflowExecutionService.LoadWorkflowAsync(currentWorkflow.Id) ?? currentWorkflow;
                    RefreshFromWorkflow(currentWorkflow, activeOutputStepKey);
                }

                if (record is not null)
                {
                    StatusSeverity = record.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
                    StatusMessage = record.Summary;
                    IsStatusInfoOpen = true;
                }
            }
            catch (OperationCanceledException)
            {
                StatusSeverity = InfoBarSeverity.Warning;
                StatusMessage = "La ejecución fue cancelada.";
                IsStatusInfoOpen = true;
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"ExecuteAsync crashed: {ex}");
                StatusSeverity = InfoBarSeverity.Error;
                StatusMessage = $"La ejecución falló: {ex.Message}";
                IsStatusInfoOpen = true;
            }
            finally
            {
                activeOutputStepKey = null;
                RefreshSelectedStepOutput();
            }
        }

        private async Task SkipAheadToSelectedStepAsync(bool runAfterPreparing)
        {
            if (currentWorkflow is null || SelectedStep is null)
            {
                ShowStatus(InfoBarSeverity.Warning, "Selecciona un workflow y un paso antes de saltar el flujo.");
                return;
            }

            var skippedCount = 0;
            foreach (var step in currentWorkflow.Steps)
            {
                if (step.StepKey == SelectedStep.StepKey)
                {
                    break;
                }

                if (step.Status is WorkflowStepStatus.Succeeded or WorkflowStepStatus.Skipped)
                {
                    continue;
                }

                step.Status = WorkflowStepStatus.Skipped;
                step.StatusReason = $"Paso omitido manualmente para continuar desde {SelectedStep.DisplayName}.";
                step.FinishedAt = DateTimeOffset.UtcNow;
                step.UserDecision = "skip";
                skippedCount++;
            }

            currentWorkflow.LastExecutionSummary = skippedCount > 0
                ? $"{skippedCount} paso(s) omitidos para continuar desde {SelectedStep.DisplayName}."
                : $"{SelectedStep.DisplayName} ya estaba listo para ejecutarse sin omitir pasos previos.";
            App.Host.WorkflowEngine.RefreshStatuses(currentWorkflow);
            await workflowStore.SaveAsync(currentWorkflow);
            RefreshFromWorkflow(currentWorkflow, SelectedStep.StepKey);
            ShowStatus(InfoBarSeverity.Warning, currentWorkflow.LastExecutionSummary);

            if (runAfterPreparing)
            {
                await RunSelectedStepAsync();
            }
        }

        private void RefreshFromWorkflow(WorkflowInstance workflow, WorkflowStepKey? preferredSelectedStep = null)
        {
            utilityOutputActive = false;
            DisplayName = workflow.DisplayName;
            RootPath = workflow.RootPath;
            StepItems.Clear();
            foreach (var step in workflow.Steps)
            {
                StepItems.Add(step);
            }

            SelectedStep = preferredSelectedStep is not null
                ? StepItems.FirstOrDefault(step => step.StepKey == preferredSelectedStep)
                    ?? StepItems.FirstOrDefault(step => step.StepKey == workflow.CurrentStep)
                    ?? StepItems.FirstOrDefault()
                : StepItems.FirstOrDefault(step => step.StepKey == workflow.CurrentStep) ?? StepItems.FirstOrDefault();
            NextStepLabel = App.Host.WorkflowEngine.GetNextReadyStep(workflow)?.DisplayName ?? "Sin pasos listos";
            GlobalStatus = workflow.Steps.Any(step => step.Status == WorkflowStepStatus.Failed)
                ? "Con errores"
                : workflow.Steps.Any(step => step.Status == WorkflowStepStatus.NeedsDecision)
                    ? "Requiere decisión"
                    : workflow.Steps.All(step => step.Status is WorkflowStepStatus.Succeeded or WorkflowStepStatus.Skipped)
                        ? "Completado"
                        : "En progreso";
            StatusMessage = workflow.LastExecutionSummary;
            IsStatusInfoOpen = true;
            StatusSeverity = workflow.Steps.Any(step => step.Status == WorkflowStepStatus.Failed)
                ? InfoBarSeverity.Error
                : InfoBarSeverity.Informational;
            UpdateTranslationDecisionVisibility(workflow);
            UpdateQuickOptionsVisibility();
            UpdatePackageRarDetailActions();
            RefreshSelectedStepOutput();
        }

        private void AppendOutput(string line)
        {
            if (IsStructuredOutputMetadata(line))
            {
                return;
            }

            void UpdateOutput()
            {
                LiveOutput += string.IsNullOrWhiteSpace(LiveOutput) ? line : $"{Environment.NewLine}{line}";
                if (activeOutputStepKey is not null && SelectedStep?.StepKey == activeOutputStepKey)
                {
                    DetailOutput = LiveOutput;
                }
                else if (utilityOutputActive)
                {
                    DetailOutput = LiveOutput;
                }
            }

            var dispatcherQueue = App.MainWindowInstance?.DispatcherQueue;
            if (dispatcherQueue is not null && !dispatcherQueue.HasThreadAccess)
            {
                if (!dispatcherQueue.TryEnqueue(UpdateOutput))
                {
                    DiagnosticsTrace.Write("AppendOutput could not enqueue UI update.");
                }

                return;
            }

            UpdateOutput();
        }

        public void ShowStatus(InfoBarSeverity severity, string message)
        {
            StatusSeverity = severity;
            StatusMessage = message;
            IsStatusInfoOpen = true;
        }

        private void SelectStep(WorkflowStepKey stepKey)
        {
            SelectedStep = StepItems.FirstOrDefault(step => step.StepKey == stepKey) ?? SelectedStep;
        }

        private void RefreshSelectedStepOutput()
        {
            if (SelectedStep is null)
            {
                DetailOutput = "Selecciona un paso para ver detalle y salida.";
                return;
            }

            if (activeOutputStepKey is not null
                && SelectedStep.StepKey == activeOutputStepKey
                && !string.IsNullOrWhiteSpace(LiveOutput))
            {
                DetailOutput = LiveOutput;
                return;
            }

            if (utilityOutputActive && !string.IsNullOrWhiteSpace(LiveOutput))
            {
                DetailOutput = LiveOutput;
                return;
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(SelectedStep.StdoutLogPath) && File.Exists(SelectedStep.StdoutLogPath))
            {
                var stdout = File.ReadAllText(SelectedStep.StdoutLogPath).Trim();
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    parts.Add(stdout);
                }
            }

            if (!string.IsNullOrWhiteSpace(SelectedStep.StderrLogPath) && File.Exists(SelectedStep.StderrLogPath))
            {
                var stderr = File.ReadAllText(SelectedStep.StderrLogPath).Trim();
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    parts.Add(parts.Count == 0 ? stderr : $"--- STDERR ---{Environment.NewLine}{stderr}");
                }
            }

            DetailOutput = parts.Count > 0
                ? string.Join($"{Environment.NewLine}{Environment.NewLine}", parts)
                : "No hay salida disponible para este paso todavía.";
        }

        private void BeginUtilityOutput(string title, string description, string statusMessage)
        {
            ResetWorkflowState(statusMessage);
            utilityOutputActive = true;
            SelectedStepTitle = title;
            SelectedStepDescription = description;
            DetailOutput = "Esperando salida del script...";
        }

        private void ResetWorkflowState(string message)
        {
            currentWorkflow = null;
            activeOutputStepKey = null;
            LiveOutput = string.Empty;
            HasExplicitStepSelection = false;
            DetailOutputHeightWasResized = false;
            DetailOutputHeight = 420;
            StepItems.Clear();
            foreach (var step in CreateNeutralStepTemplate())
            {
                StepItems.Add(step);
            }
            SelectedStep = StepItems.FirstOrDefault();
            DisplayName = "Selecciona un archivo o carpeta para comenzar.";
            RootPath = "Sin workflow activo";
            NextStepLabel = "Selecciona archivo o carpeta";
            GlobalStatus = "Esperando selección";
            StatusSeverity = InfoBarSeverity.Informational;
            StatusMessage = message;
            IsStatusInfoOpen = true;
            ShowTranslationDecisionActions = false;
            UpdateQuickOptionsVisibility();
            UpdatePackageRarDetailActions();
        }

        private void UpdateTranslationDecisionVisibility(WorkflowInstance? workflow)
        {
            var translationStep = workflow?.FindStep(WorkflowStepKey.TranslateSubs);
            ShowTranslationDecisionActions = translationStep?.Status == WorkflowStepStatus.NeedsDecision;
        }

        private void UpdateQuickOptionsVisibility()
        {
            ShowDownloadQuickOptions = SelectedStep?.StepKey == WorkflowStepKey.Download;
            ShowTranslateQuickOptions = SelectedStep?.StepKey == WorkflowStepKey.TranslateSubs;
            ShowCleanTracksQuickOptions = SelectedStep?.StepKey == WorkflowStepKey.CleanTracks;
            ShowPackageRarQuickOptions = SelectedStep?.StepKey == WorkflowStepKey.PackageRar;
            ShowQuickActionOptions = SelectedStep is not null
                && (ShowDownloadQuickOptions || ShowTranslateQuickOptions || ShowCleanTracksQuickOptions || ShowPackageRarQuickOptions);
            ShowSkipAheadActions = currentWorkflow is not null
                && SelectedStep is not null
                && SelectedStep.StepKey != WorkflowStepKey.Download
                && currentWorkflow.Steps
                    .TakeWhile(step => step.StepKey != SelectedStep.StepKey)
                    .Any(step => step.Status is not (WorkflowStepStatus.Succeeded or WorkflowStepStatus.Skipped));

            QuickOptionsTitle = SelectedStep is null
                ? "Opciones rápidas"
                : $"Opciones rápidas de {SelectedStep.DisplayName}";

            QuickOptionsDescription = SelectedStep?.StepKey switch
            {
                WorkflowStepKey.Download => "Ajusta cómo se lanzan las descargas de Nyaa desde el panel lateral.",
                WorkflowStepKey.TranslateSubs => "Controla los flags rápidos del traductor antes de ejecutarlo.",
                WorkflowStepKey.CleanTracks => "Controla qué hace SubForge cuando encuentra el archivo en uso y si conserva la carpeta ORIGINAL.",
                WorkflowStepKey.PackageRar => "Puedes saltar pasos previos y empaquetar de inmediato si tu release ya está lista.",
                _ => "Este paso no tiene flags rápidos expuestos en el dashboard."
            };
        }

        private void UpdatePackageRarDetailActions()
        {
            var hasHints = SelectedStep?.StepKey == WorkflowStepKey.PackageRar
                && SelectedStep.OutputHints.Count > 0;

            ShowPackageRarDetailActions = hasHints;
            ResetPackageRarCopyState();
            PackageRarDetailSummary = hasHints
                ? SelectedStep?.OutputHints.TryGetValue(WorkflowExecutionService.PackageRarWeightSummaryHintKey, out var summary) == true
                    ? summary
                    : "Los datos del empaquetado están listos para copiar."
                : "Ejecuta Empaquetar RAR para habilitar la copia rápida.";
        }

        private void CopyPackageRarHint(string hintKey, string missingMessage, string successMessage)
        {
            if (SelectedStep?.OutputHints.TryGetValue(hintKey, out var value) != true || string.IsNullOrWhiteSpace(value))
            {
                ShowStatus(InfoBarSeverity.Warning, missingMessage);
                return;
            }

            var package = new DataPackage();
            package.SetText(value);
            Clipboard.SetContent(package);
            MarkPackageRarDetailActionCopied(hintKey);
            ShowStatus(InfoBarSeverity.Success, successMessage);
        }

        private void MarkPackageRarDetailActionCopied(string hintKey)
        {
            switch (hintKey)
            {
                case WorkflowExecutionService.PackageRarRawDataHintKey:
                    PackageRarRawDataCopied = true;
                    break;
                case WorkflowExecutionService.PackageRarWeightSummaryHintKey:
                    PackageRarWeightSummaryCopied = true;
                    break;
                case WorkflowExecutionService.PackageRarCleanNameHintKey:
                    PackageRarCleanNameCopied = true;
                    break;
            }

            NotifyPackageRarCopyButtonVisuals();
        }

        private void ResetPackageRarCopyState()
        {
            PackageRarRawDataCopied = false;
            PackageRarWeightSummaryCopied = false;
            PackageRarCleanNameCopied = false;
            NotifyPackageRarCopyButtonVisuals();
        }

        private void NotifyPackageRarCopyButtonVisuals()
        {
            OnPropertyChanged(nameof(PackageRarRawDataButtonBackground));
            OnPropertyChanged(nameof(PackageRarWeightSummaryButtonBackground));
            OnPropertyChanged(nameof(PackageRarCleanNameButtonBackground));
            OnPropertyChanged(nameof(PackageRarRawDataButtonBorderBrush));
            OnPropertyChanged(nameof(PackageRarWeightSummaryButtonBorderBrush));
            OnPropertyChanged(nameof(PackageRarCleanNameButtonBorderBrush));
            OnPropertyChanged(nameof(PackageRarRawDataButtonForeground));
            OnPropertyChanged(nameof(PackageRarWeightSummaryButtonForeground));
            OnPropertyChanged(nameof(PackageRarCleanNameButtonForeground));
        }

        private static bool IsStructuredOutputMetadata(string line)
        {
            return line.StartsWith("MWO_RAW_DATA\t", StringComparison.Ordinal)
                || line.StartsWith("MWO_WEIGHT_SUMMARY\t", StringComparison.Ordinal);
        }

        private static IReadOnlyList<WorkflowStepState> CreateNeutralStepTemplate() => new List<WorkflowStepState>
        {
            new()
            {
                StepKey = WorkflowStepKey.Download,
                DisplayName = "Descarga Nyaa",
                Status = WorkflowStepStatus.Pending,
                StatusReason = "Utilidad global para traer material nuevo desde Nyaa.",
            },
            new()
            {
                StepKey = WorkflowStepKey.InspectSubs,
                DisplayName = "Inspeccionar subtítulos",
                Status = WorkflowStepStatus.Pending,
                StatusReason = "Revisa si el archivo principal contiene subtítulos en español.",
            },
            new()
            {
                StepKey = WorkflowStepKey.TranslateSubs,
                DisplayName = "Traducir subtítulos",
                Status = WorkflowStepStatus.Pending,
                StatusReason = "Solo aparece como decisión manual si la inspección no puede resolverlo.",
            },
            new()
            {
                StepKey = WorkflowStepKey.CleanTracks,
                DisplayName = "Limpiar tracks",
                Status = WorkflowStepStatus.Pending,
                StatusReason = "Ejecuta SubForge para filtrar pistas y limpiar subtítulos extra.",
            },
            new()
            {
                StepKey = WorkflowStepKey.TagAndRename,
                DisplayName = "Etiquetas y renombre",
                Status = WorkflowStepStatus.Pending,
                StatusReason = "Aplica metadatos y abre la etapa de renombre final.",
            },
            new()
            {
                StepKey = WorkflowStepKey.PackageRar,
                DisplayName = "Empaquetar RAR",
                Status = WorkflowStepStatus.Pending,
                StatusReason = "Genera el comprimido final con contraseña e información adjunta.",
            }
        };

        private static IReadOnlyList<string> BuildDownloadArgs(AppSettings settings)
        {
            var args = new List<string>
            {
                settings.DownloaderScriptPath,
                "--config",
                settings.DownloaderConfigPath,
            };

            if (settings.DownloaderDryRun)
            {
                args.Add("--dry-run");
            }

            if (settings.DownloaderForceLatest)
            {
                args.Add("--force-latest");
            }

            return args;
        }

        private static IReadOnlyList<string> BuildDownloadFromLinkArgs(AppSettings settings, string link, string mode)
        {
            return new List<string>
            {
                settings.DownloaderLinkScriptPath,
                "--config",
                settings.DownloaderConfigPath,
                "--link",
                link.Trim(),
                "--mode",
                string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase) ? "all" : "from-latest",
            };
        }

        private static Microsoft.UI.Xaml.Media.SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b) =>
            new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
    }
}
