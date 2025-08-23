using InvoiceGen.Models;
using InvoiceGen.Repo;
using Microsoft.AspNetCore.Mvc;

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
            InvoiceDate = DateTime.Now
        };

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