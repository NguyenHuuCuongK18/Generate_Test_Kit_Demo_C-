using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
}
