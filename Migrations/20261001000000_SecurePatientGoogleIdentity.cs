using dentist_clinic_api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace dentist_clinic_api.Migrations;

[DbContext(typeof(DentistDbContext))]
[Migration("20261001000000_SecurePatientGoogleIdentity")]
public class SecurePatientGoogleIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Fail on ambiguous legacy records; never merge patient records automatically.
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_Patients_NormalizedEmail"
            ON "Patients" (lower(btrim("Email")));
            CREATE UNIQUE INDEX "IX_PatientIdentities_PatientId_Provider"
            ON "PatientIdentities" ("PatientId", "Provider");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX "IX_PatientIdentities_PatientId_Provider";
            DROP INDEX "IX_Patients_NormalizedEmail";
            """);
    }
}
