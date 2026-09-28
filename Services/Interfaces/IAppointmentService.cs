using dentist_clinic_api.DTOs.Appointments;

namespace dentist_clinic_api.Services.Interfaces;

public interface IAppointmentService
{
    Task<IEnumerable<AppointmentResponseDto>> GetAppointmentsAsync(
        int? dentistId,
        int? patientId,
        string? status,
        DateTime? date);

    Task<AppointmentResponseDto?> GetAppointmentAsync(int id);

    Task<AppointmentResponseDto> CreateAppointmentAsync(
        CreateAppointmentDto dto);

    Task<AppointmentResponseDto> UpdateAppointmentAsync(
        int id,
        UpdateAppointmentDto dto);

    Task ConfirmAppointmentAsync(int id);
    Task CancelAppointmentAsync(int id);
    Task CompleteAppointmentAsync(int id);
    Task MarkNoShowAsync(int id);
    Task DeleteAppointmentAsync(int id);
}