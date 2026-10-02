using System.Net;
using System.Text.Json;
using dentist_clinic_api.Models;
using dentist_clinic_api.Services;
using Microsoft.Extensions.Configuration;

namespace dentist_clinic_api.Tests;

public class PayPalVerificationTests
{
    private sealed class Provider(string customId, string currency, string amount, string status) : HttpMessageHandler
    {
        public int CaptureCalls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api-m.sandbox.paypal.com", request.RequestUri!.Host);
            object response;
            if (request.RequestUri.AbsolutePath == "/v1/oauth2/token") response = new { access_token = "test-only" };
            else
            {
                if (request.Method == HttpMethod.Post) CaptureCalls++;
                response = new { id = "ORDER-1", status = "COMPLETED", purchase_units = new[] {
                    new { custom_id = customId, payments = new { captures = new[] {
                        new { id = "CAPTURE-1", status, amount = new { currency_code = currency, value = amount } } } } } } };
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response)) });
        }
    }
    [Theory]
    [InlineData("1", "NZD", "115.00", "COMPLETED", true)]
    [InlineData("99", "NZD", "115.00", "COMPLETED", false)]
    [InlineData("1", "USD", "115.00", "COMPLETED", false)]
    [InlineData("1", "NZD", "1.15", "COMPLETED", false)]
    [InlineData("1", "NZD", "115.00", "PENDING", false)]
    public async Task VerifiesProviderResponseAndRecoversAlreadyCapturedOrder(string customId, string currency, string amount, string status, bool valid)
    {
        using var provider = new Provider(customId, currency, amount, status); using var http = new HttpClient(provider);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["PayPal:ClientId"] = "test-client", ["PayPal:ClientSecret"] = "test-secret" }).Build();
        var gateway = new PayPalGateway(http, config, new BillingSettings(config));
        var invoice = new Invoice { Id = 1, Total = 115, PayPalOrderId = "ORDER-1" };
        if (valid) Assert.Equal("CAPTURE-1", await gateway.CaptureAndVerify(invoice, default));
        else Assert.Equal(409, (await Assert.ThrowsAsync<BillingException>(() => gateway.CaptureAndVerify(invoice, default))).StatusCode);
        Assert.Equal(0, provider.CaptureCalls);
    }
}
