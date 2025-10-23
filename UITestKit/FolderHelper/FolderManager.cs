using System.IO;
using System.Windows;
using System.Windows.Controls;
using UITestKit.Model;

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
                TestKitsRootFolder = Path.Combine(saveLocation, $"{projectName}");

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

        public void CreateFileIgnore()
        {
            string projectHeaderPath = Path.Combine(TestKitsRootFolder, "Ignore.xlsx");

            using (var package = new OfficeOpenXml.ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Sheet1");

                worksheet.Cells["A1"].Value = "Ignore";
                worksheet.Cells["A1"].Style.Font.Bold = true;
                worksheet.Cells["A1"].Style.Font.Size = 11;
                worksheet.Cells["A1"].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);

                worksheet.Column(1).Width = 20;
                worksheet.Column(2).AutoFit();

                package.SaveAs(new FileInfo(projectHeaderPath));
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

                    fileItem.Header = $"{fileIcon} {fileInfo.Name} ";

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

        #region load detail testcase from excel
        /// <summary>
        /// 🔑 NEW: Load test data từ Detail.xlsx
        /// </summary>
        /// <summary>
        /// 🔑 FIXED: Load test data từ Detail.xlsx với debug logging
        /// </summary>
        public Dictionary<int, TestStage> LoadTestDataFromDetailFile(string detailFilePath)
        {
            var testStages = new Dictionary<int, TestStage>();

            try
            {
                if (!File.Exists(detailFilePath))
                    throw new FileNotFoundException($"Detail.xlsx not found: {detailFilePath}");

                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(detailFilePath)))
                {
                    System.Diagnostics.Debug.WriteLine($"[FolderManager] Opening Detail.xlsx: {detailFilePath}");
                    System.Diagnostics.Debug.WriteLine($"[FolderManager] Total worksheets: {package.Workbook.Worksheets.Count}");

                    // List all sheet names
                    foreach (var ws in package.Workbook.Worksheets)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Found sheet: '{ws.Name}'");
                    }

                    // Load InputClients sheet
                    var inputSheet = package.Workbook.Worksheets["InputClients"];
                    if (inputSheet != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Loading InputClients sheet...");
                        LoadInputClients(inputSheet, testStages);
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] InputClients loaded. Current stages count: {testStages.Count}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] ⚠️ InputClients sheet NOT FOUND");
                    }

                    // Load OutputClients sheet
                    var outputClientSheet = package.Workbook.Worksheets["OutputClients"];
                    if (outputClientSheet != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Loading OutputClients sheet...");
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] OutputClients rows: {outputClientSheet.Dimension?.Rows ?? 0}");
                        LoadOutputClients(outputClientSheet, testStages);
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] OutputClients loaded");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] ⚠️ OutputClients sheet NOT FOUND");
                    }

                    // Load OutputServers sheet
                    var outputServerSheet = package.Workbook.Worksheets["OutputServers"];
                    if (outputServerSheet != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Loading OutputServers sheet...");
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] OutputServers rows: {outputServerSheet.Dimension?.Rows ?? 0}");
                        LoadOutputServers(outputServerSheet, testStages);
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] OutputServers loaded");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] ⚠️ OutputServers sheet NOT FOUND");
                    }

                    // Debug: Log loaded data
                    foreach (var stage in testStages)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Stage {stage.Key}:");
                        System.Diagnostics.Debug.WriteLine($"  - InputClients: {stage.Value.InputClients.Count}");
                        System.Diagnostics.Debug.WriteLine($"  - OutputClients: {stage.Value.OutputClients.Count}");
                        System.Diagnostics.Debug.WriteLine($"  - OutputServers: {stage.Value.OutputServers.Count}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[FolderManager] ✅ Loaded {testStages.Count} stages from Detail.xlsx");
                return testStages;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] ❌ Error loading Detail.xlsx: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[FolderManager] StackTrace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Helper: Safe convert Excel cell value to int
        /// </summary>
        private int? SafeGetInt(object cellValue)
        {
            if (cellValue == null) return null;

            if (cellValue is int intValue)
                return intValue;

            if (cellValue is double doubleValue)
                return (int)doubleValue;

            if (cellValue is decimal decimalValue)
                return (int)decimalValue;

            if (int.TryParse(cellValue.ToString(), out int parsedValue))
                return parsedValue;

            return null;
        }

        /// <summary>
        /// Helper: Safe convert Excel cell value to string
        /// </summary>
        private string SafeGetString(object cellValue)
        {
            return cellValue?.ToString() ?? "";
        }

        private void LoadInputClients(OfficeOpenXml.ExcelWorksheet worksheet, Dictionary<int, TestStage> testStages)
        {
            int rowCount = worksheet.Dimension?.Rows ?? 0;
            System.Diagnostics.Debug.WriteLine($"[FolderManager] LoadInputClients - Row count: {rowCount}");

            if (rowCount < 2) return;

            int loadedCount = 0;
            for (int row = 2; row <= rowCount; row++)
            {
                try
                {
                    var stage = SafeGetInt(worksheet.Cells[row, 1].Value);

                    if (!stage.HasValue || stage.Value <= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Skipping (invalid Stage: {worksheet.Cells[row, 1].Value})");
                        continue;
                    }

                    if (!testStages.ContainsKey(stage.Value))
                    {
                        testStages[stage.Value] = new TestStage();
                    }

                    var inputClient = new Input_Client
                    {
                        Stage = stage.Value,
                        Action = SafeGetString(worksheet.Cells[row, 2].Value),
                        Input = SafeGetString(worksheet.Cells[row, 3].Value),
                        DataType = SafeGetString(worksheet.Cells[row, 4].Value)
                    };

                    testStages[stage.Value].InputClients.Add(inputClient);
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Error: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[FolderManager] ✅ Loaded {loadedCount} InputClients");
        }

        private void LoadOutputClients(OfficeOpenXml.ExcelWorksheet worksheet, Dictionary<int, TestStage> testStages)
        {
            int rowCount = worksheet.Dimension?.Rows ?? 0;
            System.Diagnostics.Debug.WriteLine($"[FolderManager] LoadOutputClients - Row count: {rowCount}");

            if (rowCount < 2)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] ⚠️ OutputClients sheet is empty (no data rows)");
                return;
            }

            // Debug: Print header row
            System.Diagnostics.Debug.WriteLine($"[FolderManager] OutputClients Headers:");
            for (int col = 1; col <= 7; col++)
            {
                System.Diagnostics.Debug.WriteLine($"  Col {col}: {worksheet.Cells[1, col].Value}");
            }

            int loadedCount = 0;
            for (int row = 2; row <= rowCount; row++)
            {
                try
                {
                    var stage = SafeGetInt(worksheet.Cells[row, 1].Value);

                    if (!stage.HasValue || stage.Value <= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Skipping (invalid Stage: {worksheet.Cells[row, 1].Value})");
                        continue;
                    }

                    if (!testStages.ContainsKey(stage.Value))
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Creating new TestStage for Stage {stage.Value}");
                        testStages[stage.Value] = new TestStage();
                    }

                    var outputClient = new OutputClient
                    {
                        Stage = stage.Value,
                        Method = SafeGetString(worksheet.Cells[row, 2].Value),
                        StatusCode = SafeGetString(worksheet.Cells[row, 3].Value),
                        DataResponse = SafeGetString(worksheet.Cells[row, 4].Value),
                        Output = SafeGetString(worksheet.Cells[row, 5].Value),
                        DataTypeMiddleWare = SafeGetString(worksheet.Cells[row, 6].Value),
                        ByteSize = SafeGetString(worksheet.Cells[row, 7].Value)
                    };

                    testStages[stage.Value].OutputClients.Add(outputClient);
                    loadedCount++;

                    System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Loaded OutputClient: Stage={outputClient.Stage}, Method={outputClient.Method}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Error: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[FolderManager] ✅ Loaded {loadedCount} OutputClients");
        }

        private void LoadOutputServers(OfficeOpenXml.ExcelWorksheet worksheet, Dictionary<int, TestStage> testStages)
        {
            int rowCount = worksheet.Dimension?.Rows ?? 0;
            System.Diagnostics.Debug.WriteLine($"[FolderManager] LoadOutputServers - Row count: {rowCount}");

            if (rowCount < 2)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderManager] ⚠️ OutputServers sheet is empty (no data rows)");
                return;
            }

            // Debug: Print header row
            System.Diagnostics.Debug.WriteLine($"[FolderManager] OutputServers Headers:");
            for (int col = 1; col <= 7; col++)
            {
                System.Diagnostics.Debug.WriteLine($"  Col {col}: {worksheet.Cells[1, col].Value}");
            }

            int loadedCount = 0;
            for (int row = 2; row <= rowCount; row++)
            {
                try
                {
                    var stage = SafeGetInt(worksheet.Cells[row, 1].Value);

                    if (!stage.HasValue || stage.Value <= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Skipping (invalid Stage: {worksheet.Cells[row, 1].Value})");
                        continue;
                    }

                    if (!testStages.ContainsKey(stage.Value))
                    {
                        System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Creating new TestStage for Stage {stage.Value}");
                        testStages[stage.Value] = new TestStage();
                    }

                    var outputServer = new OutputServer
                    {
                        Stage = stage.Value,
                        Method = SafeGetString(worksheet.Cells[row, 2].Value),
                        DataRequest = SafeGetString(worksheet.Cells[row, 4].Value),
                        Output = SafeGetString(worksheet.Cells[row, 5].Value),
                        DataTypeMiddleware = SafeGetString(worksheet.Cells[row, 6].Value),
                        ByteSize = SafeGetString(worksheet.Cells[row, 7].Value)
                    };

                    testStages[stage.Value].OutputServers.Add(outputServer);
                    loadedCount++;

                    System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Loaded OutputServer: Stage={outputServer.Stage}, Method={outputServer.Method}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FolderManager] Row {row} - Error: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[FolderManager] ✅ Loaded {loadedCount} OutputServers");
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