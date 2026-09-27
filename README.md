# 🦷 Dentist Clinic Management System

A full-stack Dentist Clinic Management System built with ASP.NET Core, PostgreSQL, Entity Framework Core, and Angular.

The project is designed around real clinic workflows including patient management, dentists, dental services, appointment scheduling, authentication, authorization, and appointment status management.

## Backend Technology

- ASP.NET Core / C#
- .NET 10
- Entity Framework Core
- PostgreSQL
- JWT Authentication
- Role-Based Authorization
- OpenAPI + Scalar
- Docker / Docker Compose
- xUnit Integration Testing

## Core Features

### Authentication & Authorization

- JWT-based authentication
- Password hashing
- Protected API endpoints
- Role-based access control
- Admin, Receptionist, and Dentist roles

### Patient Management

- Create patients
- View patients
- Update patient information
- Delete patients

### Dentist Management

- Manage dentist information
- Active/inactive dentist status
- Admin-controlled create, update, and delete operations

### Dental Services

- Manage clinic services
- Service pricing
- Treatment duration
- Active/inactive services

### Appointment Scheduling

The appointment system contains business rules rather than basic CRUD only.

It supports:

- Patient, dentist, and service validation
- Automatic appointment end-time calculation
- Dentist scheduling conflict detection
- Patient scheduling conflict detection
- Cancelled appointment handling
- Appointment filtering by dentist, patient, status, and date

### Appointment Workflow

Appointments follow a controlled status workflow:

Pending → Confirmed → Completed

Appointments can also be marked as:

- Cancelled
- No-Show

Status changes are handled through dedicated workflow endpoints rather than allowing arbitrary status changes through the normal update endpoint.

## Error Handling

The API includes centralized exception handling and returns safe API error responses without exposing internal implementation details.

## API Documentation

Interactive API documentation is available through Scalar during development.

The API also exposes an OpenAPI specification.

## Testing

The project includes integration tests covering:

- API availability
- Authentication
- JWT-protected endpoints
- Role-based authorization
- Appointment business rules
- Scheduling conflicts
- Appointment status workflows

Current test suite:

**8 tests passing**

## Docker

The ASP.NET Core API can run inside a Linux Docker container.

Docker Compose provides:

- API container configuration
- Port mapping
- Environment-based configuration
- Secure injection of database and JWT settings

The application runs on:

http://localhost:8080

Example endpoint:

GET /api/DentalServices

PostgreSQL is accessed through Entity Framework Core.

Secrets and environment files are excluded from Git.

## Project Status

### Backend

✅ REST API  
✅ PostgreSQL database  
✅ Entity Framework Core  
✅ Authentication  
✅ Role-based authorization  
✅ Appointment scheduling logic  
✅ Error handling  
✅ OpenAPI documentation  
✅ Integration testing  
✅ Docker  

### Frontend

🚧 Angular frontend in development

## Next Step

Connect the Angular frontend to the ASP.NET Core API and build the complete clinic user experience.