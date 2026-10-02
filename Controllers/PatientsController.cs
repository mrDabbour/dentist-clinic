using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dentist_clinic_api.Data;
using dentist_clinic_api.Models;
using dentist_clinic_api.DTOs.Patients;
using Microsoft.AspNetCore.Authorization;
namespace dentist_clinic_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Receptionist,Dentist")]
public class PatientsController : ControllerBase
{
    private readonly DentistDbContext _context;

    public PatientsController(DentistDbContext context)
    {
        _context = context;
    }

    // ==========================================
    // GET: api/patients
    // Get all patients
    // ==========================================
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PatientResponseDto>>> GetPatients()
    {
        var patients = await _context.Patients
            .AsNoTracking()
            .Select(patient => new PatientResponseDto
            {
                Id = patient.Id,
                FirstName = patient.FirstName,
                LastName = patient.LastName,
                Email = patient.Email,
                Phone = patient.Phone,
                CreatedAt = patient.CreatedAt
            })
            .ToListAsync();

        return Ok(patients);
    }

    // ==========================================
    // GET: api/patients/1
    // Get one patient by ID
    // ==========================================
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PatientResponseDto>> GetPatient(int id)
    {
        var patient = await _context.Patients
            .AsNoTracking()
            .Where(patient => patient.Id == id)
            .Select(patient => new PatientResponseDto
            {
                Id = patient.Id,
                FirstName = patient.FirstName,
                LastName = patient.LastName,
                Email = patient.Email,
                Phone = patient.Phone,
                CreatedAt = patient.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (patient == null)
        {
            return NotFound(new
            {
                message = $"Patient with ID {id} was not found."
            });
        }

        return Ok(patient);
    }

    

   // ==========================================
// POST: api/patients
// Create a new patient
// ==========================================
[HttpPost]
public async Task<ActionResult<PatientResponseDto>> CreatePatient(
    CreatePatientDto dto)
{
    // Normalize input
    var firstName = dto.FirstName.Trim();
    var lastName = dto.LastName.Trim();

    var normalizedEmail =
        dto.Email.Trim().ToLowerInvariant();

    var normalizedPhone =
        new string(
            dto.Phone
                .Where(char.IsDigit)
                .ToArray()
        );

    // ==========================================
    // Check duplicate email
    // ==========================================
    var emailExists =
        await _context.Patients
            .AsNoTracking()
            .AnyAsync(p =>
                p.Email.ToLower() == normalizedEmail
            );

    if (emailExists)
    {
        return Conflict(new
        {
            message =
                "A patient with this email already exists."
        });
    }

    // ==========================================
    // Check duplicate phone
    // ==========================================
    var phoneExists =
        await _context.Patients
            .AsNoTracking()
            .AnyAsync(p =>
                p.Phone != null &&
                p.Phone.Replace(" ", "")
                       .Replace("-", "")
                       .Replace("(", "")
                       .Replace(")", "") ==
                normalizedPhone
            );

    if (phoneExists)
    {
        return Conflict(new
        {
            message =
                "A patient with this phone number already exists."
        });
    }

    // ==========================================
    // Create patient
    // ==========================================
    var patient = new Patient
    {
        FirstName = firstName,
        LastName = lastName,
        Email = normalizedEmail,
        Phone = normalizedPhone,
        CreatedAt = DateTime.UtcNow
    };

    _context.Patients.Add(patient);

    await _context.SaveChangesAsync();

    // ==========================================
    // Response
    // ==========================================
    var response = new PatientResponseDto
    {
        Id = patient.Id,
        FirstName = patient.FirstName,
        LastName = patient.LastName,
        Email = patient.Email,
        Phone = patient.Phone,
        CreatedAt = patient.CreatedAt
    };

    return CreatedAtAction(
        nameof(GetPatient),
        new { id = patient.Id },
        response
    );
}

// ==========================================
// PUT: api/patients/1
// Update an existing patient
// Returns: 200 OK + updated patient
// ==========================================
[HttpPut("{id:int}")]
public async Task<ActionResult<PatientResponseDto>> UpdatePatient(
    int id,
    UpdatePatientDto dto)
{
    var patient = await _context.Patients.FindAsync(id);

    if (patient == null)
    {
        return NotFound(new
        {
            message = $"Patient with ID {id} was not found."
        });
    }

    var firstName = dto.FirstName.Trim();
    var lastName = dto.LastName.Trim();

    var normalizedEmail =
        dto.Email.Trim().ToLowerInvariant();

    var normalizedPhone =
        new string(
            dto.Phone
                .Where(char.IsDigit)
                .ToArray()
        );

    // Check another patient does not own this email
    var emailExists =
        await _context.Patients
            .AsNoTracking()
            .AnyAsync(p =>
                p.Id != id &&
                p.Email.ToLower() == normalizedEmail
            );

    if (emailExists)
    {
        return Conflict(new
        {
            message =
                "Another patient with this email already exists."
        });
    }

    // Check another patient does not own this phone
    var phoneExists =
        await _context.Patients
            .AsNoTracking()
            .AnyAsync(p =>
                p.Id != id &&
                p.Phone != null &&
                p.Phone.Replace(" ", "")
                       .Replace("-", "")
                       .Replace("(", "")
                       .Replace(")", "") ==
                normalizedPhone
            );

    if (phoneExists)
    {
        return Conflict(new
        {
            message =
                "Another patient with this phone number already exists."
        });
    }

    patient.FirstName = firstName;
    patient.LastName = lastName;
    patient.Email = normalizedEmail;
    patient.Phone = normalizedPhone;

    await _context.SaveChangesAsync();

    var response = new PatientResponseDto
    {
        Id = patient.Id,
        FirstName = patient.FirstName,
        LastName = patient.LastName,
        Email = patient.Email,
        Phone = patient.Phone,
        CreatedAt = patient.CreatedAt
    };

    return Ok(response);
}

    // ==========================================
    // DELETE: api/patients/1
    // Delete a patient
    // ==========================================
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePatient(int id)
    {
        var patient = await _context.Patients.FindAsync(id);

        if (patient == null)
        {
            return NotFound(new
            {
                message = $"Patient with ID {id} was not found."
            });
        }

        _context.Patients.Remove(patient);

        await _context.SaveChangesAsync();

        return NoContent();
    }
}