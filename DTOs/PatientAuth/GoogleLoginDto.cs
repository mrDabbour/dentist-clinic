using System.ComponentModel.DataAnnotations;

namespace dentist_clinic_api.DTOs.PatientAuth;

public class GoogleLoginDto
{
    [Required]
    [MaxLength(10000)]
    public string IdToken { get; set; } = string.Empty;
}
