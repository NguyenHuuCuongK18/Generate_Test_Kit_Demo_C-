using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using System.Windows;
using UITestKit.MiddlewareHandling;
using UITestKit.ServiceExcute;

namespace UITestKit
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            this.Exit += App_Exit;
        }
        private void App_Exit(object sender, ExitEventArgs e)
        {
            // Gọi phương thức StopAll đồng bộ để đảm bảo client và server
            // được dừng hoàn toàn trước khi ứng dụng thoát.
            ExecutableManager.Instance.StopAll();
        }
    }

}
