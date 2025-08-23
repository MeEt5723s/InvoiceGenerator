using InvoiceGen.Models;
using InvoiceGen.Repo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

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
            InvoiceNumber = $"INV{DateTime.Now:yyyyMMdd}-{new Random().Next(100, 999)}",
            InvoiceDate = DateTime.Now,
            // Provide a default selected service
            ServiceDescription = InvoiceModel.AvailableServices.First()
        };

        // Pass the list of available services for the dropdown
        ViewBag.ServiceList = new SelectList(InvoiceModel.AvailableServices);

        return View(model);
    }

    [HttpPost]
    public IActionResult GenerateInvoice(InvoiceModel model, string customFileName)
    {
        if (ModelState.IsValid)
        {
            var pdf = _pdfService.GenerateInvoicePdf(model);

            // Use the custom file name if provided, else default
            var fileName = !string.IsNullOrWhiteSpace(customFileName) ? customFileName : $"Invoice-{model.InvoiceNumber}.pdf";

            // Add ".pdf" extension if missing
            if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".pdf";
            }

            // Return the PDF file to download with the chosen file name
            return File(pdf, "application/pdf", fileName);
        }

        ViewBag.ServiceList = new SelectList(InvoiceModel.AvailableServices);

        // If validation fails, show form again with model data
        return View("Index", model);
    }

}