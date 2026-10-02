# Patient booking journey

The patient portal now supports Service -> Dentist -> Date and time -> Review -> Booking request received.

- Only active services and dentists are offered.
- The date/time screen displays the clinic timezone and filters out existing dentist and patient appointments.
- Changing the service or dentist clears downstream selections. Back and Edit actions let the patient revisit earlier steps.
- The review step shows the patient contact details, selected treatment, duration, clinician, date/time and estimated fee; notes are optional.
- Submission takes the patient ID from the validated patient JWT, never from the request body. New bookings are Pending until staff confirm them.
- The confirmation URL includes the appointment ID so a refresh can restore the result. The retrieval endpoint only returns the signed-in patient's appointment.
- The sign-out button is styled and remains visible on mobile.

## Opening hours

Default: Monday-Friday, 09:00-17:00, Pacific/Auckland; 15-minute starting intervals; bookings up to 90 days ahead. The whole service duration must fit before closing. Times are stored in UTC and converted to the configured clinic timezone for display, including daylight saving.

These are clinic-wide defaults for all active dentists. Individual working rosters, holidays and service-to-dentist eligibility are not represented in the current database.

Override the defaults through the `Booking` configuration section or equivalent environment variables:

```json
{
  "Booking": {
    "TimeZone": "Pacific/Auckland",
    "OpensAt": "09:00",
    "ClosesAt": "17:00",
    "SlotIntervalMinutes": 15,
    "DaysAhead": 90,
    "WorkingDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
  }
}
```

## API

All endpoints below require a patient JWT.

- `GET /api/patient-booking/config`: booking window, timezone and opening hours.
- `GET /api/patient-booking/dentists`: active clinician cards without staff email or phone information.
- `GET /api/patient-booking/slots?dentalServiceId=1&dentistId=1&date=2026-10-05`: available UTC start/end pairs for a clinic-local date.
- `POST /api/patient-booking/appointments`: `{ dentalServiceId, dentistId, startTime, notes }`; returns 201 and the pending booking.
- `GET /api/patient-booking/appointments/{id}`: retrieves an owned booking, otherwise 404.

A conflict returns 409, and the UI refreshes the time choices. No email or SMS notification is implied by this flow.

## Database safeguards

Migration `20261001010000_PreventOverlappingAppointments` installs PostgreSQL `btree_gist` and adds exclusion constraints for non-cancelled dentist and patient appointment ranges. This protects concurrent patient and staff inserts/updates; adjacent appointments are allowed. Applying it requires extension privileges and a database without legacy overlapping appointments. It was applied to the configured local database.

Reference: https://www.postgresql.org/docs/current/rangetypes.html#RANGETYPES-CONSTRAINT
