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

        protected override async void OnExit(ExitEventArgs e)
        {
            // Gọi StopBoth() khi đóng app
            await ExecutableManager.Instance.StopAllAsync();
            MiddlewareStart.Instance.Stop();
            base.OnExit(e);
        }
    }

}
