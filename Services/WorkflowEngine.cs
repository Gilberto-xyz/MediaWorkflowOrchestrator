namespace MediaWorkflowOrchestrator.Services
{
    public sealed class WorkflowEngine : IWorkflowEngine
    {
        public WorkflowInstance CreateWorkflow(string selectedPath, bool isFile)
        {
            var resolvedPath = Path.GetFullPath(selectedPath);
            var rootPath = isFile ? Path.GetDirectoryName(resolvedPath) ?? resolvedPath : resolvedPath;
            var primaryVideo = isFile ? resolvedPath : FindPrimaryVideoPath(resolvedPath);
            var displayName = isFile ? Path.GetFileNameWithoutExtension(resolvedPath) : Path.GetFileName(rootPath);

            var workflow = new WorkflowInstance
            {
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Workflow nuevo" : displayName,
                RootPath = rootPath,
                PrimaryVideoPath = primaryVideo,
                Steps = CreateDefaultSteps(),
            };

            RefreshStatuses(workflow);
            return workflow;
        }

        public WorkflowStepState? GetNextReadyStep(WorkflowInstance workflow) =>
            workflow.Steps.FirstOrDefault(step => step.Status == WorkflowStepStatus.Ready);

        public void ApplyInspectionResult(WorkflowInstance workflow, SubtitleInspectionResult inspectionResult)
        {
            var inspectStep = workflow.FindStep(WorkflowStepKey.InspectSubs);
            if (inspectStep is not null)
            {
                inspectStep.Status = WorkflowStepStatus.Succeeded;
                inspectStep.StatusReason = inspectionResult.Message;
                inspectStep.FinishedAt = DateTimeOffset.UtcNow;
            }

            switch (inspectionResult.Availability)
            {
                case SubtitleSpanishAvailability.Present:
                    ApplyTranslationDecision(workflow, translateRequired: false);
                    break;
                case SubtitleSpanishAvailability.Missing:
                    ApplyTranslationDecision(workflow, translateRequired: true);
                    break;
                default:
                    var translationStep = workflow.FindStep(WorkflowStepKey.TranslateSubs);
                    if (translationStep is not null)
                    {
                        translationStep.Status = WorkflowStepStatus.NeedsDecision;
                        translationStep.StatusReason = "La inspección no pudo determinar si hay subtítulos en español.";
                    }
                    break;
            }

            RefreshStatuses(workflow);
        }

        public void ApplyTranslationDecision(WorkflowInstance workflow, bool translateRequired)
        {
            var translationStep = workflow.FindStep(WorkflowStepKey.TranslateSubs);
            if (translationStep is null)
            {
                return;
            }

            if (translateRequired)
            {
                translationStep.UserDecision = "translate";
                translationStep.Status = WorkflowStepStatus.Pending;
                translationStep.StatusReason = "No se detectó español; el paso de traducción es obligatorio.";
                translationStep.StartedAt = null;
                translationStep.FinishedAt = null;
                translationStep.ExitCode = null;
                translationStep.StdoutLogPath = string.Empty;
                translationStep.StderrLogPath = string.Empty;
            }
            else
            {
                translationStep.UserDecision = "skip";
                translationStep.Status = WorkflowStepStatus.Skipped;
                translationStep.StatusReason = "Hay subtítulos en español; traducción omitida por recomendación.";
                translationStep.FinishedAt = DateTimeOffset.UtcNow;
            }

            RefreshStatuses(workflow);
        }

        public void RefreshStatuses(WorkflowInstance workflow)
        {
            var previousSatisfied = true;
            foreach (var step in workflow.Steps)
            {
                if (step.Status is WorkflowStepStatus.Succeeded or WorkflowStepStatus.Skipped)
                {
                    continue;
                }

                if (step.Status is WorkflowStepStatus.Running or WorkflowStepStatus.Failed or WorkflowStepStatus.NeedsDecision)
                {
                    previousSatisfied = false;
                    continue;
                }

                step.Status = previousSatisfied ? WorkflowStepStatus.Ready : WorkflowStepStatus.Blocked;
                step.StatusReason = step.Status switch
                {
                    WorkflowStepStatus.Ready => GetReadyReason(step.StepKey),
                    WorkflowStepStatus.Blocked => "Espera a que termine o se resuelva el paso anterior.",
                    _ => step.StatusReason,
                };

                previousSatisfied = false;
            }

            workflow.CurrentStep = GetNextReadyStep(workflow)?.StepKey ?? workflow.CurrentStep;
            workflow.UpdatedAt = DateTimeOffset.UtcNow;
        }

        private static string GetReadyReason(WorkflowStepKey stepKey) => stepKey switch
        {
            WorkflowStepKey.InspectSubs => "Listo para inspeccionar si el video ya contiene subtítulos en español.",
            WorkflowStepKey.TranslateSubs => "Listo para traducir subtítulos al español.",
            WorkflowStepKey.CleanTracks => "Listo para limpiar pistas y subtítulos extra.",
            WorkflowStepKey.TagAndRename => "Listo para aplicar etiquetas y renombrado final.",
            WorkflowStepKey.PackageRar => "Listo para generar el RAR con contraseña.",
            _ => "Paso listo para ejecutar."
        };

        private static List<WorkflowStepState> CreateDefaultSteps() => new()
        {
            new WorkflowStepState
            {
                StepKey = WorkflowStepKey.Download,
                DisplayName = "Descarga Nyaa",
                Status = WorkflowStepStatus.Skipped,
                StatusReason = "La descarga se maneja como utilidad global."
            },
            new WorkflowStepState
            {
                StepKey = WorkflowStepKey.InspectSubs,
                DisplayName = "Inspeccionar subtítulos",
                StatusReason = "Revisa si el archivo principal contiene subtítulos en español."
            },
            new WorkflowStepState
            {
                StepKey = WorkflowStepKey.TranslateSubs,
                DisplayName = "Traducir subtítulos",
                StatusReason = "Pendiente de inspección o decisión manual."
            },
            new WorkflowStepState
            {
                StepKey = WorkflowStepKey.CleanTracks,
                DisplayName = "Limpiar tracks",
                StatusReason = "Ejecuta SubForge para filtrar pistas."
            },
            new WorkflowStepState
            {
                StepKey = WorkflowStepKey.TagAndRename,
                DisplayName = "Etiquetas y renombre",
                StatusReason = "Aplica metadatos y renombrado compatible con tu flujo actual."
            },
            new WorkflowStepState
            {
                StepKey = WorkflowStepKey.PackageRar,
                DisplayName = "Empaquetar RAR",
                StatusReason = "Genera capturas e incluye la contraseña operativa."
            }
        };

        private static string FindPrimaryVideoPath(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return string.Empty;
            }

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".webm", ".avi" };
            return Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => extensions.Contains(Path.GetExtension(path)))
                ?? string.Empty;
        }
    }
}
