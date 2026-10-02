using Google.Apis.Auth;
using dentist_clinic_api.Services.Interfaces;

namespace dentist_clinic_api.Services;

public class GoogleTokenValidator(IConfiguration configuration) : IGoogleTokenValidator
{
    public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken)
    {
        var clientId = configuration["GoogleAuth:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Google sign-in is not configured.");

        // The Google library verifies signature, issuer, expiry and audience.
        return GoogleJsonWebSignature.ValidateAsync(idToken,
            new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
    }
}
