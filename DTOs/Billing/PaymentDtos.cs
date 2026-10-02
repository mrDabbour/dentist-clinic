using System.ComponentModel.DataAnnotations;

namespace dentist_clinic_api.DTOs.Billing;
public class BankTransferDto
{
    [Required, MaxLength(100)] public string Reference { get; set; } = "";
}
public class VerifyBankPaymentDto
{
    [Required, MaxLength(200)] public string VerificationReference { get; set; } = "";
}
public class CapturePayPalDto
{
    [Required, MaxLength(100)] public string OrderId { get; set; } = "";
}
