namespace dentist_clinic_api.DTOs.Billing;

public enum GstPriceMode
{
    NotRegistered,
    Inclusive,
    Exclusive
}

public record GstBreakdown(decimal Subtotal, decimal GstRate, decimal GstAmount, decimal Total, bool GstRegistered);
