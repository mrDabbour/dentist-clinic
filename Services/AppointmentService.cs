using Microsoft.EntityFrameworkCore;
using dentist_clinic_api.Data;
using dentist_clinic_api.DTOs.Appointments;
using dentist_clinic_api.Services.Interfaces;

namespace dentist_clinic_api.Services;

public class AppointmentService : IAppointmentService
{
    private readonly DentistDbContext _context;

    private static readonly string[] AllowedStatuses =
    {
        "Pending",
        "Confirmed",
        "Completed",
        "Cancelled",
        "NoShow"
    };

    public AppointmentService(DentistDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<AppointmentResponseDto>> GetAppointmentsAsync(
        int? dentistId,
        int? patientId,
        string? status,
        DateTime? date)
    {
        var query = _context.Appointments
            .AsNoTracking()
            .AsQueryable();

        if (dentistId.HasValue)
        {
            query = query.Where(a =>
                a.DentistId == dentistId.Value);
        }

        if (patientId.HasValue)
        {
            query = query.Where(a =>
                a.PatientId == patientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = AllowedStatuses.FirstOrDefault(
                allowedStatus =>
                    allowedStatus.Equals(
                        status.Trim(),
                        StringComparison.OrdinalIgnoreCase));

            if (normalizedStatus == null)
            {
                throw new ArgumentException(
                    "Status must be Pending, Confirmed, Completed, Cancelled or NoShow.");
            }

            query = query.Where(a =>
                a.Status == normalizedStatus);
        }

        if (date.HasValue)
        {
            var startOfDay = DateTime.SpecifyKind(
                date.Value.Date,
                DateTimeKind.Utc);

            var endOfDay = startOfDay.AddDays(1);

            query = query.Where(a =>
                a.StartTime >= startOfDay &&
                a.StartTime < endOfDay);
        }

        return await query
            .OrderBy(a => a.StartTime)
            .Select(a => new AppointmentResponseDto
            {
                Id = a.Id,

                PatientId = a.PatientId,
                PatientName =
                    a.Patient.FirstName + " " +
                    a.Patient.LastName,

                DentistId = a.DentistId,
                DentistName =
                    a.Dentist.FirstName + " " +
                    a.Dentist.LastName,

                DentalServiceId = a.DentalServiceId,
                ServiceName = a.DentalService.Name,

                Price = a.DentalService.Price,

                StartTime = a.StartTime,
                EndTime = a.EndTime,

                Status = a.Status,
                Notes = a.Notes,

                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            })
            .ToListAsync();
    }

 public async Task<AppointmentResponseDto?> GetAppointmentAsync(int id)
{
    return await _context.Appointments
        .AsNoTracking()
        .Where(a => a.Id == id)
        .Select(a => new AppointmentResponseDto
        {
            Id = a.Id,

            PatientId = a.PatientId,
            PatientName =
                a.Patient.FirstName + " " + a.Patient.LastName,

            DentistId = a.DentistId,
            DentistName =
                a.Dentist.FirstName + " " + a.Dentist.LastName,

            DentalServiceId = a.DentalServiceId,
            ServiceName = a.DentalService.Name,

            Price = a.DentalService.Price,

            StartTime = a.StartTime,
            EndTime = a.EndTime,

            Status = a.Status,
            Notes = a.Notes,

            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt
        })
        .FirstOrDefaultAsync();
}

    public Task<AppointmentResponseDto> CreateAppointmentAsync(
        CreateAppointmentDto dto)
        => throw new NotImplementedException();

    public Task<AppointmentResponseDto> UpdateAppointmentAsync(
        int id,
        UpdateAppointmentDto dto)
        => throw new NotImplementedException();

    public Task ConfirmAppointmentAsync(int id)
        => throw new NotImplementedException();

    public Task CancelAppointmentAsync(int id)
        => throw new NotImplementedException();

    public Task CompleteAppointmentAsync(int id)
        => throw new NotImplementedException();

    public Task MarkNoShowAsync(int id)
        => throw new NotImplementedException();

    public Task DeleteAppointmentAsync(int id)
        => throw new NotImplementedException();
}