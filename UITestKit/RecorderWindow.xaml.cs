// UITestKit/RecorderWindow.xaml.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using UITestKit.MiddlewareHandling;
using UITestKit.Model;
using UITestKit.Service;
using UITestKit.ServiceExcute;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace UITestKit
{
    public partial class RecorderWindow : Window, INotifyPropertyChanged
    {
        private readonly ExecutableManager _manager;
        private readonly MiddlewareStart _middlewareStart = MiddlewareStart.Instance;
        private readonly HashSet<string> _ignoreTexts = new HashSet<string>();

        private int _stepCounter = 0;
        private int _selectedStageKey = -1;
        private TestStage _selectedStageData = new TestStage();

        public Dictionary<int, TestStage> TestStages { get; } = new Dictionary<int, TestStage>();
        public ObservableCollection<int> StageKeys { get; } = new ObservableCollection<int>();

        public TestStage SelectedStageData
        {
            get => _selectedStageData;
            set
            {
                if (_selectedStageData != value)
                {
                    _selectedStageData = value;
                    OnPropertyChanged(nameof(SelectedStageData));
                }
            }
        }

        public int SelectedStageKey
        {
            get => _selectedStageKey;
            set
            {
                if (_selectedStageKey != value)
                {
                    _selectedStageKey = value;
                    OnPropertyChanged(nameof(SelectedStageKey));

                    if (TestStages.TryGetValue(_selectedStageKey, out var stage))
                    {
                        SelectedStageData = stage;
                    }
                }
            }
        }

        public RecorderWindow(ExecutableManager manager, string path)
        {
            InitializeComponent();
            DataContext = this;

            _manager = manager;
            InitializeIgnoreList();

            // Subscribe events
            _manager.ClientOutputReceived += data => Dispatcher.Invoke(() => HandleProcessOutput(true, data));
            _manager.ServerOutputReceived += data => Dispatcher.Invoke(() => HandleProcessOutput(false, data));
            _middlewareStart.Recorder = this;

            // Launch console windows
            new ClientConsoleWindow { Recorder = this }.Show();
            new ServerConsoleWindow { Recorder = this }.Show();

            // Add initial stage and select it
            AddActionStage("Connect");
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Ignore List Handling
        private void InitializeIgnoreList()
        {
            try
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string projectRootPath = Path.GetFullPath(Path.Combine(baseDirectory, @"..\..\..\.."));
                string filePath = Path.Combine(projectRootPath, "Ignore.xlsx");

                var ignoreList = IgnoreListLoader.IgnoreLoader(filePath);
                if (ignoreList != null)
                {
                    foreach (var item in ignoreList)
                    {
                        _ignoreTexts.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể load file ignore: {ex.Message}", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool ShouldIgnore(string line)
        {
            if (string.IsNullOrEmpty(line) || _ignoreTexts.Count == 0)
                return false;

            return _ignoreTexts.Any(ignore => line.Contains(ignore, StringComparison.OrdinalIgnoreCase));
        }
        #endregion

        #region Stage Management
        public void AddActionStage(string action, string input = "", string dataType = "")
        {
            _stepCounter++;

            var testStage = new TestStage();
            testStage.InputClients.Add(new Input_Client
            {
                Stage = _stepCounter,
                Input = input,
                DataType = dataType,
                Action = action
            });

            TestStages[_stepCounter] = testStage;
            StageKeys.Add(_stepCounter);
            SelectedStageKey = _stepCounter;
        }
        #endregion

        #region HandleProcessOutput
        private void HandleProcessOutput(bool isClient, string data)
        {
            if (!TestStages.Any()) return;
            if (ShouldIgnore(data)) return;

            var currentStage = TestStages.Keys.Max();
            if (!TestStages.ContainsKey(currentStage)) return;

            var testStage = TestStages[currentStage];

            if (isClient)
            {
                AppendOutput(
                    testStage.OutputClients,
                    currentStage,
                    data,
                    () => new OutputClient { Stage = currentStage }
                );
            }
            else
            {
                AppendOutput(
                    testStage.OutputServers,
                    currentStage,
                    data,
                    () => new OutputServer { Stage = currentStage }
                );
            }

            OnPropertyChanged(nameof(SelectedStageData));
        }

        private void AppendOutput<T>(
            ObservableCollection<T> collection,
            int stage,
            string data,
            Func<T> createNew) where T : class
        {
            var existingItem = collection.LastOrDefault(item =>
            {
                var stageProp = item.GetType().GetProperty("Stage");
                return stageProp != null && (int)stageProp.GetValue(item) == stage;
            });

            if (existingItem != null)
            {
                var outputProp = existingItem.GetType().GetProperty("Output");
                if (outputProp != null)
                {
                    var currentOutput = (string)outputProp.GetValue(existingItem);

                    var newOutput = string.IsNullOrEmpty(currentOutput)
                        ? data
                        : currentOutput + "\n" + data;

                    outputProp.SetValue(existingItem, newOutput);
                }
            }
            else
            {
                var newItem = createNew();
                var outputProp = newItem.GetType().GetProperty("Output");
                outputProp?.SetValue(newItem, data);
                collection.Add(newItem);
            }
        }
        #endregion

        #region Button Click Handlers
        private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var exporter = new ExcelExporter();
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string projectRootPath = Path.GetFullPath(Path.Combine(baseDirectory, @"..\..\..\.."));
                string pathExport = Path.Combine(projectRootPath, "TestResult.xlsx");

                // TODO: Implement export logic
                // exporter.ExportToExcel(pathExport, TestStages);

                await CloseAllAsync();
                MessageBox.Show("Exported to TestResult.xlsx", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnCloseAll_Click(object sender, RoutedEventArgs e)
        {
            await CloseAllAsync();
        }

        private void BtnAddStage_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement add stage dialog
            AddActionStage("New Action");
        }

        private void BtnUpdateStage_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement update stage logic
            if (SelectedStageKey > 0)
            {
                OnPropertyChanged(nameof(SelectedStageData));
            }
        }

        private void BtnDeleteStage_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedStageKey <= 0 || !TestStages.ContainsKey(SelectedStageKey))
                return;

            var result = MessageBox.Show($"Delete Stage {SelectedStageKey}?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                TestStages.Remove(SelectedStageKey);
                StageKeys.Remove(SelectedStageKey);

                // Select next available stage
                if (StageKeys.Count > 0)
                {
                    SelectedStageKey = StageKeys[0];
                }
            }
        }

        private void CmbStages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox || comboBox.SelectedItem is not int selectedKey)
                return;

            SelectedStageKey = selectedKey;
        }
        #endregion

        #region Cleanup
        private async Task CloseAllAsync()
        {
            try
            {
                await _manager.StopAllAsync();
                await _middlewareStart.StopAsync();

                // Close all windows except MainWindow
                var windowsToClose = Application.Current.Windows
                    .Cast<Window>()
                    .Where(w => w is not MainWindow)
                    .ToList();

                foreach (var window in windowsToClose)
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error closing windows: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion
    }
}