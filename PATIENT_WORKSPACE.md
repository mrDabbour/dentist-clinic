# Patient appointment workspace

`/my-appointments` is a guarded patient page. The public navigation shows its link for signed-in patients, and the booking and invoice pages link back to it.

`GET /api/patient-booking/appointments?view=upcoming&page=1&pageSize=10` returns only the patient identified by the authenticated JWT. Supported views are upcoming, history, and all. Page size is limited to 50. Terminal appointment statuses and elapsed visits appear in history. Responses include aggregate counts, clinic timezone, and a minimal payment summary. Reading the list does not create invoices. Where an invoice exists, its frozen total is used instead of the current service price.

The page checks status every 15 seconds. Confirmed/completed visits link to payment requests; paid visits link to printable receipts. Bank transfers remain awaiting verification until checked by staff. Appointment changes can be requested through the visitor's email app; the application does not silently cancel or reschedule visits.

Public service and clinician booking links carry `service` / `dentist` query parameters. The sign-in and phone-profile steps preserve a validated internal return URL. Booking preselection uses active service/dentist data and never submits automatically. Payment pages label paid documents Payment receipt and support print/save PDF.

Automated email confirmations/reminders and self-service change rules are not enabled. The clinic must supply its email provider configuration and cancellation/rescheduling policy for those features.
