using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UITestKit.ServiceExcute
{
    public class ExecutableManager
    {
        private Process? _clientProcess;
        private Process? _serverProcess;

        public event Action<string>? ClientOutputReceived;
        public event Action<string>? ServerOutputReceived;

        private readonly string _debugFolder =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "process_logs");

        public ExecutableManager()
        {
            Directory.CreateDirectory(_debugFolder);
        }

        /// <summary>
        /// Khởi tạo sẵn thông tin process mà chưa chạy.
        /// </summary>
        public void Init(string clientPath, string serverPath)
        {
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
            if (!File.Exists(exePath))
                throw new FileNotFoundException($"Executable not found: {exePath}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            process.Exited += (s, e) => onOutput($"[{role}] exited.");

            return process;
        }

        #region Start/Stop

        /// <summary>
        /// Chạy server trước để middleware có thể kết nối.
        /// </summary>
        public void StartServer()
        {
            if (_serverProcess == null)
                throw new InvalidOperationException("Server process not initialized.");

            StartProcessAndMonitor(_serverProcess, msg => ServerOutputReceived?.Invoke(msg), "server.log");
        }

        /// <summary>
        /// Chạy client sau khi middleware đã sẵn sàng.
        /// </summary>
        public void StartClient()
        {
            if (_clientProcess == null)
                throw new InvalidOperationException("Client process not initialized.");

            StartProcessAndMonitor(_clientProcess, msg => ClientOutputReceived?.Invoke(msg), "client.log");
        }

        /// <summary>
        /// Trước đây là StartBoth, giữ lại nếu cần chạy song song.
        /// </summary>
        public void StartBoth()
        {
            StartServer();
            StartClient();
        }

        private void StartProcessAndMonitor(Process process, Action<string> onOutput, string logFile)
        {
            process.Start();

            // Đọc output liên tục (kể cả Console.Write)
            Task.Run(async () =>
            {
                var reader = process.StandardOutput;
                char[] buffer = new char[256];
                int read;
                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string chunk = new(buffer, 0, read);
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        onOutput(chunk);
                        AppendDebugFile(logFile, chunk);
                    }
                }
            });

            // Đọc error stream
            Task.Run(async () =>
            {
                var errReader = process.StandardError;
                char[] buffer = new char[256];
                int read;
                while ((read = await errReader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string chunk = new(buffer, 0, read);
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        onOutput("[ERR] " + chunk);
                        AppendDebugFile(logFile, "[ERR] " + chunk);
                    }
                }
            });
        }

        public void StopAll()
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
                    process.CloseMainWindow();
                    if (!process.WaitForExit(2000))
                    {
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
        #endregion

        #region Input/Output

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

        #endregion
    }
}
