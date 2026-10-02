using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace dentist_clinic_api.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is dentist_clinic_api.Services.BillingException billingException)
        {
            httpContext.Response.StatusCode = billingException.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(new { message = billingException.Message, code = billingException.Code }, cancellationToken);
            return true;
        }
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is Npgsql.PostgresException { SqlState: "23P01", ConstraintName: "EX_Appointments_DentistTime" or "EX_Appointments_PatientTime" })
            {
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    message = "That time is no longer available. Please choose another time."
                }, cancellationToken);
                return true;
            }
        }

        _logger.LogError(
            exception,
            "Unhandled exception occurred while processing {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = "An unexpected error occurred. Please try again later."
        };

        httpContext.Response.StatusCode =
            StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            cancellationToken);

        return true;
    }
}
