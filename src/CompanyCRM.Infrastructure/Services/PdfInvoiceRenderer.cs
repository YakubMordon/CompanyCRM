using System.Globalization;
using System.Text;
using CompanyCRM.Domain.Entities;

namespace CompanyCRM.Infrastructure.Services;

public static class PdfInvoiceRenderer
{
    public static byte[] Render(Invoice invoice)
    {
        var content = BuildPageContentStream(invoice);
        var contentBytes = Encoding.ASCII.GetBytes(content);

        var objects = new List<string>
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> >>\nendobj\n",
            $"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n{content}endstream\nendobj\n",
            "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
            "6 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>\nendobj\n"
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        var offsets = new List<int> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append(obj);
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Count + 1}\n");
        sb.Append("0000000000 65535 f \n");
        for (int i = 1; i <= objects.Count; i++)
        {
            sb.Append($"{offsets[i]:D10} 00000 n \n");
        }

        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\n");
        sb.Append($"startxref\n{xrefOffset}\n%%EOF\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string BuildPageContentStream(Invoice invoice)
    {
        var sb = new StringBuilder();
        sb.Append("BT\n");

        // Header
        AppendText(sb, "F2", 20, 50, 740, "COMPANYCRM INVOICE");
        AppendText(sb, "F1", 11, 50, 718, $"Invoice Number: {invoice.InvoiceNumber}");
        AppendText(sb, "F1", 11, 50, 702, $"Project: {invoice.Project?.Name ?? "Project"} ({invoice.Project?.Code ?? "PRJ"})");
        AppendText(sb, "F1", 11, 50, 686, $"Billing Period: {invoice.Year}-{invoice.Month:D2}");
        AppendText(sb, "F1", 11, 50, 670, $"Status: {invoice.Status}   Due Date: {invoice.DueDate:yyyy-MM-dd}");

        // Table Header
        int y = 630;
        AppendText(sb, "F2", 11, 50, y, "Team Member");
        AppendText(sb, "F2", 11, 250, y, "Hours");
        AppendText(sb, "F2", 11, 340, y, "Rate");
        AppendText(sb, "F2", 11, 440, y, "Amount");
        y -= 18;

        foreach (var line in invoice.LineItems.Take(25))
        {
            AppendText(sb, "F1", 10, 50, y, Truncate(line.EmployeeName, 32));
            AppendText(sb, "F1", 10, 250, y, line.Hours.ToString("0.00", CultureInfo.InvariantCulture));
            AppendText(sb, "F1", 10, 340, y, $"${line.HourlyRate.ToString("0.00", CultureInfo.InvariantCulture)}");
            AppendText(sb, "F1", 10, 440, y, $"${line.Amount.ToString("0.00", CultureInfo.InvariantCulture)}");
            y -= 16;
        }

        // Totals
        y -= 16;
        AppendText(sb, "F1", 11, 340, y, "Total Hours:");
        AppendText(sb, "F1", 11, 440, y, invoice.TotalBillableHours.ToString("0.00", CultureInfo.InvariantCulture));
        y -= 16;
        AppendText(sb, "F1", 11, 340, y, "Subtotal:");
        AppendText(sb, "F1", 11, 440, y, $"${invoice.SubtotalAmount.ToString("0.00", CultureInfo.InvariantCulture)}");
        y -= 16;
        AppendText(sb, "F1", 11, 340, y, $"Tax ({invoice.TaxRatePercent:0.##}%):");
        AppendText(sb, "F1", 11, 440, y, $"${invoice.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture)}");
        y -= 18;
        AppendText(sb, "F2", 12, 340, y, "Total Due:");
        AppendText(sb, "F2", 12, 440, y, $"${invoice.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture)} {invoice.Currency}");

        sb.Append("ET\n");
        return sb.ToString();
    }

    private static void AppendText(StringBuilder sb, string font, int size, int x, int y, string text)
    {
        var escaped = text
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)");
        sb.Append($"/{font} {size} Tf 1 0 0 1 {x} {y} Tm ({escaped}) Tj\n");
    }

    private static string Truncate(string value, int maxLen) =>
        value.Length <= maxLen ? value : value[..maxLen];
}
