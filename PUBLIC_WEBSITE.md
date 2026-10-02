# Clinic information pages

Public pages: `/contact`, `/about`, `/team`. Navigation includes an accessible mobile menu.

Contact details were supplied by the owner: `mohammeddabboornz@gmail.com` and `022 597 4228`. Email and telephone links open the visitor's email/phone app; no message is sent by the server.

`GET /api/public/clinic` returns configured appointment hours, clinic contact details, and aggregate figures. Patients helped counts distinct patients with **Completed** appointments, not registered accounts or pending requests. Dentist/service counts include active records only. Figures animate when scrolled into view, with reduced-motion support and a stable accessible number.

`GET /api/public/clinic/team` publishes active dentists' names, specialties and supplied biographies. It excludes their email addresses, phone numbers and registration identifiers. Professional experience comes from `Dentist.Biography`; years of experience are not inferred from the record creation date. Staff can update existing dentist records to complete biographies.

Genuine testimonials can be supplied through `Clinic:Reviews`, an array of objects with `Author` and `Quote`. Publish only approved real feedback. Until provided, About shows an invitation to give feedback rather than invented quotes or ratings. Optional contact configuration keys: `Clinic:Email`, `Clinic:Phone`, `Clinic:TelephoneLink`.
