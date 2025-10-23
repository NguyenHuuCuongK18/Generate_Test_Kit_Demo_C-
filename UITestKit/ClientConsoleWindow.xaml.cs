using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UITestKit.MiddlewareHandling;
using UITestKit.Service;
using UITestKit.ServiceExcute;

namespace UITestKit
{
    /// <summary>
    /// Interaction logic for ClientConsoleWindow.xaml
    /// </summary>
    public partial class ClientConsoleWindow : Window
    {
        private readonly ExecutableManager _manager = ExecutableManager.Instance;
        private readonly MiddlewareStart _middleware = MiddlewareStart.Instance;
        public RecorderWindow Recorder { get; set; }

        public ClientConsoleWindow()
        {
            InitializeComponent();
            _manager.ClientOutputReceived += data
                => Dispatcher.Invoke(() => txtOutput.AppendText(data + "\n"));
        }
        private void BtnSendInput_Click(object sender, RoutedEventArgs e)
        {
            string input = txtClientInput.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            var dataType = DataInspector.DetecDataType(Encoding.UTF8.GetBytes(input));
            Recorder?.AddActionStage("Client Input", input, dataType);

            _manager.SendClientInput(input);
            txtClientInput.Clear();
        }

        private async void BtnEndClient_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Recorder?.AddActionStage("ClientClose");

                await _manager.StopClientAsync();
                await _middleware.StopAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi dừng Client: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);

            }
        }

        private void BtnStartClientAgain_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Recorder?.AddActionStage("StartClient");
                _manager.StartClient();
                _middleware.StartAsync();
                MessageBox.Show("Client đã được khởi động lại thành công!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi khởi động lại Client: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
        }
    }
}
