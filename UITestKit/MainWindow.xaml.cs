using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using UITestKit.MiddlewareHandling;
using UITestKit.Model;
using UITestKit.ServiceExcute;
using UITestKit.ServiceSetting;
using UITestKit.Views;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace UITestKit
{
    public partial class MainWindow : Window
    {
        private readonly ExecutableManager _manager = ExecutableManager.Instance;
        private readonly MiddlewareStart _middlewareStart = MiddlewareStart.Instance;

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

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
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

                var config = new ConfigModel
                {
                    ClientPath = clientPath,
                    ServerPath = serverPath,
                    SaveLocation = saveLocation,
                    ProjectName = projectName,
                    Protocol = protocol,
                };

                string json = System.Text.Json.JsonSerializer.Serialize(config,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_configFilePath, json);
                // Ghi de appsettings
                string destServer = Path.GetDirectoryName(serverPath)!;
                AppSettingHandling.ReplaceAppSetting(templateServer, destServer,"appsettings.json",replaceOnlyPublish: false);
                string destClient = Path.GetDirectoryName(clientPath)!;
                AppSettingHandling.ReplaceAppSetting(templateClient, destClient, "appsettings.json", false);


                // Start exe
                _manager.Init(clientPath, serverPath);

                var recorder = new RecorderWindow(_manager,saveLocation);
                recorder.Show();

                _manager.StartServer();
                if (protocol.Equals("HTTP", StringComparison.OrdinalIgnoreCase))
                {
                    await _middlewareStart.StartAsync(useHttp: true);
                }
                else if (protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase))
                {
                    await _middlewareStart.StartAsync(useHttp: false);
                }
                _manager.StartClient();
                

                System.Windows.MessageBox.Show($"Configuration saved to:\n{_configFilePath}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error saving config:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _manager.StopAll();
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
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading config:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

   
}
