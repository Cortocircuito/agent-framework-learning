using System.ComponentModel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace _07_shared_state_memory;

public class MedicalReportExporter
{
    // Set license once at class level
    static MedicalReportExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Description("Saves the medical report into a professional PDF file. Call this only ONCE per report.")]
    public string SaveReportToPdf(
        [Description("The full text content of the medical report")]
        string reportContent,
        [Description("The patient's actual full name extracted from the conversation (e.g., 'Juan Palomo', 'Herbert Heartstone'). Use 'Unknown_Patient' ONLY if no name was provided in the conversation.")]
        string patientName = "Unknown_Patient")
    {
        try
        {
            // Validate report content
            if (string.IsNullOrWhiteSpace(reportContent))
                return "Error: Cannot create PDF with empty report content.";

            if (reportContent.Length < 50)
                return "Error: Report content seems too short (minimum 50 characters expected).";

            // Sanitize filename to prevent path traversal
            string sanitizedName = SanitizePatientName(patientName);

            // Build filename with timestamp including time to avoid collisions
            string fileName = $"Report_{sanitizedName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

            // Ensure files go to a safe, dedicated directory
            string safeOutputDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "MedicalReports"
            );
            Directory.CreateDirectory(safeOutputDir);

            string fullPath = Path.Combine(safeOutputDir, fileName);

            // Prevent overwriting existing files
            int counter = 1;
            while (File.Exists(fullPath))
            {
                fileName = $"Report_{sanitizedName}_{DateTime.Now:yyyyMMdd_HHmmss}_{counter}.pdf";
                fullPath = Path.Combine(safeOutputDir, fileName);
                counter++;
            }

            // Generate the PDF using a professional medical report format
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Black));

                    // Header
                    page.Header().Column(column =>
                    {
                        column.Item().BorderBottom(2).BorderColor(Colors.Blue.Darken2).PaddingBottom(10).Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("MEDICAL REPORT").FontSize(24).SemiBold().FontColor(Colors.Blue.Darken2);
                                col.Item().Text("Confidential Patient Record").FontSize(10).Italic().FontColor(Colors.Grey.Darken1);
                            });

                            row.ConstantItem(100).AlignRight().Column(col =>
                            {
                                col.Item().Text(DateTime.Now.ToString("dd MMM yyyy")).FontSize(10).SemiBold();
                                col.Item().Text(DateTime.Now.ToString("HH:mm")).FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });

                        column.Item().PaddingTop(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                    });

                    // Content
                    page.Content().PaddingVertical(15).Column(column =>
                    {
                        // Patient Information Box
                        column.Item().Background(Colors.Blue.Lighten4).Padding(15).Column(patientBox =>
                        {
                            patientBox.Item().Text("PATIENT INFORMATION").FontSize(13).SemiBold().FontColor(Colors.Blue.Darken3);
                            patientBox.Item().PaddingTop(5).Text(txt =>
                            {
                                txt.Span("Name: ").SemiBold();
                                txt.Span(patientName).FontColor(Colors.Blue.Darken2);
                            });
                            patientBox.Item().Text(txt =>
                            {
                                txt.Span("Report Date: ").SemiBold();
                                txt.Span(DateTime.Now.ToString("D"));
                            });
                        });

                        // Main Report Content
                        column.Item().PaddingTop(20).Column(content =>
                        {
                            content.Item().Text("CLINICAL REPORT").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken2);
                            content.Item().PaddingTop(2).LineHorizontal(1).LineColor(Colors.Blue.Darken2);

                            content.Item().PaddingTop(15).Text(reportContent)
                                .FontSize(11)
                                .LineHeight(1.5f)
                                .Justify();
                        });

                        // Confidentiality Notice
                        column.Item().PaddingTop(30).Background(Colors.Grey.Lighten3).Padding(10).Text(
                            "CONFIDENTIAL: This document contains privileged medical information. " +
                            "Unauthorized access, use, or disclosure is prohibited by law.")
                            .FontSize(8)
                            .Italic()
                            .FontColor(Colors.Grey.Darken2);
                    });

                    // Footer
                    page.Footer().BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Text(txt =>
                        {
                            txt.Span("Generated: ").FontSize(8).FontColor(Colors.Grey.Darken1);
                            txt.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).FontSize(8).FontColor(Colors.Grey.Darken1);
                        });

                        row.ConstantItem(100).AlignCenter().Text(txt =>
                        {
                            txt.Span("Page ").FontSize(9);
                            txt.CurrentPageNumber().FontSize(9);
                            txt.Span(" of ").FontSize(9);
                            txt.TotalPages().FontSize(9);
                        });
                    });
                });
            }).GeneratePdf(fullPath);

            return $"Success: PDF report saved to {fullPath}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Error: Permission denied writing to file - {ex.Message}";
        }
        catch (IOException ex)
        {
            return $"Error: File I/O error - {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error creating PDF: {ex.Message}";
        }
    }

    /// <summary>
    /// Sanitizes patient name to create a safe filename, preventing path traversal attacks.
    /// </summary>
    private static string SanitizePatientName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Unknown_Patient";

        // Remove all invalid filename characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = new string(input
            .Where(c => !invalidChars.Contains(c))
            .ToArray());

        // Explicitly remove path separators (defense in depth)
        sanitized = sanitized
            .Replace("/", "")
            .Replace("\\", "")
            .Replace("..", "");

        // Replace spaces with underscores
        sanitized = sanitized.Replace(" ", "_");

        // Limit length to prevent filesystem issues
        if (sanitized.Length > 50)
            sanitized = sanitized.Substring(0, 50);

        // Ensure we have a valid result
        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown_Patient" : sanitized;
    }
}
