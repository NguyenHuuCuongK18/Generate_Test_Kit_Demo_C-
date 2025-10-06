using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using UITestKit.Model;
using System.Threading;
using System.Threading.Tasks;
using UITestKit.Service;

namespace UITestKit.MiddlewareHandling
{
    public class MiddlewareStart
    {
        private CancellationTokenSource? _cts;
        private bool _isSessionRunning;

        private HttpListener? _httpListener;
        private TcpListener? _tcpListener;

        private const int PROXY_PORT = 5000;       // Port client kết nối đến
        private const int REAL_SERVER_PORT = 5001; // Port server thực

        public ObservableCollection<LoggedRequest> LoggedRequests { get; } = new();

        public MiddlewareStart() { }

        #region Start / Stop

        public async Task StartAsync(bool useHttp = true)
        {
            if (_isSessionRunning) return;

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

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _isSessionRunning = false;

                _httpListener?.Stop();
                _tcpListener?.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MiddlewareStop ERR] {ex.Message}");
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
            var logEntry = new LoggedRequest
            {
                Method = request.HttpMethod,
                Url = request.Url?.ToString() ?? ""
            };

            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                logEntry.RequestBody = await reader.ReadToEndAsync();
            }

            try
            {
                var realServerUrl = $"http://localhost:{REAL_SERVER_PORT}{request.Url?.AbsolutePath}";
                using var client = new HttpClient();
                var forwardRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), realServerUrl);

                if (!string.IsNullOrEmpty(logEntry.RequestBody))
                {
                    forwardRequest.Content = new StringContent(
                        logEntry.RequestBody,
                        Encoding.UTF8,
                        request.ContentType ?? "application/json"
                    );
                }

                var responseMessage = await client.SendAsync(forwardRequest);
                var responseBytes = await responseMessage.Content.ReadAsByteArrayAsync();

                logEntry.StatusCode = (int)responseMessage.StatusCode;
                logEntry.ResponseBody = Encoding.UTF8.GetString(responseBytes);

                var response = context.Response;
                response.StatusCode = (int)responseMessage.StatusCode;
                response.ContentType = responseMessage.Content.Headers.ContentType?.ToString();
                response.ContentLength64 = responseBytes.Length;

                await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                logEntry.ResponseBody = $"[HTTP Proxy ERR] {ex.Message}";
                logEntry.StatusCode = -1;
            }

            AddRequestLog(logEntry);
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

                    var c2s = RelayDataAsync(clientStream, serverStream, "Client -> Server", token);
                    var s2c = RelayDataAsync(serverStream, clientStream, "Server -> Client", token);

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
            int read;
            while ((read = await from.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await to.WriteAsync(buffer, 0, read, token);
                var dataType = DataInspector.DetecDataType(buffer.Take(read).ToArray());

                var data = Encoding.UTF8.GetString(buffer, 0, read);
                var entry = new LoggedRequest
                {
                    Method = direction,
                    Url = data.Length > 100 ? data[..100] + "..." : data,
                    DataType = dataType,
                    RequestBody = data,
                    StatusCode = read
                };

                AddRequestLog(entry);
            }
        }

        #endregion

        #region Helper

        private void AddRequestLog(LoggedRequest log)
        {
            AppendToFile(log);
            Application.Current.Dispatcher.Invoke(() =>
            {
                LoggedRequests.Add(log);
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
