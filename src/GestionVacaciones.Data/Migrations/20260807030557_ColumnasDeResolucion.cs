using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionVacaciones.Data.Migrations
{
    /// <inheritdoc />
    public partial class ColumnasDeResolucion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FechaResolucion",
                table: "Solicitudes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoDeRechazo",
                table: "Solicitudes",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResueltoPorId",
                table: "Solicitudes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_ResueltoPorId",
                table: "Solicitudes",
                column: "ResueltoPorId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Solicitud_ResolucionCoherente",
                table: "Solicitudes",
                sql: "([Estado] = 0 AND [ResueltoPorId] IS NULL AND [FechaResolucion] IS NULL AND [MotivoDeRechazo] IS NULL) OR ([Estado] = 1 AND [ResueltoPorId] IS NOT NULL AND [FechaResolucion] IS NOT NULL AND [MotivoDeRechazo] IS NULL) OR ([Estado] = 2 AND [ResueltoPorId] IS NOT NULL AND [FechaResolucion] IS NOT NULL AND [MotivoDeRechazo] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Solicitudes_Empleados_ResueltoPorId",
                table: "Solicitudes",
                column: "ResueltoPorId",
                principalTable: "Empleados",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Solicitudes_Empleados_ResueltoPorId",
                table: "Solicitudes");

            migrationBuilder.DropIndex(
                name: "IX_Solicitudes_ResueltoPorId",
                table: "Solicitudes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Solicitud_ResolucionCoherente",
                table: "Solicitudes");

            migrationBuilder.DropColumn(
                name: "FechaResolucion",
                table: "Solicitudes");

            migrationBuilder.DropColumn(
                name: "MotivoDeRechazo",
                table: "Solicitudes");

            migrationBuilder.DropColumn(
                name: "ResueltoPorId",
                table: "Solicitudes");
        }
    }
}
