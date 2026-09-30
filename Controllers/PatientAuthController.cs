using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using dentist_clinic_api.Data;
using dentist_clinic_api.DTOs.PatientAuth;
using dentist_clinic_api.Models;

namespace dentist_clinic_api.Controllers;

[ApiController]
[Route("api/patient-auth")]
public class PatientAuthController : ControllerBase
{
    private readonly DentistDbContext _context;
    private readonly IConfiguration _configuration;

    public PatientAuthController(
        DentistDbContext context,
        IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin(
        GoogleLoginDto dto)
    {
        var googleClientId =
            _configuration["GoogleAuth:ClientId"];

        if (string.IsNullOrWhiteSpace(googleClientId))
        {
            throw new InvalidOperationException(
                "Google Client ID is not configured.");
        }

        GoogleJsonWebSignature.Payload payload;

        try
        {
            payload =
                await GoogleJsonWebSignature.ValidateAsync(
                    dto.IdToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { googleClientId }
                    });

            // Only accept a Google account with
            // a verified email address.
            if (payload.EmailVerified != true)
            {
                return Unauthorized(new
                {
                    message = "Google email is not verified."
                });
            }
        }
        catch (InvalidJwtException)
        {
            return Unauthorized(new
            {
                message = "Invalid Google token."
            });
        }

        // Google account must contain the
        // required identity information.
        if (string.IsNullOrWhiteSpace(payload.Subject) ||
            string.IsNullOrWhiteSpace(payload.Email))
        {
            return Unauthorized(new
            {
                message =
                    "Google account information is incomplete."
            });
        }

        var googleSubject = payload.Subject;

        var email =
            payload.Email
                .Trim()
                .ToLowerInvariant();

        // First try to find an existing Google identity.
        var identity =
            await _context.PatientIdentities
                .Include(x => x.Patient)
                .FirstOrDefaultAsync(x =>
                    x.Provider == "Google" &&
                    x.ProviderSubjectId == googleSubject);

        Patient? patient;

        if (identity != null)
        {
            // Returning Google patient.
            patient = identity.Patient;
        }
        else
        {
            // Google identity does not exist yet.
            // Check whether this email already belongs
            // to an existing patient.
            patient =
                await _context.Patients
                    .FirstOrDefaultAsync(p =>
                        p.Email.ToLower() == email);

            if (patient == null)
            {
                // Completely new patient.
                patient = new Patient
                {
                    FirstName =
                        payload.GivenName ?? string.Empty,

                    LastName =
                        payload.FamilyName ?? string.Empty,

                    Email = email,

                    // Temporary until we collect
                    // the patient's phone number.
                    Phone = null,

                    CreatedAt = DateTime.UtcNow
                };

                _context.Patients.Add(patient);

                await _context.SaveChangesAsync();
            }

            // Link the patient to their Google account.
            var newIdentity = new PatientIdentity
            {
                PatientId = patient.Id,
                Provider = "Google",
                ProviderSubjectId = googleSubject,
                CreatedAt = DateTime.UtcNow
            };

            _context.PatientIdentities.Add(newIdentity);

            await _context.SaveChangesAsync();
        }

        return Ok(
            CreatePatientAuthResponse(patient));
    }


    [HttpPut("profile")]
public async Task<IActionResult> UpdateProfile(
    UpdatePatientProfileDto dto)
{
    var patientIdClaim = User.FindFirst("patientId")?.Value;

    if (!int.TryParse(patientIdClaim, out var patientId))
    {
        return Unauthorized(new
        {
            message = "Invalid patient session."
        });
    }

    var patient = await _context.Patients
        .FirstOrDefaultAsync(p => p.Id == patientId);

    if (patient == null)
    {
        return NotFound(new
        {
            message = "Patient not found."
        });
    }

    var phone = dto.Phone.Trim();

    var phoneExists = await _context.Patients
        .AnyAsync(p =>
            p.Id != patientId &&
            p.Phone == phone);

    if (phoneExists)
    {
        return Conflict(new
        {
            message =
                "This phone number is already registered."
        });
    }

    patient.Phone = phone;

    await _context.SaveChangesAsync();

    return Ok(new
    {
        patient.Id,
        patient.FirstName,
        patient.LastName,
        patient.Email,
        patient.Phone
    });
}

    private object CreatePatientAuthResponse(
        Patient patient)
    {
        var jwtKey =
            _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "JWT key is not configured.");

        var issuer =
            _configuration["Jwt:Issuer"];

        var audience =
            _configuration["Jwt:Audience"];

        var expirationMinutes =
            int.TryParse(
                _configuration["Jwt:ExpirationMinutes"],
                out var minutes)
                ? minutes
                : 60;

        var expiresAt =
            DateTime.UtcNow
                .AddMinutes(expirationMinutes);

        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                patient.Id.ToString()),

            new Claim(
                JwtRegisteredClaimNames.Email,
                patient.Email),

            new Claim(
                ClaimTypes.Name,
                $"{patient.FirstName} {patient.LastName}"),

            new Claim(
                ClaimTypes.Role,
                "Patient"),

            new Claim(
                "patientId",
                patient.Id.ToString()),

            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString())
        };

        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var token =
            new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);

        return new
        {
            patientId = patient.Id,
            firstName = patient.FirstName,
            lastName = patient.LastName,
            email = patient.Email,
            phone = patient.Phone,
            role = "Patient",

            token =
                new JwtSecurityTokenHandler()
                    .WriteToken(token),

            expiresAt
        };
    }
}