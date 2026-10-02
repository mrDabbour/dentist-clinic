using dentist_clinic_api.Models;

namespace dentist_clinic_api.DTOs.PatientAuth;

public class PatientProfileResponseDto
{
    public int PatientId { get; init; }
    // Retained for clients that used the original profile-update response.
    public int Id => PatientId;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool RequiresProfileCompletion => string.IsNullOrWhiteSpace(Phone);

    public static PatientProfileResponseDto FromPatient(Patient patient) => new()
    {
        PatientId = patient.Id,
        FirstName = patient.FirstName,
        LastName = patient.LastName,
        Email = patient.Email,
        Phone = patient.Phone
    };
}
