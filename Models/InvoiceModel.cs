// Models/InvoiceModel.cs
using System.ComponentModel.DataAnnotations;

namespace InvoiceGen.Models
{
    public class InvoiceModel : IValidatableObject
    {
        [Required]
        public string InvoiceNumber { get; set; } = string.Empty;

        [Required]
        public DateTime InvoiceDate { get; set; } = DateTime.Now;

        // Bill To Details
        [Required]
        public string BillToName { get; set; } = string.Empty;

        public string BillToAddress { get; set; } = string.Empty;

        // Made these properly nullable - removed [Required] and made them nullable strings
        public string? BillToCity { get; set; }
        public string? BillToState { get; set; }
        public string? BillToCountry { get; set; }
        public string BillToContact { get; set; } = string.Empty;
        public string BillToEmail { get; set; } = string.Empty;

        // Company Details - These are now constants
        public string CompanyName => "Ami Associates T/As Aussizz Group";
        public string CompanyAddress => "1st Floor, 105 Atlantis,\nNear Genda Circle,\nVADODARA-390020,\nGUJARAT\nINDIA";
        public string GSTNumber => "24GOIPS7235K1ZC";

        // Service Details - Modified to support custom service description
        [Required]
        public string ServiceDescription { get; set; } = string.Empty;

        // New property for custom service description when "Other" is selected
        public string? CustomServiceDescription { get; set; }

        // And optionally a static list of possible services (or populate dynamically)
        public static List<string> AvailableServices => new List<string>
        {
            "IELTS coaching",
            "PTE coaching",
            "Partner Temporary Provision Visa (Sub Class 820/320)",
            "Skilled Independent Visa (Sub Class - 189)",
            "Skilled Nominated Visa (Sub Class - 190)",
            "Skilled Work Regional (Provisional) Visa (Sub Class - 491)",
            "Student Visa (Sub Class - 500)",
            "UK Dependent Visa",
            "Canada Dependent Visa",
            "Dependent Visa",
            "Visitor Visa",
            "Other"
        };

        // Property to get the final service description (either selected or custom)
        public string FinalServiceDescription
        {
            get
            {
                return ServiceDescription == "Other" && !string.IsNullOrWhiteSpace(CustomServiceDescription)
                    ? CustomServiceDescription
                    : ServiceDescription;
            }
        }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than 0")]
        public decimal Price { get; set; }

        [Range(0, double.MaxValue)]
        public decimal TaxableAmount { get; set; }

        // Fixed tax rate - cannot be changed
        public decimal TaxRate => 18;

        [Range(0, double.MaxValue, ErrorMessage = "Received amount cannot be negative.")]
        public decimal ReceivedAmount { get; set; }

        // Calculated Properties
        public decimal TaxAmount => (TaxableAmount * TaxRate) / 100;
        public decimal CGSTAmount => TaxAmount / 2;
        public decimal SGSTAmount => TaxAmount / 2;
        public decimal NetAmount => TaxableAmount + TaxAmount;
        public decimal DueAmount => NetAmount - ReceivedAmount;

        // Made disclaimer nullable
        public string? Disclaimer { get; set; }
        public bool IsRefundable { get; set; } = false; // false = Non-refundable, true = Refundable

        public string RefundableStatus => IsRefundable ? "Refundable" : "Nonrefundable";

        // Custom validation method
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();

            // Validate that Taxable Amount is not greater than Price
            if (TaxableAmount > Price)
            {
                results.Add(new ValidationResult(
                    "Taxable amount cannot be greater than the price.",
                    new[] { nameof(TaxableAmount) }));
            }

            // Validate that Received Amount is not greater than Net Amount
            if (ReceivedAmount > NetAmount)
            {
                results.Add(new ValidationResult(
                    $"Received amount cannot be greater than the net amount (₹{NetAmount:N2}).",
                    new[] { nameof(ReceivedAmount) }));
            }

            // Validate custom service description when "Other" is selected
            if (ServiceDescription == "Other" && string.IsNullOrWhiteSpace(CustomServiceDescription))
            {
                results.Add(new ValidationResult(
                    "Custom service description is required when 'Other' is selected.",
                    new[] { nameof(CustomServiceDescription) }));
            }

            return results;
        }
    }
}