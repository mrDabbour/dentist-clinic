using System.Security.Claims;
using dentist_clinic_api.Data;
using dentist_clinic_api.DTOs.Appointments;
using dentist_clinic_api.DTOs.PatientBooking;
using dentist_clinic_api.Services;
using dentist_clinic_api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dentist_clinic_api.Controllers;

[ApiController]
[Route("api/patient-booking")]
[Authorize(Roles = "Patient")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PatientBookingController(DentistDbContext context, BookingSchedule schedule, TimeProvider clock) : ControllerBase
{
    private bool TryGetPatientId(out int patientId) =>
        int.TryParse(User.FindFirstValue("patientId"), out patientId) && patientId > 0;

    [HttpGet("config")]
    public IActionResult GetConfiguration()
    {
        var today = schedule.Today(clock.GetUtcNow());
        return Ok(new { timeZone = schedule.TimeZoneId, minDate = today, maxDate = today.AddDays(schedule.DaysAhead),
            opensAt = schedule.OpensAt.ToString("HH:mm"), closesAt = schedule.ClosesAt.ToString("HH:mm"),
            workingDays = schedule.WorkingDays.Select(d => d.ToString()), schedule.SlotIntervalMinutes });
    }

    [HttpGet("dentists")]
    public async Task<IActionResult> GetDentists(CancellationToken cancellationToken) =>
        Ok(await context.Dentists.AsNoTracking().Where(d => d.IsActive)
            .OrderBy(d => d.FirstName).ThenBy(d => d.LastName)
            .Select(d => new { d.Id, d.FirstName, d.LastName, d.Specialty, d.Biography })
            .ToListAsync(cancellationToken));

    [HttpGet("slots")]
    public async Task<IActionResult> GetSlots([FromQuery] int dentalServiceId, [FromQuery] int dentistId,
        [FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        if (!TryGetPatientId(out var patientId)) return Unauthorized(new { message = "Invalid patient session." });
        if (!schedule.IsBookableDate(date, clock.GetUtcNow()))
            return BadRequest(new { message = "Choose a date within the booking window." });
        var service = await context.DentalServices.AsNoTracking().SingleOrDefaultAsync(s => s.Id == dentalServiceId && s.IsActive, cancellationToken);
        if (service == null || service.DurationMinutes <= 0)
            return BadRequest(new { message = "The selected service is unavailable." });
        if (!await context.Dentists.AnyAsync(d => d.Id == dentistId && d.IsActive, cancellationToken))
            return BadRequest(new { message = "The selected dentist is unavailable." });

        var candidates = schedule.Candidates(date, service.DurationMinutes, clock.GetUtcNow()).ToList();
        if (candidates.Count == 0) return Ok(new { date, timeZone = schedule.TimeZoneId, slots = Array.Empty<object>() });
        var first = candidates[0];
        var last = candidates[^1].AddMinutes(service.DurationMinutes);
        var busy = await context.Appointments.AsNoTracking()
            .Where(a => a.Status != "Cancelled" && (a.DentistId == dentistId || a.PatientId == patientId) &&
                a.StartTime < last && a.EndTime > first)
            .Select(a => new { a.StartTime, a.EndTime }).ToListAsync(cancellationToken);
        return Ok(new { date, timeZone = schedule.TimeZoneId,
            slots = candidates.Where(start => !busy.Any(a => a.StartTime < start.AddMinutes(service.DurationMinutes) && a.EndTime > start))
                .Select(start => new { startTime = start, endTime = start.AddMinutes(service.DurationMinutes) }) });
    }

    [HttpPost("appointments")]
    public async Task<IActionResult> CreateBooking(CreatePatientBookingDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetPatientId(out var patientId)) return Unauthorized(new { message = "Invalid patient session." });
        var patient = await context.Patients.FindAsync(new object[] { patientId }, cancellationToken);
        if (patient == null) return NotFound(new { message = "Patient not found." });
        if (string.IsNullOrWhiteSpace(patient.Phone))
            return BadRequest(new { message = "Complete your contact details before booking." });
        var service = await context.DentalServices.SingleOrDefaultAsync(s => s.Id == dto.DentalServiceId && s.IsActive, cancellationToken);
        var dentist = await context.Dentists.SingleOrDefaultAsync(d => d.Id == dto.DentistId && d.IsActive, cancellationToken);
        if (service == null || service.DurationMinutes <= 0 || dentist == null)
            return BadRequest(new { message = "The selected service or dentist is unavailable. Please choose again." });

        var start = dto.StartTime.UtcDateTime;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, schedule.TimeZone));
        if (!schedule.Candidates(date, service.DurationMinutes, clock.GetUtcNow()).Contains(start))
            return BadRequest(new { message = "Choose an available time during clinic opening hours." });
        var end = start.AddMinutes(service.DurationMinutes);
        if (await context.Appointments.AnyAsync(a => a.Status != "Cancelled" &&
            (a.DentistId == dto.DentistId || a.PatientId == patientId) && a.StartTime < end && a.EndTime > start, cancellationToken))
            return Conflict(new { message = "That time is no longer available. Please choose another time." });

        var appointment = new Appointment
        {
            PatientId = patientId, DentistId = dentist.Id, DentalServiceId = service.Id,
            StartTime = start, EndTime = end, Status = "Pending",
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            CreatedAt = clock.GetUtcNow().UtcDateTime, UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(appointment);
        // PostgreSQL exclusion constraints also protect against concurrent staff/patient bookings.
        await context.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetBooking), new { id = appointment.Id }, Map(appointment, patient, dentist, service));
    }

    [HttpGet("appointments/{id:int}")]
    public async Task<IActionResult> GetBooking(int id, CancellationToken cancellationToken)
    {
        if (!TryGetPatientId(out var patientId)) return Unauthorized(new { message = "Invalid patient session." });
        var appointment = await context.Appointments.AsNoTracking().Include(a => a.Patient)
            .Include(a => a.Dentist).Include(a => a.DentalService)
            .SingleOrDefaultAsync(a => a.Id == id && a.PatientId == patientId, cancellationToken);
        return appointment == null ? NotFound(new { message = "Appointment not found." }) :
            Ok(Map(appointment, appointment.Patient, appointment.Dentist, appointment.DentalService));
    }

    [HttpGet("appointments")]
    public async Task<IActionResult> GetMyAppointments([FromQuery] string view = "upcoming", [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (!TryGetPatientId(out var patientId)) return Unauthorized(new { message = "Invalid patient session." });
        if (view is not ("upcoming" or "history" or "all") || page < 1 || pageSize is < 1 or > 50)
            return BadRequest(new { message = "Choose a valid appointment view and page." });
        var now = clock.GetUtcNow().UtcDateTime;
        var owned = context.Appointments.AsNoTracking().Where(a => a.PatientId == patientId);
        var upcoming = owned.Where(a => a.EndTime >= now && a.Status != "Completed" && a.Status != "Cancelled" && a.Status != "NoShow");
        var allCount = await owned.CountAsync(cancellationToken);
        var upcomingCount = await upcoming.CountAsync(cancellationToken);
        var history = owned.Where(a => a.EndTime < now || a.Status == "Completed" || a.Status == "Cancelled" || a.Status == "NoShow");
        var filtered = view == "upcoming" ? upcoming : view == "history" ? history : owned;
        var total = view == "upcoming" ? upcomingCount : view == "history" ? allCount - upcomingCount : allCount;
        var ordered = view == "upcoming" ? filtered.OrderBy(a => a.StartTime).ThenBy(a => a.Id)
            : filtered.OrderByDescending(a => a.StartTime).ThenByDescending(a => a.Id);
        // Large invalid page numbers cannot overflow Skip's integer arithmetic.
        if ((long)(page - 1) * pageSize > int.MaxValue) return BadRequest();
        var visits = await ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Include(a => a.Dentist).Include(a => a.DentalService).ToListAsync(cancellationToken);
        var ids = visits.Select(a => a.Id).ToArray();
        var invoices = await context.Invoices.AsNoTracking().Where(i => i.PatientId == patientId && ids.Contains(i.AppointmentId))
            .ToDictionaryAsync(i => i.AppointmentId, cancellationToken);
        var items = visits.Select(a => {
            invoices.TryGetValue(a.Id, out var invoice);
            return new {
                a.Id, a.DentistId, dentistName = $"{a.Dentist.FirstName} {a.Dentist.LastName}",
                a.DentalServiceId, serviceName = a.DentalService.Name,
                price = invoice?.Total ?? a.DentalService.Price, a.StartTime, a.EndTime, a.Status,
                invoice = invoice == null ? null : new {
                    invoice.Id, invoice.Number, invoice.Total, invoice.Currency, invoice.PaymentStatus,
                    invoice.PaymentMethod, invoice.PaidAt
                }
            };
        }).ToArray();
        return Ok(new { items, page, pageSize, total, nextPage = (long)page * pageSize < total ? (int?)(page + 1) : null,
            counts = new { upcoming = upcomingCount, history = allCount - upcomingCount, all = allCount }, timeZone = schedule.TimeZoneId });
    }

    private static AppointmentResponseDto Map(Appointment a, Patient p, Dentist d, DentalService s) => new()
    {
        Id = a.Id, PatientId = p.Id, PatientName = $"{p.FirstName} {p.LastName}",
        DentistId = d.Id, DentistName = $"{d.FirstName} {d.LastName}", DentalServiceId = s.Id,
        ServiceName = s.Name, Price = s.Price, StartTime = a.StartTime, EndTime = a.EndTime,
        Status = a.Status, Notes = a.Notes, CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt
    };
}
