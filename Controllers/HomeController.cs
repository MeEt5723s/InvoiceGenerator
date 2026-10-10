using ClosedXML.Excel;
using InvoiceGen.Models;
using InvoiceGen.Repo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Linq;

public class HomeController : Controller
{
    private readonly IPdfService _pdfService;

    public HomeController(IPdfService pdfService)
    {
        _pdfService = pdfService;
    }

    public IActionResult Index()
    {
        var model = new InvoiceModel
        {
            InvoiceDate = System.DateTime.Now,
            // Provide a default selected service
            ServiceDescription = InvoiceModel.AvailableServices.First()
        };
        ViewBag.ServiceList = new SelectList(InvoiceModel.AvailableServices);
        return View(model);
    }

    [HttpPost]
    public IActionResult GenerateInvoice(InvoiceModel model, string customFileName)
    {
        // Additional server-side validation for custom service
        if (model.ServiceDescription == "Other" && string.IsNullOrWhiteSpace(model.CustomServiceDescription))
        {
            ModelState.AddModelError(nameof(model.CustomServiceDescription),
                "Custom service description is required when 'Other' is selected.");
        }

        if (ModelState.IsValid)
        {
            var pdf = _pdfService.GenerateInvoicePdf(model);

            // Default: "INV0072-Deepak_Soni". A name typed by the user wins, but is sanitized.
            var fileName = !string.IsNullOrWhiteSpace(customFileName)
                ? SanitizeForFileName(System.IO.Path.GetFileNameWithoutExtension(customFileName))
                : BuildPdfBaseName(model.InvoiceNumber, model.BillToName);
            fileName += ".pdf";

            // Return PDF for download
            return File(pdf, "application/pdf", fileName);
        }

        ViewBag.ServiceList = new SelectList(InvoiceModel.AvailableServices, model.ServiceDescription);
        return View("Index", model);
    }

    [HttpPost]
    public IActionResult PreviewInvoice(InvoiceModel model)
    {
        // Additional server-side validation for custom service
        if (model.ServiceDescription == "Other" && string.IsNullOrWhiteSpace(model.CustomServiceDescription))
        {
            ModelState.AddModelError(nameof(model.CustomServiceDescription),
                "Custom service description is required when 'Other' is selected.");
        }

        if (!ModelState.IsValid)
        {
            // Return empty response with 400 Bad Request so new tab stays blank
            Response.StatusCode = 400;
            return Content(string.Empty);
        }

        var pdf = _pdfService.GenerateInvoicePdf(model);
        // Stream PDF to new tab
        return File(pdf, "application/pdf");
    }

    // Column order the Excel importer expects. Kept in one place so the
    // template generator and the parser can never drift apart.
    private static readonly string[] TemplateHeaders = new[]
    {
        "Invoice Number (optional)",
        "Invoice Date (dd/mm/yyyy)",
        "Customer Name *",
        "Address *",
        "City",
        "State",
        "Country",
        "Contact Number *",
        "Email *",
        "Service Description *",
        "Custom Service Description (only if Service = Other)",
        "Price *",
        "Taxable Amount",
        "Received Amount",
        "Disclaimer Text",
        "Refundable (Yes/No)",
        "GST No"
    };

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Invoices");

        for (int i = 0; i < TemplateHeaders.Length; i++)
        {
            ws.Cell(1, i + 1).Value = TemplateHeaders[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        // One example row so the expected format/values are obvious.
        ws.Cell(2, 1).Value = "";
        ws.Cell(2, 2).Value = "11/09/2026";
        ws.Cell(2, 3).Value = "John Smith";
        ws.Cell(2, 4).Value = "12 Example St";
        ws.Cell(2, 5).Value = "Melbourne";
        ws.Cell(2, 6).Value = "VIC";
        ws.Cell(2, 7).Value = "Australia";
        ws.Cell(2, 8).Value = "0400 000 000";
        ws.Cell(2, 9).Value = "john@example.com";
        ws.Cell(2, 10).Value = InvoiceModel.AvailableServices.First();
        ws.Cell(2, 11).Value = "";
        ws.Cell(2, 12).Value = 1500;
        ws.Cell(2, 13).Value = 1500;
        ws.Cell(2, 14).Value = 0;
        ws.Cell(2, 15).Value = "";
        ws.Cell(2, 16).Value = "No";
        ws.Cell(2, 17).Value = "";

        // Data-validation dropdown on the Service Description column so people
        // don't mistype a value that doesn't match InvoiceModel.AvailableServices.
        // Excel's inline List() string is capped at 255 characters, and our service
        // names are long enough to blow past that — so the values go on a hidden
        // helper sheet instead, and the validation points at that range.
        var listsSheet = workbook.Worksheets.Add("Lists");
        var services = InvoiceModel.AvailableServices;
        for (int i = 0; i < services.Count; i++)
        {
            listsSheet.Cell(i + 1, 1).Value = services[i];
        }
        listsSheet.Visibility = XLWorksheetVisibility.VeryHidden;

        var serviceRange = listsSheet.Range(1, 1, services.Count, 1);
        var dv = ws.Range("J2:J1000").CreateDataValidation();
        dv.List(serviceRange);

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "InvoiceImportTemplate.xlsx");
    }

    [HttpPost]
    public IActionResult ImportExcel(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            return Json(new { success = false, message = "No file was uploaded." });
        }

        var invoices = new List<InvoiceModel>();
        var rowErrors = new List<string>();

        try
        {
            using var stream = new MemoryStream();
            excelFile.CopyTo(stream);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.First();

            // Skip the header row; stop at the first fully blank row.
            var dataRows = worksheet.RowsUsed().Skip(1);

            int rowNumber = 1;
            foreach (var row in dataRows)
            {
                rowNumber++;

                string GetStr(int col) => row.Cell(col).GetString().Trim();

                var billToName = GetStr(3);
                // Treat a row with no customer name as blank/decorative and skip it silently.
                if (string.IsNullOrWhiteSpace(billToName)) continue;

                var invoice = new InvoiceModel
                {
                    // Left blank if the sheet doesn't provide one - it's optional.
                    InvoiceNumber = GetStr(1),
                    BillToName = billToName,
                    BillToAddress = GetStr(4),
                    BillToCity = GetStr(5),
                    BillToState = GetStr(6),
                    BillToCountry = GetStr(7),
                    BillToContact = GetStr(8),
                    BillToEmail = GetStr(9),
                    ServiceDescription = GetStr(10),
                    CustomServiceDescription = GetStr(11),
                    Disclaimer = GetStr(15),
                    IsRefundable = GetStr(16).Equals("Yes", StringComparison.OrdinalIgnoreCase),
                    // Optional - stays empty (null) if the cell is blank.
                    BillToGST = string.IsNullOrWhiteSpace(GetStr(17)) ? null : GetStr(17)
                };

                // Invoice Date (column 2)
                var dateCell = row.Cell(2);
                invoice.InvoiceDate = dateCell.TryGetValue(out DateTime parsedDate) ? parsedDate : DateTime.Now;

                // Numeric columns
                invoice.Price = row.Cell(12).TryGetValue(out decimal price) ? price : 0;
                invoice.TaxableAmount = row.Cell(13).TryGetValue(out decimal taxable) ? taxable : 0;
                invoice.ReceivedAmount = row.Cell(14).TryGetValue(out decimal received) ? received : 0;

                // Validate against the same rules the manual form uses.
                var context = new ValidationContext(invoice);
                var results = new List<ValidationResult>();
                bool isValid = Validator.TryValidateObject(invoice, context, results, true);
                results.AddRange(invoice.Validate(context));

                if (!isValid || results.Any())
                {
                    var messages = results.Select(r => r.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m));
                    rowErrors.Add($"Row {rowNumber} ({billToName}): {string.Join("; ", messages)}");
                    continue;
                }

                invoices.Add(invoice);
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Could not read the Excel file: " + ex.Message });
        }

        if (!invoices.Any() && !rowErrors.Any())
        {
            return Json(new { success = false, message = "No usable rows were found in the file." });
        }

        return Json(new { success = true, invoices, errors = rowErrors });
    }

    [HttpPost]
    public IActionResult GenerateAllInvoices([FromBody] List<InvoiceModel> invoices)
    {
        if (invoices == null || !invoices.Any())
        {
            return BadRequest("No invoices were provided.");
        }

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var invoice in invoices)
            {
                var context = new ValidationContext(invoice);
                var results = new List<ValidationResult>();
                bool isValid = Validator.TryValidateObject(invoice, context, results, true);
                results.AddRange(invoice.Validate(context));
                if (!isValid || results.Any())
                {
                    continue; // skip rows that don't pass validation
                }

                var pdf = _pdfService.GenerateInvoicePdf(invoice);

                // "INV0072/2026-27" + "Deepak Soni" -> "INV0072-Deepak_Soni"
                var baseName = BuildPdfBaseName(invoice.InvoiceNumber, invoice.BillToName);
                var entryName = baseName + ".pdf";
                int suffix = 1;
                while (!usedNames.Add(entryName))
                {
                    entryName = $"{baseName}-{++suffix}.pdf";
                }

                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(pdf, 0, pdf.Length);
            }
        }

        zipStream.Position = 0;
        return File(zipStream.ToArray(), "application/zip", $"Invoices_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
    }

    private static string SanitizeForFileName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "Invoice";
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Invoice" : cleaned;
    }

    // "INV0072/2026-27" + "Deepak Soni"  ->  "INV0072-Deepak_Soni"
    // Only the part of the invoice number before the first slash is used.
    private static string BuildPdfBaseName(string? invoiceNumber, string? customerName)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars()
            .Concat(new[] { '/', '\\' })
            .ToHashSet();

        string Clean(string s) =>
            new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        // Keep only what comes before the first slash
        var numberPart = (invoiceNumber ?? "").Split('/', '\\')[0].Trim();
        numberPart = Clean(numberPart);

        // Spaces in the name become underscores
        var nameParts = (customerName ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var namePart = Clean(string.Join("_", nameParts));

        var parts = new[] { numberPart, namePart }
            .Where(p => !string.IsNullOrWhiteSpace(p));

        var result = string.Join("-", parts);
        return string.IsNullOrWhiteSpace(result) ? "Invoice" : result;
    }
}