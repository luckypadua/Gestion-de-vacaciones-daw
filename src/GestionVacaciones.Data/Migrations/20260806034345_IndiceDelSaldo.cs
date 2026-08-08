using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionVacaciones.Data.Migrations
{
    /// <inheritdoc />
    public partial class IndiceDelSaldo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Solicitud_EmpleadoId_Estado_FechaInicio",
                table: "Solicitudes",
                columns: new[] { "EmpleadoId", "Estado", "FechaInicio" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Solicitud_EmpleadoId_Estado_FechaInicio",
                table: "Solicitudes");
        }
    }
}
