using System.Threading.Tasks;
using System.Windows;
using UITestKit.MiddlewareHandling;
using UITestKit.ServiceExcute;

namespace UITestKit
{
    /// <summary>
    /// Interaction logic for ServerConsoleWindow.xaml
    /// </summary>
    public partial class ServerConsoleWindow : Window
    {
        private readonly ExecutableManager _manager = ExecutableManager.Instance;
        private readonly MiddlewareStart _middlewareStart = MiddlewareStart.Instance;
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
            await _middlewareStart.StopAsync();
            await _manager.StopServerAsync();
        }

        private  async void BtnStartServerAgain_Click(object sender, RoutedEventArgs e)
        {
            Recorder?.AddActionStage("StartServer");
            await _middlewareStart.StartAsync();
             _manager.StartServer();
        }
    }
}
