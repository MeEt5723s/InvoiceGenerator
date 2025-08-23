using InvoiceGen.Models;
using System.Reflection;

namespace InvoiceGen.Repo
{
    public interface IPdfService
    {
        byte[] GenerateInvoicePdf(InvoiceModel invoice);
    }
}