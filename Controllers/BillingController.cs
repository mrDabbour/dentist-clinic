using System.Security.Claims;
using System.Text.Json;
using dentist_clinic_api.Data;
using dentist_clinic_api.DTOs.Billing;
using dentist_clinic_api.Models;
using dentist_clinic_api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dentist_clinic_api.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize(Roles = "Patient,Admin,Receptionist")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class BillingController(DentistDbContext context, InvoiceService invoices, BillingSettings settings, IPayPalGateway payPal) : ControllerBase
{
    private int? PatientId()
    {
        if (!User.IsInRole("Patient")) return null;
        if (!int.TryParse(User.FindFirstValue("patientId"), out var id) || id < 1) throw new BillingException(401, "Invalid patient session.");
        return id;
    }
    private object ResponseFor(Invoice invoice, bool canPay) => new { invoice, canPay, payPalAvailable = payPal.IsConfigured,
        bankAccountName = settings.BankAccountName, bankAccountNumber = settings.BankAccountNumber };

    [HttpPost("appointments/{appointmentId:int}/invoice")]
    public async Task<IActionResult> IssueInvoice(int appointmentId, CancellationToken cancellationToken)
    {
        var invoice = await invoices.Issue(appointmentId, PatientId(), cancellationToken);
        var status = await context.Appointments.Where(a => a.Id == appointmentId).Select(a => a.Status).SingleAsync(cancellationToken);
        return Ok(ResponseFor(invoice, status is "Confirmed" or "Completed"));
    }

    [HttpPost("invoices/{id:int}/bank-transfer")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> ReportBankTransfer(int id, BankTransferDto dto, CancellationToken cancellationToken) =>
        Ok(await invoices.WithLock(id, PatientId(), async invoice =>
        {
            if (invoice.PaymentStatus == "Paid") return ResponseFor(invoice, false);
            await invoices.EnsurePayable(invoice, cancellationToken);
            invoice.PaymentStatus = "AwaitingVerification"; invoice.PaymentMethod = "BankTransfer";
            invoice.BankTransferReference = dto.Reference.Trim();
            return ResponseFor(invoice, true);
        }, cancellationToken));

    [HttpPost("invoices/{id:int}/verify-bank-payment")]
    [Authorize(Roles = "Admin,Receptionist")]
    public async Task<IActionResult> VerifyBankPayment(int id, VerifyBankPaymentDto dto, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        return Ok(await invoices.WithLock(id, null, invoice =>
        {
            if (invoice.PaymentStatus == "Paid") throw new BillingException(409, "This invoice is already paid.");
            if (invoice.PaymentStatus != "AwaitingVerification" || invoice.PaymentMethod != "BankTransfer")
                throw new BillingException(409, "No bank transfer is awaiting verification.");
            invoice.VerificationReference = dto.VerificationReference.Trim(); invoice.VerifiedByUserId = userId;
            invoices.SetPaid(invoice, "BankTransfer");
            return Task.FromResult(ResponseFor(invoice, false));
        }, cancellationToken));
    }

    [HttpPost("invoices/{id:int}/paypal-order")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> CreatePayPalOrder(int id, CancellationToken cancellationToken) =>
        Ok(await invoices.WithLock(id, PatientId(), async invoice =>
        {
            await invoices.EnsurePayable(invoice, cancellationToken);
            if (invoice.PaymentStatus is "Paid" or "AwaitingVerification") throw new BillingException(409, "This invoice is already paid or awaiting bank verification.");
            if (invoice.PayPalOrderId == null)
            {
                var approval = await payPal.CreateOrder(invoice, cancellationToken);
                invoice.PayPalOrderId = approval.OrderId; invoice.PayPalApprovalUrl = approval.ApprovalUrl;
            }
            return new { orderId = invoice.PayPalOrderId, approvalUrl = invoice.PayPalApprovalUrl };
        }, cancellationToken));

    [HttpPost("invoices/{id:int}/paypal-capture")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> CapturePayPalOrder(int id, CapturePayPalDto dto, CancellationToken cancellationToken) =>
        Ok(await invoices.WithLock(id, PatientId(), async invoice =>
        {
            if (invoice.PayPalOrderId != dto.OrderId) throw new BillingException(409, "This payment does not match your invoice.");
            if (invoice.PaymentStatus == "Paid") return ResponseFor(invoice, false);
            if (invoice.PaymentStatus == "AwaitingVerification") throw new BillingException(409, "Your bank transfer is awaiting verification. Please contact the clinic.");
            await invoices.EnsurePayable(invoice, cancellationToken);
            var captureId = await payPal.CaptureAndVerify(invoice, cancellationToken);
            invoices.SetPaid(invoice, "PayPal", captureId);
            return ResponseFor(invoice, false);
        }, cancellationToken));

    [HttpGet("staff/invoices")]
    [Authorize(Roles = "Admin,Receptionist")]
    public async Task<IActionResult> StaffInvoices(CancellationToken cancellationToken) =>
        Ok(await context.Invoices.AsNoTracking().OrderByDescending(i => i.IssuedAt).Take(200).ToListAsync(cancellationToken));

    [AllowAnonymous]
    [HttpPost("paypal/webhook")]
    [RequestSizeLimit(262144)]
    public async Task<IActionResult> PayPalWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            if (!await payPal.VerifyWebhook(body, Request.Headers.ToDictionary(h => h.Key.ToUpperInvariant(), h => h.Value.ToString()), cancellationToken)) return BadRequest();
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.GetProperty("event_type").GetString() != "PAYMENT.CAPTURE.COMPLETED") return Ok();
            var resource = json.RootElement.GetProperty("resource");
            var orderId = resource.GetProperty("supplementary_data").GetProperty("related_ids").GetProperty("order_id").GetString();
            var invoice = await context.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.PayPalOrderId == orderId, cancellationToken);
            if (invoice == null) return Ok();
            await invoices.WithLock(invoice.Id, null, locked =>
            {
                if (!PayPalGateway.AmountMatches(resource.GetProperty("amount"), locked)) throw new BillingException(409, "Payment amount mismatch.");
                var captureId = resource.GetProperty("id").GetString();
                if (locked.PaymentStatus == "Paid" && (locked.PaymentMethod != "PayPal" || locked.PayPalCaptureId != captureId))
                    throw new BillingException(409, "An additional payment requires clinic review.");
                if (locked.PaymentStatus != "Paid") invoices.SetPaid(locked, "PayPal", captureId);
                return Task.FromResult(true);
            }, cancellationToken);
            return Ok();
        }
        catch (JsonException) { return BadRequest(); }
        catch (KeyNotFoundException) { return BadRequest(); }
    }
}
