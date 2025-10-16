using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
            var endButton = (Button)sender;
            try
            {
                endButton.IsEnabled = false;
                txtClientInput.IsEnabled = false;
                Recorder?.AddActionStage("ClientClose");
                await _manager.StopClientAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi dừng Client: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);

                // Nếu có lỗi, bật lại các control để người dùng thử lại
                endButton.IsEnabled = true;
                txtClientInput.IsEnabled = true;
            }
        }

        private void BtnStartClientAgain_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                 _manager.StartClient();
                MessageBox.Show("Client đã được khởi động lại thành công!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi khởi động lại Client: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
        }
    }
}
