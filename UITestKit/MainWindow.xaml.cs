using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System;
using System.IO;
using System.Windows;
using UITestKit.ServiceExcute;

namespace UITestKit
{
    public partial class MainWindow : Window
    {
        private ExecutableManager _manager = new ExecutableManager();

        // Luôn lưu config vào AppData để chắc chắn có quyền ghi
        private readonly string _configFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UITestKit");
        private readonly string _configFilePath;

        public MainWindow()
        {
            InitializeComponent();

            Directory.CreateDirectory(_configFolder);
            _configFilePath = Path.Combine(_configFolder, "appconfig.json");

            LoadConfig();
        }

        private void BtnBrowseClient_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select Client Executable"
            };

            if (dialog.ShowDialog() == true)
                txtClientPath.Text = dialog.FileName;
        }

        private void BtnBrowseServer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select Server Executable"
            };

            if (dialog.ShowDialog() == true)
                txtServerPath.Text = dialog.FileName;
        }

        private void BtnBrowseSave_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog();
            if (dialog.ShowDialog(this) == true)
                txtSaveLocation.Text = dialog.SelectedPath;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string clientPath = txtClientPath.Text.Trim();
                string serverPath = txtServerPath.Text.Trim();
                string saveLocation = txtSaveLocation.Text.Trim();
                string projectName = txtProjectName.Text.Trim();

                if (string.IsNullOrWhiteSpace(clientPath) ||
                    string.IsNullOrWhiteSpace(serverPath) ||
                    string.IsNullOrWhiteSpace(saveLocation) ||
                    string.IsNullOrWhiteSpace(projectName))
                {
                    MessageBox.Show("Please fill all fields.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var config = new ConfigModel
                {
                    ClientPath = clientPath,
                    ServerPath = serverPath,
                    SaveLocation = saveLocation,
                    ProjectName = projectName
                };

                string json = System.Text.Json.JsonSerializer.Serialize(config,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_configFilePath, json);

                // Start exe
                _manager.Init(clientPath, serverPath);

                var recorder = new RecorderWindow(_manager);
                recorder.Show();

                _manager.StartBoth();

                MessageBox.Show($"Configuration saved to:\n{_configFilePath}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving config:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _manager.StopBoth();
            Close();
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var config = System.Text.Json.JsonSerializer.Deserialize<ConfigModel>(json);

                    if (config != null)
                    {
                        txtClientPath.Text = config.ClientPath;
                        txtServerPath.Text = config.ServerPath;
                        txtSaveLocation.Text = config.SaveLocation;
                        txtProjectName.Text = config.ProjectName;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading config:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class ConfigModel
    {
        public string ClientPath { get; set; } = string.Empty;
        public string ServerPath { get; set; } = string.Empty;
        public string SaveLocation { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
    }
}
