using System.ComponentModel.DataAnnotations;

namespace dentist_clinic_api.DTOs.PatientAuth;

public class UpdatePatientProfileDto
{
    [Required]
    [MaxLength(30)]
    [RegularExpression(@"^\+?[0-9 ()-]+$", ErrorMessage = "Enter a valid phone number.")]
    public string Phone { get; set; } = string.Empty;
}
