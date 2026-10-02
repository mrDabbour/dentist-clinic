using dentist_clinic_api.Data;
using dentist_clinic_api.DTOs.DentalServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dentist_clinic_api.Controllers;

[ApiController]
[Route("api/public/services")]
[AllowAnonymous]
public class PublicServicesController(DentistDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetServices(CancellationToken cancellationToken) =>
        Ok(await context.DentalServices.AsNoTracking().Where(s => s.IsActive)
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .Select(s => new DentalServiceResponseDto {
                Id = s.Id, Name = s.Name, Category = s.Category, Description = s.Description,
                Price = s.Price, DurationMinutes = s.DurationMinutes, IsActive = s.IsActive
            }).ToListAsync(cancellationToken));
}
