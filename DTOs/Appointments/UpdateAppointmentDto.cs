using System.ComponentModel.DataAnnotations;

namespace dentist_clinic_api.DTOs.Appointments;

public class UpdateAppointmentDto
{
    [Range(1, int.MaxValue)]
    public int PatientId { get; set; }

    [Range(1, int.MaxValue)]
    public int DentistId { get; set; }

    [Range(1, int.MaxValue)]
    public int DentalServiceId { get; set; }

    [Required]
    public DateTime StartTime { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}