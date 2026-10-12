using System.ComponentModel.DataAnnotations;
using InvoiceGen.Models;
using Xunit;

// These tests pin down the GST / totals maths and the validation rules.
// The invoice bot's AI agent is NOT allowed to edit this folder.
// If one of these fails after a change, the change broke the invoice maths.
namespace InvoiceGen.Tests
{
    public class InvoiceModelTests
    {
        private static InvoiceModel Make(decimal price, decimal taxable, decimal received = 0) =>
            new InvoiceModel
            {
                BillToName = "Test Customer",
                ServiceDescription = "IELTS coaching",
                Price = price,
                TaxableAmount = taxable,
                ReceivedAmount = received
            };

        // ---------------------------------------------------------- tax maths

        [Fact]
        public void TaxRate_IsFixedAt18Percent()
        {
            Assert.Equal(18m, Make(100, 100).TaxRate);
        }

        [Fact]
        public void Totals_AreCorrect_ForRound1500()
        {
            var m = Make(price: 1500, taxable: 1500);
            Assert.Equal(270m, m.TaxAmount);
            Assert.Equal(135m, m.CGSTAmount);
            Assert.Equal(135m, m.SGSTAmount);
            Assert.Equal(1770m, m.NetAmount);
            Assert.Equal(1770m, m.DueAmount);
        }

        [Fact]
        public void Totals_AreCorrect_WithPartPayment()
        {
            var m = Make(price: 1000, taxable: 1000, received: 500);
            Assert.Equal(180m, m.TaxAmount);
            Assert.Equal(1180m, m.NetAmount);
            Assert.Equal(680m, m.DueAmount);
        }

        [Fact]
        public void Tax_IsOnTaxableAmount_NotOnPrice()
        {
            var m = Make(price: 2000, taxable: 1000);
            Assert.Equal(180m, m.TaxAmount);
            Assert.Equal(1180m, m.NetAmount);
        }

        [Fact]
        public void CgstPlusSgst_EqualsTotalTax()
        {
            var m = Make(price: 12345.67m, taxable: 12345.67m);
            Assert.Equal(m.TaxAmount, m.CGSTAmount + m.SGSTAmount);
        }

        [Fact]
        public void ZeroTaxable_GivesZeroTax()
        {
            var m = Make(price: 500, taxable: 0);
            Assert.Equal(0m, m.TaxAmount);
            Assert.Equal(0m, m.NetAmount);
        }

        [Fact]
        public void FullyPaid_GivesZeroDue()
        {
            var m = Make(price: 1000, taxable: 1000, received: 1180);
            Assert.Equal(0m, m.DueAmount);
        }

        // ---------------------------------------------------------- validation

        private static List<ValidationResult> Validate(InvoiceModel m) =>
            m.Validate(new ValidationContext(m)).ToList();

        [Fact]
        public void ValidInvoice_HasNoErrors()
        {
            Assert.Empty(Validate(Make(1500, 1500, 100)));
        }

        [Fact]
        public void Taxable_GreaterThanPrice_IsRejected()
        {
            var errors = Validate(Make(price: 1000, taxable: 1500));
            Assert.Contains(errors, e => e.MemberNames.Contains(nameof(InvoiceModel.TaxableAmount)));
        }

        [Fact]
        public void Received_GreaterThanNet_IsRejected()
        {
            // Net is 1180, so 1181 is too much.
            var errors = Validate(Make(price: 1000, taxable: 1000, received: 1181));
            Assert.Contains(errors, e => e.MemberNames.Contains(nameof(InvoiceModel.ReceivedAmount)));
        }

        [Fact]
        public void Received_EqualToNet_IsAccepted()
        {
            Assert.Empty(Validate(Make(price: 1000, taxable: 1000, received: 1180)));
        }

        [Fact]
        public void OtherService_WithoutCustomText_IsRejected()
        {
            var m = Make(1000, 1000);
            m.ServiceDescription = "Other";
            m.CustomServiceDescription = "  ";
            var errors = Validate(m);
            Assert.Contains(errors, e => e.MemberNames.Contains(nameof(InvoiceModel.CustomServiceDescription)));
        }

        // ---------------------------------------------------------- service text

        [Fact]
        public void FinalServiceDescription_UsesCustomText_WhenOther()
        {
            var m = Make(1000, 1000);
            m.ServiceDescription = "Other";
            m.CustomServiceDescription = "Document translation";
            Assert.Equal("Document translation", m.FinalServiceDescription);
        }

        [Fact]
        public void FinalServiceDescription_UsesSelectedService_Normally()
        {
            var m = Make(1000, 1000);
            m.ServiceDescription = "PTE coaching";
            m.CustomServiceDescription = "ignored";
            Assert.Equal("PTE coaching", m.FinalServiceDescription);
        }

        // ---------------------------------------------------------- company constants

        [Fact]
        public void CompanyGstNumber_IsUnchanged()
        {
            Assert.Equal("24GOIPS7235K1ZC", Make(1, 1).GSTNumber);
        }

        [Fact]
        public void RefundableStatus_Text()
        {
            var m = Make(1, 1);
            Assert.Equal("Nonrefundable", m.RefundableStatus);
            m.IsRefundable = true;
            Assert.Equal("Refundable", m.RefundableStatus);
        }
    }
}
