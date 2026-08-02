using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionVacaciones.Data.Migrations
{
    /// <inheritdoc />
    public partial class InicialV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Empleados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Correo = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ManagerId = table.Column<int>(type: "int", nullable: true),
                    DesignadoId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empleados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Empleados_Empleados_DesignadoId",
                        column: x => x.DesignadoId,
                        principalTable: "Empleados",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Empleados_Empleados_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Empleados",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Solicitudes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmpleadoId = table.Column<int>(type: "int", nullable: false),
                    FechaInicio = table.Column<DateOnly>(type: "date", nullable: false),
                    FechaFin = table.Column<DateOnly>(type: "date", nullable: false),
                    DiasCorridos = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    FechaCreacion = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Solicitudes", x => x.Id);
                    table.CheckConstraint("CK_Solicitud_DiasCoincidenConPeriodo", "[DiasCorridos] = DATEDIFF(day, [FechaInicio], [FechaFin]) + 1");
                    table.CheckConstraint("CK_Solicitud_DiasPositivos", "[DiasCorridos] > 0");
                    table.CheckConstraint("CK_Solicitud_EstadoValido", "[Estado] BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_Solicitud_PeriodoCoherente", "[FechaFin] >= [FechaInicio]");
                    table.ForeignKey(
                        name: "FK_Solicitudes_Empleados_EmpleadoId",
                        column: x => x.EmpleadoId,
                        principalTable: "Empleados",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Empleado_Correo",
                table: "Empleados",
                column: "Correo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empleados_DesignadoId",
                table: "Empleados",
                column: "DesignadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Empleados_ManagerId",
                table: "Empleados",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Solicitud_EmpleadoId_FechaCreacion",
                table: "Solicitudes",
                columns: new[] { "EmpleadoId", "FechaCreacion" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Solicitudes");

            migrationBuilder.DropTable(
                name: "Empleados");
        }
    }
}
