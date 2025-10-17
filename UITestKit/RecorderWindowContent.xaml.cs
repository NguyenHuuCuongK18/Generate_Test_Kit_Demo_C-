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
        private bool _hasUnsavedChanges = false; // 🔑 Track unsaved changes

        public RecorderWindowContent(string tabId, ConfigModel config, MainDashboard dashboard)
        {
            InitializeComponent();
            TabId = tabId;
            _config = config;
            _dashboard = dashboard;

            InitializeRecorder();
        }

        private async void InitializeRecorder()
        {
            try
            {
                StatusText.Text = "Initializing Test Kit...";
                _dashboard?.LogMessage($"[{TabId}] Initializing Test Kit...");

                // Khởi tạo ExecutableManager
                _manager.Init(_config.ClientPath, _config.ServerPath);

                // Tạo RecorderWindow instance
                _recorderWindow = new RecorderWindow(_manager, _config.SaveLocation);

                // IMPORTANT: Set Recorder cho Middleware trước khi embed content
                _middlewareStart.Recorder = _recorderWindow;

                // Subscribe to events từ ExecutableManager (RecorderWindowContent layer)
                _manager.ClientOutputReceived += OnClientOutput;
                _manager.ServerOutputReceived += OnServerOutput;

                // Embed RecorderWindow content VÀ giữ DataContext
                EmbedRecorderWindowContent();

                // Start processes
                await StartProcessesAsync();

                StatusText.Text = "✅ Test Kit Running";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));

                _isRunning = true;
                _hasUnsavedChanges = false; // 🔑 Reset flag khi khởi tạo
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

        /// <summary>
        /// Embed RecorderWindow content và giữ nguyên DataContext để binding hoạt động
        /// </summary>
        private void EmbedRecorderWindowContent()
        {
            try
            {
                if (_recorderWindow.Content is DockPanel recorderContent)
                {
                    // Remove content from RecorderWindow
                    _recorderWindow.Content = null;

                    // Clear container (remove placeholder)
                    RecorderContentArea.Children.Clear();

                    // CRITICAL: Set DataContext của embedded content = RecorderWindow
                    // Điều này đảm bảo tất cả bindings vẫn hoạt động
                    recorderContent.DataContext = _recorderWindow;

                    // Add to container
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

        #region Event Handlers (RecorderWindowContent layer)

        private void OnClientOutput(string data)
        {
            if (_isDisposed) return;
            _dashboard?.LogMessage($"[{TabId}] 📤 Client: {data}");

            // 🔑 Mark as having unsaved changes khi có output mới
            _hasUnsavedChanges = true;
        }

        private void OnServerOutput(string data)
        {
            if (_isDisposed) return;
            _dashboard?.LogMessage($"[{TabId}] 📥 Server: {data}");

            // 🔑 Mark as having unsaved changes khi có output mới
            _hasUnsavedChanges = true;
        }

        #endregion

        private async Task StartProcessesAsync()
        {
            try
            {
                // 1. Start Server
                _dashboard?.LogMessage($"[{TabId}] Starting Server...");
                _manager.StartServer();
                await Task.Delay(1000);

                // 2. Start Middleware (chỉ nếu chưa chạy)
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

                // 3. Start Client
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

        /// <summary>
        /// 🔑 MAIN CLEANUP METHOD - Stop và cleanup tất cả resources
        /// KHÔNG disable UI, chỉ disable các button chỉnh sửa
        /// Data được preserve để có thể xem lại và select
        /// </summary>
        public async Task StopAllProcessesAsync()
        {
            if (!_isRunning || _isDisposed) return;

            try
            {
                _dashboard?.LogMessage($"[{TabId}] ========================================");
                _dashboard?.LogMessage($"[{TabId}] Starting cleanup process...");

                // 🔑 STEP 1: Cleanup RecorderWindow TRƯỚC để unsubscribe events
                // NHƯNG KHÔNG CLEAR DATA
                if (_recorderWindow != null)
                {
                    _dashboard?.LogMessage($"[{TabId}] Cleaning up RecorderWindow (preserving data)...");

                    // 🔑 Log data trước khi cleanup
                    _dashboard?.LogMessage($"[{TabId}] TestStages count BEFORE cleanup: {_recorderWindow.TestStages.Count}");
                    _dashboard?.LogMessage($"[{TabId}] StageKeys count BEFORE cleanup: {_recorderWindow.StageKeys.Count}");

                    _recorderWindow.Cleanup();

                    // 🔑 Log data sau khi cleanup
                    _dashboard?.LogMessage($"[{TabId}] TestStages count AFTER cleanup: {_recorderWindow.TestStages.Count}");
                    _dashboard?.LogMessage($"[{TabId}] StageKeys count AFTER cleanup: {_recorderWindow.StageKeys.Count}");

                    _dashboard?.LogMessage($"[{TabId}] ✅ RecorderWindow cleanup completed (data preserved)");
                }

                // 🔑 STEP 2: Close Console Windows
                CloseConsoleWindows();

                // 🔑 STEP 3: Disable ONLY edit buttons (không disable ComboBox và DataGrid)
                DisableEditButtonsOnly();

                // 🔑 STEP 4: Unsubscribe RecorderWindowContent events
                if (_manager != null)
                {
                    _dashboard?.LogMessage($"[{TabId}] Unsubscribing RecorderWindowContent events...");
                    _manager.ClientOutputReceived -= OnClientOutput;
                    _manager.ServerOutputReceived -= OnServerOutput;
                    _dashboard?.LogMessage($"[{TabId}] ✅ RecorderWindowContent events unsubscribed");
                }

                // 🔑 STEP 5: Stop Client & Server processes
                if (_manager != null)
                {
                    _dashboard?.LogMessage($"[{TabId}] Stopping Client and Server processes...");
                    await _manager.StopAllAsync();
                    _dashboard?.LogMessage($"[{TabId}] ✅ Client and Server stopped");
                }

                // 🔑 STEP 6: Stop Middleware nếu TestKit này là owner
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

                    // Clear recorder reference để không nhận data nữa
                    if (_middlewareStart.Recorder == _recorderWindow)
                    {
                        _middlewareStart.Recorder = null;
                        _dashboard?.LogMessage($"[{TabId}] Cleared Middleware recorder reference");
                    }
                }

                // 🔑 STEP 7: Mark as disposed
                _isDisposed = true;
                _isRunning = false;

                // 🔑 STEP 8: Force refresh bindings để đảm bảo ComboBox hoạt động
                Dispatcher.Invoke(() =>
                {
                    RefreshBindings();
                    _dashboard?.LogMessage($"[{TabId}] ✅ Bindings refreshed");
                });

                // Update UI
                StatusText.Text = "⏹ Stopped";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(149, 165, 166));

                _dashboard?.LogMessage($"[{TabId}] ✅ All processes stopped, data and UI preserved");
                _dashboard?.LogMessage($"[{TabId}] ========================================");

                // Notify dashboard
                OnTestKitClosed?.Invoke(TabId);
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ❌ ERROR during cleanup: {ex.Message}");
                _dashboard?.LogMessage($"[{TabId}] StackTrace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// 🔑 NEW METHOD: Close console windows của TestKit này
        /// </summary>
        private void CloseConsoleWindows()
        {
            try
            {
                _dashboard?.LogMessage($"[{TabId}] Closing console windows...");

                // Lấy tất cả windows trong application
                var windowsToClose = System.Windows.Application.Current.Windows
                    .Cast<Window>()
                    .Where(w => w is ClientConsoleWindow || w is ServerConsoleWindow)
                    .Where(w =>
                    {
                        // Check nếu window này thuộc về RecorderWindow hiện tại
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
                        _dashboard?.LogMessage($"[{TabId}] Closed console window: {window.GetType().Name}");
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
        /// 🔑 FIXED: Chỉ disable các button chỉnh sửa
        /// KHÔNG disable ComboBox và DataGrid
        /// KHÔNG có banner overlay
        /// </summary>
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
                        // 🔑 DEBUG: Log TestStages count
                        _dashboard?.LogMessage($"[{TabId}] TestStages count: {_recorderWindow.TestStages.Count}");
                        _dashboard?.LogMessage($"[{TabId}] StageKeys count: {_recorderWindow.StageKeys.Count}");

                        // Log stage keys
                        if (_recorderWindow.StageKeys.Count > 0)
                        {
                            var keys = string.Join(", ", _recorderWindow.StageKeys);
                            _dashboard?.LogMessage($"[{TabId}] StageKeys: {keys}");
                        }

                        // Tìm và disable CÁC BUTTON CHỈNH SỬA
                        DisableSpecificButtons(content);

                        // 🔑 Set DataGrid thành ReadOnly (nhưng vẫn có thể select)
                        SetDataGridsReadOnly(content);

                        // 🔑 DEBUG: Check ComboBox state
                        var comboBoxes = FindVisualChildren<ComboBox>(content);
                        foreach (var cb in comboBoxes)
                        {
                            _dashboard?.LogMessage($"[{TabId}] ComboBox - IsEnabled: {cb.IsEnabled}, ItemsSource count: {cb.Items.Count}");
                        }

                        _dashboard?.LogMessage($"[{TabId}] ✅ Edit buttons disabled, ComboBox and data preserved");
                    }
                }

                if (BtnStopRecord != null)
                {
                    BtnStopRecord.IsEnabled = false;
                    BtnStopRecord.Opacity = 0.5;
                }

                if (BtnSave != null)
                {
                    // Keep Save button enabled
                    BtnSave.Content = "💾 Export Data";
                }

                _dashboard?.LogMessage($"[{TabId}] ✅ Toolbar buttons updated");
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ⚠️ Warning: Could not disable edit buttons - {ex.Message}");
            }
        }

        /// <summary>
        /// Disable các button chỉnh sửa cụ thể
        /// </summary>
        private void DisableSpecificButtons(DockPanel content)
        {
            try
            {
                var buttons = FindVisualChildren<Button>(content);

                foreach (var button in buttons)
                {
                    string buttonContent = button.Content?.ToString() ?? "";

                    // Chỉ disable: Add Stage, Update Stage, Delete Stage, Submit, Close All
                    if (buttonContent.Contains("Add Stage") ||
                        buttonContent.Contains("Update Stage") ||
                        buttonContent.Contains("Delete Stage") ||
                        buttonContent.Contains("Submit") ||
                        buttonContent.Contains("Close All"))
                    {
                        button.IsEnabled = false;
                        button.Opacity = 0.5;

                        _dashboard?.LogMessage($"[{TabId}] Disabled button: {buttonContent}");
                    }
                }
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ⚠️ Warning: Could not disable specific buttons - {ex.Message}");
            }
        }

        /// <summary>
        /// Set DataGrids thành ReadOnly nhưng vẫn có thể select
        /// </summary>
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

                    _dashboard?.LogMessage($"[{TabId}] Set DataGrid to read-only (selection enabled)");
                }
            }
            catch (Exception ex)
            {
                _dashboard?.LogMessage($"[{TabId}] ⚠️ Warning: Could not set DataGrids read-only - {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method để tìm tất cả children của một type trong visual tree
        /// </summary>
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
        /// 🔑 ENHANCED: Stop với cảnh báo save trước
        /// </summary>
        private async void BtnStopRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_isDisposed) return;

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
                    // Save trước khi stop
                    _dashboard?.LogMessage($"[{TabId}] Saving before stop...");

                    try
                    {
                        SaveTestData();
                        _hasUnsavedChanges = false;
                        _dashboard?.LogMessage($"[{TabId}] ✅ Data saved successfully");
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
                "This will:\n" +
                "• Stop Client and Server processes\n" +
                "• Close console windows\n" +
                "• Hãy save testkit trước khi stop bởi 1 số message thừa sẽ được server hoặc client phản hồi khiến Output bị sai\n\n" +
                "Continue?",
                "Confirm Stop",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await StopAllProcessesAsync();
            }
        }

        /// <summary>
        /// 🔑 ENHANCED: Save với update flag
        /// </summary>
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_recorderWindow == null)
            {
                MessageBox.Show("RecorderWindow not initialized!", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SaveTestData();

                _hasUnsavedChanges = false;

                MessageBox.Show(
                    $"Test data exported successfully!\n\n{GetLastExportPath()}",
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

        /// <summary>
        /// 🔑 NEW METHOD: Extract save logic
        /// </summary>
        private string _lastExportPath = "";

        private void SaveTestData()
        {
            StatusText.Text = "💾 Exporting to Detail.xlsx...";

            try
            {
                var folderManager = UITestKit.Services.FolderManager.Instance;
                string detailFilePath = folderManager.GetDetailExcelPath(_config.SaveLocation);

                _dashboard?.LogMessage($"[{TabId}] Exporting to: {detailFilePath}");

                // 🔑 Simplified: Use wrapper method
                var exporter = new ExcelExporter();
                exporter.ExportTestKitData(detailFilePath, _recorderWindow.TestStages);

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
        private string GetLastExportPath()
        {
            return string.IsNullOrEmpty(_lastExportPath) ? "Unknown path" : _lastExportPath;
        }

        public RecorderWindow GetRecorderWindow()
        {
            return _recorderWindow;
        }

        /// <summary>
        /// Force refresh bindings khi cần
        /// </summary>
        public void RefreshBindings()
        {
            if (_isDisposed) return;

            if (_recorderWindow != null && RecorderContentArea.Children.Count > 0)
            {
                var content = RecorderContentArea.Children[0] as DockPanel;
                if (content != null)
                {
                    // Re-apply DataContext để trigger binding refresh
                    content.DataContext = null;
                    content.DataContext = _recorderWindow;

                    _dashboard?.LogMessage($"[{TabId}] Bindings refreshed");
                }
            }
        }
    }
}