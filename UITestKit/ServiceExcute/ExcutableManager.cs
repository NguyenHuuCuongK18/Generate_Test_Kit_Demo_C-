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

        // Lock objects để đảm bảo thread safety
        private readonly object _clientLock = new object();
        private readonly object _serverLock = new object();
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
        /// Thread-safe với lock và xử lý InvalidOperationException
        /// </summary>
        public void Init(string clientPath, string serverPath)
        {
            _clientPath = clientPath;
            _serverPath = serverPath;

            // Client process initialization
            lock (_clientLock)
            {
                if (_clientProcess != null)
                {
                    bool needRecreate = false;

                    try
                    {
                        // HasExited throws InvalidOperationException if process never started
                        needRecreate = _clientProcess.HasExited ||
                                      !string.Equals(_clientProcess.StartInfo.FileName, _clientPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (InvalidOperationException)
                    {
                        // Process was not started yet or already disposed
                        needRecreate = true;
                    }
                    catch (Exception ex)
                    {
                        AppendDebugFile("client.log", $"Error checking client process: {ex.Message}");
                        needRecreate = true;
                    }

                    if (needRecreate)
                    {
                        AppendDebugFile("client.log", $"Recreating client process. NewFile={_clientPath}");
                        try { _clientProcess.Dispose(); } catch { }
                        _clientProcess = null;
                    }
                }

                if (_clientProcess == null && !string.IsNullOrEmpty(_clientPath))
                {
                    _clientProcess = CreateProcess(_clientPath, msg =>
                    {
                        ClientOutputReceived?.Invoke(msg);
                        AppendDebugFile("client.log", msg);
                    }, "Client");
                    AppendDebugFile("client.log", $"Client process created: {_clientPath}");
                }
            }

            // Server process initialization
            lock (_serverLock)
            {
                if (_serverProcess != null)
                {
                    bool needRecreate = false;

                    try
                    {
                        needRecreate = _serverProcess.HasExited ||
                                      !string.Equals(_serverProcess.StartInfo.FileName, _serverPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (InvalidOperationException)
                    {
                        needRecreate = true;
                    }
                    catch (Exception ex)
                    {
                        AppendDebugFile("server.log", $"Error checking server process: {ex.Message}");
                        needRecreate = true;
                    }

                    if (needRecreate)
                    {
                        AppendDebugFile("server.log", $"Recreating server process. NewFile={_serverPath}");
                        try { _serverProcess.Dispose(); } catch { }
                        _serverProcess = null;
                    }
                }

                if (_serverProcess == null && !string.IsNullOrEmpty(_serverPath))
                {
                    _serverProcess = CreateProcess(_serverPath, msg =>
                    {
                        ServerOutputReceived?.Invoke(msg);
                        AppendDebugFile("server.log", msg);
                    }, "Server");
                    AppendDebugFile("server.log", $"Server process created: {_serverPath}");
                }
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

            process.Exited += (s, e) =>
            {
                try
                {
                    var msg = $"[{role}] exited.";
                    onOutput(msg);
                    AppendDebugFile(role.ToLower() + ".log", msg);
                }
                catch { }
            };

            return process;
        }
        #endregion

        #region Start Methods
        /// <summary>
        /// Chạy server trước để middleware có thể kết nối.
        /// </summary>
        public void StartServer()
        {
            lock (_serverLock)
            {
                try
                {
                    AppendDebugFile("server.log", "=== StartServer called ===");

                    // Đảm bảo paths hiện thời được sử dụng và process được tái tạo khi cần
                    Init(_clientPath, _serverPath);

                    if (_serverProcess == null)
                        throw new InvalidOperationException("Server process not initialized after Init().");

                    // Kiểm tra xem process có thể start được không
                    bool needRecreate = false;
                    try
                    {
                        // Nếu HasExited = true -> process đã chạy và đã dừng -> cần tạo mới
                        // Nếu HasExited throw exception -> process chưa start bao giờ -> OK để start
                        needRecreate = _serverProcess.HasExited;
                        AppendDebugFile("server.log", $"Process HasExited = {needRecreate}");
                    }
                    catch (InvalidOperationException)
                    {
                        // Process chưa được start lần nào -> OK để start
                        AppendDebugFile("server.log", "Process never started before - OK to start");
                        needRecreate = false;
                    }

                    // Kiểm tra file path có đúng không
                    if (!needRecreate && !string.Equals(_serverProcess.StartInfo.FileName, _serverPath, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendDebugFile("server.log", $"Path mismatch: {_serverProcess.StartInfo.FileName} != {_serverPath}");
                        needRecreate = true;
                    }

                    // Nếu cần tái tạo process
                    if (needRecreate)
                    {
                        AppendDebugFile("server.log", "Recreating server process before start");
                        try { _serverProcess.Dispose(); } catch { }
                        _serverProcess = CreateProcess(_serverPath, msg =>
                        {
                            ServerOutputReceived?.Invoke(msg);
                            AppendDebugFile("server.log", msg);
                        }, "Server");
                    }

                    AppendDebugFile("server.log", $"Starting server: {_serverProcess.StartInfo.FileName}");
                    StartProcessAndMonitor(_serverProcess, msg => ServerOutputReceived?.Invoke(msg), "server.log");
                    AppendDebugFile("server.log", "✓ Server started successfully");
                }
                catch (Exception ex)
                {
                    AppendDebugFile("server.log", $"❌ ERROR StartServer: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Chạy client sau khi middleware đã sẵn sàng.
        /// </summary>
        public void StartClient()
        {
            lock (_clientLock)
            {
                try
                {
                    AppendDebugFile("client.log", "=== StartClient called ===");

                    Init(_clientPath, _serverPath);

                    if (_clientProcess == null)
                        throw new InvalidOperationException("Client process not initialized after Init().");

                    bool needRecreate = false;
                    try
                    {
                        needRecreate = _clientProcess.HasExited;
                        AppendDebugFile("client.log", $"Process HasExited = {needRecreate}");
                    }
                    catch (InvalidOperationException)
                    {
                        AppendDebugFile("client.log", "Process never started before - OK to start");
                        needRecreate = false;
                    }

                    if (!needRecreate && !string.Equals(_clientProcess.StartInfo.FileName, _clientPath, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendDebugFile("client.log", $"Path mismatch: {_clientProcess.StartInfo.FileName} != {_clientPath}");
                        needRecreate = true;
                    }

                    if (needRecreate)
                    {
                        AppendDebugFile("client.log", "Recreating client process before start");
                        try { _clientProcess.Dispose(); } catch { }
                        _clientProcess = CreateProcess(_clientPath, msg =>
                        {
                            ClientOutputReceived?.Invoke(msg);
                            AppendDebugFile("client.log", msg);
                        }, "Client");
                    }

                    AppendDebugFile("client.log", $"Starting client: {_clientProcess.StartInfo.FileName}");
                    StartProcessAndMonitor(_clientProcess, msg => ClientOutputReceived?.Invoke(msg), "client.log");
                    AppendDebugFile("client.log", "✓ Client started successfully");
                }
                catch (Exception ex)
                {
                    AppendDebugFile("client.log", $"❌ ERROR StartClient: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            }
        }
        #endregion

        #region Process Monitoring - OPTIMIZED with Console.Write() support
        private void StartProcessAndMonitor(Process process, Action<string> onOutput, string logFile)
        {
            if (process == null)
            {
                AppendDebugFile(logFile, "❌ Process is null, cannot start");
                throw new InvalidOperationException("Process is null");
            }

            try
            {
                bool alreadyStarted = false;
                try
                {
                    // Nếu HasExited không throw exception -> process đã được start
                    // Nếu HasExited = false -> đang chạy
                    // Nếu HasExited = true -> đã chạy và đã dừng
                    alreadyStarted = !process.HasExited;
                    AppendDebugFile(logFile, $"Process check: alreadyStarted={alreadyStarted}, HasExited={process.HasExited}");
                }
                catch (InvalidOperationException)
                {
                    // Process chưa start -> OK
                    AppendDebugFile(logFile, "Process never started - proceeding with Start()");
                    alreadyStarted = false;
                }

                if (alreadyStarted)
                {
                    AppendDebugFile(logFile, "⚠ Process already running, skipping Start()");
                    return;
                }

                process.Start();
                AppendDebugFile(logFile, $"✓ Process.Start() called. PID={process.Id}");
            }
            catch (InvalidOperationException ex)
            {
                AppendDebugFile(logFile, $"❌ Start() failed: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                AppendDebugFile(logFile, $"❌ Unexpected error in Start(): {ex.Message}\n{ex.StackTrace}");
                throw;
            }

            // Monitor stdout
            Task.Run(async () =>
            {
                try
                {
                    var reader = process.StandardOutput;
                    var buffer = new char[1024];
                    var lineBuffer = new StringBuilder();
                    CancellationTokenSource? flushCts = null;

                    const int FLUSH_DELAY_MS = 100;

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
                                else if (c != '\r')
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

            // Monitor stderr
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
            lock (_clientLock)
            {
                try
                {
                    if (_clientProcess != null && !_clientProcess.HasExited)
                    {
                        _clientProcess.StandardInput.WriteLine(input);
                        AppendDebugFile("client.log", $">>> Input sent: {input}");
                    }
                    else
                    {
                        AppendDebugFile("client.log", "Cannot send input - process is null or exited");
                    }
                }
                catch (Exception ex)
                {
                    AppendDebugFile("client.log", $"SendInput error: {ex.Message}");
                }
            }
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

        public async Task StopAllAsync()
        {
            try
            {
                AppendDebugFile("Stop.log", "=== StopAllAsync called ===");

                var stopClientTask = StopClientAsync();
                var stopServerTask = StopServerAsync();

                await Task.WhenAll(stopClientTask, stopServerTask);

                AppendDebugFile("Stop.log", "✓ All processes stopped");
            }
            catch (Exception ex)
            {
                AppendDebugFile("Stop.log", $"❌ StopAllAsync error: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"[StopAllAsync ERR] {ex.Message}",
                        "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        public async Task StopClientAsync()
        {
            Process? processToStop = null;

            lock (_clientLock)
            {
                processToStop = _clientProcess;
                _clientProcess = null; // Set null ngay để tránh race condition
                AppendDebugFile("Stop.log", $"Client process captured for stopping: {(processToStop != null ? "Yes" : "No")}");
            }

            try
            {
                AppendDebugFile("Stop.log", "=== StopClientAsync - Before Stop ===");
                await StopProcessAsync(processToStop, "Client");
                AppendDebugFile("Stop.log", "=== StopClientAsync - After Stop ===");
            }
            catch (Exception ex)
            {
                AppendDebugFile("Stop.log", $"❌ StopClientAsync error: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"[StopClientAsync ERR] {ex.Message}",
                        "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        public async Task StopServerAsync()
        {
            Process? processToStop = null;

            lock (_serverLock)
            {
                processToStop = _serverProcess;
                _serverProcess = null; // Set null ngay
                AppendDebugFile("Stop.log", $"Server process captured for stopping: {(processToStop != null ? "Yes" : "No")}");
            }

            try
            {
                AppendDebugFile("Stop.log", "=== StopServerAsync - Before Stop ===");
                await StopProcessAsync(processToStop, "Server");
                AppendDebugFile("Stop.log", "=== StopServerAsync - After Stop ===");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show("Tiến trình server đã được dừng thành công.",
                        "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                AppendDebugFile("Stop.log", $"❌ StopServerAsync error: {ex.Message}");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"[StopServerAsync ERR] {ex.Message}",
                        "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async Task StopProcessAsync(Process? process, string role)
        {
            if (process == null)
            {
                AppendDebugFile("Stop.log", $"[{role}] Process is null, nothing to stop");
                return;
            }

            bool hasExited = false;
            int? pid = null;

            try
            {
                pid = process.Id;
                hasExited = process.HasExited;
                AppendDebugFile("Stop.log", $"[{role}] PID={pid}, HasExited={hasExited}");
            }
            catch (InvalidOperationException)
            {
                AppendDebugFile("Stop.log", $"[{role}] Process not started or already disposed");
                try { process.Dispose(); } catch { }
                return;
            }

            if (hasExited)
            {
                AppendDebugFile("Stop.log", $"[{role}] Process already exited");
                try { process.Dispose(); } catch { }
                return;
            }

            try
            {
                var tcs = new TaskCompletionSource<bool>();
                process.Exited += (sender, e) => tcs.TrySetResult(true);

                await Task.Run(async () =>
                {
                    try
                    {
                        AppendDebugFile("Stop.log", $"[{role}] Calling Kill() on PID={pid}");

                        var killTask = Task.Run(() => process.Kill(false));
                        var timeoutTask = Task.Delay(5000);
                        var completedTask = await Task.WhenAny(killTask, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            AppendDebugFile($"{role.ToLower()}.log", $"[{role}] Kill timed out after 5s, using taskkill");

                            using (var taskKillProcess = new Process())
                            {
                                taskKillProcess.StartInfo = new ProcessStartInfo
                                {
                                    FileName = "taskkill",
                                    Arguments = $"/F /PID {pid}",
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };
                                taskKillProcess.Start();
                                taskKillProcess.WaitForExit(2000);
                            }
                        }
                        else
                        {
                            // Wait for Exited event or timeout
                            await Task.WhenAny(tcs.Task, Task.Delay(1000));
                        }

                        AppendDebugFile("Stop.log", $"[{role}] ✓ Process killed successfully");
                    }
                    catch (Exception ex)
                    {
                        AppendDebugFile($"{role.ToLower()}.log", $"[{role}] ❌ StopProcess ERR: {ex.Message}\n{ex.StackTrace}");
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppendDebugFile("Stop.log", $"[{role}] ❌ Exception in StopProcessAsync: {ex.Message}");
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }

        #endregion
    }
}