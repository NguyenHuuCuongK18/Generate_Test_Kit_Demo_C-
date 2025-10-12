// UITestKit/RecorderWindow.xaml.cs
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using UITestKit.MiddlewareHandling;
using UITestKit.Model;
using UITestKit.Service;
using UITestKit.ServiceExcute;
using UITestKit.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace UITestKit
{
    public partial class RecorderWindow : Window
    {
        private readonly ExecutableManager _manager = ExecutableManager.Instance;
        private int _stepCounter = 0;
        private string path = string.Empty;
        private readonly MiddlewareStart _middlewareStart = MiddlewareStart.Instance;
        private HashSet<string> _ignoreTexts = new HashSet<string>();

        public BindingList<Input_Client> InputClients { get; } = new BindingList<Input_Client>();
        public BindingList<OutputClient> OutputClients { get; } = new BindingList<OutputClient>();
        public BindingList<OutputServer> OutputServers { get; } = new BindingList<OutputServer>();

        public RecorderWindow(ExecutableManager manager, string path)
        {
            InitializeComponent();
            DataContext = this;
            _manager = manager;
            AddActionStage("Connect");
            InitializeIgnoreList();
            // Subscribe sự kiện TRƯỚC khi start process để không bị miss output ban đầu
            _manager.ClientOutputReceived += data => Dispatcher.Invoke(() => HandleProcessOutput(isClient: true, data));
            _manager.ServerOutputReceived += data => Dispatcher.Invoke(() => HandleProcessOutput(isClient: false, data));

            _middlewareStart.Recorder = this;

            // launch console windows
            var clientConsole = new ClientConsoleWindow();
            clientConsole.Recorder = this;
            clientConsole.Show();

            var serverConsole = new ServerConsoleWindow();
            serverConsole.Recorder = this;
            serverConsole.Show();
        }

        #region load Ignore and check should be ignore
        private void InitializeIgnoreList()
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
        #endregion

        #region create action without Input from client
        // create action without Input from client
        public void AddActionStage(string action, string input = "", string dataType = "")
        {
            _stepCounter++;
            InputClients.Add(new Input_Client
            {
                Stage = _stepCounter,
                Input = input,
                DataType = dataType,
                Action = action
            });

        }
        #endregion

        private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            var exporter = new ExcelExporter();
            string pathExport = Path.Combine(path, "TestResult.xlsx");
            exporter.ExportToExcelParams(pathExport,
                    ("Input_Client", InputClients.Cast<object>().ToList()),
                    ("Output_Client", OutputClients.Cast<object>().ToList()),
                    ("Output_Server", OutputServers.Cast<object>().ToList())
                );
            //BtnCloseAll_Click(sender, e);
            MessageBox.Show("Exported to TestCases.xlsx");
        }

        private async void BtnCloseAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _manager.StopAllAsync();
                _middlewareStart.StopAsync();
                // Lặp qua tất cả các cửa sổ đang mở
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is not MainWindow)
                    {
                        window.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Đã xảy ra lỗi khi đóng cửa sổ: {ex.Message}",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Tìm step phù hợp để cập nhật output. Nếu không tìm thấy -> tạo step mới.
        /// </summary>
        ///
        #region HandleProcessOutput
        private void HandleProcessOutput(bool isClient, string data)
        {
            if (InputClients.Count == 0) return;
            if (ShouldIgnore(data)) return;
            var currentStage = InputClients.Last().Stage;

            if (isClient)
            {
                var outputClientProcess = OutputClients.LastOrDefault(client => client.Stage == currentStage);
                if (outputClientProcess != null)
                {
                    outputClientProcess.Output += data + "\n";
                }
                else
                {
                    outputClientProcess = new OutputClient
                    {
                        Stage = currentStage,
                        Output = data + "\n"
                    };
                    OutputClients.Add(outputClientProcess);
                }
            }
            else
            {
                var outServerProcess = OutputServers.LastOrDefault(server => server.Stage == currentStage);
                if (outServerProcess != null)
                {
                    outServerProcess.Output += data + "\n";

                }
                else
                {
                    outServerProcess = new OutputServer
                    {
                        Stage = currentStage,
                        Output = data + "\n"
                    };
                    OutputServers.Add(outServerProcess);
                }
            }

        }
        #endregion
    }
}
