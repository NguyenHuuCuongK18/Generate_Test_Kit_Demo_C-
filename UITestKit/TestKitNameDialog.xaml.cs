using System;
using System.IO;
using System.Windows;

namespace UITestKit
{
    public partial class TestKitNameDialog : Window
    {
        public string TestKitName { get; private set; }
        private string _projectName;
        private int _testKitNumber;
        private string _rootFolder;

        public TestKitNameDialog(string projectName, int testKitNumber, string rootFolder)
        {
            InitializeComponent();

            _projectName = projectName;
            _testKitNumber = testKitNumber;
            _rootFolder = rootFolder;

            TxtProjectName.Text = projectName;

            // Default name
            string defaultName = $"TestKit_{testKitNumber:D3}_{DateTime.Now:yyyyMMdd_HHmmss}";
            TxtTestKitName.Text = defaultName;

            UpdateFolderPreview();

            TxtTestKitName.SelectAll();
            TxtTestKitName.Focus();

            TxtTestKitName.TextChanged += (s, e) => UpdateFolderPreview();
        }

        private void UpdateFolderPreview()
        {
            string testKitName = TxtTestKitName.Text.Trim();

            if (string.IsNullOrWhiteSpace(testKitName))
            {
                TxtFolderPreview.Text = "[Enter a name to see preview]";
                return;
            }

            TxtFolderPreview.Text = Path.Combine(_rootFolder, testKitName) + "\\";
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string testKitName = TxtTestKitName.Text.Trim();

            if (string.IsNullOrWhiteSpace(testKitName))
            {
                TxtValidation.Text = "❌ Test Kit name cannot be empty!";
                TxtValidation.Visibility = Visibility.Visible;
                return;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (testKitName.IndexOfAny(invalidChars) >= 0)
            {
                TxtValidation.Text = "❌ Name contains invalid characters!";
                TxtValidation.Visibility = Visibility.Visible;
                return;
            }

            if (testKitName.Length > 100)
            {
                TxtValidation.Text = "❌ Name is too long (max 100 characters)!";
                TxtValidation.Visibility = Visibility.Visible;
                return;
            }

            TestKitName = testKitName;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}