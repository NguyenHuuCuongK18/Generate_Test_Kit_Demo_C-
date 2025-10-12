using System.Text;
using System.Windows;
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
        public RecorderWindow Recorder { get;set; }

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
            Recorder?.AddActionStage("ClientClose");
            await _manager.StopClientAsync();
            this.Close();
        }
    }
}
