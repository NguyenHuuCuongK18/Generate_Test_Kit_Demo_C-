using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;

namespace UITestKit.Services
{
    public class FolderManager
    {
        #region Singleton
        private static readonly Lazy<FolderManager> _instance = new Lazy<FolderManager>(() => new FolderManager());
        public static FolderManager Instance => _instance.Value;
        private FolderManager() { }
        #endregion

        #region Properties
        public string TestKitsRootFolder { get; private set; }
        public string ProjectName { get; private set; }
        public event Action<string> OnFolderStructureChanged;
        #endregion

        #region Initialize & Setup

        public string Initialize(string saveLocation, string projectName)
        {
            System.Diagnostics.Debug.WriteLine("[FolderManager] Initialize START");

            try
            {
                if (string.IsNullOrWhiteSpace(saveLocation))
                    throw new ArgumentException("Save location cannot be empty", nameof(saveLocation));

                if (string.IsNullOrWhiteSpace(projectName))
                    throw new ArgumentException("Project name cannot be empty", nameof(projectName));

                ProjectName = projectName;
                TestKitsRootFolder = Path.Combine(saveLocation, $"{projectName}_TestKits");

                if (!Directory.Exists(saveLocation))
                {
                    Directory.CreateDirectory(saveLocation);
                }

                if (!Directory.Exists(TestKitsRootFolder))
                {
                    Directory.CreateDirectory(TestKitsRootFolder);
                }

                System.Diagnostics.Debug.WriteLine("[FolderManager] Initialize COMPLETE");
                return TestKitsRootFolder;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] EXCEPTION: {ex.Message}");
                throw;
            }
        }

        public bool IsInitialized()
        {
            return !string.IsNullOrEmpty(TestKitsRootFolder) && Directory.Exists(TestKitsRootFolder);
        }

        #endregion

        #region TestKit Folder Management

        public string CreateTestKitFolder(string testKitName, bool createExcelTemplates = true)
        {
            if (!IsInitialized())
                throw new InvalidOperationException("FolderManager has not been initialized. Call Initialize() first.");

            ValidateTestKitName(testKitName);

            string testKitFolderPath = Path.Combine(TestKitsRootFolder, testKitName);

            if (!Directory.Exists(testKitFolderPath))
            {
                Directory.CreateDirectory(testKitFolderPath);
            }

            if (createExcelTemplates)
            {
                CreateTestKitExcelFiles(testKitFolderPath);
            }

            RaiseEvent($"Created TestKit folder: {testKitFolderPath}");

            return testKitFolderPath;
        }

        /// <summary>
        /// Tạo Header.xlsx và Detail.xlsx cho TestKit
        /// </summary>
        private void CreateTestKitExcelFiles(string testKitFolderPath)
        {
            try
            {
                string headerFilePath = Path.Combine(testKitFolderPath, "Header.xlsx");
                string detailFilePath = Path.Combine(testKitFolderPath, "Detail.xlsx");

                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                if (!File.Exists(headerFilePath))
                {
                    CreateTestKitHeaderFile(headerFilePath);
                }

                if (!File.Exists(detailFilePath))
                {
                    CreateEmptyExcelFile(detailFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] Warning: Could not create Excel files - {ex.Message}");
            }
        }

        /// <summary>
        /// Tạo Header.xlsx cho TestKit (A1 = "StartFirst")
        /// </summary>
        private void CreateTestKitHeaderFile(string filePath)
        {
            using (var package = new OfficeOpenXml.ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Header");

                worksheet.Cells["A1"].Value = "StartFirst";
                worksheet.Cells["A1"].Style.Font.Bold = true;
                worksheet.Cells["A1"].Style.Font.Size = 14;
                worksheet.Cells["A1"].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);

                worksheet.Column(1).Width = 20;
                worksheet.Column(2).AutoFit();

                package.SaveAs(new FileInfo(filePath));
            }
        }

        /// <summary>
        /// Tạo Detail.xlsx trống
        /// </summary>
        private void CreateEmptyExcelFile(string filePath)
        {
            using (var package = new OfficeOpenXml.ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Sheet1");
                package.SaveAs(new FileInfo(filePath));
            }
        }

        /// <summary>
        /// 🔑 NEW: Tạo Header.xlsx cho toàn bộ project (ngang hàng với TestKits folders)
        /// Sheet1: QuestionMark (TestCase, Mark)
        /// Sheet2: Config (Key, Value) với Type=TCP/HTTP
        /// </summary>
        public void CreateProjectHeaderFile(string protocol)
        {
            if (!IsInitialized())
                throw new InvalidOperationException("FolderManager has not been initialized");

            try
            {
                string projectHeaderPath = Path.Combine(TestKitsRootFolder, "Header.xlsx");

                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                using (var package = new OfficeOpenXml.ExcelPackage())
                {
                    // Sheet 1: QuestionMark
                    var sheet1 = package.Workbook.Worksheets.Add("QuestionMark");
                    sheet1.Cells["A1"].Value = "TestCase";
                    sheet1.Cells["B1"].Value = "Mark";
                    sheet1.Cells["A1:B1"].Style.Font.Bold = true;
                    sheet1.Column(1).Width = 20;
                    sheet1.Column(2).Width = 10;

                    // Sheet 2: Config
                    var sheet2 = package.Workbook.Worksheets.Add("Config");
                    sheet2.Cells["A1"].Value = "Key";
                    sheet2.Cells["B1"].Value = "Value";
                    sheet2.Cells["A1:B1"].Style.Font.Bold = true;

                    sheet2.Cells["A2"].Value = "Type";
                    sheet2.Cells["B2"].Value = protocol;

                    sheet2.Column(1).Width = 20;
                    sheet2.Column(2).Width = 10;

                    package.SaveAs(new FileInfo(projectHeaderPath));
                }

                System.Diagnostics.Debug.WriteLine($"[FolderManager] Created project Header.xlsx: {projectHeaderPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] Warning: Could not create project Header.xlsx - {ex.Message}");
            }
        }

        public string GetDetailExcelPath(string testKitFolderPath)
        {
            return Path.Combine(testKitFolderPath, "Detail.xlsx");
        }

        public string GetHeaderExcelPath(string testKitFolderPath)
        {
            return Path.Combine(testKitFolderPath, "Header.xlsx");
        }

        public void UpdateHeaderStatus(string testKitFolderPath, string status, string description = null)
        {
            try
            {
                string headerFilePath = GetHeaderExcelPath(testKitFolderPath);

                if (!File.Exists(headerFilePath))
                    return;

                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(headerFilePath)))
                {
                    var worksheet = package.Workbook.Worksheets["Header"];
                    if (worksheet == null) return;

                    for (int row = 1; row <= 20; row++)
                    {
                        var cellValue = worksheet.Cells[row, 1].GetValue<string>();

                        if (cellValue == "Status:")
                        {
                            worksheet.Cells[row, 2].Value = status;

                            if (status == "Completed")
                            {
                                worksheet.Cells[row, 2].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                                worksheet.Cells[row, 2].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGreen);
                            }
                            else if (status == "Failed")
                            {
                                worksheet.Cells[row, 2].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                                worksheet.Cells[row, 2].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightPink);
                            }
                        }

                        if (cellValue == "Description:" && !string.IsNullOrEmpty(description))
                        {
                            worksheet.Cells[row, 2].Value = description;
                        }

                        if (cellValue == "Last Updated:")
                        {
                            worksheet.Cells[row, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                    }

                    package.Save();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] Warning: Could not update Header.xlsx - {ex.Message}");
            }
        }

        public bool TestKitFolderExists(string testKitName)
        {
            if (!IsInitialized()) return false;
            string testKitFolderPath = Path.Combine(TestKitsRootFolder, testKitName);
            return Directory.Exists(testKitFolderPath);
        }

        public List<string> GetAllTestKitFolders()
        {
            if (!IsInitialized()) return new List<string>();

            try
            {
                return Directory.GetDirectories(TestKitsRootFolder)
                    .Select(d => new DirectoryInfo(d).Name)
                    .OrderBy(n => n)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public TestKitFolderInfo GetTestKitFolderInfo(string testKitName)
        {
            string folderPath = Path.Combine(TestKitsRootFolder, testKitName);

            if (!Directory.Exists(folderPath))
                return null;

            var dirInfo = new DirectoryInfo(folderPath);

            return new TestKitFolderInfo
            {
                Name = testKitName,
                FullPath = folderPath,
                CreatedDate = dirInfo.CreationTime,
                LastModifiedDate = dirInfo.LastWriteTime,
                TotalFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories).Length,
                TotalSize = GetDirectorySize(folderPath)
            };
        }

        private long GetDirectorySize(string path)
        {
            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(path);
                return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            }
            catch
            {
                return 0;
            }
        }

        #endregion

        #region TreeView Management

        /// <summary>
        /// 🔑 UPDATED: Build TreeView với cả folders VÀ files
        /// </summary>
        public void BuildTreeView(TreeView treeView)
        {
            if (treeView == null) throw new ArgumentNullException(nameof(treeView));
            if (!IsInitialized()) return;

            try
            {
                treeView.Items.Clear();

                TreeViewItem rootItem = new TreeViewItem
                {
                    Header = $"📁 {ProjectName}",
                    IsExpanded = true,
                    Tag = TestKitsRootFolder,
                    FontWeight = FontWeights.Bold
                };

                LoadSubFoldersAndFiles(rootItem, TestKitsRootFolder);

                treeView.Items.Add(rootItem);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] BuildTreeView error: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔑 UPDATED: Load cả subfolders VÀ files
        /// </summary>
        private void LoadSubFoldersAndFiles(TreeViewItem parentItem, string parentPath)
        {
            try
            {
                // Load folders
                var directories = Directory.GetDirectories(parentPath);

                foreach (var dir in directories.OrderBy(d => d))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    var folderItem = new TreeViewItem
                    {
                        Tag = dir
                    };

                    int filesCount = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories).Length;
                    int subDirsCount = Directory.GetDirectories(dir).Length;

                    string header = $"📂 {dirInfo.Name}";
                    if (filesCount > 0 || subDirsCount > 0)
                    {
                        List<string> infoParts = new List<string>();
                        if (subDirsCount > 0) infoParts.Add($"{subDirsCount} folders");
                        if (filesCount > 0) infoParts.Add($"{filesCount} files");
                        header += $" ({string.Join(", ", infoParts)})";
                    }

                    folderItem.Header = header;

                    // Recursive load
                    LoadSubFoldersAndFiles(folderItem, dir);

                    parentItem.Items.Add(folderItem);
                }

                // 🔑 Load files
                var files = Directory.GetFiles(parentPath);

                foreach (var file in files.OrderBy(f => f))
                {
                    var fileInfo = new FileInfo(file);
                    var fileItem = new TreeViewItem
                    {
                        Tag = file
                    };

                    string fileIcon = GetFileIcon(fileInfo.Extension);
                    string fileSize = FormatFileSize(fileInfo.Length);

                    fileItem.Header = $"{fileIcon} {fileInfo.Name} ({fileSize})";

                    parentItem.Items.Add(fileItem);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] LoadSubFoldersAndFiles error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get icon cho file type
        /// </summary>
        private string GetFileIcon(string extension)
        {
            return extension.ToLower() switch
            {
                ".xlsx" => "📊",
                ".xls" => "📊",
                ".txt" => "📄",
                ".log" => "📝",
                ".json" => "📋",
                ".xml" => "📋",
                ".png" => "🖼️",
                ".jpg" => "🖼️",
                ".jpeg" => "🖼️",
                _ => "📄"
            };
        }

        /// <summary>
        /// Format file size
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public void RefreshTreeView(TreeView treeView)
        {
            BuildTreeView(treeView);
        }

        #endregion

        #region Validation

        private void ValidateTestKitName(string testKitName)
        {
            if (string.IsNullOrWhiteSpace(testKitName))
                throw new ArgumentException("TestKit name cannot be empty", nameof(testKitName));

            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (testKitName.IndexOfAny(invalidChars) >= 0)
                throw new ArgumentException($"TestKit name contains invalid characters: {testKitName}");

            if (testKitName.Length > 100)
                throw new ArgumentException("TestKit name is too long (max 100 characters)");
        }

        #endregion

        #region File Operations

        public string GenerateExportFilePath(string testKitFolderPath, string prefix = "TestResult", string extension = ".xlsx")
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{prefix}_{timestamp}{extension}";
            string filePath = Path.Combine(testKitFolderPath, fileName);

            int counter = 1;
            while (File.Exists(filePath))
            {
                fileName = $"{prefix}_{timestamp}_{counter}{extension}";
                filePath = Path.Combine(testKitFolderPath, fileName);
                counter++;
            }

            return filePath;
        }

        public void OpenFolderInExplorer(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                throw new ArgumentException($"Folder does not exist: {folderPath}");

            System.Diagnostics.Process.Start("explorer.exe", folderPath);
        }

        public void OpenRootFolderInExplorer()
        {
            if (!IsInitialized())
                throw new InvalidOperationException("FolderManager has not been initialized");

            OpenFolderInExplorer(TestKitsRootFolder);
        }

        #endregion

        #region Event Management

        private void RaiseEvent(string message)
        {
            try
            {
                OnFolderStructureChanged?.Invoke(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] Event handler error: {ex.Message}");
            }
        }

        #endregion

        #region Cleanup

        public void DeleteTestKitFolder(string testKitName, bool deleteFiles = true)
        {
            string folderPath = Path.Combine(TestKitsRootFolder, testKitName);

            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"TestKit folder not found: {folderPath}");

            Directory.Delete(folderPath, deleteFiles);

            RaiseEvent($"Deleted TestKit folder: {folderPath}");
        }

        public void Reset()
        {
            TestKitsRootFolder = null;
            ProjectName = null;
        }

        #endregion
    }

    #region Helper Classes

    public class TestKitFolderInfo
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }

        public string FormattedSize
        {
            get
            {
                string[] sizes = { "B", "KB", "MB", "GB" };
                double len = TotalSize;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        public override string ToString()
        {
            return $"{Name} - {TotalFiles} files ({FormattedSize})";
        }
    }

    #endregion
}