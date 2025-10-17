using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UITestKit.MiddlewareHandling;
using UITestKit.Model;
using UITestKit.ServiceExcute;
using UITestKit.ServiceSetting;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace UITestKit
{
    public partial class MainWindow : Window
    {
        // Luôn lưu config vào AppData để chắc chắn có quyền ghi
        private readonly string _configFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UITestKit");
        private readonly string _configFilePath;

        public ConfigModel SavedConfig { get; private set; }

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
                string templateClient = txtClientAppSettings.Text.Trim();
                string templateServer = txtServerAppSettings.Text.Trim();
                string protocol = ((ComboBoxItem)cbProtocol.SelectedItem).Content.ToString();

                if (string.IsNullOrWhiteSpace(clientPath) ||
                    string.IsNullOrWhiteSpace(serverPath) ||
                    string.IsNullOrWhiteSpace(saveLocation) ||
                    string.IsNullOrWhiteSpace(projectName) ||
                    string.IsNullOrEmpty(templateClient) ||
                    string.IsNullOrEmpty(templateServer) ||
                    string.IsNullOrEmpty(protocol))
                {
                    MessageBox.Show("Please fill all fields.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Tạo config object
                SavedConfig = new ConfigModel
                {
                    ClientPath = clientPath,
                    ServerPath = serverPath,
                    SaveLocation = saveLocation,
                    ProjectName = projectName,
                    Protocol = protocol,
                    ClientAppSettings = templateClient,
                    ServerAppSettings = templateServer
                };

                // Lưu config vào file
                string json = System.Text.Json.JsonSerializer.Serialize(SavedConfig,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);

                // Replace appsettings (prepare for future use)
                string destServer = Path.GetDirectoryName(serverPath)!;
                AppSettingHandling.ReplaceAppSetting(templateServer, destServer, "appsettings.json", replaceOnlyPublish: false);
                string destClient = Path.GetDirectoryName(clientPath)!;
                AppSettingHandling.ReplaceAppSetting(templateClient, destClient, "appsettings.json", false);

                MessageBox.Show("Configuration saved successfully!\nYou can now create Test Kits.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving config:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        private void BtnBrowseClientAppSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Select Client appsettings.json"
            };
            if (dlg.ShowDialog() == true)
            {
                txtClientAppSettings.Text = dlg.FileName;
            }
        }

        private void BtnBrowseServerAppSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Select Server appsettings.json"
            };
            if (dlg.ShowDialog() == true)
            {
                txtServerAppSettings.Text = dlg.FileName;
            }
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
                        txtClientAppSettings.Text = config.ClientAppSettings;
                        txtServerAppSettings.Text = config.ServerAppSettings;

                        // Set protocol
                        for (int i = 0; i < cbProtocol.Items.Count; i++)
                        {
                            if (((ComboBoxItem)cbProtocol.Items[i]).Content.ToString() == config.Protocol)
                            {
                                cbProtocol.SelectedIndex = i;
                                break;
                            }
                        }
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
}