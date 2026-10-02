using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using dentist_clinic_api.Data;
using dentist_clinic_api.DTOs.Appointments;
using dentist_clinic_api.Models;
using dentist_clinic_api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace dentist_clinic_api.Tests;

public class PatientBookingTests
{
    private const string Key = "booking-test-key-with-at-least-32-characters";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-04T19:00:00Z"); // Monday 8am Auckland.
    private static readonly DateTime Start = DateTime.Parse("2026-10-04T20:00:00Z").ToUniversalTime();
    private static string Token(int id, string role = "Patient") => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "booking-tests", "booking-tests", new[] { new Claim("patientId", id.ToString()), new Claim(ClaimTypes.Role, role) },
        expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)));

    [Fact]
    public async Task Journey_ReturnsActiveDentistsAndSlots_AndCreatesOwnedPendingBooking()
    {
        using var factory = new BookingFactory(); using var client = factory.CreateClient(); Authenticate(client, 1);
        var dentists = await client.GetFromJsonAsync<List<Dentist>>("/api/patient-booking/dentists");
        Assert.Equal(2, dentists!.Count); Assert.DoesNotContain(dentists, d => d.FirstName == "Inactive");
        var slots = await client.GetFromJsonAsync<SlotResult>("/api/patient-booking/slots?dentalServiceId=1&dentistId=1&date=2026-10-05");
        Assert.Equal(29, slots!.Slots.Count); Assert.Equal(Start, slots.Slots[0].StartTime);
        Assert.Equal(Start.AddHours(7), slots.Slots[^1].StartTime);
        var response = await client.PostAsJsonAsync("/api/patient-booking/appointments", new
        { dentalServiceId = 1, dentistId = 1, patientId = 999, startTime = Start, notes = "  Please call me  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = await response.Content.ReadFromJsonAsync<AppointmentResponseDto>();
        Assert.Equal(1, booking!.PatientId); Assert.Equal("Pending", booking.Status);
        Assert.Equal(Start.AddHours(1), booking.EndTime); Assert.Equal("Please call me", booking.Notes);
        Assert.Equal(120m, booking.Price);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
        Authenticate(client, 2);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(response.Headers.Location)).StatusCode);
    }

    [Theory]
    [InlineData("Pending", 25)]
    [InlineData("Cancelled", 29)]
    public async Task Slots_ExcludeOverlapButReuseCancelledTime(string status, int count)
    {
        using var factory = new BookingFactory(); using var client = factory.CreateClient(); Authenticate(client, 1);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
        db.Appointments.Add(new Appointment { PatientId = 2, DentistId = 1, DentalServiceId = 1, StartTime = Start, EndTime = Start.AddHours(1), Status = status });
        await db.SaveChangesAsync();
        var slots = await client.GetFromJsonAsync<SlotResult>("/api/patient-booking/slots?dentalServiceId=1&dentistId=1&date=2026-10-05");
        Assert.Equal(count, slots!.Slots.Count);
        Assert.Equal(status == "Cancelled" ? Start : Start.AddHours(1), slots.Slots[0].StartTime);
    }
    [Fact]
    public async Task MyAppointments_OnlyReturnsOwnedVisitsAndCorrectHistoryAndPagination()
    {
        using var factory = new BookingFactory(); using var client = factory.CreateClient(); Authenticate(client, 1);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
        db.Appointments.AddRange(
            new Appointment { PatientId = 1, DentistId = 1, DentalServiceId = 1, StartTime = Start, EndTime = Start.AddHours(1), Status = "Pending" },
            new Appointment { PatientId = 1, DentistId = 1, DentalServiceId = 1, StartTime = Start.AddDays(1), EndTime = Start.AddDays(1).AddHours(1), Status = "Completed" },
            new Appointment { PatientId = 2, DentistId = 2, DentalServiceId = 1, StartTime = Start, EndTime = Start.AddHours(1), Status = "Confirmed" });
        await db.SaveChangesAsync();
        var upcoming = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/patient-booking/appointments");
        Assert.Equal(1, upcoming.GetProperty("items").GetArrayLength());
        Assert.Equal("Pending", upcoming.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(2, upcoming.GetProperty("counts").GetProperty("all").GetInt32());
        var history = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/patient-booking/appointments?view=history");
        Assert.Equal("Completed", history.GetProperty("items")[0].GetProperty("status").GetString());
        var firstPage = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/patient-booking/appointments?view=all&pageSize=1");
        Assert.Equal(2, firstPage.GetProperty("nextPage").GetInt32()); Assert.Single(firstPage.GetProperty("items").EnumerateArray());
        Assert.Empty(db.Invoices);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/patient-booking/appointments?page=0")).StatusCode);
    }

    [Fact]
    public async Task PatientConflict_IsBlockedEvenWithADifferentDentist()
    {
        using var factory = new BookingFactory(); using var client = factory.CreateClient(); Authenticate(client, 1);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/patient-booking/appointments", Request(1, Start))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/patient-booking/appointments", Request(2, Start))).StatusCode);
        var slots = await client.GetFromJsonAsync<SlotResult>("/api/patient-booking/slots?dentalServiceId=1&dentistId=2&date=2026-10-05");
        Assert.DoesNotContain(slots!.Slots, s => s.StartTime < Start.AddHours(1));
    }

    [Theory]
    [InlineData("2026-10-04T18:00:00Z")]
    [InlineData("2026-10-04T20:07:00Z")]
    [InlineData("2026-10-05T03:15:00Z")]
    [InlineData("2026-10-09T20:00:00Z")]
    public async Task Booking_RejectsPastOffGridAfterHoursAndWeekendTimes(string startTime)
    {
        using var factory = new BookingFactory(); using var client = factory.CreateClient(); Authenticate(client, 1);
        var response = await client.PostAsJsonAsync("/api/patient-booking/appointments", new { dentalServiceId = 1, dentistId = 1, startTime });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClosedDaysHaveNoSlots_AndInactiveDentistAndIncompleteProfileAreRejected()
    {
        using var factory = new BookingFactory(); using var client = factory.CreateClient(); Authenticate(client, 1);
        var slots = await client.GetFromJsonAsync<SlotResult>("/api/patient-booking/slots?dentalServiceId=1&dentistId=1&date=2026-10-10");
        Assert.Empty(slots!.Slots);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/patient-booking/appointments", Request(3, Start))).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
        (await db.Patients.FindAsync(1))!.Phone = null; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/patient-booking/appointments", Request(1, Start))).StatusCode);
    }

    [Fact]
    public async Task Booking_RequiresPatientAuthentication()
    {
        using var factory = new BookingFactory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/patient-booking/dentists")).StatusCode);
        Authenticate(client, 1, "Admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/patient-booking/appointments", Request(1, Start))).StatusCode);
    }

    [Fact]
    public void Schedule_UsesAucklandDayAndCorrectUtcOffsetAcrossDst()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var schedule = new BookingSchedule(config);
        var winter = schedule.Candidates(new DateOnly(2026, 7, 6), 60, DateTimeOffset.Parse("2026-07-05T00:00:00Z")).First();
        var summer = schedule.Candidates(new DateOnly(2026, 10, 5), 60, Now).First();
        Assert.Equal(21, winter.Hour); Assert.Equal(20, summer.Hour);
    }

    [Fact]
    public async Task ConcurrentPostgresBookings_OnlyOnePatientCanReserveTheSameDentistTime()
    {
        using var factory = new BookingFactory(usePostgres: true);
        using var firstClient = factory.CreateClient(); using var secondClient = factory.CreateClient();
        var seed = await Seed(factory.Services);
        Authenticate(firstClient, seed.Patients[0].Id); Authenticate(secondClient, seed.Patients[1].Id);
        try
        {
            var request = new { dentalServiceId = seed.Service.Id, dentistId = seed.Dentists[0].Id, startTime = Start };
            var responses = await Task.WhenAll(firstClient.PostAsJsonAsync("/api/patient-booking/appointments", request),
                secondClient.PostAsJsonAsync("/api/patient-booking/appointments", request));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            foreach (var response in responses) response.Dispose();
        }
        finally
        {
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
            db.Appointments.RemoveRange(await db.Appointments.Where(a => a.DentalServiceId == seed.Service.Id).ToListAsync());
            db.Patients.RemoveRange(seed.Patients); db.Dentists.RemoveRange(seed.Dentists); db.DentalServices.Remove(seed.Service);
            await db.SaveChangesAsync();
        }
    }

    private static object Request(int dentistId, DateTime startTime) => new { dentalServiceId = 1, dentistId, startTime };
    private static void Authenticate(HttpClient client, int id, string role = "Patient") => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(id, role));
    private record SeedData(Patient[] Patients, Dentist[] Dentists, DentalService Service);
    private static async Task<SeedData> Seed(IServiceProvider services)
    {
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DentistDbContext>();
        var marker = Guid.NewGuid().ToString("N");
        var patients = Enumerable.Range(0, 2).Select(i => new Patient { FirstName = "Booking", LastName = "Test", Email = $"booking-{marker}-{i}@test.example", Phone = $"021{i}1234567" }).ToArray();
        var dentists = Enumerable.Range(0, 3).Select(i => new Dentist { FirstName = i == 2 ? "Inactive" : "Dentist", LastName = $"Test{i}", Email = $"dentist-{marker}-{i}@test.example", RegistrationNumber = $"{marker}-{i}", IsActive = i != 2 }).ToArray();
        var service = new DentalService { Name = $"Booking test {marker}", DurationMinutes = 60, Price = 120, IsActive = true };
        db.Patients.AddRange(patients); db.Dentists.AddRange(dentists); db.DentalServices.Add(service); await db.SaveChangesAsync();
        return new SeedData(patients, dentists, service);
    }
    private class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private class SlotResult { public List<Slot> Slots { get; set; } = []; }
    private class Slot { public DateTime StartTime { get; set; } public DateTime EndTime { get; set; } }
    private class BookingFactory(bool usePostgres = false) : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:Key", Key); builder.UseSetting("Jwt:Issuer", "booking-tests"); builder.UseSetting("Jwt:Audience", "booking-tests");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new FixedClock());
                if (!usePostgres)
                {
                    services.RemoveAll<DbContextOptions<DentistDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<DentistDbContext>>();
                    connection.Open(); services.AddDbContext<DentistDbContext>(o => o.UseSqlite(connection));
                }
            });
        }
        protected override Microsoft.Extensions.Hosting.IHost CreateHost(Microsoft.Extensions.Hosting.IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            if (!usePostgres) { using var scope = host.Services.CreateScope(); scope.ServiceProvider.GetRequiredService<DentistDbContext>().Database.EnsureCreated(); Seed(host.Services).GetAwaiter().GetResult(); }
            return host;
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) connection.Dispose(); }
    }
}

