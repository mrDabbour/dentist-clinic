using System.ComponentModel.DataAnnotations;

namespace dentist_clinic_api.DTOs.PatientBooking;

public class CreatePatientBookingDto
{
    [Range(1, int.MaxValue)] public int DentalServiceId { get; set; }
    [Range(1, int.MaxValue)] public int DentistId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}
