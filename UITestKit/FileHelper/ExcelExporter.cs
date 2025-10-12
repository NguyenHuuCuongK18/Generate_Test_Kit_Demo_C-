using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.IO;
using System.Reflection;
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

            // ===== Đảm bảo thư mục tồn tại =====
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var package = new ExcelPackage();

            foreach (var (sheetName, data) in sheetsData)
            {
                if (data == null || data.Count == 0) continue;

                var firstItem = data.FirstOrDefault(d => d != null);
                if (firstItem == null) continue;

                var worksheet = package.Workbook.Worksheets.Add(sheetName);

                var properties = firstItem.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var getters = properties.Select(p => (Func<object, object>)(obj => p.GetValue(obj))).ToArray();

                // ===== HEADER =====
                for (int i = 0; i < properties.Length; i++)
                    worksheet.Cells[1, i + 1].Value = properties[i].Name;

                using (var headerRange = worksheet.Cells[1, 1, 1, properties.Length])
                {
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }

                // ===== DATA =====
                int row = 2;
                foreach (var item in data)
                {
                    if (item == null) continue;
                    for (int col = 0; col < getters.Length; col++)
                    {
                        var value = getters[col](item);
                        worksheet.Cells[row, col + 1].Value = value?.ToString();
                    }
                    row++;
                }

                if (row < 2000)
                    worksheet.Cells.AutoFitColumns();
            }

            // ===== Lưu file =====
            package.SaveAs(new FileInfo(filePath));
        }
        catch (Exception ex)
        {
            try
            {
                string logPath = Path.Combine(
                    Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    "ExportLog.txt"
                );

                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Lỗi khi export Excel:\n{ex}\n\n");

                System.Windows.MessageBox.Show(
                    $"Xuất Excel thất bại!\nChi tiết lỗi đã được ghi tại:\n{logPath}",
                    "Export Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
            }
            catch
            {
                // Nếu ghi log cũng lỗi thì ít nhất hiển thị message
                System.Windows.MessageBox.Show($"Export Excel thất bại: {ex.Message}",
                    "Export Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }

}
