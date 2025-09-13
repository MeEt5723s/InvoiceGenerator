using InvoiceGen.Models;
using InvoiceGen.Repo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
            InvoiceNumber = $"INV{System.DateTime.Now:yyyyMMdd}-{new System.Random().Next(100, 999)}",
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
            var fileName = !string.IsNullOrWhiteSpace(customFileName) ? customFileName : $"Invoice-{model.InvoiceNumber}.pdf";
            if (!fileName.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".pdf";
            }
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
}