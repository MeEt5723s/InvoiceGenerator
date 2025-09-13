using InvoiceGen.Models;
using iText.Html2pdf;
using iText.Kernel.Pdf;

namespace InvoiceGen.Repo
{
    public class PdfService : IPdfService
    {
        //public byte[] GenerateInvoicePdf(InvoiceModel invoice)
        //{
        //    var html = GenerateInvoiceHtml(invoice);

        //    using var memoryStream = new MemoryStream();
        //    var pdfWriter = new PdfWriter(memoryStream);
        //    var pdfDocument = new PdfDocument(pdfWriter);

        //    HtmlConverter.ConvertToPdf(html, pdfDocument);

        //    return memoryStream.ToArray();
        //}
        private readonly IWebHostEnvironment _environment;
        public PdfService(IWebHostEnvironment environment)
        {
            _environment = environment;
        }
        public byte[] GenerateInvoicePdf(InvoiceModel invoice)
        {
            var html = GenerateInvoiceHtml(invoice);

            using var memoryStream = new MemoryStream();
            HtmlConverter.ConvertToPdf(html, memoryStream);

            return memoryStream.ToArray();
        }

        //        private string GenerateInvoiceHtml(InvoiceModel invoice)
        //        {
        //            var logoPath = Path.Combine(_environment.WebRootPath, "images", "logo.jpg");
        //            var logoBase64 = "";

        //            if (File.Exists(logoPath))
        //            {
        //                var logoBytes = File.ReadAllBytes(logoPath);
        //                logoBase64 = Convert.ToBase64String(logoBytes);
        //            }

        //            var logoHtml = !string.IsNullOrEmpty(logoBase64)
        //                ? $"<img src='data:image/jpeg;base64,{logoBase64}' alt='Company Logo' style='width: 90px; height: 90px;' />"
        //                : "<div style='width: 90px; height: 90px; border: 2px solid #000; border-radius: 50%; text-align: center; line-height: 86px; font-weight: bold; font-size: 24px;'>🦘</div>";

        //            // Conditional city+state HTML
        //            string cityState = "";
        //            if (!string.IsNullOrWhiteSpace(invoice.BillToCity) || !string.IsNullOrWhiteSpace(invoice.BillToState))
        //            {
        //                var city = invoice.BillToCity ?? "";
        //                var state = invoice.BillToState ?? "";
        //                cityState = $"<p>{city}{(string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state) ? "" : ", ")}{state}</p>";
        //            }

        //            // Conditional country HTML
        //            string country = !string.IsNullOrWhiteSpace(invoice.BillToCountry)
        //                ? $"<p>{invoice.BillToCountry}</p>"
        //                : "";

        //            // Conditional disclaimer section HTML
        //            string disclaimerSection = !string.IsNullOrWhiteSpace(invoice.Disclaimer)
        //                ? $@"
        //        <div class='disclaimer'>
        //            <h4>Disclaimer:</h4>
        //            <p>{invoice.Disclaimer}</p>
        //            <p><strong>{invoice.RefundableStatus}</strong></p>
        //        </div>"
        //                : $@"
        //        <div class='disclaimer'>
        //            <p><strong>{invoice.RefundableStatus}</strong></p>
        //        </div>";

        //            return $@"
        //<!DOCTYPE html>
        //<html>
        //<head>
        //    <meta charset='utf-8'>
        //    <style>
        //        body {{
        //            font-family: Arial, Helvetica, sans-serif;
        //            margin: 32px;
        //            font-size: 13px;
        //        }}
        //        .main-container {{
        //            width: 90%;
        //            margin-left: auto;
        //            margin-right: auto;
        //        }}
        //        .header {{
        //            text-align: center;
        //            margin-bottom: 18px;
        //        }}
        //        .header h2 {{
        //            font-size: 20px;
        //            font-weight: bold;
        //            margin: 0;
        //            background: #f0f0f0;
        //            color: #222;
        //            padding: 10px 0;
        //            border-radius: 4px;
        //        }}
        //        .top-row {{
        //            display: flex;
        //            justify-content: space-between;
        //            align-items: flex-start;
        //            margin-bottom: 20px;
        //        }}
        //        .logo-area {{
        //            flex: 0 0 90px;
        //            margin-right: 10px;
        //        }}
        //        .invoice-info {{
        //            flex: 1 1 auto;
        //            text-align: right;
        //            margin-top: 10px;
        //            font-size: 13px;
        //        }}
        //        .invoice-info p {{
        //            margin: 0 0 6px 0;
        //        }}
        //        .bill-company-section {{
        //            width: 100%;
        //            display: table;
        //            margin-bottom: 12px;
        //        }}
        //        .bill-to, .company-details {{
        //            display: table-cell;
        //            vertical-align: top;
        //            padding-right: 24px;
        //            padding-bottom: 10px;
        //            font-size: 13px;
        //        }}
        //        .bill-to h3 {{
        //            font-size: 15px;
        //            font-weight: bold;
        //            margin: 0 0 7px 0;
        //        }}
        //        .company-details {{
        //            text-align: right;
        //            padding-left: 24px;
        //        }}
        //        .table-section {{
        //            width: 100%;
        //        }}
        //        .services-table {{
        //            width: 100%;
        //            table-layout: fixed;
        //            border-collapse: collapse;
        //            margin: 24px 0 10px 0;
        //            word-wrap: break-word;
        //        }}
        //        .services-table th, .services-table td {{
        //            border: 1px solid #888;
        //            padding: 7px 6px;
        //            font-size: 13px;
        //            overflow-wrap: break-word;
        //            white-space: normal;
        //            vertical-align: middle;
        //        }}
        //        .services-table th {{
        //            background-color: #f5f5f5;
        //            font-weight: bold;
        //            text-align:left;
        //            white-space: normal;
        //        }}
        //        .totals-table {{
        //            width: 45%;
        //            float: right;
        //            border-collapse: collapse;
        //            margin: 22px 0 0 0;
        //            font-size: 13px;
        //        }}
        //        .totals-table td {{
        //            border: 1px solid #888;
        //            padding: 8px 6px;
        //        }}
        //        .totals-table tr td:first-child {{
        //            font-weight: bold;
        //        }}
        //        .disclaimer {{
        //            margin-top: 54px;
        //            font-size: 13px;
        //            clear: both;
        //            width: 100%;
        //        }}
        //        .disclaimer h4 {{
        //            margin-top: 0;
        //            font-size: 14px;
        //            font-weight: bold;
        //        }}
        //        .clearfix {{ clear: both; }}
        //    </style>
        //</head>
        //<body>
        //    <div class='main-container'>
        //        <div class='header'>
        //            <h2>TAX INVOICE</h2>
        //        </div>
        //        <div class='top-row'>
        //            <div class='logo-area'>
        //                {logoHtml}
        //            </div>
        //            <div class='invoice-info'>
        //                <p><strong>Invoice No:</strong> {invoice.InvoiceNumber}</p>
        //                <p><strong>Invoice Date:</strong> {invoice.InvoiceDate:dd/MM/yyyy}</p>
        //            </div>
        //        </div>
        //        <div class='bill-company-section'>
        //            <div class='bill-to'>
        //                <h3>Bill To:</h3>
        //                <p><strong>{invoice.BillToName}</strong></p>
        //                <p>{invoice.BillToAddress}</p>
        //                {cityState}
        //                {country}
        //                <p><strong>Contact No:</strong> {invoice.BillToContact}</p>
        //                <p><strong>Email:</strong> {invoice.BillToEmail}</p>
        //            </div>
        //            <div class='company-details'>
        //                <p><strong>{invoice.CompanyName}</strong></p>
        //                <p>{invoice.CompanyAddress.Replace("\n", "<br>")}</p>
        //                <p><strong>GST No:</strong> {invoice.GSTNumber}</p>
        //            </div>
        //        </div>
        //        <div class='clearfix'></div>
        //        <div class='table-section'>
        //            <table class='services-table'>
        //                <thead>
        //                    <tr>
        //                        <th>Services</th>
        //                        <th>Price</th>
        //                        <th>Taxable Amount</th>
        //                        <th>Tax</th>
        //                        <th>Tax Amount</th>
        //                        <th>Net Amount</th>
        //                    </tr>
        //                </thead>
        //                <tbody>
        //                    <tr>
        //                        <td>{invoice.ServiceDescription}</td>
        //                        <td>INR {invoice.Price:N2}</td>
        //                        <td>INR {invoice.TaxableAmount:N2}</td>
        //                        <td>{invoice.TaxRate:N1}%</td>
        //                        <td>INR {invoice.TaxAmount:N2}</td>
        //                        <td>INR {invoice.NetAmount:N2}</td>
        //                    </tr>
        //                </tbody>
        //            </table>
        //        </div>
        //        <table class='totals-table'>
        //            <tr>
        //                <td>Taxable Total</td>
        //                <td>INR {invoice.TaxableAmount:N2}</td>
        //            </tr>
        //            <tr>
        //                <td>CGST ({invoice.TaxRate / 2:N1}%)</td>
        //                <td>INR {invoice.CGSTAmount:N2}</td>
        //            </tr>
        //            <tr>
        //                <td>SGST ({invoice.TaxRate / 2:N1}%)</td>
        //                <td>INR {invoice.SGSTAmount:N2}</td>
        //            </tr>
        //            <tr>
        //                <td>Net Amount</td>
        //                <td><strong>INR {invoice.NetAmount:N2}</strong></td>
        //            </tr>
        //            <tr>
        //                <td>Receive Amount</td>
        //                <td>INR {invoice.ReceivedAmount:N2}</td>
        //            </tr>
        //            <tr>
        //                <td>Due Amount</td>
        //                <td><strong>INR {invoice.DueAmount:N2}</strong></td>
        //            </tr>
        //        </table>

        //        {disclaimerSection}

        //    </div>
        //</body>
        //</html>";
        //        }
        private string GenerateInvoiceHtml(InvoiceModel invoice)
        {
            var logoPath = Path.Combine(_environment.WebRootPath, "images", "logo.jpg");
            var logoBase64 = "";

            if (File.Exists(logoPath))
            {
                var logoBytes = File.ReadAllBytes(logoPath);
                logoBase64 = Convert.ToBase64String(logoBytes);
            }

            var logoHtml = !string.IsNullOrEmpty(logoBase64)
                ? $"<img src='data:image/jpeg;base64,{logoBase64}' alt='Company Logo' style='width: 90px; height: 90px;' />"
                : "<div style='width: 90px; height: 90px; border: 2px solid #000; border-radius: 50%; text-align: center; line-height: 86px; font-weight: bold; font-size: 24px;'>🦘</div>";

            // Conditional city+state HTML
            string cityState = "";
            if (!string.IsNullOrWhiteSpace(invoice.BillToCity) || !string.IsNullOrWhiteSpace(invoice.BillToState))
            {
                var city = invoice.BillToCity ?? "";
                var state = invoice.BillToState ?? "";
                cityState = $"<p>{city}{(string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state) ? "" : ", ")}{state}</p>";
            }

            // Conditional country HTML
            string country = !string.IsNullOrWhiteSpace(invoice.BillToCountry)
                ? $"<p>{invoice.BillToCountry}</p>"
                : "";

            // Conditional disclaimer section HTML
            string disclaimerSection = !string.IsNullOrWhiteSpace(invoice.Disclaimer)
                ? $@"
    <div class='disclaimer'>
        <h4>Disclaimer:</h4>
        <p>{invoice.Disclaimer}</p>
        <p><strong>{invoice.RefundableStatus}</strong></p>
    </div>"
                : $@"
    <div class='disclaimer'>
        <p><strong>{invoice.RefundableStatus}</strong></p>
    </div>";

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{
            font-family: Arial, Helvetica, sans-serif;
            margin: 8px;
            font-size: 13px;
        }}
        .main-container {{
            width: 90%;
            margin-left: auto;
            margin-right: auto;
        }}
        .header {{
            text-align: center;
            margin-bottom: 18px;
        }}
        .header h2 {{
            font-size: 20px;
            font-weight: bold;
            margin: 0;
            background: #f0f0f0;
            color: #222;
            padding: 10px 0;
            border-radius: 4px;
        }}
        .top-row {{
            display: flex;
            justify-content: space-between;
            align-items: flex-start;
            margin-bottom: 20px;
        }}
        .logo-area {{
            flex: 0 0 90px;
            margin-right: 10px;
        }}
        .invoice-info {{
            flex: 1 1 auto;
            text-align: right;
            margin-top: 10px;
            font-size: 13px;
        }}
        .invoice-info p {{
            margin: 0 0 6px 0;
        }}
        .bill-company-section {{
            width: 100%;
            display: table;
            margin-bottom: 12px;
        }}
        .bill-to, .company-details {{
            display: table-cell;
            vertical-align: top;
            padding-right: 24px;
            padding-bottom: 10px;
            font-size: 13px;
        }}
        .bill-to h3 {{
            font-size: 15px;
            font-weight: bold;
            margin: 0 0 7px 0;
        }}
        .company-details {{
            text-align: right;
            padding-left: 24px;
        }}
        .table-section {{
            width: 100%;
        }}
        .services-table {{
            width: 100%;
            table-layout: fixed;
            border-collapse: collapse;
            margin: 24px 0 10px 0;
            word-wrap: break-word;
        }}
        .services-table th, .services-table td {{
            border: 1px solid #888;
            padding: 7px 6px;
            font-size: 13px;
            overflow-wrap: break-word;
            white-space: normal;
            vertical-align: middle;
        }}
        .services-table th {{
            background-color: #f5f5f5;
            font-weight: bold;
            text-align:left;
            white-space: normal;
        }}
        .services-table .service-description {{
            word-break: break-word;
            max-width: 150px;
        }}
        .totals-table {{
            width: 45%;
            float: right;
            border-collapse: collapse;
            margin: 22px 0 0 0;
            font-size: 13px;
        }}
        .totals-table td {{
            border: 1px solid #888;
            padding: 8px 6px;
        }}
        .totals-table tr td:first-child {{
            font-weight: bold;
        }}
        .disclaimer {{
            margin-top: 60px;
            font-size: 13px;
            clear: both;
            width: 100%;
            border-top: 2px solid #888;
            padding-top: 25px;
        }}
        .disclaimer h4 {{
            margin-top: 0;
            font-size: 14px;
            font-weight: bold;
        }}
        .clearfix {{ clear: both; }}
    </style>
</head>
<body>
    <div class='main-container'>
        <div class='header'>
            <h2>TAX INVOICE</h2>
        </div>
        <div class='top-row'>
            <div class='logo-area'>
                {logoHtml}
            </div>
            <div class='invoice-info'>
                <p><strong>Invoice No:</strong> {invoice.InvoiceNumber}</p>
                <p><strong>Invoice Date:</strong> {invoice.InvoiceDate:dd/MM/yyyy}</p>
            </div>
        </div>
        <div class='bill-company-section'>
            <div class='bill-to'>
                <h3>Bill To:</h3>
                <p>{invoice.BillToName}</p>
                <p>{invoice.BillToAddress}</p>
                {cityState}
                {country}
                <p><strong>Contact No:</strong> {invoice.BillToContact}</p>
                <p><strong>Email:</strong> {invoice.BillToEmail}</p>
            </div>
            <div class='company-details'>
                <p><strong>{invoice.CompanyName}</strong></p>
                <p>{invoice.CompanyAddress.Replace("\n", "<br>")}</p>
                <p><strong>GST No:</strong> {invoice.GSTNumber}</p>
            </div>
        </div>
        <div class='clearfix'></div>
        <div class='table-section'>
            <table class='services-table'>
                <thead>
                    <tr>
                        <th>Services</th>
                        <th>Price</th>
                        <th>Taxable Amount</th>
                        <th>Tax</th>
                        <th>Tax Amount</th>
                        <th>Net Amount</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td class='service-description'>{System.Web.HttpUtility.HtmlEncode(invoice.FinalServiceDescription)}</td>
                        <td>INR {invoice.Price:N2}</td>
                        <td>INR {invoice.TaxableAmount:N2}</td>
                        <td>{invoice.TaxRate:N1}%</td>
                        <td>INR {invoice.TaxAmount:N2}</td>
                        <td>INR {invoice.NetAmount:N2}</td>
                    </tr>
                </tbody>
            </table>
        </div>
        <table class='totals-table'>
            <tr>
                <td>Taxable Total</td>
                <td>INR {invoice.TaxableAmount:N2}</td>
            </tr>
            <tr>
                <td>CGST ({invoice.TaxRate / 2:N1}%)</td>
                <td>INR {invoice.CGSTAmount:N2}</td>
            </tr>
            <tr>
                <td>SGST ({invoice.TaxRate / 2:N1}%)</td>
                <td>INR {invoice.SGSTAmount:N2}</td>
            </tr>
            <tr>
                <td>Net Amount</td>
                <td><strong>INR {invoice.NetAmount:N2}</strong></td>
            </tr>
            <tr>
                <td>Receive Amount</td>
                <td>INR {invoice.ReceivedAmount:N2}</td>
            </tr>
            <tr>
                <td>Due Amount</td>
                <td><strong>INR {invoice.DueAmount:N2}</strong></td>
            </tr>
        </table>

        <div style='height: 20px; clear: both;'></div>

        {disclaimerSection}

    </div>
</body>
</html>";
        }
    }
}
