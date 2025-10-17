using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.IO;
using System.IO.Packaging;
using System.Reflection;
using System.Windows;
using UITestKit.Model;
using LicenseContext = OfficeOpenXml.LicenseContext;

public class ExcelExporter
{
    public ExcelExporter()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public void ExportToExcel(string filePath, List<TestStep> steps)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("TestCases");

            // Header
            worksheet.Cells[1, 1].Value = "Step";
            worksheet.Cells[1, 2].Value = "Client Input";
            worksheet.Cells[1, 3].Value = "Client Output";
            worksheet.Cells[1, 4].Value = "Server Output";

            using (var range = worksheet.Cells[1, 1, 1, 4])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }

            // Data
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                worksheet.Cells[i + 2, 1].Value = step.StepNumber;
                worksheet.Cells[i + 2, 2].Value = step.ClientInput;
                worksheet.Cells[i + 2, 3].Value = step.ClientOutput;
                worksheet.Cells[i + 2, 4].Value = step.ServerOutput;
            }

            worksheet.Cells.AutoFitColumns();

            package.SaveAs(new FileInfo(filePath));
        }
    }


    /// <summary>
    /// Export một hoặc nhiều sheet vào cùng 1 file Excel.
    /// </summary>
    /// <param name="filePath">Đường dẫn file cần lưu.</param>
    /// <param name="sheetsData">Danh sách sheet với tên sheet và dữ liệu tương ứng.</param>
    public void ExportToExcelParams(string filePath, params (string SheetName, ICollection<object> Data)[] sheetsData)
    {
        try
        {
            if (sheetsData == null || sheetsData.Length == 0)
                throw new ArgumentException("Không có dữ liệu để xuất.");

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage();

            foreach (var (sheetName, data) in sheetsData)
            {
                if (data == null || !data.Any()) continue;

                var firstItem = data.FirstOrDefault(d => d != null);
                if (firstItem == null) continue;

                var worksheet = package.Workbook.Worksheets.Add(sheetName);

                var properties = firstItem.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

                // ===== HEADER =====
                for (int i = 0; i < properties.Length; i++)
                {
                    worksheet.Cells[1, i + 1].Value = properties[i].Name;
                }

                using (var headerRange = worksheet.Cells[1, 1, 1, properties.Length])
                {
                    headerRange.Style.Font.Bold = true;
                }

                
                // Thay thế LoadFromCollection bằng vòng lặp thủ công để đảm bảo hoạt động với ICollection<object>
                if (data.Any())
                {
                    int currentRow = 2;
                    foreach (var item in data)
                    {
                        if (item == null) continue;
                        for (int i = 0; i < properties.Length; i++)
                        {
                            var value = properties[i].GetValue(item);
                            worksheet.Cells[currentRow, i + 1].Value = value;
                        }
                        currentRow++;
                    }
                }

                // ===== TÙY CHỈNH CỘT (Giữ nguyên) =====
                const double MAX_COLUMN_WIDTH = 60;
                const double MIN_COLUMN_WIDTH = 10;

                for (int i = 1; i <= properties.Length; i++)
                {
                    var column = worksheet.Column(i);
                    var propertyName = properties[i - 1].Name;
                    column.Style.WrapText = true;
                    if ((propertyName.Equals("DataResponse", StringComparison.OrdinalIgnoreCase))
                        || (propertyName.Equals("Output", StringComparison.OrdinalIgnoreCase)))
                    {
                        column.Style.WrapText = true;
                        column.Width = MAX_COLUMN_WIDTH;
                    }else if((propertyName.Equals("DataTypeMiddleWare", StringComparison.OrdinalIgnoreCase))||
                        (propertyName.Equals("DataRequest", StringComparison.OrdinalIgnoreCase)))
                    {
                        column.Style.WrapText = true;
                        column.Width = MIN_COLUMN_WIDTH * 2;
                    }
                    else
                    {
                        column.AutoFit();
                    }

                    if (column.Width > MAX_COLUMN_WIDTH) column.Width = MAX_COLUMN_WIDTH;
                    if (column.Width < MIN_COLUMN_WIDTH) column.Width = MIN_COLUMN_WIDTH;
                }

                if (worksheet.Dimension != null)
                {
                    worksheet.Cells[worksheet.Dimension.Address].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
                }
            }

            package.SaveAs(new FileInfo(filePath));

            MessageBox.Show(
                $"Xuất file Excel thành công!\nĐường dẫn: {filePath}",
                "Thành công",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            // ... (phần xử lý lỗi giữ nguyên)
            try
            {
                string logPath = Path.Combine(Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory, "ExportLog.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Lỗi khi export Excel:\n{ex}\n\n");
                MessageBox.Show($"Xuất Excel thất bại!\nChi tiết lỗi đã được ghi tại:\n{logPath}", "Lỗi Xuất File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                MessageBox.Show($"Xuất Excel thất bại: {ex.Message}", "Lỗi Xuất File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Wrapper method để export TestKit data
    /// </summary>
    public void ExportTestKitData(string filePath, Dictionary<int, TestStage> testStages)
    {
        var sheetsList = new List<(string SheetName, ICollection<object> Data)>();

        // Collect all data from all stages
        var allInputClients = new List<object>();
        var allOutputClients = new List<object>();
        var allOutputServers = new List<object>();

        foreach (var stage in testStages.OrderBy(s => s.Key))
        {
            allInputClients.AddRange(stage.Value.InputClients.Cast<object>());
            allOutputClients.AddRange(stage.Value.OutputClients.Cast<object>());
            allOutputServers.AddRange(stage.Value.OutputServers.Cast<object>());
        }

        if (allInputClients.Any())
            sheetsList.Add(("InputClients", allInputClients));

        if (allOutputClients.Any())
            sheetsList.Add(("OutputClients", allOutputClients));

        if (allOutputServers.Any())
            sheetsList.Add(("OutputServers", allOutputServers));

        // Use existing method
        ExportToExcelParams(filePath, sheetsList.ToArray());
    }
}
