using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using dentist_clinic_api.Models;

namespace dentist_clinic_api.Services;

public record PayPalApproval(string OrderId, string ApprovalUrl);
public interface IPayPalGateway
{
    bool IsConfigured { get; }
    Task<PayPalApproval> CreateOrder(Invoice invoice, CancellationToken cancellationToken);
    Task<string> CaptureAndVerify(Invoice invoice, CancellationToken cancellationToken);
    Task<bool> VerifyWebhook(string body, IDictionary<string, string> headers, CancellationToken cancellationToken);
}

public class PayPalGateway(HttpClient http, IConfiguration configuration, BillingSettings settings) : IPayPalGateway
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["PayPal:ClientId"]) && !string.IsNullOrWhiteSpace(configuration["PayPal:ClientSecret"]);
    private bool Live => configuration.GetValue("PayPal:Live", false);
    private string BaseUrl => Live ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    private static object Money(decimal value) => new { currency_code = "NZD", value = value.ToString("F2", CultureInfo.InvariantCulture) };
    private static string RequestId(Invoice invoice, string operation) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invoice.PaymentRequestId + operation)))[..32];

    private async Task<string> AccessToken(CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new BillingException(503, "PayPal is temporarily unavailable. Please use bank transfer or contact the clinic.");
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(configuration["PayPal:ClientId"] + ":" + configuration["PayPal:ClientSecret"])));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new BillingException(503, "PayPal is temporarily unavailable. Please try again later.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<JsonDocument> Send(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessToken(cancellationToken));
        if (requestId != null) request.Headers.Add("PayPal-Request-Id", requestId);
        request.Headers.Add("Prefer", "return=representation");
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new BillingException(502, "PayPal could not complete the request. Please try again or contact the clinic.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task<PayPalApproval> CreateOrder(Invoice invoice, CancellationToken cancellationToken)
    {
        var unit = new Dictionary<string, object>
        {
            ["reference_id"] = invoice.Id.ToString(CultureInfo.InvariantCulture), ["custom_id"] = invoice.Id.ToString(CultureInfo.InvariantCulture),
            ["invoice_id"] = invoice.Number,
            ["amount"] = new { currency_code = invoice.Currency, value = invoice.Total.ToString("F2", CultureInfo.InvariantCulture),
                breakdown = new { item_total = Money(invoice.Subtotal), tax_total = Money(invoice.GstAmount) } },
            ["items"] = new[] { new { name = "Clinic appointment payment", quantity = "1", unit_amount = Money(invoice.Subtotal), tax = Money(invoice.GstAmount) } }
        };
        if (!string.IsNullOrWhiteSpace(configuration["PayPal:MerchantId"])) unit["payee"] = new { merchant_id = configuration["PayPal:MerchantId"] };
        using var order = await Send(HttpMethod.Post, "/v2/checkout/orders", new
        {
            intent = "CAPTURE", purchase_units = new[] { unit },
            payment_source = new { paypal = new { experience_context = new
            {
                user_action = "PAY_NOW", shipping_preference = "NO_SHIPPING",
                return_url = $"{settings.FrontendOrigin}/invoice/{invoice.AppointmentId}?paypal=return",
                cancel_url = $"{settings.FrontendOrigin}/invoice/{invoice.AppointmentId}?paypal=cancel"
            } } }
        }, RequestId(invoice, "create"), cancellationToken);
        var root = order.RootElement;
        var approval = root.GetProperty("links").EnumerateArray().First(l => l.GetProperty("rel").GetString() is "approve" or "payer-action").GetProperty("href").GetString()!;
        var uri = new Uri(approval);
        if (uri.Scheme != "https" || uri.Host != (Live ? "www.paypal.com" : "www.sandbox.paypal.com"))
            throw new BillingException(502, "PayPal returned an invalid checkout link.");
        return new(root.GetProperty("id").GetString()!, approval);
    }

    public async Task<string> CaptureAndVerify(Invoice invoice, CancellationToken cancellationToken)
    {
        var path = "/v2/checkout/orders/" + Uri.EscapeDataString(invoice.PayPalOrderId!);
        using var current = await Send(HttpMethod.Get, path, null, null, cancellationToken);
        // Recover a successful capture if the previous request ended before its database commit.
        if (current.RootElement.GetProperty("status").GetString() == "COMPLETED")
            return VerifiedCapture(current.RootElement, invoice);
        using var captured = await Send(HttpMethod.Post, path + "/capture", new { }, RequestId(invoice, "capture"), cancellationToken);
        // Verify the order on the server; approval and browser redirects alone are not payment evidence.
        using var verified = await Send(HttpMethod.Get, path, null, null, cancellationToken);
        return VerifiedCapture(verified.RootElement, invoice);
    }

    private static string VerifiedCapture(JsonElement root, Invoice invoice)
    {
        if (root.GetProperty("id").GetString() != invoice.PayPalOrderId || root.GetProperty("status").GetString() != "COMPLETED")
            throw new BillingException(409, "PayPal payment is not completed yet.");
        var units = root.GetProperty("purchase_units").EnumerateArray().ToArray();
        if (units.Length != 1 || units[0].GetProperty("custom_id").GetString() != invoice.Id.ToString(CultureInfo.InvariantCulture))
            throw new BillingException(409, "The PayPal payment does not match this invoice.");
        var captures = units[0].GetProperty("payments").GetProperty("captures").EnumerateArray().ToArray();
        if (captures.Length != 1) throw new BillingException(409, "This payment requires clinic review.");
        var capture = captures[0];
        if (capture.GetProperty("status").GetString() != "COMPLETED" || !AmountMatches(capture.GetProperty("amount"), invoice))
            throw new BillingException(409, "The PayPal payment amount does not match this invoice.");
        return capture.GetProperty("id").GetString()!;
    }

    public static bool AmountMatches(JsonElement amount, Invoice invoice) =>
        amount.GetProperty("currency_code").GetString() == invoice.Currency &&
        decimal.TryParse(amount.GetProperty("value").GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value == invoice.Total;

    public async Task<bool> VerifyWebhook(string body, IDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        var webhookId = configuration["PayPal:WebhookId"];
        if (string.IsNullOrWhiteSpace(webhookId)) throw new BillingException(503, "Payment notification service is unavailable.");
        var required = new[] { "PAYPAL-AUTH-ALGO", "PAYPAL-CERT-URL", "PAYPAL-TRANSMISSION-ID", "PAYPAL-TRANSMISSION-SIG", "PAYPAL-TRANSMISSION-TIME" };
        if (required.Any(h => !headers.TryGetValue(h, out var value) || string.IsNullOrWhiteSpace(value))) return false;
        using var json = JsonDocument.Parse(body);
        using var response = await Send(HttpMethod.Post, "/v1/notifications/verify-webhook-signature", new
        {
            auth_algo = headers[required[0]], cert_url = headers[required[1]], transmission_id = headers[required[2]],
            transmission_sig = headers[required[3]], transmission_time = headers[required[4]], webhook_id = webhookId,
            webhook_event = json.RootElement.Clone()
        }, null, cancellationToken);
        return response.RootElement.GetProperty("verification_status").GetString() == "SUCCESS";
    }
}
