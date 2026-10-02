using System.Security.Claims;
using System.Text.Json;
using dentist_clinic_api.Controllers;
using dentist_clinic_api.Data;
using dentist_clinic_api.DTOs.Billing;
using dentist_clinic_api.Models;
using dentist_clinic_api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace dentist_clinic_api.Tests;

public class BillingTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public DentistDbContext Db { get; }
        public BillingSettings Settings { get; }
        public InvoiceService Service { get; }
        public Fixture(bool configured = true, string status = "Confirmed")
        {
            connection.Open();
            Db = new(new DbContextOptionsBuilder<DentistDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            Db.Appointments.Add(new Appointment { Id = 1, Status = status, StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddHours(1),
                Patient = new Patient { Id = 1, FirstName = "Test", LastName = "Patient", Email = "billing@example.test" },
                Dentist = new Dentist { Id = 1, Email = "dentist@example.test", RegistrationNumber = "TEST" },
                DentalService = new DentalService { Id = 1, Name = "Consultation", Price = 115, DurationMinutes = 60 } });
            Db.SaveChanges();
            Settings = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Billing:GstPriceMode"] = configured ? "Inclusive" : null,
                ["Billing:GstNumber"] = "TEST-ONLY", ["Billing:SupplierName"] = "Test Clinic" }).Build());
            Service = new(Db, Settings, TimeProvider.System);
        }
        public BillingController Controller(string role, int patientId = 1) => new(Db, Service, Settings, new FakePayPal()) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                    new Claim(ClaimTypes.Role, role), new Claim("patientId", patientId.ToString()),
                    new Claim(ClaimTypes.NameIdentifier, "42") }, "test")) } } };
        public void Dispose() { Db.Dispose(); connection.Dispose(); }
    }
    private sealed class FakePayPal : IPayPalGateway
    {
        public bool IsConfigured => true;
        public Task<PayPalApproval> CreateOrder(Invoice i, CancellationToken c) => Task.FromResult(new PayPalApproval("ORDER-1", "https://www.sandbox.paypal.com/checkoutnow?token=ORDER-1"));
        public Task<string> CaptureAndVerify(Invoice i, CancellationToken c) => Task.FromResult("CAPTURE-1");
        public Task<bool> VerifyWebhook(string b, IDictionary<string,string> h, CancellationToken c) => Task.FromResult(false);
    }

    [Fact]
    public async Task Invoice_IsOwnedImmutableAndIssuedOnce()
    {
        using var f = new Fixture();
        var invoice = await f.Service.Issue(1, 1, default);
        Assert.Equal(100m, invoice.Subtotal); Assert.Equal(15m, invoice.GstAmount); Assert.Equal(115m, invoice.Total);
        f.Db.DentalServices.Single().Price = 230; await f.Db.SaveChangesAsync();
        Assert.Equal(invoice.Id, (await f.Service.Issue(1, 1, default)).Id);
        Assert.Equal(115m, invoice.Total); Assert.Single(f.Db.Invoices);
        Assert.Equal(404, (await Assert.ThrowsAsync<BillingException>(() => f.Service.Issue(1, 2, default))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<BillingException>(() => f.Service.WithLock(invoice.Id, 2, _ => Task.FromResult(true), default))).StatusCode);
    }
    [Fact]
    public async Task PublicCatalogue_ReturnsOnlyActiveServices()
    {
        using var f = new Fixture();
        f.Db.DentalServices.Add(new DentalService { Name = "Hidden treatment", IsActive = false });
        await f.Db.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>(await new PublicServicesController(f.Db).GetServices(default));
        var services = Assert.IsType<List<dentist_clinic_api.DTOs.DentalServices.DentalServiceResponseDto>>(result.Value);
        Assert.Single(services); Assert.Equal("Consultation", services[0].Name); Assert.Equal(115m, services[0].Price);
    }
    [Fact]
    public async Task PublicClinic_ShowsRealCompletedFiguresAndOnlyPublicDoctorFields()
    {
        using var f = new Fixture(status: "Completed");
        f.Db.Dentists.Add(new Dentist { FirstName = "Hidden", IsActive = false }); await f.Db.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var controller = new PublicClinicController(f.Db, config, new BookingSchedule(config));
        var response = Assert.IsType<OkObjectResult>(await controller.GetInfo(default));
        var info = Assert.IsType<PublicClinicInfo>(response.Value);
        Assert.Equal(1, info.Figures.PatientsHelped); Assert.Equal(1, info.Figures.CompletedVisits);
        Assert.Equal("mohammeddabboornz@gmail.com", info.Email); Assert.Equal("+64225974228", info.TelephoneLink);
        Assert.Empty(info.Reviews);
        var teamResponse = Assert.IsType<OkObjectResult>(await controller.GetTeam(default));
        var team = Assert.IsType<List<PublicDoctor>>(teamResponse.Value); Assert.Single(team);
        var json = JsonSerializer.Serialize(team); Assert.DoesNotContain("Email", json); Assert.DoesNotContain("Phone", json);
    }
    [Theory]
    [InlineData(false, "Confirmed", 409)]
    [InlineData(true, "Pending", 409)]
    public async Task Invoice_RequiresTaxConfigurationAndConfirmation(bool configured, string status, int expected)
    {
        using var f = new Fixture(configured, status);
        var error = await Assert.ThrowsAsync<BillingException>(() => f.Service.Issue(1, 1, default));
        Assert.Equal(expected, error.StatusCode);
        if (!configured) Assert.Equal("BILLING_SETUP_REQUIRED", error.Code);
        Assert.Empty(f.Db.Invoices);
    }
    [Fact]
    public async Task InclusivePaymentRequest_DoesNotRequireGstNumber_AndCompletingDetailsDoesNotRepriceIt()
    {
        using var f = new Fixture();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Billing:GstPriceMode"] = "Inclusive", ["Billing:SupplierName"] = "Test Clinic" }).Build();
        var service = new InvoiceService(f.Db, new BillingSettings(config), TimeProvider.System);
        var invoice = await service.Issue(1, 1, default);
        Assert.Null(invoice.SupplierGstNumber); Assert.Equal(115m, invoice.Total);
        Assert.Equal(100m, invoice.Subtotal); Assert.Equal(15m, invoice.GstAmount);
        await f.Controller("Patient").ReportBankTransfer(invoice.Id, new() { Reference = invoice.Number }, default);
        Assert.Equal("AwaitingVerification", invoice.PaymentStatus);
        f.Db.DentalServices.Single().Price = 230; await f.Db.SaveChangesAsync();
        var completed = await f.Service.Issue(1, 1, default);
        Assert.Equal(invoice.Id, completed.Id); Assert.Equal("TEST-ONLY", completed.SupplierGstNumber);
        Assert.Equal(115m, completed.Total); Assert.Single(f.Db.Invoices);
    }
    [Fact]
    public async Task BankTransfer_RequiresStaffVerificationAndRecordsEvidence()
    {
        using var f = new Fixture(); var invoice = await f.Service.Issue(1, 1, default);
        await f.Controller("Patient").ReportBankTransfer(invoice.Id, new() { Reference = " TEST " }, default);
        Assert.Equal("AwaitingVerification", invoice.PaymentStatus); Assert.Null(invoice.PaidAt);
        await f.Controller("Receptionist").VerifyBankPayment(invoice.Id, new() { VerificationReference = " bank transaction 123 " }, default);
        Assert.Equal("Paid", invoice.PaymentStatus); Assert.Equal("bank transaction 123", invoice.VerificationReference);
        Assert.Equal(42, invoice.VerifiedByUserId); Assert.NotNull(invoice.PaidAt);
        Assert.Equal(409, (await Assert.ThrowsAsync<BillingException>(() => f.Controller("Receptionist").VerifyBankPayment(invoice.Id, new() { VerificationReference = "duplicate" }, default))).StatusCode);
    }
    [Fact]
    public async Task PayPal_RejectsForgedOrderAndMarksPaidOnlyAfterCapture()
    {
        using var f = new Fixture(); var invoice = await f.Service.Issue(1, 1, default); var controller = f.Controller("Patient");
        await controller.CreatePayPalOrder(invoice.Id, default); Assert.Equal("Unpaid", invoice.PaymentStatus);
        Assert.Equal(409, (await Assert.ThrowsAsync<BillingException>(() => controller.CapturePayPalOrder(invoice.Id, new() { OrderId = "FORGED" }, default))).StatusCode);
        Assert.Equal("Unpaid", invoice.PaymentStatus);
        await controller.CapturePayPalOrder(invoice.Id, new() { OrderId = "ORDER-1" }, default);
        Assert.Equal("Paid", invoice.PaymentStatus); Assert.Equal("CAPTURE-1", invoice.PayPalCaptureId);
    }
    [Theory]
    [InlineData("NZD", "115.00", true)]
    [InlineData("USD", "115.00", false)]
    [InlineData("NZD", "1.15", false)]
    public void PayPal_RequiresExactCurrencyAndAmount(string currency, string value, bool matches)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { currency_code = currency, value }));
        Assert.Equal(matches, PayPalGateway.AmountMatches(json.RootElement, new Invoice { Total = 115 }));
    }
}
