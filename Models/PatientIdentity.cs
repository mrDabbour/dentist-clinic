namespace dentist_clinic_api.Models;

public class PatientIdentity
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    // Google, Local, etc.
    public string Provider { get; set; } = string.Empty;

    // Google's stable "sub" identifier
    public string ProviderSubjectId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Patient Patient { get; set; } = null!;
}