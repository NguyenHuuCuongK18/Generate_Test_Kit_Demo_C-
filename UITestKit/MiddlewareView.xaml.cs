using System.Windows;
using UITestKit.MiddlewareHandling;

namespace UITestKit.Views
{
    public partial class MiddlewareView : Window
    {
        public MiddlewareView(MiddlewareStart middlewareInstance)
        {
            InitializeComponent();
            DataContext = middlewareInstance;
        }
    }
}
