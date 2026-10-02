
using dentist_clinic_api.Models.Auth;
using Microsoft.EntityFrameworkCore;
using dentist_clinic_api.Models;

namespace dentist_clinic_api.Data;

public class DentistDbContext : DbContext
{
    public DentistDbContext(DbContextOptions<DentistDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<Dentist> Dentists { get; set; }
    public DbSet<DentalService> DentalServices { get; set; }
   public DbSet<PatientIdentity> PatientIdentities { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<PatientNotification> PatientNotifications { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.Ignore(i => i.Number);
            entity.HasIndex(i => i.AppointmentId).IsUnique();
            entity.HasIndex(i => i.PayPalOrderId).IsUnique();
            entity.HasIndex(i => i.PayPalCaptureId).IsUnique();
            entity.HasOne<Appointment>().WithMany().HasForeignKey(i => i.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Patient>().WithMany().HasForeignKey(i => i.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(i => i.Subtotal).HasPrecision(10, 2);
            entity.Property(i => i.GstAmount).HasPrecision(10, 2);
            entity.Property(i => i.Total).HasPrecision(10, 2);
            entity.Property(i => i.GstRate).HasPrecision(5, 4);
            entity.Property(i => i.BankTransferReference).HasMaxLength(100);
            entity.Property(i => i.VerificationReference).HasMaxLength(200);
        });
        modelBuilder.Entity<PatientNotification>(entity =>
        {
            entity.HasIndex(n => new { n.AppointmentId, n.Type }).IsUnique();
            entity.HasOne<Appointment>().WithMany().HasForeignKey(n => n.AppointmentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Patient>().WithMany().HasForeignKey(n => n.PatientId).OnDelete(DeleteBehavior.Cascade);
        });

        // =========================
        // Dentist
        // =========================
        modelBuilder.Entity<Dentist>(entity =>
        {
            entity.Property(d => d.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(d => d.LastName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(d => d.Email)
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(d => d.Phone)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(d => d.RegistrationNumber)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(d => d.Specialty)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(d => d.Biography)
                .HasMaxLength(1000);

            entity.HasIndex(d => d.Email)
                .IsUnique();

            entity.HasIndex(d => d.RegistrationNumber)
                .IsUnique();

            entity.HasIndex(d => d.IsActive);
        });

        // =========================
        // Dental Service
        // =========================
        modelBuilder.Entity<DentalService>(entity =>
        {
            entity.Property(s => s.Name)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(s => s.Category)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(s => s.Description)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(s => s.Price)
                .HasPrecision(10, 2)
                .IsRequired();

            entity.Property(s => s.DurationMinutes)
                .IsRequired();

            entity.HasIndex(s => s.Name);
            entity.HasIndex(s => s.Category);
            entity.HasIndex(s => s.IsActive);
        });

        // =========================
        // Appointment
        // =========================
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.Property(a => a.Status)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(a => a.Notes)
                .HasMaxLength(1000);

            // Appointment -> Patient
            entity.HasOne(a => a.Patient)
                .WithMany()
                .HasForeignKey(a => a.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            // Appointment -> Dentist
            entity.HasOne(a => a.Dentist)
                .WithMany()
                .HasForeignKey(a => a.DentistId)
                .OnDelete(DeleteBehavior.Restrict);

            // Appointment -> DentalService
            entity.HasOne(a => a.DentalService)
                .WithMany()
                .HasForeignKey(a => a.DentalServiceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => a.PatientId);
            entity.HasIndex(a => a.DentistId);
            entity.HasIndex(a => a.DentalServiceId);
            entity.HasIndex(a => a.StartTime);
            entity.HasIndex(a => a.Status);

            // Useful when checking a dentist's schedule
            entity.HasIndex(a => new
            {
                a.DentistId,
                a.StartTime
            });

        });

        modelBuilder.Entity<User>(entity =>
{
    entity.Property(u => u.FirstName)
        .HasMaxLength(100)
        .IsRequired();

    entity.Property(u => u.LastName)
        .HasMaxLength(100)
        .IsRequired();

    entity.Property(u => u.Email)
        .HasMaxLength(255)
        .IsRequired();

    entity.Property(u => u.PasswordHash)
        .IsRequired();

    entity.Property(u => u.Role)
        .HasMaxLength(50)
        .IsRequired();

    entity.HasIndex(u => u.Email)
        .IsUnique();

    entity.HasIndex(u => u.Role);

    entity.HasIndex(u => u.IsActive);
});


modelBuilder.Entity<PatientIdentity>(entity =>
{
    entity.HasKey(x => x.Id);

    entity.Property(x => x.Provider)
        .IsRequired()
        .HasMaxLength(50);

    entity.Property(x => x.ProviderSubjectId)
        .IsRequired()
        .HasMaxLength(255);

    entity.HasIndex(x => new
    {
        x.Provider,
        x.ProviderSubjectId
    })
    .IsUnique();

    entity.HasOne(x => x.Patient)
        .WithMany()
        .HasForeignKey(x => x.PatientId)
        .OnDelete(DeleteBehavior.Cascade);
});
    }
}
