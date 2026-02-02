using System.ComponentModel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace _05_multi_agent_system_advance;

public class MedicalReportExporter
{
    [Description("Saves the medical report into a professional PDF file. Call this only ONCE per report.")]
    public string SaveReportToPdf(
        [Description("The full text content of the medical report")]
        string reportContent,
        [Description("The patient's actual full name extracted from the conversation (e.g., 'Juan Palomo', 'Herbert Heartstone'). Use 'Unknown_Patient' ONLY if no name was provided in the conversation.")]
        string patientName = "Unknown_Patient")
    {
        try
        {
            // Set the license to Community (Free for personal/small projects)
            QuestPDF.Settings.License = LicenseType.Community;

            string fileName = $"Report_{patientName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.pdf";

            // Generate the PDF using a fluent API
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(50);
                    page.Header().Text("WEEKLY MEDICAL REPORT").FontSize(20).SemiBold().FontColor(Colors.Blue.Medium);

                    page.Content().PaddingVertical(10).Column(column =>
                    {
                        column.Item().Text($"Date: {DateTime.Now:D}").Italic();
                        column.Item().PaddingTop(10).Text(reportContent).FontSize(12);
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                    });
                });
            }).GeneratePdf(fileName);

            return $"Success: PDF report saved as {fileName}";
        }
        catch (Exception ex)
        {
            return $"Error creating PDF: {ex.Message}";
        }
    }
}