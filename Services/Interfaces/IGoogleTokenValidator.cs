using Google.Apis.Auth;

namespace dentist_clinic_api.Services.Interfaces;

public interface IGoogleTokenValidator
{
    Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken);
}
