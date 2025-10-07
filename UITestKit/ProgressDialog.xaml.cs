using System;
using System.Threading.Tasks;
using System.Windows;

namespace UITestKit
{
    public partial class ProgressDialog : Window
    {
        public ProgressDialog(string message, int durationMs = 3000)
        {
            InitializeComponent();
            txtStatus.Text = message;
            _ = RunProgress(durationMs);
        }

        private async Task RunProgress(int durationMs)
        {
            int steps = 50; // số lần cập nhật thanh tiến trình
            int delay = durationMs / steps;

            for (int i = 0; i <= steps; i++)
            {
                progressBar.Value = (i / (double)steps) * 100;
                txtCountdown.Text = $"Còn lại: {(durationMs - i * delay) / 1000.0:F1} giây";
                await Task.Delay(delay);
            }

            this.DialogResult = true;
            this.Close();
        }
    }
}
