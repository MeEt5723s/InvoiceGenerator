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
    public IActionResult GenerateInvoice(InvoiceModel model)
    {
        if (ModelState.IsValid)
        {
            var pdf = _pdfService.GenerateInvoicePdf(model);
            return File(pdf, "application/pdf", $"Invoice-{model.InvoiceNumber}.pdf");
        }

        return View("Index", model);
    }
}