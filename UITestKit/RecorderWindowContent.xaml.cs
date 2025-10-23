using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UITestKit.Model;
using UITestKit.ServiceExcute;
using UITestKit.MiddlewareHandling;

namespace UITestKit
{
    public partial class RecorderWindowContent : UserControl
    {
        public string TabId { get; private set; }
        public event Action<object> OnDataChanged;
        public event Action<string> OnTestKitClosed;

        private RecorderWindow _recorderWindow;
        private ExecutableManager _manager = ExecutableManager.Instance;
        private MiddlewareStart _middlewareStart = MiddlewareStart.Instance;
        private ConfigModel _config;
        private MainDashboard _dashboard;
        private bool _isRunning = false;
        private bool _isMiddlewareOwner = false;
        private bool _isDisposed = false;
        private bool _hasUnsavedChanges = false;
        private bool _isReadOnly = false; // 🔑 NEW
        private string _lastExportPath = "";

        /// <summary>
        /// 🔑 UPDATED: Constructor với read-only mode support
        /// </summary>
        public RecorderWindowContent(string tabId, ConfigModel config, MainDashboard dashboard, bool isReadOnly = false)
        {
            InitializeComponent();
            TabId = tabId;
            _config = config;
            _dashboard = dashboard;
            _isReadOnly = isReadOnly;

            if (_isReadOnly)
            {
                InitializeReadOnlyMode();
            }
            else
            {
                InitializeRecorder();
            }
        }

        /// <summary>
        /// 🔑 NEW: Initialize in read-only mode (no processes started)
        /// </summary>
        private void InitializeReadOnlyMode()
        {
            try
            {
                StatusText.Text = "📖 Read-Only Mode";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219));

                _dashboard?.LogMessage($"[{TabId}] Initializing in read-only mode...");

                // Create RecorderWindow without starting processes
                _recorderWindow = new RecorderWindow(_manager, _config.SaveLocation);

                // Embed content
                EmbedRecorderWindowContent();

                // Disable all edit buttons
                DisableAllEditButtons();

                _isDisposed = false;
                _isRunning = false;

                _dashboard?.LogMessage($"[{TabId}] ✅ Read-only mode initialized");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                _dashboard?.LogMessage($"[{TabId}] ❌ ERROR: {ex.Message}");
                MessageBox.Show($"Failed to initialize read-only mode:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void InitializeRecorder()
        {
            try
            {
                StatusText.Text = "Initializing Test Kit...";
                _dashboard?.LogMessage($"[{TabId}] Initializing Test Kit...");

                _manager.Init(_config.ClientPath, _config.ServerPath);

                _recorderWindow = new RecorderWindow(_manager, _config.SaveLocation);

                _middlewareStart.Recorder = _recorderWindow;

                _manager.ClientOutputReceived += OnClientOutput;
                _manager.ServerOutputReceived += OnServerOutput;

                EmbedRecorderWindowContent();

                await StartProcessesAsync();

                StatusText.Text = "✅ Test Kit Running";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));

                _isRunning = true;
                _hasUnsavedChanges = false;
                _dashboard?.LogMessage($"[{TabId}] ✅ Test Kit started successfully");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));

                _dashboard?.LogMessage($"[{TabId}] ❌ ERROR: {ex.Message}");
                MessageBox.Show($"Failed to initialize Test Kit:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EmbedRecorderWindowContent()
        {
            try
            {
                if (_recorderWindow.Content is DockPanel recorderContent)
                {
                    _recorderWindow.Content = null;

                    RecorderContentArea.Children.Clear();

                    recorderContent.DataContext = _recorderWindow;

                    RecorderContentArea.Children.Add(recorderContent);

                    _dashboard?.LogMessage($"[{TabId}] RecorderWindow content embedded with DataContext preserved");
                }
                else
                {
                    throw new InvalidOperationException("RecorderWindow content is not a DockPanel");
                }
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ❌ Failed to embed content: {ex.Message}");
                throw;
            }
        }

        #region Event Handlers

        private void OnClientOutput(string data)
        {
            if (_isDisposed) return;
            _dashboard?.LogMessage($"[{TabId}] 📤 Client: {data}");
            _hasUnsavedChanges = true;
        }

        private void OnServerOutput(string data)
        {
            if (_isDisposed) return;
            _dashboard?.LogMessage($"[{TabId}] 📥 Server: {data}");
            _hasUnsavedChanges = true;
        }

        #endregion

        private async Task StartProcessesAsync()
        {
            try
            {
                _dashboard?.LogMessage($"[{TabId}] Starting Server...");
                _manager.StartServer();
                await Task.Delay(1000);

                if (!IsMiddlewareRunning())
                {
                    _dashboard?.LogMessage($"[{TabId}] Starting Middleware ({_config.Protocol})...");
                    bool useHttp = _config.Protocol.Equals("HTTP", StringComparison.OrdinalIgnoreCase);
                    await _middlewareStart.StartAsync(useHttp);
                    _isMiddlewareOwner = true;
                    await Task.Delay(500);
                }
                else
                {
                    _dashboard?.LogMessage($"[{TabId}] Middleware already running, using existing instance");
                    _middlewareStart.Recorder = _recorderWindow;
                }

                _dashboard?.LogMessage($"[{TabId}] Starting Client...");
                _manager.StartClient();
                await Task.Delay(500);

                _dashboard?.LogMessage($"[{TabId}] All processes started");
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ❌ Failed to start processes: {ex.Message}");
                throw;
            }
        }

        private bool IsMiddlewareRunning()
        {
            var field = typeof(MiddlewareStart).GetField("_isSessionRunning",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                return (bool)field.GetValue(_middlewareStart);
            }

            return false;
        }

        public async Task StopAllProcessesAsync()
        {
            if (_isReadOnly) return; // Read-only mode doesn't have processes to stop

            if (!_isRunning || _isDisposed) return;

            try
            {
                _dashboard?.LogMessage($"[{TabId}] ========================================");
                _dashboard?.LogMessage($"[{TabId}] Starting cleanup process...");

                if (_recorderWindow != null)
                {
                    _dashboard?.LogMessage($"[{TabId}] Cleaning up RecorderWindow (preserving data)...");
                    _recorderWindow.Cleanup();
                    _dashboard?.LogMessage($"[{TabId}] ✅ RecorderWindow cleanup completed");
                }

                CloseConsoleWindows();

                DisableEditButtonsOnly();

                if (_manager != null)
                {
                    _dashboard?.LogMessage($"[{TabId}] Unsubscribing RecorderWindowContent events...");
                    _manager.ClientOutputReceived -= OnClientOutput;
                    _manager.ServerOutputReceived -= OnServerOutput;
                    _dashboard?.LogMessage($"[{TabId}] ✅ RecorderWindowContent events unsubscribed");
                }

                if (_manager != null)
                {
                    _dashboard?.LogMessage($"[{TabId}] Stopping Client and Server processes...");
                    await _manager.StopAllAsync();
                    _dashboard?.LogMessage($"[{TabId}] ✅ Client and Server stopped");
                }

                if (_isMiddlewareOwner && _middlewareStart != null)
                {
                    _dashboard?.LogMessage($"[{TabId}] Stopping Middleware (owner)...");
                    await _middlewareStart.StopAsync();
                    _isMiddlewareOwner = false;
                    _dashboard?.LogMessage($"[{TabId}] ✅ Middleware stopped");
                }
                else
                {
                    _dashboard?.LogMessage($"[{TabId}] ⚠️ Keeping Middleware running (not owner)");

                    if (_middlewareStart.Recorder == _recorderWindow)
                    {
                        _middlewareStart.Recorder = null;
                        _dashboard?.LogMessage($"[{TabId}] Cleared Middleware recorder reference");
                    }
                }

                _isDisposed = true;
                _isRunning = false;

                Dispatcher.Invoke(() =>
                {
                    RefreshBindings();
                    _dashboard?.LogMessage($"[{TabId}] ✅ Bindings refreshed");
                });

                StatusText.Text = "⏹ Stopped";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(149, 165, 166));

                _dashboard?.LogMessage($"[{TabId}] ✅ All processes stopped, data and UI preserved");
                _dashboard?.LogMessage($"[{TabId}] ========================================");

                OnTestKitClosed?.Invoke(TabId);
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ❌ ERROR during cleanup: {ex.Message}");
                throw;
            }
        }

        private void CloseConsoleWindows()
        {
            try
            {
                _dashboard?.LogMessage($"[{TabId}] Closing console windows...");

                var windowsToClose = System.Windows.Application.Current.Windows
                    .Cast<Window>()
                    .Where(w => w is ClientConsoleWindow || w is ServerConsoleWindow)
                    .Where(w =>
                    {
                        if (w is ClientConsoleWindow clientConsole)
                        {
                            return clientConsole.Recorder == _recorderWindow;
                        }
                        if (w is ServerConsoleWindow serverConsole)
                        {
                            return serverConsole.Recorder == _recorderWindow;
                        }
                        return false;
                    })
                    .ToList();

                int closedCount = 0;
                foreach (var window in windowsToClose)
                {
                    try
                    {
                        window.Close();
                        closedCount++;
                    }
                    catch (Exception ex)
                    {
                        _dashboard?.LogMessage($"[{TabId}] ⚠️ Failed to close console window: {ex.Message}");
                    }
                }

                _dashboard?.LogMessage($"[{TabId}] ✅ Closed {closedCount} console window(s)");
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ⚠️ Warning: Could not close console windows - {ex.Message}");
            }
        }

        /// <summary>
        /// 🔑 NEW: Disable all buttons in read-only mode
        /// </summary>
        private void DisableAllEditButtons()
        {
            try
            {
                if (BtnStartRecord != null)
                {
                    BtnStartRecord.IsEnabled = false;
                    BtnStartRecord.Opacity = 0.3;
                }

                if (BtnStopRecord != null)
                {
                    BtnStopRecord.IsEnabled = false;
                    BtnStopRecord.Opacity = 0.3;
                }

                if (BtnSave != null)
                {
                    BtnSave.IsEnabled = false;
                    BtnSave.Opacity = 0.3;
                }

                if (RecorderContentArea != null && RecorderContentArea.Children.Count > 0)
                {
                    var content = RecorderContentArea.Children[0] as DockPanel;
                    if (content != null)
                    {
                        DisableSpecificButtons(content);
                        SetDataGridsReadOnly(content);
                    }
                }

                _dashboard?.LogMessage($"[{TabId}] ✅ All edit buttons disabled (read-only mode)");
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ⚠️ Warning: Could not disable edit buttons - {ex.Message}");
            }
        }

        private void DisableEditButtonsOnly()
        {
            try
            {
                _dashboard?.LogMessage($"[{TabId}] Disabling edit buttons only...");

                if (RecorderContentArea != null && RecorderContentArea.Children.Count > 0)
                {
                    var content = RecorderContentArea.Children[0] as DockPanel;
                    if (content != null)
                    {
                        DisableSpecificButtons(content);
                        SetDataGridsReadOnly(content);

                        _dashboard?.LogMessage($"[{TabId}] ✅ Edit buttons disabled, ComboBox and data preserved");
                    }
                }

                if (BtnStartRecord != null)
                {
                    BtnStartRecord.IsEnabled = false;
                    BtnStartRecord.Opacity = 0.5;
                }

                if (BtnStopRecord != null)
                {
                    BtnStopRecord.IsEnabled = false;
                    BtnStopRecord.Opacity = 0.5;
                }

                if (BtnSave != null)
                {
                    BtnSave.Content = "💾 Export Data";
                }

                _dashboard?.LogMessage($"[{TabId}] ✅ Toolbar buttons updated");
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ⚠️ Warning: Could not disable edit buttons - {ex.Message}");
            }
        }

        private void DisableSpecificButtons(DockPanel content)
        {
            try
            {
                var buttons = FindVisualChildren<Button>(content);

                foreach (var button in buttons)
                {
                    string buttonContent = button.Content?.ToString() ?? "";

                    if (buttonContent.Contains("Add Stage") ||
                        buttonContent.Contains("Update Stage") ||
                        buttonContent.Contains("Delete Stage") ||
                        buttonContent.Contains("Submit") ||
                        buttonContent.Contains("Close All"))
                    {
                        button.IsEnabled = false;
                        button.Opacity = 0.5;
                    }
                }
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ⚠️ Warning: Could not disable specific buttons - {ex.Message}");
            }
        }

        private void SetDataGridsReadOnly(DockPanel content)
        {
            try
            {
                var dataGrids = FindVisualChildren<DataGrid>(content);
                foreach (var dataGrid in dataGrids)
                {
                    dataGrid.IsReadOnly = true;
                    dataGrid.CanUserAddRows = false;
                    dataGrid.CanUserDeleteRows = false;
                }
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ⚠️ Warning: Could not set DataGrids read-only - {ex.Message}");
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);

                if (child != null && child is T)
                {
                    yield return (T)child;
                }

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }

        /// <summary>
        /// 🔑 NEW: Load test stages from Dictionary (for read-only mode)
        /// </summary>
        public void LoadTestStages(Dictionary<int, TestStage> testStages)
        {
            if (_recorderWindow == null) return;

            try
            {
                _dashboard?.LogMessage($"[{TabId}] Loading {testStages.Count} stages...");

                _recorderWindow.TestStages.Clear();
                _recorderWindow.StageKeys.Clear();

                foreach (var stageKvp in testStages.OrderBy(s => s.Key))
                {
                    _recorderWindow.TestStages[stageKvp.Key] = stageKvp.Value;
                    _recorderWindow.StageKeys.Add(stageKvp.Key);
                }

                if (_recorderWindow.StageKeys.Count > 0)
                {
                    _recorderWindow.SelectedStageKey = _recorderWindow.StageKeys[0];
                }

                // Force UI refresh
                RefreshBindings();

                _dashboard?.LogMessage($"[{TabId}] ✅ Loaded {testStages.Count} stages");
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ❌ Error loading stages: {ex.Message}");
            }
        }

        private void BtnStartRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
            {
                MessageBox.Show(
                    "Cannot record in read-only mode.\n\nThis TestKit was loaded from a file.",
                    "Read-Only Mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_isDisposed)
            {
                MessageBox.Show(
                    "Test Kit has been stopped.\n\nPlease create a new Test Kit to record new data.",
                    "Test Kit Stopped",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!_isRunning)
            {
                MessageBox.Show("Test Kit is not running!", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_recorderWindow != null)
            {
                string action = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter action name for new stage:",
                    "New Stage",
                    "New Action");

                if (!string.IsNullOrWhiteSpace(action))
                {
                    _recorderWindow.AddActionStage(action);
                    _dashboard?.LogMessage($"[{TabId}] ▶ Started new stage: {action}");

                    StatusText.Text = $"▶ Recording: {action}";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));

                    _hasUnsavedChanges = true;

                    OnDataChanged?.Invoke(new { Action = "StartRecord", TabId = this.TabId, StageName = action });
                }
            }
        }

        private async void BtnStopRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly || _isDisposed) return;

            if (_hasUnsavedChanges)
            {
                var saveWarning = MessageBox.Show(
                    "⚠️ You have unsaved changes!\n\n" +
                    "Would you like to save your test data before stopping?\n\n" +
                    "• Click YES to save and stop\n" +
                    "• Click NO to stop without saving\n" +
                    "• Click CANCEL to continue testing",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (saveWarning == MessageBoxResult.Cancel)
                {
                    _dashboard?.LogMessage($"[{TabId}] Stop cancelled by user");
                    return;
                }

                if (saveWarning == MessageBoxResult.Yes)
                {
                    try
                    {
                        SaveTestData();
                        _hasUnsavedChanges = false;
                    }
                    catch (Exception ex)
                    {
                        _dashboard?.LogMessage($"[{TabId}] ❌ Save failed: {ex.Message}");

                        var continueStop = MessageBox.Show(
                            $"Failed to save data:\n{ex.Message}\n\nDo you want to stop anyway?",
                            "Save Failed",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Error);

                        if (continueStop == MessageBoxResult.No)
                        {
                            return;
                        }
                    }
                }
            }

            var result = MessageBox.Show(
                "Stop this Test Kit?\n\n" +
                "This will stop all processes but you can still view the recorded data.",
                "Confirm Stop",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await StopAllProcessesAsync();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
            {
                MessageBox.Show(
                    "Cannot save in read-only mode.\n\nThis TestKit was loaded from a file.",
                    "Read-Only Mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_recorderWindow == null)
            {
                MessageBox.Show("RecorderWindow not initialized!", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SaveTestData();

                MessageBox.Show(
                    $"Test data exported successfully to Detail.xlsx!\n\n{_lastExportPath}",
                    "Export Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "❌ Export failed";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));

                _dashboard?.LogMessage($"[{TabId}] ❌ Export failed: {ex.Message}");
                MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveTestData()
        {
            StatusText.Text = "💾 Exporting to Detail.xlsx...";

            try
            {
                var folderManager = UITestKit.Services.FolderManager.Instance;
                string detailFilePath = folderManager.GetDetailExcelPath(_config.SaveLocation);

                _dashboard?.LogMessage($"[{TabId}] Exporting to: {detailFilePath}");

                var exporter = new ExcelExporter();

                var sheetsData = PrepareDataForExport();

                exporter.ExportToExcelParams(detailFilePath, sheetsData);

                _lastExportPath = detailFilePath;

                StatusText.Text = $"✅ Exported to Detail.xlsx";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));

                folderManager.UpdateHeaderStatus(_config.SaveLocation, "Completed", "Test data exported successfully");

                OnDataChanged?.Invoke(new { Action = "Save", TabId = this.TabId, FilePath = detailFilePath });
                _dashboard?.LogMessage($"[{TabId}] 💾 Test data exported to Detail.xlsx");

                _hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                StatusText.Text = "❌ Export failed";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                _dashboard?.LogMessage($"[{TabId}] ❌ Export failed: {ex.Message}");
                throw;
            }
        }

        private (string SheetName, ICollection<object> Data)[] PrepareDataForExport()
        {
            var sheetsList = new List<(string SheetName, ICollection<object> Data)>();

            var allInputClients = new List<object>();
            foreach (var stage in _recorderWindow.TestStages.OrderBy(s => s.Key))
            {
                foreach (var inputClient in stage.Value.InputClients)
                {
                    allInputClients.Add(inputClient);
                }
            }
            if (allInputClients.Any())
            {
                sheetsList.Add(("InputClients", allInputClients));
            }

            var allOutputClients = new List<object>();
            foreach (var stage in _recorderWindow.TestStages.OrderBy(s => s.Key))
            {
                foreach (var outputClient in stage.Value.OutputClients)
                {
                    allOutputClients.Add(outputClient);
                }
            }
            if (allOutputClients.Any())
            {
                sheetsList.Add(("OutputClients", allOutputClients));
            }

            var allOutputServers = new List<object>();
            foreach (var stage in _recorderWindow.TestStages.OrderBy(s => s.Key))
            {
                foreach (var outputServer in stage.Value.OutputServers)
                {
                    allOutputServers.Add(outputServer);
                }
            }
            if (allOutputServers.Any())
            {
                sheetsList.Add(("OutputServers", allOutputServers));
            }

            return sheetsList.ToArray();
        }

        public void UpdateStatus(string message)
        {
            if (_isDisposed) return;
            StatusText.Text = message;
        }

        public RecorderWindow GetRecorderWindow()
        {
            return _recorderWindow;
        }

        public void RefreshBindings()
        {
            if (_isDisposed) return;

            if (_recorderWindow != null && RecorderContentArea.Children.Count > 0)
            {
                var content = RecorderContentArea.Children[0] as DockPanel;
                if (content != null)
                {
                    content.DataContext = null;
                    content.DataContext = _recorderWindow;

                    _dashboard?.LogMessage($"[{TabId}] Bindings refreshed");
                }
            }
        }
    }
}