using dentist_clinic_api.Data;
using dentist_clinic_api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dentist_clinic_api.Controllers;

public record PublicDoctor(int Id, string FirstName, string LastName, string Specialty, string? Biography);
public record PublicReview(string Author, string Quote);
public record ClinicFigures(int PatientsHelped, int CompletedVisits, int ActiveDentists, int AvailableServices);
public record PublicClinicInfo(string Name, string Email, string Phone, string TelephoneLink,
    string OpensAt, string ClosesAt, string[] WorkingDays, string TimeZone, ClinicFigures Figures, PublicReview[] Reviews);

[ApiController]
[Route("api/public/clinic")]
[AllowAnonymous]
public class PublicClinicController(DentistDbContext context, IConfiguration configuration, BookingSchedule schedule) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetInfo(CancellationToken cancellationToken)
    {
        var completed = context.Appointments.AsNoTracking().Where(a => a.Status == "Completed");
        var figures = new ClinicFigures(
            await completed.Select(a => a.PatientId).Distinct().CountAsync(cancellationToken),
            await completed.CountAsync(cancellationToken),
            await context.Dentists.CountAsync(d => d.IsActive, cancellationToken),
            await context.DentalServices.CountAsync(s => s.IsActive, cancellationToken));
        var reviews = configuration.GetSection("Clinic:Reviews").Get<PublicReview[]>() ?? [];
        return Ok(new PublicClinicInfo(configuration["Billing:SupplierName"] ?? "Dintest Clinic",
            configuration["Clinic:Email"] ?? "mohammeddabboornz@gmail.com",
            configuration["Clinic:Phone"] ?? "022 597 4228",
            configuration["Clinic:TelephoneLink"] ?? "+64225974228",
            schedule.OpensAt.ToString("HH:mm"), schedule.ClosesAt.ToString("HH:mm"),
            schedule.WorkingDays.Select(d => d.ToString()).ToArray(), schedule.TimeZoneId, figures,
            reviews.Where(r => !string.IsNullOrWhiteSpace(r.Author) && !string.IsNullOrWhiteSpace(r.Quote)).ToArray()));
    }

    [HttpGet("team")]
    public async Task<IActionResult> GetTeam(CancellationToken cancellationToken) =>
        Ok(await context.Dentists.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.LastName).ThenBy(d => d.FirstName)
            .Select(d => new PublicDoctor(d.Id, d.FirstName, d.LastName, d.Specialty, d.Biography)).ToListAsync(cancellationToken));
}
