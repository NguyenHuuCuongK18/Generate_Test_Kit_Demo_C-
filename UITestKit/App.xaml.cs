using System.Configuration;
using System.Data;
using System.Windows;
using UITestKit.ServiceExcute;

namespace UITestKit
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static ExecutableManager? _executableManager;

        // Hàm khởi tạo ExecutableManager ở đâu đó trong app (ví dụ khi MainWindow mở)
        public static void SetExecutableManager(ExecutableManager manager)
        {
            _executableManager = manager;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Gọi StopBoth() khi đóng app
            _executableManager?.StopAll();
            base.OnExit(e);
        }
    }

}
