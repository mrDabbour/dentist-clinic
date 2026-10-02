using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using System.Text;
using dentist_clinic_api.Data;
using dentist_clinic_api.Middleware;
using Scalar.AspNetCore;
using dentist_clinic_api.Services;
using dentist_clinic_api.Services.Interfaces;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddSingleton<BookingSchedule>();
builder.Services.AddSingleton<BillingSettings>();
 builder.Services.AddScoped<InvoiceService>();
 builder.Services.AddHttpClient<IPayPalGateway, PayPalGateway>(client => client.Timeout = TimeSpan.FromSeconds(25));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

// =========================================================
// Database
// =========================================================

builder.Services.AddDbContext<DentistDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);


// =========================================================
// JWT Authentication
// =========================================================

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT key is not configured."
    );

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer =
                    builder.Configuration["Jwt:Issuer"],

                ValidAudience =
                    builder.Configuration["Jwt:Audience"],

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)
                    ),

                ClockSkew = TimeSpan.Zero
            };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many sign-in attempts. Please wait a minute and try again." }, cancellationToken);
    };
    options.AddPolicy("patient-google-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});


// =========================================================
// Controllers / OpenAPI / Error Handling
// =========================================================

builder.Services.AddControllers();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();

        document.Components.SecuritySchemes =
            new Dictionary<string, IOpenApiSecurityScheme>
            {
                ["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    In = ParameterLocation.Header,
                    BearerFormat = "JWT",
                    Description = "Enter your JWT bearer token."
                }
            };

        return Task.CompletedTask;
    });
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:4200" })
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
// =========================================================
// Build Application
// =========================================================

var app = builder.Build();


// =========================================================
// HTTP Pipeline
// =========================================================

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.UseCors("AngularDev");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();


// =========================================================
// Root Endpoint
// =========================================================

app.MapGet("/", () => new
{
    message = "Dentist Clinic API is running",
    status = "OK"
});

app.Run();

public partial class Program { }
