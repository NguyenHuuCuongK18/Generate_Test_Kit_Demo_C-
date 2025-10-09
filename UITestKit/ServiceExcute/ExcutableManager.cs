using System;
using System.Diagnostics;
using System.IO;
using System.Security.RightsManagement;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UITestKit.Service;

namespace UITestKit.ServiceExcute
{
    public class ExecutableManager
    {
        private static readonly Lazy<ExecutableManager> _instance =
            new(() => new ExecutableManager());
        public static ExecutableManager Instance => _instance.Value;
        private HashSet<string> _ignoreTexts = new HashSet<string>();

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
        //load list ignore
        public void InitializeIgnoreList(string excelPath)
        {
            try
            {
                var file = Path.Combine("D:\\CSharp_Project\\TestKitGenerator", "Ignore.xlsx");
                _ignoreTexts = IgnoreListLoader.IgnoreLoader(file);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể load file ignore: {ex.Message}");
                _ignoreTexts = new HashSet<string>();
            }
        }
        // method check isIgnore
        private bool ShouldIgnore(string line)
        {
            if (_ignoreTexts == null || _ignoreTexts.Count == 0)
                return false;

            foreach (var ignore in _ignoreTexts)
            {
                if (line.Contains(ignore, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
                string leftover = string.Empty;

                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string chunk = leftover + new string(buffer, 0, read);
                    var lines = chunk.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                    // Nếu chunk KHÔNG kết thúc bằng newline, thì dòng cuối bị cắt dở -> lưu lại
                    if (!chunk.EndsWith("\n") && !chunk.EndsWith("\r"))
                    {
                        leftover = lines[^1];
                        lines = lines.Take(lines.Length - 1).ToArray();
                    }
                    else
                    {
                        leftover = string.Empty;
                    }

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (ShouldIgnore(line)) continue;
                        onOutput(line);
                    }
                }

                // Flush dòng cuối cùng nếu còn sót
                if (!string.IsNullOrWhiteSpace(leftover))
                    onOutput(leftover);
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

        public async Task StopAllAsync()
        {
            ProgressDialog? dialog = null;
            try
            {
                // Hiển thị loading dialog không chặn
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog = new ProgressDialog("Đang dừng tất cả tiến trình...");
                    dialog.Owner = Application.Current.MainWindow;
                    dialog.Show(); // Không dùng ShowDialog -> không chặn luồng
                });

                // Dừng tiến trình song song
                var stopClientTask = StopProcessAsync(_clientProcess, "Client");
                var stopServerTask = StopProcessAsync(_serverProcess, "Server");

                await Task.WhenAll(stopClientTask, stopServerTask);

                _clientProcess = null;
                _serverProcess = null;

                // Đóng dialog sau khi dừng xong
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog?.Close();
                    MessageBox.Show("Tất cả tiến trình đã được dừng thành công.",
                        "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog?.Close();
                    MessageBox.Show($"[StopAllAsync ERR] {ex.Message}",
                        "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }



        private async Task StopProcessAsync(Process? process, string role)
        {
            if (process == null) return;

            try
            {
                if (!process.HasExited)
                {
                    bool hasWindow = process.MainWindowHandle != IntPtr.Zero;

                    if (hasWindow)
                    {
                        process.CloseMainWindow();
                        if (await WaitForExitAsync(process, 2000))
                        {
                            AppendDebugFile($"{role.ToLower()}.log", $"[{role}] closed normally.");
                            return;
                        }
                    }

                    process.Kill(true);
                    await WaitForExitAsync(process, 3000);
                    AppendDebugFile($"{role.ToLower()}.log", $"[{role}] killed by manager.");
                }
            }
            catch (Exception ex)
            {
                AppendDebugFile($"{role.ToLower()}.log", $"[StopProcess ERR] {ex}");
            }
            finally
            {
                process.Dispose();
            }
        }

        private static Task<bool> WaitForExitAsync(Process process, int milliseconds)
        {
            return Task.Run(() => process.WaitForExit(milliseconds));
        }


        #endregion
    }
}
