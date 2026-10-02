# Google patient sign-in

## Local flow

Open `http://localhost:4200/patient-login` with the API available on `http://localhost:8080`.

1. Angular fetches the public Google client ID from `GET /api/patient-auth/config`.
2. The official Google Identity Services button returns an ID token to its JavaScript callback.
3. Angular posts `{ "idToken": "..." }` to `POST /api/patient-auth/google`.
4. The API uses Google's .NET library to validate the signature, issuer, expiry and configured audience. It requires a verified email and a stable Google subject.
5. The API atomically creates/links a patient and returns a clinic patient JWT, its expiry, the patient profile and `requiresProfileCompletion`.
6. New patients enter their phone number. Returning patients with a contact number go directly to `/book`.

The patient session is stored in `sessionStorage` and cleared on expiry or sign-out. It is independent of staff login. Browser storage is still accessible to JavaScript: keep the frontend free of injected scripts. A future cookie-based session requires a separate CSRF-protected design; this implementation uses explicit Bearer headers and does not set authentication cookies.

## Google Console setup

Use an OAuth client of type **Web application**. Add `http://localhost:4200` as an authorized JavaScript origin. For a deployment, also add the exact HTTPS frontend origin. This uses the JavaScript popup callback, not a Google form-post redirect endpoint.

Set the backend `GoogleAuth:ClientId` through .NET user secrets or the environment variable `GoogleAuth__ClientId`. Docker Compose currently supplies a public client ID; if changing it, update that value as well. No Google client secret is needed for this ID-token flow. Never put the clinic JWT signing key in frontend configuration.

Configure your OAuth consent screen and test users as appropriate for the Google project's publishing state. Allow Google's `https://accounts.google.com/gsi/client` script in the deployed site's content-security policy. Serve the frontend and API over HTTPS in production.

The Angular patient API URL is centralized in `src/app/services/patient-auth.service.ts`. The existing staff services still have their own development URLs. Configure `Cors:AllowedOrigins` (for example `Cors__AllowedOrigins__0=https://your-clinic.example`) for your deployed frontend. Behind a proxy, configure trusted forwarded headers before relying on per-IP throttling; do not trust arbitrary forwarded IP headers.

## API contract

- `GET /api/patient-auth/config`: public `{ clientId }`, or 503 if not configured.
- `POST /api/patient-auth/google`: public ID-token exchange; 20 attempts per IP per minute.
- `GET /api/patient-auth/me`: patient-only authoritative profile.
- `PUT /api/patient-auth/profile`: patient-only `{ phone }`; normalizes phone formatting and returns the profile.
- `GET /api/patient-auth/services`: patient-only active service catalog for the existing booking screen.

Authenticated requests must include `Authorization: Bearer <clinic patient JWT>`.

Profile responses include `patientId`, `id` (compatibility alias), `firstName`, `lastName`, `email`, nullable `phone`, and `requiresProfileCompletion`. Login also returns `role`, `token` and `expiresAt`.

Error statuses: 400 invalid input, 401 invalid/expired credentials, 403 wrong role, 409 account-linking or contact-number conflict, 429 too many attempts, 503 unavailable configuration or Google validation service.

## Account-linking policy

Returning patients are identified by Google's stable `sub`, never by a changed Google email. Their existing clinic contact details are preserved.

An existing patient record can be linked automatically only if Google is authoritative for the address (Gmail or verified Google Workspace), and that patient has no other Google identity. A third-party email collision or an existing different Google subject returns 409 and asks the patient to contact the clinic. Staff-assisted identity verification is required in those cases; no verification or merging workflow is invented by this change.

Patient creation and identity creation share a serializable transaction. Concurrent initial sign-ins retry after PostgreSQL serialization/uniqueness conflicts.

## Database migration

`20261001000000_SecurePatientGoogleIdentity` adds unique indexes on normalized patient email and `(PatientId, Provider)`. It has been applied to the configured local database. For another environment:

```powershell
dotnet ef database update
```

If legacy records contain duplicate normalized emails or provider links, the migration stops. Review those records with the clinic; do not automatically merge patient records. The normalized-email index is a PostgreSQL expression index maintained by explicit migration SQL, outside the EF model snapshot.

## Validation

Backend authentication tests use isolated SQLite databases and a substitute Google token validator; existing integration tests and the concurrency test use the configured PostgreSQL database. Browser tests cover credential exchange, session expiry, returning-user routing, account-linking errors, unavailable Google script and component cleanup.

A real Google popup sign-in still needs a manual check using a permitted Google account and the configured Google Console origin. Automated tests do not claim to authenticate a real Google account.

The complete booking flow is documented in [PATIENT_BOOKING.md](PATIENT_BOOKING.md).

Google verification guidance: https://developers.google.com/identity/gsi/web/guides/verify-google-id-token


