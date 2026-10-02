namespace dentist_clinic_api.Models;

public class Patient
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}