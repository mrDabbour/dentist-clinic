using dentist_clinic_api.DTOs.Billing;

namespace dentist_clinic_api.Services;

public class BillingSettings(IConfiguration configuration)
{
    public string SupplierName => configuration["Billing:SupplierName"] ?? "Dintest Clinic";
    public string SupplierAddress => configuration["Billing:SupplierAddress"] ?? "";
    public string? GstNumber => configuration["Billing:GstNumber"];
    public GstPriceMode? PriceMode => Enum.TryParse<GstPriceMode>(configuration["Billing:GstPriceMode"], true, out var mode) && Enum.IsDefined(mode) ? mode : null;
    public bool IsReady => PriceMode.HasValue && !string.IsNullOrWhiteSpace(SupplierName);
    public string BankAccountName => configuration["Billing:BankAccountName"] ?? "Mohammed Dabboor";
    public string BankAccountNumber => configuration["Billing:BankAccountNumber"] ?? "15-39530983855000";
    public string FrontendOrigin
    {
        get
        {
            var uri = new Uri(configuration["Billing:FrontendOrigin"] ?? "http://localhost:4200");
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
                throw new InvalidOperationException("Billing frontend must use HTTPS or localhost.");
            return uri.GetLeftPart(UriPartial.Authority);
        }
    }
}

public class BillingException(int statusCode, string message, string? code = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
}
