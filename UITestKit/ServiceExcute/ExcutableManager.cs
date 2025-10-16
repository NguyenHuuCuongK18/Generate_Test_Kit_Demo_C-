using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace UITestKit.ServiceExcute
{
    public class ExecutableManager
    {
        #region Singleton
        private static readonly Lazy<ExecutableManager> _instance = new(() => new ExecutableManager());
        public static ExecutableManager Instance => _instance.Value;
        #endregion

        #region Fields
        private Process? _clientProcess;
        private Process? _serverProcess;
        private string _clientPath = string.Empty;
        private string _serverPath = string.Empty;

        private readonly string _debugFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "process_logs");
        #endregion

        #region Events
        public event Action<string>? ClientOutputReceived;
        public event Action<string>? ServerOutputReceived;
        #endregion

        #region Constructor
        private ExecutableManager()
        {
            Directory.CreateDirectory(_debugFolder);
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Khởi tạo sẵn thông tin process mà chưa chạy.
        /// </summary>
        public void Init(string clientPath, string serverPath)
        {
            _clientPath = clientPath;
            _serverPath = serverPath;

            if (_clientProcess == null)
            {
                _clientProcess = CreateProcess(clientPath, msg =>
                {
                    ClientOutputReceived?.Invoke(msg);
                    AppendDebugFile("client.log", msg);
                }, "Client");
            }

            if (_serverProcess == null)
            {
                _serverProcess = CreateProcess(serverPath, msg =>
                {
                    ServerOutputReceived?.Invoke(msg);
                    AppendDebugFile("server.log", msg);
                }, "Server");
            }
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
        #endregion

        #region Start Methods
        /// <summary>
        /// Chạy server trước để middleware có thể kết nối.
        /// </summary>
        public void StartServer()
        {
            Init(_clientPath, _serverPath);

            if (_serverProcess == null)
                throw new InvalidOperationException("Server process not initialized.");

            StartProcessAndMonitor(_serverProcess, msg => ServerOutputReceived?.Invoke(msg), "server.log");
        }

        /// <summary>
        /// Chạy client sau khi middleware đã sẵn sàng.
        /// </summary>
        public void StartClient()
        {
            Init(_clientPath, _clientPath);

            if (_clientProcess == null)
                throw new InvalidOperationException("Client process not initialized.");

            StartProcessAndMonitor(_clientProcess, msg => ClientOutputReceived?.Invoke(msg), "client.log");
        }
        #endregion

        #region Process Monitoring - OPTIMIZED with Console.Write() support
        private void StartProcessAndMonitor(Process process, Action<string> onOutput, string logFile)
        {
            process.Start();

            Task.Run(async () =>
            {
                try
                {
                    var reader = process.StandardOutput;
                    var buffer = new char[1024];
                    var lineBuffer = new StringBuilder();
                    CancellationTokenSource? flushCts = null;

                    const int FLUSH_DELAY_MS = 100; // Delay để flush partial content

                    void FlushPartialLine()
                    {
                        if (lineBuffer.Length == 0) return;

                        var content = lineBuffer.ToString();
                        lineBuffer.Clear();

                        onOutput(content);
                        AppendDebugFile(logFile, content);
                    }

                    void ScheduleFlush()
                    {
                        var oldCts = Interlocked.Exchange(ref flushCts, new CancellationTokenSource());
                        oldCts?.Cancel();
                        oldCts?.Dispose();

                        var currentCts = flushCts;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(FLUSH_DELAY_MS, currentCts.Token);

                                lock (lineBuffer)
                                {
                                    FlushPartialLine();
                                }
                            }
                            catch (TaskCanceledException) { }
                        });
                    }

                    int read;
                    while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        lock (lineBuffer)
                        {
                            for (int i = 0; i < read; i++)
                            {
                                char c = buffer[i];

                                if (c == '\n')
                                {
                                    FlushPartialLine();

                                    var cts = Interlocked.Exchange(ref flushCts, null);
                                    cts?.Cancel();
                                    cts?.Dispose();
                                }
                                else if (c != '\r') // Ignore \r
                                {
                                    lineBuffer.Append(c);
                                }
                            }

                            if (lineBuffer.Length > 0)
                            {
                                ScheduleFlush();
                            }
                        }
                    }

                    // Final flush when stream ends
                    lock (lineBuffer)
                    {
                        var cts = Interlocked.Exchange(ref flushCts, null);
                        cts?.Cancel();
                        cts?.Dispose();

                        FlushPartialLine();
                    }
                }
                catch (Exception ex)
                {
                    var errorMsg = $"[Output Error] {ex.Message}";
                    onOutput(errorMsg);
                    AppendDebugFile(logFile, errorMsg);
                }
            });

            // ===== ERROR (stderr) 
            Task.Run(async () =>
            {
                try
                {
                    var reader = process.StandardError;
                    var buffer = new char[1024];
                    var errorBuffer = new StringBuilder();

                    int read;
                    while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        for (int i = 0; i < read; i++)
                        {
                            char c = buffer[i];

                            if (c == '\n')
                            {
                                if (errorBuffer.Length > 0)
                                {
                                    var errMsg = $"[ERR] {errorBuffer}";
                                    errorBuffer.Clear();

                                    onOutput(errMsg);
                                    AppendDebugFile(logFile, errMsg);
                                }
                            }
                            else if (c != '\r')
                            {
                                errorBuffer.Append(c);
                            }
                        }
                    }

                    // Final flush
                    if (errorBuffer.Length > 0)
                    {
                        var errMsg = $"[ERR] {errorBuffer}";
                        onOutput(errMsg);
                        AppendDebugFile(logFile, errMsg);
                    }
                }
                catch (Exception ex)
                {
                    var errorMsg = $"[Error Stream Error] {ex.Message}";
                    onOutput(errorMsg);
                    AppendDebugFile(logFile, errorMsg);
                }
            });
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
        #endregion

        #region Stop Methods 

        #region StopAllAsync
        public async Task StopAllAsync()
        {
            ProgressDialog? dialog = null;
            try
            {
                // Hiển thị loading dialog không chặn
                //await Application.Current.Dispatcher.InvokeAsync(() =>
                //{
                //    dialog = new ProgressDialog("Đang dừng tất cả tiến trình...");
                //    dialog.Owner = Application.Current.MainWindow;
                //    dialog.Show(); // Không dùng ShowDialog -> không chặn luồng
                //});

                // Dừng tiến trình song song
                var stopClientTask = StopProcessAsync(_clientProcess, "Client");
                var stopServerTask = StopProcessAsync(_serverProcess, "Server");

                await Task.WhenAll(stopClientTask, stopServerTask);

                _clientProcess = null;
                _serverProcess = null;

                // Đóng dialog sau khi dừng xong
                //await Application.Current.Dispatcher.InvokeAsync(() =>
                //{
                //    dialog?.Close();
                //    MessageBox.Show("Tất cả tiến trình đã được dừng thành công.",
                //        "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                //});
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
        #endregion

        #region StopClientAsync
        public async Task StopClientAsync()
        {
            ProgressDialog? dialog = null;
            try
            {
                // Hiển thị loading dialog không chặn

                await StopProcessAsync(_clientProcess, "Client");
                AppendDebugFile("Stop.log", "Before Client Kill");
                _clientProcess = null;

            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog?.Close();
                    MessageBox.Show($"[StopClientAsync ERR] {ex.Message}",
                        "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            AppendDebugFile("Stop.log", "After Client Kill");
        }
        #endregion

        #region StopServerAsync
        public async Task StopServerAsync()
        {
            ProgressDialog? dialog = null;
            try
            {
                // Hiển thị loading dialog không chặn

                await StopProcessAsync(_serverProcess, "Server");

                _serverProcess = null;

                // Đóng dialog sau khi dừng xong
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog?.Close();
                    MessageBox.Show("Tiến trình server đã được dừng thành công.",
                        "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog?.Close();
                    MessageBox.Show($"[StopServerAsync ERR] {ex.Message}",
                        "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
        #endregion

        #region StopProcessAsync method 
        private async Task StopProcessAsync(Process? process, string role)
        {
            if (process == null || process.HasExited) return;

            try
            {
                var tcs = new TaskCompletionSource<bool>();
                process.Exited += (sender, e) => tcs.TrySetResult(true);

                await Task.Run(async () =>
                {
                    try
                    {
                        AppendDebugFile("Stop.log", $"[{role}] Before Kill");
                        var killTask = Task.Run(() => process.Kill(false)); // Dùng Kill(false) cho console đơn giản
                        var timeoutTask = Task.Delay(5000); // Timeout 5s
                        var completedTask = await Task.WhenAny(killTask, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            AppendDebugFile($"{role.ToLower()}.log", $"[{role}] Kill timed out, process may still be running");
                            // Fallback: Dùng taskkill nếu cần
                            using (var taskKillProcess = new Process())
                            {
                                taskKillProcess.StartInfo = new ProcessStartInfo
                                {
                                    FileName = "taskkill",
                                    Arguments = $"/F /PID {process.Id}",
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };
                                taskKillProcess.Start();
                                taskKillProcess.WaitForExit(2000);
                            }
                        }
                        else
                        {
                            AppendDebugFile("Stop.log", $"[{role}] After Kill");
                            await Task.WhenAny(tcs.Task, Task.Delay(1000)); // Chờ exited event
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendDebugFile($"{role.ToLower()}.log", $"[{role}] StopProcess ERR: {ex}");
                    }
                }).ConfigureAwait(false); // Tránh capture UI context
            }
            catch (InvalidOperationException)
            {
                // Process exited or invalid
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }
        #endregion

        #endregion
    }
}