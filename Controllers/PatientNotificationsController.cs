using System.Security.Claims;
using dentist_clinic_api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dentist_clinic_api.Controllers;

[ApiController]
[Route("api/patient-notifications")]
[Authorize(Roles = "Patient")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PatientNotificationsController(DentistDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("patientId"), out var patientId)) return Unauthorized();
        return Ok(await context.PatientNotifications.AsNoTracking().Where(n => n.PatientId == patientId)
            .OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync(cancellationToken));
    }
    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("patientId"), out var patientId)) return Unauthorized();
        var notification = await context.PatientNotifications.SingleOrDefaultAsync(n => n.Id == id && n.PatientId == patientId, cancellationToken);
        if (notification == null) return NotFound();
        notification.ReadAt ??= DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
