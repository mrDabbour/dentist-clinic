namespace dentist_clinic_api.Models;

public class Invoice
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public string Number => $"DC-{Id:D6}";
    public string SupplierName { get; set; } = "";
    public string SupplierAddress { get; set; } = "";
    public string? SupplierGstNumber { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal GstRate { get; set; }
    public decimal GstAmount { get; set; }
    public decimal Total { get; set; }
    public bool GstRegistered { get; set; }
    public string Currency { get; set; } = "NZD";
    public string PaymentStatus { get; set; } = "Unpaid";
    public string? PaymentMethod { get; set; }
    public string? BankTransferReference { get; set; }
    public string? VerificationReference { get; set; }
    public int? VerifiedByUserId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? PayPalApprovalUrl { get; set; }
    public string? PayPalCaptureId { get; set; }
    public Guid PaymentRequestId { get; set; } = Guid.NewGuid();
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
}
