using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using dentist_clinic_api.Data;
using dentist_clinic_api.DTOs.PatientAuth;
using dentist_clinic_api.Models;
using dentist_clinic_api.Services;
using dentist_clinic_api.Services.Interfaces;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace dentist_clinic_api.Tests;

public class PatientGoogleAuthenticationTests
{
    [Fact]
    public async Task FirstLogin_CreatesOnePatientAndIdentity_AndReturnsUsablePatientToken()
    {
        using var factory = new PatientFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/patient-auth/google", new { idToken = "valid" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var auth = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(auth);
        Assert.True(auth.RequiresProfileCompletion);
        Assert.Equal("Patient", auth.Role);
        Assert.True(auth.ExpiresAt > DateTime.UtcNow);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var profile = await client.GetFromJsonAsync<PatientProfileResponseDto>("/api/patient-auth/me");
        Assert.Equal(auth.PatientId, profile!.PatientId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/patients")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
        Assert.Equal(1, await db.Patients.CountAsync());
        Assert.Equal(1, await db.PatientIdentities.CountAsync());
    }

    [Fact]
    public async Task ReturningLogin_UsesSubjectEvenIfGoogleEmailChanges()
    {
        using var factory = new PatientFactory();
        using var client = factory.CreateClient();
        var first = await Login(client);
        factory.Validator.Payload.Email = "changed@gmail.com";
        var second = await Login(client);
        Assert.Equal(first.PatientId, second.PatientId);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<DentistDbContext>().PatientIdentities.CountAsync());
    }

    [Theory]
    [InlineData("existing@gmail.com", null, true)]
    [InlineData("existing@clinic.test", "clinic.test", true)]
    [InlineData("existing@external.test", null, false)]
    public async Task ExistingRecord_LinksOnlyWhenGoogleIsAuthoritative(string email, string? domain, bool allowed)
    {
        using var factory = new PatientFactory();
        factory.Validator.Payload.Email = email;
        factory.Validator.Payload.HostedDomain = domain;
        using var client = factory.CreateClient();
        int patientId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
            var patient = new Patient { FirstName = "Existing", LastName = "Patient", Email = email.ToUpperInvariant(), Phone = "0211234567" };
            db.Patients.Add(patient);
            await db.SaveChangesAsync();
            patientId = patient.Id;
        }
        var response = await client.PostAsJsonAsync("/api/patient-auth/google", new { idToken = "valid" });
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Conflict, response.StatusCode);
        if (allowed)
        {
            var auth = await response.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.Equal(patientId, auth!.PatientId);
            Assert.False(auth.RequiresProfileCompletion);
        }
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DentistDbContext>();
        Assert.Equal(1, await verifyDb.Patients.CountAsync());
        Assert.Equal(allowed ? 1 : 0, await verifyDb.PatientIdentities.CountAsync());
    }

    [Fact]
    public async Task ExistingGoogleLink_CannotBeReassignedToAnotherSubject()
    {
        using var factory = new PatientFactory();
        using var client = factory.CreateClient();
        await Login(client);
        factory.Validator.Payload.Subject = "different-subject";
        var response = await client.PostAsJsonAsync("/api/patient-auth/google", new { idToken = "valid" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("invalid", true)]
    [InlineData("valid", false)]
    public async Task InvalidOrUnverifiedToken_DoesNotCreatePatient(string token, bool verified)
    {
        using var factory = new PatientFactory();
        factory.Validator.Payload.EmailVerified = verified;
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/patient-auth/google", new { idToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<DentistDbContext>().Patients.CountAsync());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    [InlineData("Dentist", HttpStatusCode.Forbidden)]
    [InlineData("Patient", HttpStatusCode.Unauthorized)]
    public async Task Profile_RejectsAnonymousStaffAndPatientWithoutPatientClaim(string? role, HttpStatusCode expected)
    {
        using var factory = new PatientFactory();
        using var client = factory.CreateClient();
        if (role != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MakeToken(role));
        Assert.Equal(expected, (await client.GetAsync("/api/patient-auth/me")).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync("/api/patient-auth/profile", new { phone = "0211234567" })).StatusCode);
    }

    [Fact]
    public async Task Profile_NormalizesPhone_AndOnlyUpdatesAuthenticatedPatient()
    {
        using var factory = new PatientFactory();
        using var client = factory.CreateClient();
        var auth = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var response = await client.PutAsJsonAsync("/api/patient-auth/profile", new { phone = "+64 (21) 123-4567", patientId = 999 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<PatientProfileResponseDto>();
        Assert.Equal(auth.PatientId, profile!.PatientId);
        Assert.Equal("64211234567", profile.Phone);
        Assert.False(profile.RequiresProfileCompletion);
        Assert.False((await Login(client)).RequiresProfileCompletion);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/patient-auth/profile", new { phone = "abc" })).StatusCode);
    }

    [Fact]
    public async Task Login_RejectsMissingToken_AndRateLimitsRepeatedAttempts()
    {
        using var factory = new PatientFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/patient-auth/google", new { })).StatusCode);
        HttpResponseMessage? last = null;
        for (var i = 0; i < 21; i++)
        {
            last?.Dispose();
            last = await client.PostAsJsonAsync("/api/patient-auth/google", new { idToken = "invalid" });
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        last.Dispose();
    }

    [Fact]
    public async Task Config_ReturnsOnlyPublicClientId_AndMissingConfigReturns503()
    {
        using var factory = new PatientFactory();
        using var client = factory.CreateClient();
        var config = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/patient-auth/config");
        Assert.Single(config!);
        Assert.Equal("test-client", config!["clientId"]);
        using var missingFactory = new PatientFactory("");
        using var missingClient = missingFactory.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await missingClient.GetAsync("/api/patient-auth/config")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await missingClient.PostAsJsonAsync("/api/patient-auth/google", new { idToken = "valid" })).StatusCode);
    }

    [Fact]
    public async Task PatientCatalog_ReturnsOnlyActiveServices()
    {
        using var factory = new PatientFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
            db.DentalServices.AddRange(new DentalService { Name = "Active", IsActive = true }, new DentalService { Name = "Inactive", IsActive = false });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/patient-auth/services")).StatusCode);
        var auth = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var services = await client.GetFromJsonAsync<List<dentist_clinic_api.DTOs.DentalServices.DentalServiceResponseDto>>("/api/patient-auth/services");
        Assert.Single(services!);
        Assert.Equal("Active", services![0].Name);
    }

    [Fact]
    public async Task ConcurrentFirstLogins_CreateExactlyOnePatientAndIdentityInPostgres()
    {
        var validator = new FakeValidator();
        validator.Payload.Email = $"google-concurrency-{Guid.NewGuid():N}@gmail.com";
        validator.Payload.Subject = $"concurrent-{Guid.NewGuid():N}";
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GoogleAuth:ClientId", "test-client");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGoogleTokenValidator>();
                services.AddSingleton<IGoogleTokenValidator>(validator);
            });
        });
        using var client = factory.CreateClient();
        try
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
                client.PostAsJsonAsync("/api/patient-auth/google", new { idToken = "valid" })));
            var patientIds = new List<int>();
            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    patientIds.Add((await response.Content.ReadFromJsonAsync<LoginResponse>())!.PatientId);
                }
            }
            Assert.Single(patientIds.Distinct());
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
            Assert.Equal(1, await db.Patients.CountAsync(p => p.Email == validator.Payload.Email));
            Assert.Equal(1, await db.PatientIdentities.CountAsync(p => p.ProviderSubjectId == validator.Payload.Subject));
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
            var patients = await db.Patients.Where(p => p.Email == validator.Payload.Email).ToListAsync();
            var ids = patients.Select(p => p.Id).ToList();
            db.PatientIdentities.RemoveRange(await db.PatientIdentities.Where(i => ids.Contains(i.PatientId)).ToListAsync());
            db.Patients.RemoveRange(patients);
            await db.SaveChangesAsync();
        }
    }
    private static async Task<LoginResponse> Login(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/patient-auth/google", new { idToken = "valid" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private const string TestKey = "test-only-signing-key-at-least-32-characters-long";
    private static string MakeToken(string role) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "test-issuer", "test-audience", new[] { new Claim(ClaimTypes.Role, role), new Claim(JwtRegisteredClaimNames.Sub, "1") },
        expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)), SecurityAlgorithms.HmacSha256)));

    public class LoginResponse : PatientProfileResponseDto
    {
        public string Token { get; set; } = "";
        public string Role { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }

    private class FakeValidator : IGoogleTokenValidator
    {
        public GoogleJsonWebSignature.Payload Payload { get; } = new()
        {
            Subject = "stable-subject", Email = "patient@gmail.com", EmailVerified = true,
            GivenName = "Test", FamilyName = "Patient"
        };
        public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken) =>
            idToken == "invalid" ? throw new InvalidJwtException("Invalid token") : Task.FromResult(Payload);
    }

    private class PatientFactory(string clientId = "test-client") : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public FakeValidator Validator { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:Key", TestKey);
            builder.UseSetting("Jwt:Issuer", "test-issuer");
            builder.UseSetting("Jwt:Audience", "test-audience");
            builder.UseSetting("GoogleAuth:ClientId", clientId);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<DentistDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DentistDbContext>>();
                services.RemoveAll<IGoogleTokenValidator>();
                services.AddSingleton<IGoogleTokenValidator>(Validator);
                connection.Open();
                services.AddDbContext<DentistDbContext>(options => options.UseSqlite(connection));
            });
        }

        protected override Microsoft.Extensions.Hosting.IHost CreateHost(Microsoft.Extensions.Hosting.IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<DentistDbContext>().Database.EnsureCreated();
            return host;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }
}

