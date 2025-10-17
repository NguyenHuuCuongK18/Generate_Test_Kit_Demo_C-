using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ookii.Dialogs.Wpf;
using UITestKit.Model;
using UITestKit.MiddlewareHandling;
using UITestKit.Services;

namespace UITestKit
{
    public partial class MainDashboard : Window
    {
        private Dictionary<string, RecorderWindowTab> _activeTabs = new Dictionary<string, RecorderWindowTab>();
        private RecorderWindowTab _currentActiveTab = null;
        private bool _isConfigured = false;

        private ConfigModel _config;
        private MiddlewareStart _middlewareStart = MiddlewareStart.Instance;
        private FolderManager _folderManager = FolderManager.Instance;

        public MainDashboard()
        {
            InitializeComponent();
            InitializeFolderManager();
            LoadFolderStructure();
            LogMessage("🚀 Application started. Please configure settings first.");
        }

        /// <summary>
        /// 🔑 FIXED: Simplified event handler to avoid deadlock
        /// </summary>
        private void InitializeFolderManager()
        {
            _folderManager.OnFolderStructureChanged += (message) =>
            {
                // Simple logging only, no TreeView refresh during event
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogMessage($"📁 {message}");
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        private void LoadFolderStructure()
        {
            if (_folderManager.IsInitialized())
            {
                _folderManager.BuildTreeView(FolderTreeView);
            }
            else
            {
                TreeViewItem rootItem = new TreeViewItem
                {
                    Header = "📁 My Test Kits",
                    IsExpanded = true,
                    Tag = "root"
                };
                FolderTreeView.Items.Add(rootItem);
            }
        }

        /// <summary>
        /// 🔑 FIXED: Added detailed logging for debugging
        /// </summary>
        private void BtnConfigure_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogMessage("⚙️ [1/10] Starting configuration...");

                if (HasRunningTestKit())
                {
                    LogMessage("⚙️ [2/10] Has running TestKit - showing warning");
                    MessageBox.Show(
                        "Cannot reconfigure while Test Kits are running.\n\nPlease close all Test Kits first.",
                        "Configuration Locked",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var configWindow = new MainWindow();
                configWindow.Owner = this;


                bool? dialogResult = configWindow.ShowDialog();


                if (dialogResult == true && configWindow.SavedConfig != null)
                {

                    _config = configWindow.SavedConfig;
                    _isConfigured = true;

                    string rootFolder = null;
                    try
                    {
                        rootFolder = _folderManager.Initialize(_config.SaveLocation, _config.ProjectName);
                        _folderManager.CreateProjectHeaderFile(_config.Protocol);
                    }
                    catch (Exception initEx)
                    {
                        LogMessage($"   Error: {initEx.Message}");
                        throw;
                    }

                    try
                    {
                        _folderManager.BuildTreeView(FolderTreeView);
                    }
                    catch (Exception treeEx)
                    {
                    }

                    // Update UI
                    TxtConfigStatus.Text = "✅ Configured";
                    TxtConfigStatus.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                    TxtProtocol.Text = _config.Protocol;
                    BtnNewTestKit.IsEnabled = true;

                    PlaceholderPanel.Children.Clear();
                    var placeholderText = new TextBlock
                    {
                        Text = "No Test Kit Open. Click 'New Test Kit' to start.",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.Gray,
                        FontSize = 16
                    };
                    PlaceholderPanel.Children.Add(placeholderText);

                    LogMessage($"✅ Configuration completed successfully!");
                    LogMessage($"   📋 Protocol: {_config.Protocol}");
                    LogMessage($"   📦 Project: {_config.ProjectName}");
                    LogMessage($"   💾 Root Folder: {rootFolder}");
                }
                else
                {
                    LogMessage("⚙️ Config window closed without saving");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ CRITICAL ERROR in BtnConfigure_Click");
                LogMessage($"   Type: {ex.GetType().Name}");
                LogMessage($"   Message: {ex.Message}");
                LogMessage($"   StackTrace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    LogMessage($"   InnerException: {ex.InnerException.Message}");
                }

                MessageBox.Show(
                    $"Configuration failed:\n\n{ex.Message}\n\nCheck log for details.",
                    "Configuration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnNewTestKit_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConfigured)
            {
                MessageBox.Show("Please configure settings first!", "Configuration Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (HasRunningTestKit())
            {
                var result = MessageBox.Show(
                    "A Test Kit is already running.\n\n" +
                    "⚠️ WARNING: Both Test Kits will share the same Middleware.\n" +
                    "Data from Client/Server may be mixed.\n\n" +
                    "Continue anyway?",
                    "Warning: Shared Middleware",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                    return;
            }

            var nameDialog = new TestKitNameDialog(
                _config.ProjectName,
                _activeTabs.Count + 1,
                _folderManager.TestKitsRootFolder);

            nameDialog.Owner = this;

            if (nameDialog.ShowDialog() == true)
            {
                string testKitName = nameDialog.TestKitName;

                if (_folderManager.TestKitFolderExists(testKitName))
                {
                    var overwrite = MessageBox.Show(
                        $"Folder '{testKitName}' already exists.\n\nDo you want to use this folder?",
                        "Folder Exists",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (overwrite == MessageBoxResult.No)
                        return;
                }

                try
                {
                    string testKitFolderPath = _folderManager.CreateTestKitFolder(testKitName, true);

                    string tabId = Guid.NewGuid().ToString();
                    CreateNewRecorderTab(tabId, testKitName, testKitFolderPath);

                    // Refresh TreeView after creating TestKit
                    _folderManager.RefreshTreeView(FolderTreeView);

                    LogMessage($"🆕 Created new Test Kit: {testKitName}");
                    LogMessage($"   Folder: {testKitFolderPath}");
                }
                catch (Exception ex)
                {
                    LogMessage($"❌ ERROR: Failed to create test kit - {ex.Message}");
                    MessageBox.Show($"Error creating test kit: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CreateNewRecorderTab(string tabId, string tabName, string testKitFolderPath)
        {
            Button tabButton = new Button
            {
                Content = CreateTabHeader(tabName, tabId),
                Tag = tabId,
                Height = 40,
                MinWidth = 150,
                Background = new SolidColorBrush(Color.FromRgb(236, 240, 241)),
                Foreground = Brushes.Black,
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            tabButton.Click += TabButton_Click;
            TabHeaderPanel.Children.Add(tabButton);

            var testKitConfig = new ConfigModel
            {
                ClientPath = _config.ClientPath,
                ServerPath = _config.ServerPath,
                SaveLocation = testKitFolderPath,
                ProjectName = _config.ProjectName,
                Protocol = _config.Protocol,
                ClientAppSettings = _config.ClientAppSettings,
                ServerAppSettings = _config.ServerAppSettings
            };

            var recorderContent = new RecorderWindowContent(tabId, testKitConfig, this);

            recorderContent.OnTestKitClosed += (tid) => OnTestKitClosed(tid);
            recorderContent.OnDataChanged += (data) => OnRecorderDataChanged(tabId, data);
            recorderContent.Visibility = Visibility.Collapsed;

            TabContentArea.Children.Add(recorderContent);

            var tab = new RecorderWindowTab
            {
                TabId = tabId,
                TabName = tabName,
                HeaderButton = tabButton,
                Content = recorderContent,
                IsRunning = true,
                FolderPath = testKitFolderPath
            };

            _activeTabs[tabId] = tab;

            ActivateTab(tabId);
            PlaceholderPanel.Visibility = Visibility.Collapsed;
        }

        private StackPanel CreateTabHeader(string tabName, string tabId)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var indicator = new TextBlock
            {
                Text = "🔴",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 5, 0),
                FontSize = 10,
                Tag = "indicator"
            };

            var textBlock = new TextBlock
            {
                Text = tabName,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var closeButton = new Button
            {
                Content = "✕",
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.Gray,
                Tag = tabId,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            closeButton.Click += CloseTab_Click;

            closeButton.MouseEnter += (s, e) =>
            {
                closeButton.Foreground = Brushes.Red;
                closeButton.Background = new SolidColorBrush(Color.FromArgb(30, 231, 76, 60));
            };
            closeButton.MouseLeave += (s, e) =>
            {
                closeButton.Foreground = Brushes.Gray;
                closeButton.Background = Brushes.Transparent;
            };

            panel.Children.Add(indicator);
            panel.Children.Add(textBlock);
            panel.Children.Add(closeButton);

            return panel;
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tabId)
            {
                ActivateTab(tabId);
            }
        }

        private void ActivateTab(string tabId)
        {
            if (!_activeTabs.ContainsKey(tabId)) return;

            var activeTab = _activeTabs[tabId];
            if (activeTab.IsRunning && activeTab.Content.GetRecorderWindow() != null)
            {
                _middlewareStart.Recorder = activeTab.Content.GetRecorderWindow();
                LogMessage($"🔄 Middleware routing to: {activeTab.TabName}");
            }

            foreach (var tab in _activeTabs.Values)
            {
                tab.Content.Visibility = Visibility.Collapsed;
                tab.HeaderButton.Background = new SolidColorBrush(Color.FromRgb(236, 240, 241));
                tab.HeaderButton.Foreground = Brushes.Black;
            }

            activeTab.Content.Visibility = Visibility.Visible;
            activeTab.HeaderButton.Background = Brushes.White;
            activeTab.HeaderButton.Foreground = new SolidColorBrush(Color.FromRgb(41, 128, 185));

            _currentActiveTab = activeTab;
            LogMessage($"📂 Switched to: {activeTab.TabName}");
        }

        private async void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is Button button && button.Tag is string tabId)
            {
                if (_activeTabs.ContainsKey(tabId))
                {
                    var tab = _activeTabs[tabId];

                    var result = MessageBox.Show(
                        $"Close '{tab.TabName}'?\n\n" +
                        $"Folder: {tab.FolderPath}\n\n" +
                        "Any unsaved data will be lost.",
                        "Confirm Close",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        LogMessage($"⏹ Closing: {tab.TabName}...");

                        await tab.Content.StopAllProcessesAsync();

                        TabHeaderPanel.Children.Remove(tab.HeaderButton);
                        TabContentArea.Children.Remove(tab.Content);
                        _activeTabs.Remove(tabId);

                        LogMessage($"✅ Closed: {tab.TabName}");

                        // Refresh TreeView
                        _folderManager.RefreshTreeView(FolderTreeView);

                        if (_activeTabs.Count > 0)
                        {
                            var firstTab = GetFirstTab();
                            if (firstTab != null)
                                ActivateTab(firstTab.TabId);
                        }
                        else
                        {
                            PlaceholderPanel.Visibility = Visibility.Visible;
                            LogMessage("ℹ️ All Test Kits closed");
                        }
                    }
                }
            }
        }

        private void OnTestKitClosed(string tabId)
        {
            if (_activeTabs.ContainsKey(tabId))
            {
                _activeTabs[tabId].IsRunning = false;

                var header = _activeTabs[tabId].HeaderButton.Content as StackPanel;
                if (header != null)
                {
                    var indicator = header.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag?.ToString() == "indicator");
                    if (indicator != null)
                    {
                        indicator.Text = "🟢";
                    }
                }

                LogMessage($"[{_activeTabs[tabId].TabName}] Stopped");

                // Refresh TreeView to show exported files
                _folderManager.RefreshTreeView(FolderTreeView);
            }
        }

        private bool HasRunningTestKit()
        {
            return _activeTabs.Values.Any(t => t.IsRunning);
        }

        private RecorderWindowTab GetFirstTab()
        {
            return _activeTabs.Values.FirstOrDefault();
        }

        private void OnRecorderDataChanged(string tabId, object data)
        {
            // Handle data changed
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_folderManager.IsInitialized())
                {
                    MessageBox.Show("Please configure settings first!", "No Folder",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _folderManager.OpenRootFolderInExplorer();
            }
            catch (Exception ex)
            {
                LogMessage($"❌ ERROR: Could not open folder - {ex.Message}");
                MessageBox.Show($"Could not open folder:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                LogTextBox.AppendText($"[{timestamp}] {message}\n");
                LogTextBox.ScrollToEnd();
            });
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            LogMessage("🗑️ Log cleared");
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (HasRunningTestKit())
            {
                var result = MessageBox.Show(
                    "Test Kits are still running. Close all and exit?",
                    "Confirm Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                LogMessage("🔻 Shutting down...");

                foreach (var tab in _activeTabs.Values.ToList())
                {
                    await tab.Content.StopAllProcessesAsync();
                }

                await _middlewareStart.StopAsync();

                LogMessage("✅ Shutdown complete");
            }
        }
    }

    public class RecorderWindowTab
    {
        public string TabId { get; set; }
        public string TabName { get; set; }
        public Button HeaderButton { get; set; }
        public RecorderWindowContent Content { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
        public bool IsRunning { get; set; }
        public string FolderPath { get; set; }
    }
}