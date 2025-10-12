using OfficeOpenXml.ConditionalFormatting.Contracts;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using UITestKit.Model;
using UITestKit.Service;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace UITestKit.MiddlewareHandling
{
    public sealed class MiddlewareStart
    {
        #region Singleton
        private static readonly Lazy<MiddlewareStart> _instance =
            new(() => new MiddlewareStart());
        public static MiddlewareStart Instance => _instance.Value;
        private MiddlewareStart() { }
        #endregion

        public RecorderWindow? Recorder { get; set; }

        private CancellationTokenSource? _cts;
        private bool _isSessionRunning;

        private HttpListener? _httpListener;
        private TcpListener? _tcpListener;

        private const int PROXY_PORT = 5000;       // Port client kết nối đến
        private const int REAL_SERVER_PORT = 5001; // Port server thực

        #region Start / Stop

        public async Task StartAsync(bool useHttp = true)
        {
            if (_isSessionRunning)
                return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _isSessionRunning = true;

            if (useHttp)
            {
                StartHttpProxy(token);
            }
            else
            {
                StartTcpProxy(token);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Dừng toàn bộ middleware proxy và giải phóng tài nguyên.
        /// </summary>
        public async Task StopAsync()  // Đổi tên thành StopAsync để rõ ràng async
        {
            ProgressDialog? dialog = null;
            try
            {
                if (!_isSessionRunning) return;


                // Show dialog async, non-modal (không chặn)
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog = new ProgressDialog("Đang dừng Middleware Proxy...");
                    dialog.Owner = Application.Current.MainWindow;
                    dialog.Show();  // Sử dụng Show() thay ShowDialog()
                });

                // Chạy stop logic ngay lập tức (không bị chặn)
                _isSessionRunning = false;

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                if (_httpListener != null && _httpListener.IsListening)
                {
                    _httpListener.Stop();
                    _httpListener.Close();
                    _httpListener = null;
                }

                if (_tcpListener != null)
                {
                    _tcpListener.Stop();
                    _tcpListener = null;
                }

                // Log (giữ nguyên, nhưng nếu log chậm, có thể làm async sau)
                AppendToFile(new LoggedRequest
                {
                    Method = "SYSTEM",
                    Url = "Proxy stopped",
                    RequestBody = "Middleware stopped gracefully.",
                    StatusCode = 0
                });

                // Close dialog sau khi done
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog?.Close();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MiddlewareStop ERR] {ex.Message}");

                // Đảm bảo close dialog nếu error
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog?.Close();
                });
            }
        }

        #endregion

        #region HTTP Proxy

        private void StartHttpProxy(CancellationToken token)
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{PROXY_PORT}/");
                _httpListener.Start();

                Task.Run(() => ListenForHttpRequests(token), token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP Proxy ERR] {ex.Message}");
            }
        }

        private async Task ListenForHttpRequests(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener!.GetContextAsync();
                    _ = Task.Run(() => ProcessHttpRequest(context), token);
                }
                catch (HttpListenerException)
                {
                    break; // listener stopped
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Middleware ERR] {ex.Message}");
                }
            }
        }

        private async Task ProcessHttpRequest(HttpListenerContext context)
        {
            var request = context.Request;
            string requestBody;

            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            requestBody = reader.ReadToEnd();

            // get and detect dataType
            var requestBytes = request.ContentEncoding.GetBytes(requestBody);
            var requestDataType = DataInspector.DetecDataType(requestBytes);
            if (Recorder != null && Recorder.InputClients.Any())
            {
                // intial OutputServer(bắt request gửi đến server)
                var stage = Recorder.InputClients.Last().Stage;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Recorder.OutputServers.Add(new OutputServer
                    {
                        Stage = stage,
                        Method = request.HttpMethod,
                        DataTypeMiddleware = requestDataType,
                        ByteSize = requestBytes.Length,
                        DataRequest = requestBody,
                    });
                });
            }


            try
            {
                var realServerUrl = $"http://localhost:{REAL_SERVER_PORT}{request.Url?.AbsolutePath}";
                using var client = new HttpClient();
                var forwardRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), realServerUrl);
                MediaTypeHeaderValue? contentType = null;
                if (request.ContentType != null) { contentType = MediaTypeHeaderValue.Parse(request.ContentType); }
                forwardRequest.Content = new StringContent(requestBody, contentType);

                var responseMessage = await client.SendAsync(forwardRequest);
                var responseBytes = await responseMessage.Content.ReadAsByteArrayAsync();
                string responseBody = Encoding.UTF8.GetString(responseBytes);
                var responseDataType = DataInspector.DetecDataType(responseBytes);

                if (Recorder != null && Recorder.InputClients.Any())
                {
                    var stage = Recorder.InputClients.Last().Stage;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Recorder.OutputClients.Add(new OutputClient
                        {
                            Stage = stage,
                            Method = request.HttpMethod,
                            DataTypeMiddleWare = responseDataType,
                            ByteSize = requestBytes.Length,
                            StatusCode = (int)context.Response.StatusCode,
                            DataResponse = responseBody,
                        });
                    });
                }

                var response = context.Response;
                response.StatusCode = (int)responseMessage.StatusCode;
                response.ContentType = responseMessage.Content.Headers.ContentType?.ToString();
                response.ContentLength64 = responseBytes.Length;

                await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                //logEntry.ResponseBody = $"[HTTP Proxy ERR] {ex.Message}";
                //logEntry.StatusCode = -1;
            }

        }

        #endregion

        #region TCP Proxy

        private void StartTcpProxy(CancellationToken token)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Loopback, PROXY_PORT);
                _tcpListener.Start();

                Task.Run(() => ListenForTcpConnections(token), token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP Proxy ERR] {ex.Message}");
            }
        }

        private async Task ListenForTcpConnections(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _tcpListener!.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleTcpConnection(client, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TCP Accept ERR] {ex.Message}");
                }
            }
        }

        private async Task HandleTcpConnection(TcpClient client, CancellationToken token)
        {
            try
            {
                using (client)
                using (var server = new TcpClient())
                {
                    await server.ConnectAsync(IPAddress.Loopback, REAL_SERVER_PORT, token);
                    using var clientStream = client.GetStream();
                    using var serverStream = server.GetStream();

                    var c2s = RelayDataAsync(clientStream, serverStream, "Client", token);
                    var s2c = RelayDataAsync(serverStream, clientStream, "Server", token);

                    await Task.WhenAny(c2s, s2c);
                }
            }
            catch (Exception ex)
            {
                var log = new LoggedRequest
                {
                    Method = "TCP",
                    Url = "Connection Error",
                    RequestBody = ex.Message,
                    StatusCode = -1
                };
                AddRequestLog(log);
            }
        }

        private async Task RelayDataAsync(NetworkStream from, NetworkStream to, string direction, CancellationToken token)
        {
            var buffer = new byte[8192];
            int read = 0;
            while ((read = await from.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await to.WriteAsync(buffer, 0, read, token);
                var data = Encoding.UTF8.GetString(buffer, 0, read);
                var dataType = DataInspector.DetecDataType(buffer.Take(read).ToArray());
                var byteSize = read;

                // Write OutputServers/OutputClient
                if (Recorder != null && Recorder.InputClients.Any())
                {
                    var stage = Recorder.InputClients.Last().Stage;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (direction.Contains("Client"))
                        {
                            var existingOutputServer = Recorder.OutputServers.LastOrDefault(s => s.Stage == stage);
                            if (existingOutputServer != null)
                            {
                                existingOutputServer.Method = "TCP";
                                existingOutputServer.DataRequest += data;
                                existingOutputServer.ByteSize += byteSize;
                                existingOutputServer.DataTypeMiddleware = dataType;
                            }
                            else
                            {
                                Recorder.OutputServers.Add(new OutputServer
                                {
                                    Stage = stage,
                                    Method = "TCP",
                                    DataRequest = data,
                                    DataTypeMiddleware = dataType,
                                    ByteSize = byteSize
                                });
                            }
                        }
                        else
                        {
                            var existingOutputClient = Recorder.OutputClients.LastOrDefault(c => c.Stage == stage);
                            if (existingOutputClient != null)
                            {
                                existingOutputClient.Method = "TCP";
                                existingOutputClient.DataResponse += data;
                                existingOutputClient.ByteSize += byteSize;
                                existingOutputClient.DataTypeMiddleWare = dataType;
                            
                            }
                            else
                            {
                                Recorder.OutputClients.Add(new OutputClient
                                {
                                    Stage = stage,
                                    Method = "TCP",
                                    DataResponse = data,
                                    DataTypeMiddleWare = dataType,
                                    ByteSize = byteSize
                                });
                            }
                        }
                    });
                }
            }
        }

        #endregion

        #region Helper

        private void AddRequestLog(LoggedRequest log)
        {
            AppendToFile(log);
            Application.Current.Dispatcher.Invoke(() =>
            {
            });
        }

        private void AppendToFile(LoggedRequest log)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "middleware_log.txt");
                File.AppendAllText(logPath,
                    $"{DateTime.Now:O} | {log.Method} | {log.Url} | {log.StatusCode}\n{log.RequestBody}\n----\n");
            }
            catch { /* ignore */ }
        }

        #endregion
    }
}
