using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using MediaWorkflowOrchestrator.Messages;
using Windows.ApplicationModel.DataTransfer;

namespace MediaWorkflowOrchestrator.ViewModels
{
    public partial class DashboardViewModel : BaseViewModel, IRecipient<WorkflowSelectedMessage>
    {
        private const double MinDetailOutputHeight = 220;
        private const double MaxDetailOutputHeight = 1200;
        private static readonly Regex CompactMuxProgressRegex = new(
            @"^\[(?<current>\d+)\/(?<total>\d+)\]\s+Mux\s+(?<percent>\d{1,3})%:\s+(?<name>.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CompactGlobalProgressRegex = new(
            @"^\[Global\]\s+.+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex StructuredProgressRegex = new(
            @"^MWO_PROGRESS\t(?<percent>\d{1,3}(?:[.,]\d+)?)\t(?<label>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex GenericPercentRegex = new(
            @"(?<!\d)(?<percent>100(?:[.,]0+)?|\d{1,2}(?:[.,]\d+)?)\s*%",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FractionProgressRegex = new(
            @"(?<!\d)(?<current>\d{1,5})\s*/\s*(?<total>\d{1,5})(?!\d)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv",
            ".mp4",
            ".webm",
            ".avi",
            ".mov",
            ".wmv",
            ".flv",
            ".mpeg",
            ".mpg"
        };

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
        private readonly List<string> liveOutputDisplayLines = new();
        private readonly Dictionary<string, int> liveOutputReplaceableIndexes = new(StringComparer.OrdinalIgnoreCase);
        private int cleanupAudioInspectionVersion;
        private bool syncingQuickSettings;
        private string cleanupAudioSelectionContextMessage = "Selecciona Limpiar tracks para revisar audios y subtítulos antes de filtrar.";
        private WorkflowProgressSnapshot? currentProgressSnapshot;

        public DashboardViewModel()
        {
            Title = "Dashboard";
            StepItems = new ObservableCollection<WorkflowStepState>();
            VisibleStepItems = new ObservableCollection<WorkflowStepState>();
            CleanupAudioOptions = new ObservableCollection<TrackCleanupAudioOption>();
            CleanupSubtitleOptions = new ObservableCollection<TrackCleanupSubtitleOption>();
            CleanupSpecialCases = new ObservableCollection<TrackCleanupSpecialCaseItem>();
            ResetWorkflowState("La descarga Nyaa se ejecuta como utilidad global; el workflow real empieza cuando eliges el archivo o carpeta.");
            WeakReferenceMessenger.Default.Register(this);
            _ = LoadQuickSettingsAsync();
            if (!App.StartWithCleanReloadState)
            {
                _ = LoadLatestWorkflowAsync();
            }
        }

        public ObservableCollection<WorkflowStepState> StepItems { get; }
        public ObservableCollection<WorkflowStepState> VisibleStepItems { get; }
        public ObservableCollection<TrackCleanupAudioOption> CleanupAudioOptions { get; }
        public ObservableCollection<TrackCleanupSubtitleOption> CleanupSubtitleOptions { get; }
        public ObservableCollection<TrackCleanupSpecialCaseItem> CleanupSpecialCases { get; }

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
        private bool _showDetailOutputTerminal = true;

        [ObservableProperty]
        private bool _showDetailProgress;

        [ObservableProperty]
        private bool _detailProgressIsIndeterminate = true;

        [ObservableProperty]
        private double _detailProgressValue;

        [ObservableProperty]
        private string _detailProgressTitle = "Progreso del paso";

        [ObservableProperty]
        private string _detailProgressMessage = "Esperando señales del proceso...";

        [ObservableProperty]
        private string _detailProgressPercentLabel = "En curso";

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
        private bool _showTagAndRenameQuickOptions;

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
        private bool _packageRarSeriesTitleCopied;

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
        private bool _tagAndRenameAttachCoverEnabled = true;

        [ObservableProperty]
        private bool _cleanupAudioSelectionBusy;

        [ObservableProperty]
        private string _cleanupAudioSelectionMessage = "Selecciona Limpiar tracks para revisar audios y subtítulos antes de filtrar.";

        [ObservableProperty]
        private string _cleanupSubtitleSelectionMessage = "Selecciona Limpiar tracks para revisar audios y subtítulos antes de filtrar.";

        [ObservableProperty]
        private string _cleanupSpecialCasesMessage = string.Empty;

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

        [ObservableProperty]
        private string _rarCaptureCountQuick = string.Empty;

        partial void OnRarCaptureCountQuickChanged(string value)
        {
            OnPropertyChanged(nameof(RarCaptureCountButtonLabel));
            if (syncingQuickSettings)
            {
                return;
            }

            if (TryNormalizeRarCaptureCount(value, out _))
            {
                _ = PersistQuickSettingsAsync();
            }
        }

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
            OnPropertyChanged(nameof(CanOpenSelectedLog));
            _ = EnsureCleanupAudioSelectionForCurrentStepAsync(value);
        }

        public string DownloadDryRunButtonLabel => $"Dry-run: {(DownloadDryRunEnabled ? "ON" : "OFF")}";
        public string DownloadForceLatestButtonLabel => $"Force latest: {(DownloadForceLatestEnabled ? "ON" : "OFF")}";
        public string TranslateFastModeButtonLabel => $"Modo rápido: {(TranslateFastModeEnabled ? "ON" : "OFF")}";
        public string TranslateSkipSummaryButtonLabel => $"Omitir resumen: {(TranslateSkipSummaryEnabled ? "ON" : "OFF")}";
        public string CleanupCloseQbittorrentButtonLabel => $"Cerrar qBittorrent: {(CleanupCloseQbittorrentEnabled ? "ON" : "OFF")}";
        public string CleanupDeleteOriginalsButtonLabel => $"Eliminar originales: {(CleanupDeleteOriginalsEnabled ? "ON" : "OFF")}";
        public string TagAndRenameAttachCoverButtonLabel => $"Cover/poster: {(TagAndRenameAttachCoverEnabled ? "ON" : "OFF")}";
        public string CleanupAudioRefreshButtonLabel => CleanupAudioSelectionBusy ? "Cargando tracks..." : "Recargar tracks";
        public string PackageRarRawDataButtonLabel => "Raw Data";
        public string PackageRarWeightSummaryButtonLabel => "Peso completo";
        public string PackageRarCleanNameButtonLabel => "Nombre limpio";
        public string PackageRarSeriesTitleButtonLabel => "Nombre corto";
        public bool HasCleanupAudioOptions => CleanupAudioOptions.Count > 0;
        public bool HasCleanupSubtitleOptions => CleanupSubtitleOptions.Count > 0;
        public bool HasCleanupSpecialCases => CleanupSpecialCases.Count > 0;
        public string CleanupSpecialCasesPanelTitle => HasCleanupSpecialCases ? "Casos especiales detectados" : "Revision de layouts";
        public string CleanupSpecialCasesPanelMessage => HasCleanupSpecialCases
            ? CleanupSpecialCasesMessage
            : "Si el lote mezcla fuentes con otro orden de audios o subtitulos, aqui se enlistaran para que Limpiar tracks los trate como caso especial.";
        public string CleanupSpecialCasesListText => HasCleanupSpecialCases
            ? string.Join(
                Environment.NewLine,
                CleanupSpecialCases.Select(item => $"- {item.FileName}: {item.Reason}"))
            : "Sin diferencias detectadas en la inspeccion actual.";
        public bool CanOpenSelectedLog => SelectedStep is not null
            && !string.IsNullOrWhiteSpace(SelectedStep.StdoutLogPath)
            && File.Exists(SelectedStep.StdoutLogPath);
        public string RarSkipImagesButtonLabel => $"Sin imágenes: {(RarSkipImagesEnabled ? "ON" : "OFF")}";
        public string RarNoCompressButtonLabel => $"Solo info: {(RarNoCompressEnabled ? "ON" : "OFF")}";
        public string RarCompressionModeButtonLabel => $"Modo RAR: {(RarUseCompressionNormalEnabled ? "Comprimir" : "Contenedor fast")}";
        public string RarVerboseButtonLabel => $"Verbose: {(RarVerboseEnabled ? "ON" : "OFF")}";
        public string RarImageFormatButtonLabel => $"Formato imagen: {RarImageFormatQuick.ToUpperInvariant()}";
        public string RarCaptureCountButtonLabel => BuildRarCaptureCountButtonLabel();
        public string DetailOutputTerminalToggleLabel => ShowDetailOutputTerminal ? "Ocultar terminal" : "Mostrar terminal";
        public Microsoft.UI.Xaml.Controls.Symbol DetailOutputTerminalToggleSymbol => ShowDetailOutputTerminal
            ? Microsoft.UI.Xaml.Controls.Symbol.Remove
            : Microsoft.UI.Xaml.Controls.Symbol.OpenPane;
        public Microsoft.UI.Xaml.Media.Brush PackageRarRawDataButtonBackground => PackageRarRawDataCopied ? CopiedButtonBackgroundBrush : PendingButtonBackgroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarWeightSummaryButtonBackground => PackageRarWeightSummaryCopied ? CopiedButtonBackgroundBrush : PendingButtonBackgroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarCleanNameButtonBackground => PackageRarCleanNameCopied ? CopiedButtonBackgroundBrush : PendingButtonBackgroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarSeriesTitleButtonBackground => PackageRarSeriesTitleCopied ? CopiedButtonBackgroundBrush : PendingButtonBackgroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarRawDataButtonBorderBrush => PackageRarRawDataCopied ? CopiedButtonBorderBrush : PendingButtonBorderBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarWeightSummaryButtonBorderBrush => PackageRarWeightSummaryCopied ? CopiedButtonBorderBrush : PendingButtonBorderBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarCleanNameButtonBorderBrush => PackageRarCleanNameCopied ? CopiedButtonBorderBrush : PendingButtonBorderBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarSeriesTitleButtonBorderBrush => PackageRarSeriesTitleCopied ? CopiedButtonBorderBrush : PendingButtonBorderBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarRawDataButtonForeground => ButtonForegroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarWeightSummaryButtonForeground => ButtonForegroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarCleanNameButtonForeground => ButtonForegroundBrush;
        public Microsoft.UI.Xaml.Media.Brush PackageRarSeriesTitleButtonForeground => ButtonForegroundBrush;

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
        private void ToggleDetailOutputTerminal()
        {
            ShowDetailOutputTerminal = !ShowDetailOutputTerminal;
            OnPropertyChanged(nameof(DetailOutputTerminalToggleLabel));
            OnPropertyChanged(nameof(DetailOutputTerminalToggleSymbol));
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
            if (!await EnsureStepPreconditionsAsync(nextStep.StepKey))
            {
                return;
            }

            await PersistQuickSettingsAsync();
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
            if (!await EnsureStepPreconditionsAsync(SelectedStep.StepKey))
            {
                return;
            }

            await PersistQuickSettingsAsync();
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
        private async Task RefreshCleanupAudioOptionsAsync()
        {
            await RefreshCleanupAudioSelectionAsync(forceReload: true);
        }

        [RelayCommand]
        private async Task ToggleCleanupAudioSelectionAsync(TrackCleanupAudioOption? option)
        {
            if (option is null || currentWorkflow is null)
            {
                return;
            }

            option.IsSelected = !option.IsSelected;
            EnsurePrimaryCleanupAudioSelection();
            UpdateCleanupAudioSelectionMessage();
            await PersistCleanupAudioSelectionAsync();
        }

        [RelayCommand]
        private async Task SelectAllCleanupAudiosAsync()
        {
            if (CleanupAudioOptions.Count == 0 || currentWorkflow is null)
            {
                return;
            }

            foreach (var option in CleanupAudioOptions)
            {
                option.IsSelected = true;
            }

            EnsurePrimaryCleanupAudioSelection();
            UpdateCleanupAudioSelectionMessage();
            await PersistCleanupAudioSelectionAsync();
        }

        [RelayCommand]
        private async Task ClearCleanupAudioSelectionAsync()
        {
            if (CleanupAudioOptions.Count == 0 || currentWorkflow is null)
            {
                return;
            }

            foreach (var option in CleanupAudioOptions)
            {
                option.IsSelected = false;
            }

            EnsurePrimaryCleanupAudioSelection();
            UpdateCleanupAudioSelectionMessage();
            await PersistCleanupAudioSelectionAsync();
        }

        [RelayCommand]
        private async Task ToggleCleanupSubtitleSelectionAsync(TrackCleanupSubtitleOption? option)
        {
            if (option is null || currentWorkflow is null)
            {
                return;
            }

            option.IsSelected = !option.IsSelected;
            EnsurePrimaryCleanupSubtitleSelection();
            UpdateCleanupSubtitleSelectionMessage();
            await PersistCleanupAudioSelectionAsync();
        }

        [RelayCommand]
        private async Task SelectAllCleanupSubtitlesAsync()
        {
            if (CleanupSubtitleOptions.Count == 0 || currentWorkflow is null)
            {
                return;
            }

            foreach (var option in CleanupSubtitleOptions)
            {
                option.IsSelected = true;
            }

            EnsurePrimaryCleanupSubtitleSelection();
            UpdateCleanupSubtitleSelectionMessage();
            await PersistCleanupAudioSelectionAsync();
        }

        [RelayCommand]
        private async Task ClearCleanupSubtitleSelectionAsync()
        {
            if (CleanupSubtitleOptions.Count == 0 || currentWorkflow is null)
            {
                return;
            }

            foreach (var option in CleanupSubtitleOptions)
            {
                option.IsSelected = false;
            }

            EnsurePrimaryCleanupSubtitleSelection();
            UpdateCleanupSubtitleSelectionMessage();
            await PersistCleanupAudioSelectionAsync();
        }

        public async Task SetCleanupAudioPrimaryAsync(TrackCleanupAudioOption? option)
        {
            if (option is null || currentWorkflow is null)
            {
                return;
            }

            foreach (var candidate in CleanupAudioOptions)
            {
                candidate.IsPrimary = ReferenceEquals(candidate, option);
                if (ReferenceEquals(candidate, option))
                {
                    candidate.IsSelected = true;
                }
            }

            UpdateCleanupAudioSelectionMessage();
            await PersistCleanupAudioSelectionAsync();
        }

        public async Task SetCleanupSubtitlePrimaryAsync(TrackCleanupSubtitleOption? option)
        {
            if (option is null || currentWorkflow is null)
            {
                return;
            }

            foreach (var candidate in CleanupSubtitleOptions)
            {
                candidate.IsPrimary = ReferenceEquals(candidate, option);
                if (ReferenceEquals(candidate, option))
                {
                    candidate.IsSelected = true;
                }
            }

            UpdateCleanupSubtitleSelectionMessage();
            await PersistCleanupAudioSelectionAsync();
        }

        [RelayCommand]
        private async Task MarkCleanupAudioPrimaryAsync(TrackCleanupAudioOption? option)
        {
            await SetCleanupAudioPrimaryAsync(option);
        }

        [RelayCommand]
        private async Task MarkCleanupSubtitlePrimaryAsync(TrackCleanupSubtitleOption? option)
        {
            await SetCleanupSubtitlePrimaryAsync(option);
        }

        [RelayCommand]
        private async Task SaveCleanupMetadataAsync()
        {
            if (currentWorkflow is null)
            {
                ShowStatus(InfoBarSeverity.Warning, "No hay workflow activo para guardar metadata de tracks.");
                return;
            }

            UpdateCleanupAudioSelectionMessage();
            UpdateCleanupSubtitleSelectionMessage();
            await PersistCleanupAudioSelectionAsync();
            ShowStatus(InfoBarSeverity.Success, "Metadata manual de tracks guardada para Limpiar tracks y pasos siguientes.");
        }

        [RelayCommand]
        private async Task ToggleRarSkipImagesAsync()
        {
            RarSkipImagesEnabled = !RarSkipImagesEnabled;
            OnPropertyChanged(nameof(RarSkipImagesButtonLabel));
            OnPropertyChanged(nameof(RarCaptureCountButtonLabel));
            await PersistQuickSettingsAsync();
        }

        [RelayCommand]
        private async Task ToggleTagAndRenameAttachCoverAsync()
        {
            TagAndRenameAttachCoverEnabled = !TagAndRenameAttachCoverEnabled;
            OnPropertyChanged(nameof(TagAndRenameAttachCoverButtonLabel));
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
        private async Task ClearRarCaptureCountAsync()
        {
            RarCaptureCountQuick = string.Empty;
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

        [RelayCommand]
        private void CopyPackageRarSeriesTitle()
        {
            CopyPackageRarHint(
                WorkflowExecutionService.PackageRarSeriesNameHintKey,
                "No hay nombre corto disponible para copiar.",
                "Se copió el nombre corto.");
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

        private async Task<bool> EnsureStepPreconditionsAsync(WorkflowStepKey stepKey)
        {
            if (stepKey != WorkflowStepKey.CleanTracks)
            {
                return true;
            }

            var inspection = await RefreshCleanupAudioSelectionAsync(forceReload: false);
            if (inspection?.CanManuallySelectAudio == true && CleanupAudioOptions.All(option => !option.IsSelected))
            {
                ShowStatus(InfoBarSeverity.Warning, "Selecciona al menos un audio para conservar antes de ejecutar Limpiar tracks.");
                return false;
            }

            return true;
        }

        private async Task EnsureCleanupAudioSelectionForCurrentStepAsync(WorkflowStepState? step)
        {
            if (step?.StepKey != WorkflowStepKey.CleanTracks)
            {
                return;
            }

            await RefreshCleanupAudioSelectionAsync(forceReload: false);
        }

        private async Task<TrackCleanupAudioInspection?> RefreshCleanupAudioSelectionAsync(bool forceReload)
        {
            if (currentWorkflow is null)
            {
                CleanupAudioOptions.Clear();
                CleanupSubtitleOptions.Clear();
                ClearCleanupSpecialCases();
                cleanupAudioSelectionContextMessage = "Selecciona Limpiar tracks para revisar audios y subtítulos antes de filtrar.";
                UpdateCleanupAudioSelectionMessage();
                UpdateCleanupSubtitleSelectionMessage();
                OnPropertyChanged(nameof(HasCleanupAudioOptions));
                OnPropertyChanged(nameof(HasCleanupSubtitleOptions));
                return null;
            }

            var requestVersion = Interlocked.Increment(ref cleanupAudioInspectionVersion);
            CleanupAudioSelectionBusy = true;
            OnPropertyChanged(nameof(CleanupAudioRefreshButtonLabel));

            try
            {
                var inspection = await workflowExecutionService.GetTrackCleanupAudioInspectionAsync(currentWorkflow, CancellationToken.None);
                if (requestVersion != cleanupAudioInspectionVersion || currentWorkflow is null)
                {
                    return inspection;
                }

                cleanupAudioSelectionContextMessage = inspection.Message;
                var changed = ApplyCleanupAudioInspectionToWorkflow(currentWorkflow, inspection);
                SyncCleanupAudioOptionsFromWorkflow();
                SyncCleanupSubtitleOptionsFromWorkflow();
                SyncCleanupSpecialCases(inspection);
                UpdateCleanupAudioSelectionMessage();
                UpdateCleanupSubtitleSelectionMessage();

                if (changed)
                {
                    await workflowStore.SaveAsync(currentWorkflow);
                }

                return inspection;
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"RefreshCleanupAudioSelectionAsync failed: {ex}");
                cleanupAudioSelectionContextMessage = $"No se pudieron cargar los tracks: {ex.Message}";
                CleanupAudioOptions.Clear();
                CleanupSubtitleOptions.Clear();
                ClearCleanupSpecialCases();
                OnPropertyChanged(nameof(HasCleanupAudioOptions));
                OnPropertyChanged(nameof(HasCleanupSubtitleOptions));
                UpdateCleanupAudioSelectionMessage();
                UpdateCleanupSubtitleSelectionMessage();
                return null;
            }
            finally
            {
                if (requestVersion == cleanupAudioInspectionVersion)
                {
                    CleanupAudioSelectionBusy = false;
                    OnPropertyChanged(nameof(CleanupAudioRefreshButtonLabel));
                    UpdateCleanupAudioSelectionMessage();
                    UpdateCleanupSubtitleSelectionMessage();
                }
            }
        }

        private bool ApplyCleanupAudioInspectionToWorkflow(WorkflowInstance workflow, TrackCleanupAudioInspection inspection)
        {
            var targetVideoPath = inspection.TargetVideoPath ?? string.Empty;
            var updatedAudioOptions = inspection.CanManuallySelectAudio
                ? inspection.AudioOptions.ToList()
                : new List<TrackCleanupAudioOption>();
            var updatedSubtitleOptions = inspection.CanManuallySelectAudio
                ? inspection.SubtitleOptions.ToList()
                : new List<TrackCleanupSubtitleOption>();

            var changed = !string.Equals(
                workflow.TrackCleanupSelectionVideoPath,
                targetVideoPath,
                StringComparison.OrdinalIgnoreCase)
                || !AreCleanupAudioOptionsEquivalent(workflow.TrackCleanupAudioOptions, updatedAudioOptions)
                || !AreCleanupSubtitleOptionsEquivalent(workflow.TrackCleanupSubtitleOptions, updatedSubtitleOptions);

            workflow.TrackCleanupSelectionVideoPath = targetVideoPath;
            workflow.TrackCleanupAudioOptions = updatedAudioOptions;
            workflow.TrackCleanupSubtitleOptions = updatedSubtitleOptions;
            EnsurePrimaryCleanupSelections(workflow);
            return changed;
        }

        private void SyncCleanupAudioOptionsFromWorkflow()
        {
            CleanupAudioOptions.Clear();
            if (currentWorkflow is not null)
            {
                foreach (var option in currentWorkflow.TrackCleanupAudioOptions)
                {
                    CleanupAudioOptions.Add(option);
                }
            }

            OnPropertyChanged(nameof(HasCleanupAudioOptions));
        }

        private void SyncCleanupSubtitleOptionsFromWorkflow()
        {
            CleanupSubtitleOptions.Clear();
            if (currentWorkflow is not null)
            {
                foreach (var option in currentWorkflow.TrackCleanupSubtitleOptions)
                {
                    CleanupSubtitleOptions.Add(option);
                }
            }

            OnPropertyChanged(nameof(HasCleanupSubtitleOptions));
        }

        private void SyncCleanupSpecialCases(TrackCleanupAudioInspection inspection)
        {
            CleanupSpecialCasesMessage = inspection.SpecialCasesMessage ?? string.Empty;
            CleanupSpecialCases.Clear();
            foreach (var item in inspection.SpecialCases)
            {
                CleanupSpecialCases.Add(item);
            }

            OnPropertyChanged(nameof(HasCleanupSpecialCases));
            OnPropertyChanged(nameof(CleanupSpecialCasesPanelTitle));
            OnPropertyChanged(nameof(CleanupSpecialCasesPanelMessage));
            OnPropertyChanged(nameof(CleanupSpecialCasesListText));
        }

        private void ClearCleanupSpecialCases()
        {
            CleanupSpecialCasesMessage = string.Empty;
            CleanupSpecialCases.Clear();
            OnPropertyChanged(nameof(HasCleanupSpecialCases));
            OnPropertyChanged(nameof(CleanupSpecialCasesPanelTitle));
            OnPropertyChanged(nameof(CleanupSpecialCasesPanelMessage));
            OnPropertyChanged(nameof(CleanupSpecialCasesListText));
        }

        private void UpdateCleanupAudioSelectionMessage()
        {
            if (CleanupAudioSelectionBusy)
            {
                CleanupAudioSelectionMessage = "Inspeccionando audios del archivo objetivo...";
                return;
            }

            if (CleanupAudioOptions.Count > 0)
            {
                var selectedCount = CleanupAudioOptions.Count(option => option.IsSelected);
                CleanupAudioSelectionMessage = $"Se conservarán {selectedCount} de {CleanupAudioOptions.Count} audios. Marca Conservar para incluir una pista; usa Principal para fijar la pista principal o Editar para ajustar metadata.";
                return;
            }

            CleanupAudioSelectionMessage = cleanupAudioSelectionContextMessage;
        }

        private void UpdateCleanupSubtitleSelectionMessage()
        {
            if (CleanupAudioSelectionBusy)
            {
                CleanupSubtitleSelectionMessage = "Inspeccionando subtítulos del archivo objetivo...";
                return;
            }

            if (CleanupSubtitleOptions.Count > 0)
            {
                var selectedCount = CleanupSubtitleOptions.Count(option => option.IsSelected);
                CleanupSubtitleSelectionMessage = $"Se conservarán {selectedCount} de {CleanupSubtitleOptions.Count} subtítulos. Marca Conservar para incluir una pista; usa Principal para fijar la pista principal o Editar para ajustar metadata.";
                return;
            }

            if (currentWorkflow is not null
                && !string.IsNullOrWhiteSpace(currentWorkflow.TrackCleanupSelectionVideoPath)
                && CleanupAudioOptions.Count > 0)
            {
                CleanupSubtitleSelectionMessage = "No se detectaron subtítulos en el video objetivo.";
                return;
            }

            CleanupSubtitleSelectionMessage = cleanupAudioSelectionContextMessage;
        }

        private static bool AreCleanupAudioOptionsEquivalent(
            IReadOnlyList<TrackCleanupAudioOption> current,
            IReadOnlyList<TrackCleanupAudioOption> updated)
        {
            if (current.Count != updated.Count)
            {
                return false;
            }

            for (var index = 0; index < current.Count; index++)
            {
                var left = current[index];
                var right = updated[index];
                if (!string.Equals(left.TrackId, right.TrackId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(left.LanguageCode, right.LanguageCode, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(left.LanguageLabel, right.LanguageLabel, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(left.Codec, right.Codec, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                    || left.IsDefault != right.IsDefault
                    || left.IsPrimary != right.IsPrimary
                    || left.IsSelected != right.IsSelected)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreCleanupSubtitleOptionsEquivalent(
            IReadOnlyList<TrackCleanupSubtitleOption> current,
            IReadOnlyList<TrackCleanupSubtitleOption> updated)
        {
            if (current.Count != updated.Count)
            {
                return false;
            }

            for (var index = 0; index < current.Count; index++)
            {
                var left = current[index];
                var right = updated[index];
                if (!string.Equals(left.TrackId, right.TrackId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(left.LanguageCode, right.LanguageCode, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(left.LanguageLabel, right.LanguageLabel, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                    || left.IsDefault != right.IsDefault
                    || left.IsForced != right.IsForced
                    || left.IsPrimary != right.IsPrimary
                    || left.IsSelected != right.IsSelected)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task PersistCleanupAudioSelectionAsync()
        {
            if (currentWorkflow is null)
            {
                return;
            }

            try
            {
                await workflowStore.SaveAsync(currentWorkflow);
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.Write($"PersistCleanupAudioSelectionAsync failed: {ex}");
                ShowStatus(InfoBarSeverity.Error, $"No se pudo guardar la selección de tracks: {ex.Message}");
            }
        }

        private static bool TryResolveWorkflowReloadSource(WorkflowInstance workflow, out string sourcePath, out bool isFile)
        {
            var sourceSelectionIsFile = workflow.SourceSelectionIsFile == true;
            var candidatePath = sourceSelectionIsFile
                ? FirstExistingPath(workflow.SourcePrimaryVideoPath, workflow.PrimaryVideoPath, workflow.SourceRootPath, workflow.RootPath)
                : FirstExistingPath(workflow.SourceRootPath, workflow.RootPath, Path.GetDirectoryName(workflow.SourcePrimaryVideoPath), Path.GetDirectoryName(workflow.PrimaryVideoPath));

            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                candidatePath = !string.IsNullOrWhiteSpace(workflow.SourcePrimaryVideoPath)
                    ? workflow.SourcePrimaryVideoPath
                    : !string.IsNullOrWhiteSpace(workflow.SourceRootPath)
                        ? workflow.SourceRootPath
                        : !string.IsNullOrWhiteSpace(workflow.PrimaryVideoPath)
                            ? workflow.PrimaryVideoPath
                            : workflow.RootPath;
            }

            sourcePath = candidatePath ?? string.Empty;
            isFile = sourceSelectionIsFile;
            return !string.IsNullOrWhiteSpace(sourcePath);
        }

        private static string FirstExistingPath(params string?[] paths)
        {
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private void SyncQuickOptionsFromSettings()
        {
            syncingQuickSettings = true;
            try
            {
                DownloadDryRunEnabled = quickSettings.DownloaderDryRun;
                DownloadForceLatestEnabled = quickSettings.DownloaderForceLatest;
                TranslateFastModeEnabled = quickSettings.SubtitleFastMode;
                TranslateSkipSummaryEnabled = quickSettings.SubtitleSkipSummary;
                CleanupCloseQbittorrentEnabled = quickSettings.TrackCleanupCloseQbittorrent;
                CleanupDeleteOriginalsEnabled = quickSettings.TrackCleanupDeleteOriginals;
                TagAndRenameAttachCoverEnabled = quickSettings.TagAndRenameAttachCover;
                RarSkipImagesEnabled = quickSettings.RarSkipImages;
                RarNoCompressEnabled = quickSettings.RarNoCompress;
                RarUseCompressionNormalEnabled = quickSettings.RarUseCompressionNormal;
                RarVerboseEnabled = quickSettings.RarVerbose;
                RarImageFormatQuick = string.Equals(quickSettings.RarImageFormat, "png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
                RarCaptureCountQuick = quickSettings.RarCaptureCount;
                NotifyQuickOptionLabelsChanged();
            }
            finally
            {
                syncingQuickSettings = false;
            }
        }

        private void ApplyQuickOptionsToSettings()
        {
            quickSettings.DownloaderDryRun = DownloadDryRunEnabled;
            quickSettings.DownloaderForceLatest = DownloadForceLatestEnabled;
            quickSettings.SubtitleFastMode = TranslateFastModeEnabled;
            quickSettings.SubtitleSkipSummary = TranslateSkipSummaryEnabled;
            quickSettings.TrackCleanupCloseQbittorrent = CleanupCloseQbittorrentEnabled;
            quickSettings.TrackCleanupDeleteOriginals = CleanupDeleteOriginalsEnabled;
            quickSettings.TagAndRenameAttachCover = TagAndRenameAttachCoverEnabled;
            quickSettings.RarSkipImages = RarSkipImagesEnabled;
            quickSettings.RarNoCompress = RarNoCompressEnabled;
            quickSettings.RarUseCompressionNormal = RarUseCompressionNormalEnabled;
            quickSettings.RarVerbose = RarVerboseEnabled;
            quickSettings.RarImageFormat = string.Equals(RarImageFormatQuick, "png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
            if (TryNormalizeRarCaptureCount(RarCaptureCountQuick, out var normalizedCaptureCount))
            {
                quickSettings.RarCaptureCount = normalizedCaptureCount;
            }
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
            OnPropertyChanged(nameof(TagAndRenameAttachCoverButtonLabel));
            OnPropertyChanged(nameof(RarSkipImagesButtonLabel));
            OnPropertyChanged(nameof(RarNoCompressButtonLabel));
            OnPropertyChanged(nameof(RarCompressionModeButtonLabel));
            OnPropertyChanged(nameof(RarVerboseButtonLabel));
            OnPropertyChanged(nameof(RarImageFormatButtonLabel));
            OnPropertyChanged(nameof(RarCaptureCountButtonLabel));
        }

        private string BuildRarCaptureCountButtonLabel()
        {
            if (RarSkipImagesEnabled)
            {
                return "Capturas: omitidas";
            }

            if (!TryNormalizeRarCaptureCount(RarCaptureCountQuick, out var normalizedCaptureCount))
            {
                return "Capturas: valor inválido";
            }

            var singleVideoContext = IsPackageRarSingleVideoContext();
            if (string.IsNullOrWhiteSpace(normalizedCaptureCount))
            {
                return currentWorkflow is null
                    ? "Capturas: auto 300/100"
                    : singleVideoContext
                        ? "Capturas: auto 300 principal"
                        : "Capturas: auto 300/100";
            }

            return singleVideoContext
                ? $"Máx imágenes: {normalizedCaptureCount} principal"
                : $"Máx imágenes: {normalizedCaptureCount} c/u";
        }

        private bool IsPackageRarSingleVideoContext()
        {
            if (currentWorkflow is null)
            {
                return false;
            }

            if (currentWorkflow.SourceSelectionIsFile == true)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(currentWorkflow.RootPath)
                && File.Exists(currentWorkflow.RootPath)
                && IsVideoFile(currentWorkflow.RootPath))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(currentWorkflow.RootPath)
                && Directory.Exists(currentWorkflow.RootPath))
            {
                return CountRarCandidateVideos(currentWorkflow.RootPath, 2) == 1;
            }

            return !string.IsNullOrWhiteSpace(currentWorkflow.PrimaryVideoPath)
                && File.Exists(currentWorkflow.PrimaryVideoPath)
                && IsVideoFile(currentWorkflow.PrimaryVideoPath);
        }

        private static int CountRarCandidateVideos(string rootPath, int stopAt)
        {
            try
            {
                var count = 0;
                foreach (var path in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
                {
                    if (IsVideoFile(path) && !IsIgnoredRarPackagingPath(path))
                    {
                        count++;
                        if (count >= stopAt)
                        {
                            return count;
                        }
                    }
                }

                return count;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }

        private static bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path));

        private static bool IsIgnoredRarPackagingPath(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            var parts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Any(part =>
                part.Equals("capturas", StringComparison.OrdinalIgnoreCase)
                || part.Equals("RARs", StringComparison.OrdinalIgnoreCase)
                || part.Equals("rar-input", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryNormalizeRarCaptureCount(string value, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (int.TryParse(value.Trim(), out var captureCount) && captureCount > 0)
            {
                normalized = captureCount.ToString();
                return true;
            }

            return false;
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
            ClearLiveOutputBuffer();
            ResetDetailProgress(BuildProgressTitle(outputStepKey), "Iniciando proceso...");
            ShowDetailProgress = true;
            DetailOutput = "Esperando salida del proceso...";

            try
            {
                var record = await operation();
                if (record is not null)
                {
                    CompleteDetailProgress(record.Success, record.Summary);
                }

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
                CompleteDetailProgress(false, "Ejecución cancelada.");
                StatusSeverity = InfoBarSeverity.Warning;
                StatusMessage = "La ejecución fue cancelada.";
                IsStatusInfoOpen = true;
            }
            catch (Exception ex)
            {
                CompleteDetailProgress(false, ex.Message);
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
            RefreshVisibleStepItems();

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
            cleanupAudioSelectionContextMessage = "Selecciona Limpiar tracks para revisar audios y subtítulos antes de filtrar.";
            EnsurePrimaryCleanupSelections(workflow);
            SyncCleanupAudioOptionsFromWorkflow();
            SyncCleanupSubtitleOptionsFromWorkflow();
            ClearCleanupSpecialCases();
            UpdateCleanupAudioSelectionMessage();
            UpdateCleanupSubtitleSelectionMessage();
            UpdateTranslationDecisionVisibility(workflow);
            UpdateQuickOptionsVisibility();
            UpdatePackageRarDetailActions();
            RefreshSelectedStepOutput();
            OnPropertyChanged(nameof(CanOpenSelectedLog));
            OnPropertyChanged(nameof(RarCaptureCountButtonLabel));
        }

        private void ClearLiveOutputBuffer()
        {
            liveOutputDisplayLines.Clear();
            liveOutputReplaceableIndexes.Clear();
            LiveOutput = string.Empty;
        }

        private void AppendLineToLiveOutput(string line)
        {
            AppendLineToOutputBuffer(liveOutputDisplayLines, liveOutputReplaceableIndexes, line);
            LiveOutput = string.Join(Environment.NewLine, liveOutputDisplayLines);
        }

        private static string FormatOutputForDisplay(string rawOutput)
        {
            if (string.IsNullOrWhiteSpace(rawOutput))
            {
                return string.Empty;
            }

            var lines = new List<string>();
            var replaceableIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in rawOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                AppendLineToOutputBuffer(lines, replaceableIndexes, line);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static void AppendLineToOutputBuffer(List<string> lines, Dictionary<string, int> replaceableIndexes, string rawLine)
        {
            var line = rawLine.Replace("\r", string.Empty).TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var slotKey = GetReplaceableOutputSlot(line);
            if (slotKey is not null)
            {
                if (replaceableIndexes.TryGetValue(slotKey, out var existingIndex))
                {
                    lines[existingIndex] = line;
                    return;
                }

                replaceableIndexes[slotKey] = lines.Count;
            }

            lines.Add(line);
        }

        private static string? GetReplaceableOutputSlot(string line)
        {
            var muxMatch = CompactMuxProgressRegex.Match(line);
            if (muxMatch.Success)
            {
                return $"mux:{muxMatch.Groups["current"].Value}";
            }

            return CompactGlobalProgressRegex.IsMatch(line) ? "global" : null;
        }

        private void AppendOutput(string line)
        {
            if (IsStructuredOutputMetadata(line) && !IsStructuredProgressMetadata(line))
            {
                return;
            }

            void UpdateOutput()
            {
                UpdateDetailProgressFromOutput(line);
                if (IsStructuredProgressMetadata(line))
                {
                    return;
                }

                AppendLineToLiveOutput(line);
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
                ShowDetailProgress = false;
                DetailOutput = "Selecciona un paso para ver detalle y salida.";
                return;
            }

            if (activeOutputStepKey is not null
                && SelectedStep.StepKey == activeOutputStepKey
                && !string.IsNullOrWhiteSpace(LiveOutput))
            {
                ShowDetailProgress = true;
                DetailOutput = LiveOutput;
                return;
            }

            if (utilityOutputActive && !string.IsNullOrWhiteSpace(LiveOutput))
            {
                ShowDetailProgress = true;
                DetailOutput = LiveOutput;
                return;
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(SelectedStep.StdoutLogPath) && File.Exists(SelectedStep.StdoutLogPath))
            {
                var stdout = File.ReadAllText(SelectedStep.StdoutLogPath).Trim();
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    parts.Add(FormatOutputForDisplay(stdout));
                }
            }

            if (!string.IsNullOrWhiteSpace(SelectedStep.StderrLogPath) && File.Exists(SelectedStep.StderrLogPath))
            {
                var stderr = File.ReadAllText(SelectedStep.StderrLogPath).Trim();
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    var formattedStderr = FormatOutputForDisplay(stderr);
                    parts.Add(parts.Count == 0 ? formattedStderr : $"--- STDERR ---{Environment.NewLine}{formattedStderr}");
                }
            }

            ApplyProgressForSelectedStep(SelectedStep, parts);
            DetailOutput = parts.Count > 0
                ? string.Join($"{Environment.NewLine}{Environment.NewLine}", parts)
                : "No hay salida disponible para este paso todavía.";
            OnPropertyChanged(nameof(CanOpenSelectedLog));
        }

        private void BeginUtilityOutput(string title, string description, string statusMessage)
        {
            ResetWorkflowState(statusMessage);
            utilityOutputActive = true;
            SelectedStepTitle = title;
            SelectedStepDescription = description;
            ResetDetailProgress(title, "Iniciando utilidad...");
            ShowDetailProgress = true;
            DetailOutput = "Esperando salida del script...";
        }

        private void ResetWorkflowState(string message)
        {
            currentWorkflow = null;
            activeOutputStepKey = null;
            ClearLiveOutputBuffer();
            ResetDetailProgress("Progreso del paso", "Esperando señales del proceso...");
            ShowDetailProgress = false;
            HasExplicitStepSelection = false;
            DetailOutputHeightWasResized = false;
            DetailOutputHeight = 420;
            StepItems.Clear();
            foreach (var step in CreateNeutralStepTemplate())
            {
                StepItems.Add(step);
            }
            RefreshVisibleStepItems();
            SelectedStep = StepItems.FirstOrDefault();
            DisplayName = "Selecciona un archivo o carpeta para comenzar.";
            RootPath = "Sin workflow activo";
            NextStepLabel = "Selecciona archivo o carpeta";
            GlobalStatus = "Esperando selección";
            StatusSeverity = InfoBarSeverity.Informational;
            StatusMessage = message;
            IsStatusInfoOpen = true;
            cleanupAudioSelectionContextMessage = "Selecciona Limpiar tracks para revisar audios y subtítulos antes de filtrar.";
            CleanupAudioSelectionBusy = false;
            CleanupAudioOptions.Clear();
            CleanupSubtitleOptions.Clear();
            ClearCleanupSpecialCases();
            OnPropertyChanged(nameof(HasCleanupAudioOptions));
            OnPropertyChanged(nameof(HasCleanupSubtitleOptions));
            OnPropertyChanged(nameof(CleanupAudioRefreshButtonLabel));
            UpdateCleanupAudioSelectionMessage();
            UpdateCleanupSubtitleSelectionMessage();
            ShowTranslationDecisionActions = false;
            UpdateQuickOptionsVisibility();
            UpdatePackageRarDetailActions();
            OnPropertyChanged(nameof(CanOpenSelectedLog));
        }

        private void EnsurePrimaryCleanupSelections(WorkflowInstance workflow)
        {
            EnsurePrimaryCleanupAudioSelection(workflow.TrackCleanupAudioOptions);
            EnsurePrimaryCleanupSubtitleSelection(workflow.TrackCleanupSubtitleOptions);
        }

        private void EnsurePrimaryCleanupAudioSelection()
        {
            EnsurePrimaryCleanupAudioSelection(CleanupAudioOptions);
        }

        private static void EnsurePrimaryCleanupAudioSelection(IEnumerable<TrackCleanupAudioOption> options)
        {
            var selected = options.Where(option => option.IsSelected).ToList();
            if (selected.Count == 0)
            {
                foreach (var option in options)
                {
                    option.IsPrimary = false;
                }

                return;
            }

            var primary = selected.FirstOrDefault(option => option.IsPrimary)
                ?? selected.FirstOrDefault(option => option.IsDefault)
                ?? selected.First();

            foreach (var option in options)
            {
                option.IsPrimary = ReferenceEquals(option, primary);
            }
        }

        private void EnsurePrimaryCleanupSubtitleSelection()
        {
            EnsurePrimaryCleanupSubtitleSelection(CleanupSubtitleOptions);
        }

        private static void EnsurePrimaryCleanupSubtitleSelection(IEnumerable<TrackCleanupSubtitleOption> options)
        {
            var selected = options.Where(option => option.IsSelected).ToList();
            if (selected.Count == 0)
            {
                foreach (var option in options)
                {
                    option.IsPrimary = false;
                }

                return;
            }

            var primary = selected.FirstOrDefault(option => option.IsPrimary)
                ?? selected.FirstOrDefault(option => option.IsDefault)
                ?? selected.First();

            foreach (var option in options)
            {
                option.IsPrimary = ReferenceEquals(option, primary);
            }
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
            ShowTagAndRenameQuickOptions = SelectedStep?.StepKey == WorkflowStepKey.TagAndRename;
            ShowPackageRarQuickOptions = SelectedStep?.StepKey == WorkflowStepKey.PackageRar;
            ShowQuickActionOptions = SelectedStep is not null
                && (ShowDownloadQuickOptions || ShowTranslateQuickOptions || ShowCleanTracksQuickOptions || ShowTagAndRenameQuickOptions || ShowPackageRarQuickOptions);
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
                WorkflowStepKey.CleanTracks => "Controla qué hace SubForge cuando encuentra el archivo en uso y marca exactamente qué audios y subtítulos deben sobrevivir al filtrado.",
                WorkflowStepKey.TagAndRename => "Controla si el MKV recibe un poster embebido; la búsqueda automática usa IMDb y funciona con películas o series.",
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
                case WorkflowExecutionService.PackageRarSeriesNameHintKey:
                    PackageRarSeriesTitleCopied = true;
                    break;
            }

            NotifyPackageRarCopyButtonVisuals();
        }

        private void ResetPackageRarCopyState()
        {
            PackageRarRawDataCopied = false;
            PackageRarWeightSummaryCopied = false;
            PackageRarCleanNameCopied = false;
            PackageRarSeriesTitleCopied = false;
            NotifyPackageRarCopyButtonVisuals();
        }

        private void NotifyPackageRarCopyButtonVisuals()
        {
            OnPropertyChanged(nameof(PackageRarRawDataButtonBackground));
            OnPropertyChanged(nameof(PackageRarWeightSummaryButtonBackground));
            OnPropertyChanged(nameof(PackageRarCleanNameButtonBackground));
            OnPropertyChanged(nameof(PackageRarSeriesTitleButtonBackground));
            OnPropertyChanged(nameof(PackageRarRawDataButtonBorderBrush));
            OnPropertyChanged(nameof(PackageRarWeightSummaryButtonBorderBrush));
            OnPropertyChanged(nameof(PackageRarCleanNameButtonBorderBrush));
            OnPropertyChanged(nameof(PackageRarSeriesTitleButtonBorderBrush));
            OnPropertyChanged(nameof(PackageRarRawDataButtonForeground));
            OnPropertyChanged(nameof(PackageRarWeightSummaryButtonForeground));
            OnPropertyChanged(nameof(PackageRarCleanNameButtonForeground));
            OnPropertyChanged(nameof(PackageRarSeriesTitleButtonForeground));
        }

        private static bool IsStructuredOutputMetadata(string line)
        {
            return line.StartsWith("MWO_RAW_DATA\t", StringComparison.Ordinal)
                || line.StartsWith("MWO_WEIGHT_SUMMARY\t", StringComparison.Ordinal)
                || IsStructuredProgressMetadata(line);
        }

        private static bool IsStructuredProgressMetadata(string line) =>
            line.StartsWith("MWO_PROGRESS\t", StringComparison.Ordinal);

        private void ResetDetailProgress(string title, string message)
        {
            currentProgressSnapshot = null;
            DetailProgressTitle = title;
            DetailProgressMessage = message;
            DetailProgressPercentLabel = "En curso";
            DetailProgressValue = 0;
            DetailProgressIsIndeterminate = true;
        }

        private void UpdateDetailProgressFromOutput(string line)
        {
            if (!TryParseProgressSnapshot(line, activeOutputStepKey, out var snapshot))
            {
                if (ShowDetailProgress && DetailProgressIsIndeterminate)
                {
                    DetailProgressMessage = ShortenProgressMessage(line);
                }

                return;
            }

            currentProgressSnapshot = snapshot;
            ShowDetailProgress = true;
            DetailProgressIsIndeterminate = snapshot.Percent is null;
            DetailProgressValue = snapshot.Percent ?? 0;
            DetailProgressPercentLabel = snapshot.Percent is null
                ? "En curso"
                : $"{Math.Round(snapshot.Percent.Value)}%";
            DetailProgressMessage = snapshot.Message;
            if (!string.IsNullOrWhiteSpace(snapshot.Title))
            {
                DetailProgressTitle = snapshot.Title;
            }
        }

        private void CompleteDetailProgress(bool success, string message)
        {
            if (!ShowDetailProgress)
            {
                return;
            }

            DetailProgressIsIndeterminate = false;
            DetailProgressValue = success ? 100 : Math.Max(DetailProgressValue, currentProgressSnapshot?.Percent ?? 0);
            DetailProgressPercentLabel = success ? "100%" : "Detenido";
            DetailProgressMessage = string.IsNullOrWhiteSpace(message)
                ? success ? "Proceso completado." : "El proceso no terminó correctamente."
                : message;
        }

        private void ApplyProgressForSelectedStep(WorkflowStepState step, IReadOnlyList<string> outputParts)
        {
            var title = BuildProgressTitle(step.StepKey);
            var combinedOutput = string.Join(Environment.NewLine, outputParts);
            var snapshot = ParseLastProgressSnapshot(combinedOutput, step.StepKey);

            if (step.Status == WorkflowStepStatus.Running)
            {
                DetailProgressTitle = title;
                ShowDetailProgress = true;
                if (snapshot is null)
                {
                    DetailProgressIsIndeterminate = true;
                    DetailProgressValue = 0;
                    DetailProgressPercentLabel = "En curso";
                    DetailProgressMessage = "Proceso en ejecución...";
                    return;
                }

                ApplyProgressSnapshot(snapshot.Value);
                return;
            }

            if (step.Status == WorkflowStepStatus.Succeeded)
            {
                DetailProgressTitle = title;
                ShowDetailProgress = true;
                DetailProgressIsIndeterminate = false;
                DetailProgressValue = 100;
                DetailProgressPercentLabel = "100%";
                DetailProgressMessage = step.StatusReason;
                return;
            }

            if (step.Status == WorkflowStepStatus.Failed && snapshot is not null)
            {
                ApplyProgressSnapshot(snapshot.Value);
                DetailProgressTitle = title;
                DetailProgressPercentLabel = "Detenido";
                return;
            }

            ShowDetailProgress = false;
        }

        private void ApplyProgressSnapshot(WorkflowProgressSnapshot snapshot)
        {
            currentProgressSnapshot = snapshot;
            DetailProgressTitle = string.IsNullOrWhiteSpace(snapshot.Title) ? DetailProgressTitle : snapshot.Title;
            DetailProgressMessage = snapshot.Message;
            DetailProgressIsIndeterminate = snapshot.Percent is null;
            DetailProgressValue = snapshot.Percent ?? 0;
            DetailProgressPercentLabel = snapshot.Percent is null
                ? "En curso"
                : $"{Math.Round(snapshot.Percent.Value)}%";
            ShowDetailProgress = true;
        }

        private WorkflowProgressSnapshot? ParseLastProgressSnapshot(string output, WorkflowStepKey? stepKey)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            WorkflowProgressSnapshot? snapshot = null;
            foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (TryParseProgressSnapshot(line, stepKey, out var parsed))
                {
                    snapshot = parsed;
                }
            }

            return snapshot;
        }

        private static bool TryParseProgressSnapshot(string rawLine, WorkflowStepKey? stepKey, out WorkflowProgressSnapshot snapshot)
        {
            snapshot = default;
            var line = rawLine.Replace("\r", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            var structuredMatch = StructuredProgressRegex.Match(line);
            if (structuredMatch.Success && TryParsePercent(structuredMatch.Groups["percent"].Value, out var structuredPercent))
            {
                snapshot = new WorkflowProgressSnapshot(
                    BuildProgressTitle(stepKey),
                    structuredPercent,
                    ShortenProgressMessage(structuredMatch.Groups["label"].Value));
                return true;
            }

            var muxMatch = CompactMuxProgressRegex.Match(line);
            if (muxMatch.Success
                && int.TryParse(muxMatch.Groups["current"].Value, out var current)
                && int.TryParse(muxMatch.Groups["total"].Value, out var total)
                && TryParsePercent(muxMatch.Groups["percent"].Value, out var muxPercent)
                && total > 0)
            {
                var aggregate = Math.Clamp((((current - 1) + (muxPercent / 100)) / total) * 100, 0, 100);
                snapshot = new WorkflowProgressSnapshot(
                    BuildProgressTitle(stepKey),
                    aggregate,
                    $"Mux {current}/{total}: {muxMatch.Groups["name"].Value.Trim()}");
                return true;
            }

            var percentMatch = GenericPercentRegex.Match(line);
            if (percentMatch.Success && TryParsePercent(percentMatch.Groups["percent"].Value, out var percent))
            {
                snapshot = new WorkflowProgressSnapshot(BuildProgressTitle(stepKey), percent, ShortenProgressMessage(line));
                return true;
            }

            var fractionMatch = FractionProgressRegex.Match(line);
            if (fractionMatch.Success
                && int.TryParse(fractionMatch.Groups["current"].Value, out var fractionCurrent)
                && int.TryParse(fractionMatch.Groups["total"].Value, out var fractionTotal)
                && fractionTotal > 0
                && fractionCurrent <= fractionTotal)
            {
                snapshot = new WorkflowProgressSnapshot(
                    BuildProgressTitle(stepKey),
                    Math.Clamp((fractionCurrent / (double)fractionTotal) * 100, 0, 100),
                    ShortenProgressMessage(line));
                return true;
            }

            return false;
        }

        private static bool TryParsePercent(string value, out double percent)
        {
            return double.TryParse(
                value.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out percent)
                && percent >= 0
                && percent <= 100;
        }

        private static string BuildProgressTitle(WorkflowStepKey? stepKey)
        {
            return stepKey switch
            {
                WorkflowStepKey.Download => "Progreso de descarga",
                WorkflowStepKey.InspectSubs => "Progreso de inspección",
                WorkflowStepKey.TranslateSubs => "Progreso de traducción",
                WorkflowStepKey.CleanTracks => "Progreso de limpieza",
                WorkflowStepKey.TagAndRename => "Progreso de etiquetas",
                WorkflowStepKey.PackageRar => "Progreso de empaquetado",
                _ => "Progreso del proceso",
            };
        }

        private static string ShortenProgressMessage(string value)
        {
            var message = Regex.Replace(value.Trim(), @"\s+", " ");
            return message.Length <= 180 ? message : $"{message[..177]}...";
        }

        private readonly record struct WorkflowProgressSnapshot(string Title, double? Percent, string Message);

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

        private void RefreshVisibleStepItems()
        {
            VisibleStepItems.Clear();
            foreach (var step in StepItems.Where(step => step.StepKey != WorkflowStepKey.Download))
            {
                VisibleStepItems.Add(step);
            }
        }

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
                "--run-now",
                "--ephemeral",
            };
        }

        private static Microsoft.UI.Xaml.Media.SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b) =>
            new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
    }
}
