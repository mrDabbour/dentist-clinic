using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using dentist_clinic_api.Data;
using dentist_clinic_api.DTOs.PatientAuth;
using dentist_clinic_api.Models;
using dentist_clinic_api.Services.Interfaces;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace dentist_clinic_api.Controllers;

[ApiController]
[Route("api/patient-auth")]
[Authorize(Roles = "Patient")]
public class PatientAuthController(
    DentistDbContext context,
    IConfiguration configuration,
    IGoogleTokenValidator googleTokenValidator) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("config")]
    public IActionResult GetConfiguration()
    {
        var clientId = configuration["GoogleAuth:ClientId"];
        return string.IsNullOrWhiteSpace(clientId)
            ? StatusCode(503, new { message = "Google sign-in is temporarily unavailable." })
            : Ok(new { clientId });
    }

    [AllowAnonymous]
    [EnableRateLimiting("patient-google-login")]
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin(GoogleLoginDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["GoogleAuth:ClientId"]))
            return StatusCode(503, new { message = "Google sign-in is temporarily unavailable." });

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await googleTokenValidator.ValidateAsync(dto.IdToken);
        }
        catch (InvalidJwtException)
        {
            return Unauthorized(new { message = "Google sign-in could not be verified. Please try again." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Google sign-in is temporarily unavailable. Please try again." });
        }

        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Subject) ||
            string.IsNullOrWhiteSpace(payload.Email))
            return Unauthorized(new { message = "A verified Google email address is required." });

        var email = payload.Email.Trim().ToLowerInvariant();
        if (email.Length > 255 || payload.Subject.Length > 255)
            return Unauthorized(new { message = "Google account information is invalid." });

        // Retry concurrent first logins after the database rolls back a conflicting transaction.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            try
            {
                var identity = await context.PatientIdentities.Include(x => x.Patient)
                    .SingleOrDefaultAsync(x => x.Provider == "Google" &&
                        x.ProviderSubjectId == payload.Subject, cancellationToken);

                Patient patient;
                if (identity != null)
                {
                    // Google's stable subject, not its mutable email, identifies returning patients.
                    patient = identity.Patient;
                }
                else
                {
                    var existingPatients = await context.Patients
                        .Where(p => p.Email.Trim().ToLower() == email)
                        .OrderBy(p => p.Id).Take(2).ToListAsync(cancellationToken);
                    if (existingPatients.Count > 1)
                        return Conflict(new { message = "Please contact the clinic to connect your patient record." });

                    var existingPatient = existingPatients.SingleOrDefault();
                    if (existingPatient != null)
                    {
                        // Google is authoritative only for Gmail or verified Workspace addresses.
                        // Third-party email ownership requires a separate verification flow.
                        var authoritativeEmail = email.EndsWith("@gmail.com", StringComparison.Ordinal) ||
                            !string.IsNullOrWhiteSpace(payload.HostedDomain);
                        var alreadyLinked = await context.PatientIdentities.AnyAsync(x =>
                            x.PatientId == existingPatient.Id && x.Provider == "Google", cancellationToken);
                        if (!authoritativeEmail || alreadyLinked)
                            return Conflict(new { message = "Please contact the clinic to connect your patient record." });
                        patient = existingPatient;
                    }
                    else
                    {
                        patient = new Patient
                        {
                            FirstName = payload.GivenName?.Trim() ?? string.Empty,
                            LastName = payload.FamilyName?.Trim() ?? string.Empty,
                            Email = email,
                            Phone = null,
                            CreatedAt = DateTime.UtcNow
                        };
                        context.Patients.Add(patient);
                    }

                    // One atomic save creates both the patient and their identity.
                    context.PatientIdentities.Add(new PatientIdentity
                    {
                        Patient = patient,
                        Provider = "Google",
                        ProviderSubjectId = payload.Subject,
                        CreatedAt = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync(cancellationToken);
                }

                var response = CreatePatientAuthResponse(patient);
                await transaction.CommitAsync(cancellationToken);
                Response.Headers.CacheControl = "no-store";
                return Ok(response);
            }
            catch (Exception ex) when (IsConcurrentWrite(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }
        }

        return Conflict(new { message = "Sign-in is being processed. Please try again." });
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (!TryGetPatientId(out var patientId))
            return Unauthorized(new { message = "Invalid patient session." });
        var patient = await context.Patients.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return patient == null
            ? NotFound(new { message = "Patient not found." })
            : Ok(PatientProfileResponseDto.FromPatient(patient));
    }

    [HttpGet("services")]
    public async Task<IActionResult> GetAvailableServices(CancellationToken cancellationToken) =>
        Ok(await context.DentalServices.AsNoTracking().Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new dentist_clinic_api.DTOs.DentalServices.DentalServiceResponseDto
            {
                Id = s.Id, Name = s.Name, Category = s.Category, Description = s.Description,
                Price = s.Price, DurationMinutes = s.DurationMinutes, IsActive = s.IsActive,
                CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt
            }).ToListAsync(cancellationToken));

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdatePatientProfileDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetPatientId(out var patientId))
            return Unauthorized(new { message = "Invalid patient session." });
        var patient = await context.Patients.FindAsync(new object[] { patientId }, cancellationToken);
        if (patient == null)
            return NotFound(new { message = "Patient not found." });

        var phone = new string(dto.Phone.Where(char.IsAsciiDigit).ToArray());
        if (phone.Length is < 7 or > 15)
            return BadRequest(new { message = "Enter a phone number containing 7 to 15 digits." });

        var phoneExists = await context.Patients.AnyAsync(p => p.Id != patientId && p.Phone != null &&
            p.Phone.Replace(" ", "").Replace("-", "").Replace("(", "")
                .Replace(")", "").Replace("+", "") == phone, cancellationToken);
        if (phoneExists)
            return Conflict(new { message = "This phone number is already registered. Please contact the clinic." });

        patient.Phone = phone;
        await context.SaveChangesAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(PatientProfileResponseDto.FromPatient(patient));
    }

    private bool TryGetPatientId(out int patientId) =>
        int.TryParse(User.FindFirstValue("patientId"), out patientId) && patientId > 0;

    private static bool IsConcurrentWrite(Exception exception)
    {
        // Npgsql can wrap a serialization failure in both DbUpdateException and
        // InvalidOperationException, so inspect the complete exception chain.
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is PostgresException { SqlState: "40001" or "23505" })
                return true;
        return false;
    }

    private object CreatePatientAuthResponse(Patient patient)
    {
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT key is not configured.");
        var expirationMinutes = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var minutes) && minutes > 0
            ? minutes : 60;
        var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, patient.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, patient.Email),
            new Claim(ClaimTypes.Name, $"{patient.FirstName} {patient.LastName}"),
            new Claim(ClaimTypes.Role, "Patient"),
            new Claim("patientId", patient.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"], audience: configuration["Jwt:Audience"],
            claims: claims, expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256));
        return new
        {
            patientId = patient.Id, patient.FirstName, patient.LastName, patient.Email, patient.Phone,
            role = "Patient", requiresProfileCompletion = string.IsNullOrWhiteSpace(patient.Phone),
            token = new JwtSecurityTokenHandler().WriteToken(token), expiresAt
        };
    }
}
