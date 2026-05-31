using System.Diagnostics;
using System.Text;

namespace MediaWorkflowOrchestrator.Services
{
    public sealed class ProcessRunnerService : IProcessRunnerService
    {
        private const string DefaultTerminalColumns = "180";
        private const string DefaultTerminalLines = "48";
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            psi.Environment["COLUMNS"] = DefaultTerminalColumns;
            psi.Environment["LINES"] = DefaultTerminalLines;
            psi.Environment["TERM"] = "xterm";
            psi.Environment["PYTHONUTF8"] = "1";
            psi.Environment["PYTHONIOENCODING"] = "utf-8";

            foreach (var arg in request.Arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.Start();
            var stdoutTask = PumpProcessStreamAsync(process.StandardOutput, stdout, onOutput, cancellationToken);
            var stderrTask = PumpProcessStreamAsync(process.StandardError, stderr, onOutput, cancellationToken);

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
            await Task.WhenAll(stdoutTask, stderrTask);
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

        private static async Task PumpProcessStreamAsync(
            TextReader reader,
            StringBuilder capturedOutput,
            Action<string>? onOutput,
            CancellationToken cancellationToken)
        {
            var lineBuffer = new StringBuilder();
            var readBuffer = new char[1];

            while (await reader.ReadAsync(readBuffer.AsMemory(0, 1), cancellationToken) > 0)
            {
                var value = readBuffer[0];
                if (value is '\r' or '\n')
                {
                    FlushBufferedLine(lineBuffer, capturedOutput, onOutput);
                    continue;
                }

                lineBuffer.Append(value);
            }

            FlushBufferedLine(lineBuffer, capturedOutput, onOutput);
        }

        private static void FlushBufferedLine(StringBuilder lineBuffer, StringBuilder capturedOutput, Action<string>? onOutput)
        {
            if (lineBuffer.Length == 0)
            {
                return;
            }

            var line = lineBuffer.ToString();
            lineBuffer.Clear();
            capturedOutput.AppendLine(line);
            onOutput?.Invoke(line);
        }

        private static string EscapeArgument(string value) =>
            value.Contains(' ') || value.Contains('"')
                ? $"\"{value.Replace("\"", "\\\"")}\""
                : value;
    }
}
