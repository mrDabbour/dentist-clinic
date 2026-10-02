using System.Data;
using dentist_clinic_api.Data;
using dentist_clinic_api.Models;
using Microsoft.EntityFrameworkCore;

namespace dentist_clinic_api.Services;

public class InvoiceService(DentistDbContext context, BillingSettings settings, TimeProvider clock)
{
    public async Task<Invoice> Issue(int appointmentId, int? patientId, CancellationToken cancellationToken)
    {
        var appointment = await context.Appointments.Include(a => a.Patient).Include(a => a.DentalService)
            .SingleOrDefaultAsync(a => a.Id == appointmentId && (patientId == null || a.PatientId == patientId), cancellationToken);
        if (appointment == null) throw new BillingException(404, "Appointment not found.");
        var existing = await context.Invoices.SingleOrDefaultAsync(i => i.AppointmentId == appointmentId, cancellationToken);
        if (existing != null)
        {
            // Complete supplier tax details later without changing an issued payment amount.
            if (existing.GstRegistered && string.IsNullOrWhiteSpace(existing.SupplierGstNumber) && !string.IsNullOrWhiteSpace(settings.GstNumber))
            {
                existing.SupplierGstNumber = settings.GstNumber;
                await context.SaveChangesAsync(cancellationToken);
            }
            return existing;
        }
        if (appointment.Status is not ("Confirmed" or "Completed")) throw new BillingException(409, "An invoice is available after the clinic confirms your appointment.");
        if (!settings.IsReady) throw new BillingException(409,
            "The clinic has not completed its invoice setup. Please contact the clinic to arrange payment.",
            "BILLING_SETUP_REQUIRED");
        var amounts = GstCalculator.Calculate(appointment.DentalService.Price, settings.PriceMode!.Value);
        var invoice = new Invoice
        {
            AppointmentId = appointment.Id, PatientId = appointment.PatientId,
            SupplierName = settings.SupplierName, SupplierAddress = settings.SupplierAddress,
            SupplierGstNumber = amounts.GstRegistered ? settings.GstNumber : null,
            CustomerName = $"{appointment.Patient.FirstName} {appointment.Patient.LastName}", CustomerEmail = appointment.Patient.Email,
            Description = appointment.DentalService.Name, Subtotal = amounts.Subtotal, GstRate = amounts.GstRate,
            GstAmount = amounts.GstAmount, Total = amounts.Total, GstRegistered = amounts.GstRegistered,
            IssuedAt = clock.GetUtcNow().UtcDateTime,
            PaymentStatus = amounts.Total == 0 ? "Paid" : "Unpaid", PaymentMethod = amounts.Total == 0 ? "NoCharge" : null,
            PaidAt = amounts.Total == 0 ? clock.GetUtcNow().UtcDateTime : null
        };
        context.Invoices.Add(invoice);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            context.ChangeTracker.Clear();
            return await context.Invoices.SingleAsync(i => i.AppointmentId == appointmentId, cancellationToken);
        }
        return invoice;
    }

    public async Task<T> WithLock<T>(int invoiceId, int? patientId, Func<Invoice, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            context.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken);
        var query = context.Database.IsNpgsql()
            ? context.Invoices.FromSqlInterpolated($"SELECT * FROM \"Invoices\" WHERE \"Id\" = {invoiceId} FOR UPDATE")
            : context.Invoices.Where(i => i.Id == invoiceId);
        var invoice = await query.SingleOrDefaultAsync(cancellationToken);
        if (invoice == null || (patientId != null && invoice.PatientId != patientId)) throw new BillingException(404, "Invoice not found.");
        var result = await action(invoice);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task EnsurePayable(Invoice invoice, CancellationToken cancellationToken)
    {
        var status = await context.Appointments.Where(a => a.Id == invoice.AppointmentId).Select(a => a.Status).SingleAsync(cancellationToken);
        if (status is not ("Confirmed" or "Completed")) throw new BillingException(409, "This appointment cannot be paid online. Please contact the clinic.");
    }

    public void SetPaid(Invoice invoice, string method, string? captureId = null)
    {
        invoice.PaymentStatus = "Paid"; invoice.PaymentMethod = method;
        invoice.PayPalCaptureId = captureId; invoice.PaidAt = clock.GetUtcNow().UtcDateTime;
    }
}
