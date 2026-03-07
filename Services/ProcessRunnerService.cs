using System.Diagnostics;
using System.Text;

namespace MediaWorkflowOrchestrator.Services
{
    public sealed class ProcessRunnerService : IProcessRunnerService
    {
        private const string DefaultTerminalColumns = "180";
        private const string DefaultTerminalLines = "48";

        public async Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, Action<string>? onOutput, CancellationToken cancellationToken)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var startedAt = DateTimeOffset.UtcNow;
            var psi = new ProcessStartInfo
            {
                FileName = request.FileName,
                WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                    ? Environment.CurrentDirectory
                    : request.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.Environment["COLUMNS"] = DefaultTerminalColumns;
            psi.Environment["LINES"] = DefaultTerminalLines;
            psi.Environment["TERM"] = "xterm";

            foreach (var arg in request.Arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is null)
                {
                    return;
                }

                stdout.AppendLine(args.Data);
                onOutput?.Invoke(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is null)
                {
                    return;
                }

                stderr.AppendLine(args.Data);
                onOutput?.Invoke(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });

            await process.WaitForExitAsync(cancellationToken);
            var finishedAt = DateTimeOffset.UtcNow;
            var commandDisplay = string.Join(" ", new[] { request.FileName }.Concat(request.Arguments.Select(EscapeArgument)));

            return new ProcessExecutionResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                CommandDisplay = commandDisplay,
                Success = request.SuccessExitCodes.Contains(process.ExitCode),
            };
        }

        private static string EscapeArgument(string value) =>
            value.Contains(' ') || value.Contains('"')
                ? $"\"{value.Replace("\"", "\\\"")}\""
                : value;
    }
}
