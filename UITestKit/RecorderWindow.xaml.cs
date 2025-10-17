using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using UITestKit.MiddlewareHandling;
using UITestKit.Model;
using UITestKit.Service;
using UITestKit.ServiceExcute;
using MessageBox = System.Windows.MessageBox;

namespace UITestKit
{
    public partial class RecorderWindow : Window, INotifyPropertyChanged
    {
        private readonly ExecutableManager _manager;
        private readonly MiddlewareStart _middlewareStart = MiddlewareStart.Instance;
        private readonly HashSet<string> _ignoreTexts = new HashSet<string>();

        // 🔑 REMOVED: private int _stepCounter = 0;
        // Stage counter sẽ được tính động dựa trên StageKeys hiện có

        private int _selectedStageKey = -1;
        private TestStage _selectedStageData = new TestStage();
        private bool _isDisposed = false;

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

            // Subscribe events với named methods để có thể unsubscribe sau
            _manager.ClientOutputReceived += OnClientOutputReceived;
            _manager.ServerOutputReceived += OnServerOutputReceived;
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

        #region Event Handlers (Named methods để có thể unsubscribe)

        private void OnClientOutputReceived(string data)
        {
            if (_isDisposed) return;
            Dispatcher.Invoke(() => HandleProcessOutput(true, data));
        }

        private void OnServerOutputReceived(string data)
        {
            if (_isDisposed) return;
            Dispatcher.Invoke(() => HandleProcessOutput(false, data));
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

        /// <summary>
        /// 🔑 FIXED: Tính stage number động dựa trên StageKeys hiện có
        /// </summary>
        public void AddActionStage(string action, string input = "", string dataType = "")
        {
            if (_isDisposed) return;

            // 🔑 Tính next stage number dựa trên StageKeys hiện có
            int nextStageNumber = GetNextStageNumber();

            var testStage = new TestStage();
            testStage.InputClients.Add(new Input_Client
            {
                Stage = nextStageNumber,
                Input = input,
                DataType = dataType,
                Action = action
            });

            TestStages[nextStageNumber] = testStage;
            StageKeys.Add(nextStageNumber);
            SelectedStageKey = nextStageNumber;

            OnPropertyChanged(nameof(TestStages));
            OnPropertyChanged(nameof(StageKeys));
            OnPropertyChanged(nameof(SelectedStageKey));

            System.Diagnostics.Debug.WriteLine($"[RecorderWindow] Added Stage {nextStageNumber} - Action: {action}");
        }

        /// <summary>
        /// 🔑 NEW METHOD: Tính next stage number
        /// Nếu có stage bị xóa ở giữa, sẽ fill vào gap đó
        /// </summary>
        private int GetNextStageNumber()
        {
            // Nếu chưa có stage nào, return 1
            if (!StageKeys.Any())
            {
                return 1;
            }

            // Tìm gap (khoảng trống) trong stage numbering
            var sortedKeys = StageKeys.OrderBy(k => k).ToList();

            for (int i = 0; i < sortedKeys.Count; i++)
            {
                int expectedStage = i + 1;
                int actualStage = sortedKeys[i];

                // Nếu tìm thấy gap, return số đó
                if (actualStage != expectedStage)
                {
                    System.Diagnostics.Debug.WriteLine($"[RecorderWindow] Found gap at stage {expectedStage}");
                    return expectedStage;
                }
            }

            // Nếu không có gap, return max + 1
            int nextStage = sortedKeys.Max() + 1;
            System.Diagnostics.Debug.WriteLine($"[RecorderWindow] No gap found, next stage: {nextStage}");
            return nextStage;
        }

        #endregion

        #region HandleProcessOutput
        private void HandleProcessOutput(bool isClient, string data)
        {
            if (_isDisposed) return;
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
            if (_isDisposed) return;

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

        /// <summary>
        /// 🔑 ENHANCED: Xóa stage và tất cả data liên quan
        /// Stage numbering sẽ được reuse cho stage mới
        /// </summary>
        private void BtnDeleteStage_Click(object sender, RoutedEventArgs e)
        {
            if (_isDisposed)
            {
                MessageBox.Show("Cannot delete stage - RecorderWindow is disposed", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (SelectedStageKey <= 0 || !TestStages.ContainsKey(SelectedStageKey))
            {
                MessageBox.Show("Please select a valid stage to delete.", "No Stage Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Không cho phép xóa nếu chỉ còn 1 stage
            if (TestStages.Count <= 1)
            {
                MessageBox.Show("Cannot delete the last stage. At least one stage must remain.", "Delete Not Allowed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                int stageToDelete = SelectedStageKey;
                var stageData = TestStages[stageToDelete];

                // 🔑 Hiển thị thông tin stage sẽ bị xóa
                int inputCount = stageData.InputClients.Count;
                int outputClientCount = stageData.OutputClients.Count;
                int outputServerCount = stageData.OutputServers.Count;

                var result = MessageBox.Show(
                    $"Delete Stage {stageToDelete}?\n\n" +
                    $"This will permanently delete:\n" +
                    $"• {inputCount} Input Client(s)\n" +
                    $"• {outputClientCount} Output Client(s)\n" +
                    $"• {outputServerCount} Output Server(s)\n\n" +
                    $"Stage {stageToDelete} will become available for new stages.",
                    "Confirm Delete Stage",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    TestStages.Remove(stageToDelete);

                    StageKeys.Remove(stageToDelete);

                    System.Diagnostics.Debug.WriteLine($"[RecorderWindow] Deleted Stage {stageToDelete}");
                    System.Diagnostics.Debug.WriteLine($"[RecorderWindow] - Deleted {inputCount} InputClients");
                    System.Diagnostics.Debug.WriteLine($"[RecorderWindow] - Deleted {outputClientCount} OutputClients");
                    System.Diagnostics.Debug.WriteLine($"[RecorderWindow] - Deleted {outputServerCount} OutputServers");
                    System.Diagnostics.Debug.WriteLine($"[RecorderWindow] - Stage {stageToDelete} will be reused for next new stage");

                    if (StageKeys.Count > 0)
                    {
                        int newSelectedStage;

                        var higherStages = StageKeys.Where(k => k > stageToDelete).OrderBy(k => k).ToList();
                        if (higherStages.Any())
                        {
                            newSelectedStage = higherStages.First();
                        }
                        else
                        {
                            newSelectedStage = StageKeys.OrderByDescending(k => k).First();
                        }

                        SelectedStageKey = newSelectedStage;
                    }
                    else
                    {
                        SelectedStageData = new TestStage();
                        SelectedStageKey = -1;
                    }

                    // 🔑 STEP 5: Notify UI update
                    OnPropertyChanged(nameof(TestStages));
                    OnPropertyChanged(nameof(StageKeys));
                    OnPropertyChanged(nameof(SelectedStageData));
                    OnPropertyChanged(nameof(SelectedStageKey));

                    MessageBox.Show(
                        $"Stage {stageToDelete} deleted successfully.\n\n" +
                        $"Remaining stages: {StageKeys.Count}\n" +
                        $"Next new stage will be: {GetNextStageNumber()}",
                        "Stage Deleted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error deleting stage:\n{ex.Message}",
                    "Delete Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"[RecorderWindow] Delete stage error: {ex}");
            }
        }

        private void CmbStages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDisposed) return;
            if (sender is not ComboBox comboBox || comboBox.SelectedItem is not int selectedKey)
                return;

            SelectedStageKey = selectedKey;
        }

        #endregion

        #region Cleanup & Dispose

        /// <summary>
        /// 🔑 FIXED: Cleanup KHÔNG clear data
        /// Chỉ unsubscribe events và clear middleware reference
        /// GIỮ NGUYÊN TestStages và StageKeys để có thể xem lại
        /// </summary>
        public void Cleanup()
        {
            if (_isDisposed) return;

            _isDisposed = true;

            try
            {
                // Unsubscribe events từ ExecutableManager
                _manager.ClientOutputReceived -= OnClientOutputReceived;
                _manager.ServerOutputReceived -= OnServerOutputReceived;

                // Clear middleware reference
                if (_middlewareStart.Recorder == this)
                {
                    _middlewareStart.Recorder = null;
                }

                System.Diagnostics.Debug.WriteLine($"[RecorderWindow] Cleanup completed - events unsubscribed, data preserved");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecorderWindow] Cleanup error: {ex.Message}");
            }
        }

        #endregion
    }
}