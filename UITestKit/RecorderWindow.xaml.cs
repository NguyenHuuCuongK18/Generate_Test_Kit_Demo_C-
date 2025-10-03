// UITestKit/RecorderWindow.xaml.cs
using System.ComponentModel;
using System.Windows;
using UITestKit.Model;
using UITestKit.ServiceExcute;

namespace UITestKit
{
    public partial class RecorderWindow : Window
    {
        private readonly ExecutableManager _manager;
        private int _stepCounter = 0;

        public BindingList<TestStep> Steps { get; } = new BindingList<TestStep>();

        public RecorderWindow(ExecutableManager manager)
        {
            InitializeComponent();
            DataContext = this;

            _manager = manager;

            // Subscribe sự kiện TRƯỚC khi start process để không bị miss output ban đầu
            _manager.ClientOutputReceived += data => Dispatcher.Invoke(() => HandleProcessOutput(isClient: true, data));
            _manager.ServerOutputReceived += data => Dispatcher.Invoke(() => HandleProcessOutput(isClient: false, data));
        }

        private void BtnSendInput_Click(object sender, RoutedEventArgs e)
        {
            string input = txtClientInput.Text.Trim();
            if (!string.IsNullOrEmpty(input))
            {
                // tạo step mới khi user gửi input
                AddStep(clientInput: input);
                _manager.SendClientInput(input);
                txtClientInput.Clear();
            }
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            var exporter = new ExcelExporter();
            exporter.ExportToExcel("TestCases.xlsx", Steps.ToList());
            MessageBox.Show("Exported to TestCases.xlsx");
        }

        private void AddStep(string? clientInput = null, string? clientOutput = null, string? serverOutput = null)
        {
            _stepCounter++;
            Steps.Add(new TestStep
            {
                StepNumber = _stepCounter,
                ClientInput = clientInput ?? "",
                ClientOutput = clientOutput ?? "",
                ServerOutput = serverOutput ?? ""
            });
        }

        /// <summary>
        /// Tìm step phù hợp để cập nhật output. Nếu không tìm thấy -> tạo step mới.
        /// </summary>
        private void HandleProcessOutput(bool isClient, string data)
        {
            // Luôn lấy step cuối cùng để append (không tạo mới khi output xuất hiện)
            TestStep stepToUpdate = Steps.LastOrDefault();

            if (stepToUpdate == null)
            {
                // Nếu chưa có step nào (ví dụ process in ra trước khi client nhập gì)
                AddStep(
                    clientInput: null,
                    clientOutput: isClient ? data : null,
                    serverOutput: !isClient ? data : null
                );
            }
            else
            {
                if (isClient)
                    stepToUpdate.ClientOutput = AppendWithNewLine(stepToUpdate.ClientOutput, data);
                else
                    stepToUpdate.ServerOutput = AppendWithNewLine(stepToUpdate.ServerOutput, data);

                var index = Steps.IndexOf(stepToUpdate);
                if (index >= 0) Steps.ResetItem(index); // refresh UI
            }
        }

        private string AppendWithNewLine(string existing, string addition)
        {
            if (string.IsNullOrWhiteSpace(existing)) return addition;
            return existing + System.Environment.NewLine + addition;
        }
    }
}
