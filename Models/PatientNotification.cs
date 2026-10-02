namespace dentist_clinic_api.Models;

public class PatientNotification
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public int AppointmentId { get; set; }
    public string Type { get; set; } = "AppointmentConfirmed";
    public string Message { get; set; } = "Your appointment is confirmed. Please view your invoice to arrange payment.";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
