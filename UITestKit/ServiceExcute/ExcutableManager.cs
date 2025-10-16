using System;
using System.Diagnostics;
using System.IO;
using System.Security.RightsManagement;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UITestKit.Model;
using UITestKit.Service;

namespace UITestKit.ServiceExcute
{
    public class ExecutableManager
    {
        private static readonly Lazy<ExecutableManager> _instance =
            new(() => new ExecutableManager());
        public static ExecutableManager Instance => _instance.Value;
        private Process? _clientProcess;
        private Process? _serverProcess;
        private string _clientPath;
        private string _serverPath;

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

        #region Start

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
            Init(_clientPath, _serverPath);
            if (_clientProcess == null)
                throw new InvalidOperationException("Client process not initialized.");

            StartProcessAndMonitor(_clientProcess, msg => ClientOutputReceived?.Invoke(msg), "client.log");
        }

        #endregion

        #region StartProcessAndMonitor
        private void StartProcessAndMonitor(Process process, Action<string> onOutput, string logFile)
        {
            process.Start();

            // ===== OUTPUT (stdout) =====
            Task.Run(async () =>
            {
                var reader = process.StandardOutput;
                char[] buffer = new char[1024];
                int read;

                var sb = new StringBuilder();      // lưu partial line giữa các chunk
                object sbLock = new object();
                CancellationTokenSource pendingFlushCts = null;

                const int DEBOUNCE_MS = 100; // thời gian chờ trước khi flush phần partial (tùy chỉnh)

                void ScheduleFlushPartial()
                {
                    // Cancel + dispose cts trước đó (nếu có)
                    var prev = Interlocked.Exchange(ref pendingFlushCts, new CancellationTokenSource());
                    if (prev != null)
                    {
                        try { prev.Cancel(); prev.Dispose(); }
                        catch { }
                    }

                    var cts = pendingFlushCts;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(DEBOUNCE_MS, cts.Token);
                        }
                        catch (TaskCanceledException) { return; }

                        string partial;
                        lock (sbLock)
                        {
                            if (sb.Length == 0) return;
                            partial = sb.ToString();
                            sb.Clear();
                        }

                        // flush partial (đã có pause -> coi như hoàn chỉnh)
                        onOutput(partial);
                        AppendDebugFile(logFile, partial);
                    });
                }

                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        char c = buffer[i];

                        // coi cả '\r' và '\n' là terminator
                        if (c == '\r' || c == '\n')
                        {
                            string line;
                            lock (sbLock)
                            {
                                line = sb.ToString();
                                sb.Clear();
                            }

                            if (!string.IsNullOrEmpty(line))
                            {
                                onOutput(line);
                                AppendDebugFile(logFile, line);
                            }

                            // Nếu có timer flush partial đang chờ, huỷ nó (bởi ta vừa flush)
                            var prev = Interlocked.Exchange(ref pendingFlushCts, null);
                            if (prev != null)
                            {
                                try { prev.Cancel(); prev.Dispose(); }
                                catch { }
                            }
                        }
                        else
                        {
                            lock (sbLock) sb.Append(c);
                        }
                    }

                    // Nếu còn partial (không có newline trong chunk), schedule một flush sau debounce
                    bool hasPartial;
                    lock (sbLock) { hasPartial = sb.Length > 0; }
                    if (hasPartial) ScheduleFlushPartial();
                }

                // Khi stream kết thúc: huỷ timer và flush phần còn lại ngay
                var finalCts = Interlocked.Exchange(ref pendingFlushCts, null);
                if (finalCts != null) { try { finalCts.Cancel(); finalCts.Dispose(); } catch { } }

                string last;
                lock (sbLock)
                {
                    last = sb.Length > 0 ? sb.ToString() : null;
                    sb.Clear();
                }
                if (!string.IsNullOrEmpty(last))
                {
                    onOutput(last);
                    AppendDebugFile(logFile, last);
                }
            });

            // ===== ERROR (stderr) =====
            Task.Run(async () =>
            {
                var errReader = process.StandardError;
                char[] errBuf = new char[1024];
                int errRead;
                var errSb = new StringBuilder();

                while ((errRead = await errReader.ReadAsync(errBuf, 0, errBuf.Length)) > 0)
                {
                    for (int i = 0; i < errRead; i++)
                    {
                        char c = errBuf[i];
                        if (c == '\r' || c == '\n')
                        {
                            if (errSb.Length > 0)
                            {
                                var chunk = errSb.ToString();
                                errSb.Clear();
                                onOutput("[ERR] " + chunk);
                                AppendDebugFile(logFile, "[ERR] " + chunk);
                            }
                        }
                        else
                        {
                            errSb.Append(c);
                        }
                    }

                    // error thường ngắn — flush partial ngay
                    if (errSb.Length > 0)
                    {
                        var partial = errSb.ToString();
                        errSb.Clear();
                        onOutput("[ERR] " + partial);
                        AppendDebugFile(logFile, "[ERR] " + partial);
                    }
                }

                if (errSb.Length > 0)
                {
                    var leftover = errSb.ToString();
                    onOutput("[ERR] " + leftover);
                    AppendDebugFile(logFile, "[ERR] " + leftover);
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
    }
}