using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace UITestKit.ServiceExcute
{
    public class ExecutableManager
    {
        private Process? _clientProcess;
        private Process? _serverProcess;

        // Events để UI subscribe
        public event Action<string>? ClientOutputReceived;
        public event Action<string>? ServerOutputReceived;

        private readonly string _debugFolder =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "process_logs");

        public void Init(string clientPath, string serverPath)
        {
            Directory.CreateDirectory(_debugFolder);

            _clientProcess = CreateProcess(clientPath, msg =>
            {
                ClientOutputReceived?.Invoke(msg);
                AppendDebugFile("client.log", msg);
            }, "Client");

            _serverProcess = CreateProcess(serverPath, msg =>
            {
                ServerOutputReceived?.Invoke(msg);
                AppendDebugFile("server.log", msg);
            }, "Server");
        }

        private Process CreateProcess(string exePath, Action<string> onOutput, string role)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    var filtered = FilterOutput(e.Data);
                    if (!string.IsNullOrEmpty(filtered))
                        onOutput(filtered);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    var filtered = FilterOutput(e.Data);
                    if (!string.IsNullOrEmpty(filtered))
                        onOutput($"[ERR] {filtered}");
                }
            };

            return process;
        }

        public void StartBoth()
        {
            if (_clientProcess == null || _serverProcess == null)
                throw new InvalidOperationException("Processes not initialized. Call Init(...) first.");

            _clientProcess.Start();
            _clientProcess.BeginOutputReadLine();
            _clientProcess.BeginErrorReadLine();

            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();
        }

        public void StopBoth()
        {
            StopProcess(ref _clientProcess);
            StopProcess(ref _serverProcess);
        }

        private void StopProcess(ref Process? process)
        {
            if (process == null) return;

            try
            {
                if (!process.HasExited)
                {
                    // Nếu process có UI thì thử đóng "mềm"
                    process.CloseMainWindow();
                    if (!process.WaitForExit(2000)) // chờ 2 giây
                    {
                        // Nếu vẫn chưa thoát thì kill cứng
                        process.Kill(true);
                        process.WaitForExit();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StopProcess ERR] {ex.Message}");
            }
            finally
            {
                process.Dispose();
                process = null;
            }
        }

        public void SendClientInput(string input)
        {
            if (_clientProcess != null && !_clientProcess.HasExited)
                _clientProcess.StandardInput.WriteLine(input);
        }

        public void SendServerInput(string input)
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
                _serverProcess.StandardInput.WriteLine(input);
        }

        private void AppendDebugFile(string fileName, string text)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(_debugFolder, fileName),
                    $"{DateTime.Now:O} {text}{Environment.NewLine}"
                );
            }
            catch { }
        }

        private string FilterOutput(string raw)
        {
            string[] ignoreKeywords = { "system", "debug", "info" };

            if (ignoreKeywords.Any(k => raw.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return string.Empty;

            return raw.Trim();
        }
    }
}
