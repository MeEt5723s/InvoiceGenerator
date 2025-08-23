// Models/InvoiceModel.cs
using System.ComponentModel.DataAnnotations;

namespace InvoiceGen.Models
{
    public class InvoiceModel
    {
        [Required]
        public string InvoiceNumber { get; set; } = string.Empty;

        [Required]
        public DateTime InvoiceDate { get; set; } = DateTime.Now;

        // Bill To Details
        [Required]
        public string BillToName { get; set; } = string.Empty;
        public string BillToAddress { get; set; } = string.Empty;
        public string? BillToCity { get; set; } = string.Empty;
        public string? BillToState { get; set; } = string.Empty;
        public string? BillToCountry { get; set; } = string.Empty;
        public string? BillToContact { get; set; } = string.Empty;
        public string BillToEmail { get; set; } = string.Empty;

        // Company Details - These are now constants
        public string CompanyName => "Anil Associates T/As Aussizz Group";
        public string CompanyAddress => "1st Floor, 105 Atlantic,\nNear Gordia Circle,\nVADODARA-390020,\nGUJARAT\nINDIA";
        public string GSTNumber => "24GOIPS7235K1ZC";

        // Service Details - This is now a constant
        [Required]
        public string ServiceDescription { get; set; } = string.Empty;

        // And optionally a static list of possible services (or populate dynamically)
        public static List<string> AvailableServices => new List<string>
{
    "Visitor Visa - Australia (600) Tourist Stream",
    "Business Visa - Australia (subclass 600)",
    "Tourist Visa - Canada",
    "Student Visa - UK",
    "Work Visa - USA"
};


        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Taxable amount must be greater than 0")]
        public decimal TaxableAmount { get; set; }

        // Fixed tax rate - cannot be changed
        public decimal TaxRate => 18;

        [Range(0, double.MaxValue, ErrorMessage = "Received amount cannot be negative")]
        public decimal ReceivedAmount { get; set; }
        public decimal Price { get; set; }
        // Calculated Properties
        public decimal TaxAmount => (TaxableAmount * TaxRate) / 100;
        public decimal CGSTAmount => TaxAmount / 2;
        public decimal SGSTAmount => TaxAmount / 2;
        public decimal NetAmount => TaxableAmount + TaxAmount;
        public decimal DueAmount => NetAmount - ReceivedAmount;

        public string Disclaimer { get; set; } = string.Empty;
        public bool IsRefundable { get; set; } = false; // false = Non-refundable, true = Refundable

        public string RefundableStatus => IsRefundable ? "Refundable" : "Nonrefundable";


    }
}