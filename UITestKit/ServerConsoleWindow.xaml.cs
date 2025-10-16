using System.Threading.Tasks;
using System.Windows;
using UITestKit.ServiceExcute;

namespace UITestKit
{
    /// <summary>
    /// Interaction logic for ServerConsoleWindow.xaml
    /// </summary>
    public partial class ServerConsoleWindow : Window
    {
        private readonly ExecutableManager _manager = ExecutableManager.Instance;
        public RecorderWindow Recorder { get;set; }
        public ServerConsoleWindow()
        {
            InitializeComponent();
            _manager.ServerOutputReceived += data => Dispatcher.Invoke(()
                => txtOutput.AppendText(data + "\n"));
        }

        private async void BtnEndServer_Click(object sender, RoutedEventArgs e)
        {
            Recorder?.AddActionStage("ServerClose");
            await _manager.StopServerAsync();
        }

        private  void BtnStartServerAgain_Click(object sender, RoutedEventArgs e)
        {
             _manager.StartServer();
        }
    }
}
