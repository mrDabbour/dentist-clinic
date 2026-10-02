using dentist_clinic_api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace dentist_clinic_api.Migrations;

[DbContext(typeof(DentistDbContext))]
[Migration("20261001010000_PreventOverlappingAppointments")]
public class PreventOverlappingAppointments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE EXTENSION IF NOT EXISTS btree_gist;
        ALTER TABLE "Appointments" ADD CONSTRAINT "EX_Appointments_DentistTime"
        EXCLUDE USING gist ("DentistId" WITH =, tstzrange("StartTime", "EndTime", '[)') WITH &&)
        WHERE ("Status" <> 'Cancelled');
        ALTER TABLE "Appointments" ADD CONSTRAINT "EX_Appointments_PatientTime"
        EXCLUDE USING gist ("PatientId" WITH =, tstzrange("StartTime", "EndTime", '[)') WITH &&)
        WHERE ("Status" <> 'Cancelled');
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE "Appointments" DROP CONSTRAINT "EX_Appointments_PatientTime";
        ALTER TABLE "Appointments" DROP CONSTRAINT "EX_Appointments_DentistTime";
        """);
}
