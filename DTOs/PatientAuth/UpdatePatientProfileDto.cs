using System.ComponentModel.DataAnnotations;

namespace dentist_clinic_api.DTOs.PatientAuth;

public class UpdatePatientProfileDto
{
    [Required]
    [MaxLength(30)]
    public string Phone { get; set; } = string.Empty;
}