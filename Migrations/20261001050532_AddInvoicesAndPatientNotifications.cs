using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace dentist_clinic_api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicesAndPatientNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AppointmentId = table.Column<int>(type: "integer", nullable: false),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    SupplierName = table.Column<string>(type: "text", nullable: false),
                    SupplierAddress = table.Column<string>(type: "text", nullable: false),
                    SupplierGstNumber = table.Column<string>(type: "text", nullable: true),
                    CustomerName = table.Column<string>(type: "text", nullable: false),
                    CustomerEmail = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    GstRate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    GstAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    GstRegistered = table.Column<bool>(type: "boolean", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    PaymentStatus = table.Column<string>(type: "text", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    BankTransferReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VerificationReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VerifiedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PayPalOrderId = table.Column<string>(type: "text", nullable: true),
                    PayPalApprovalUrl = table.Column<string>(type: "text", nullable: true),
                    PayPalCaptureId = table.Column<string>(type: "text", nullable: true),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientNotifications_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PatientNotifications_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_AppointmentId",
                table: "Invoices",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PatientId",
                table: "Invoices",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PayPalCaptureId",
                table: "Invoices",
                column: "PayPalCaptureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PayPalOrderId",
                table: "Invoices",
                column: "PayPalOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientNotifications_AppointmentId_Type",
                table: "PatientNotifications",
                columns: new[] { "AppointmentId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientNotifications_PatientId",
                table: "PatientNotifications",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "PatientNotifications");
        }
    }
}
