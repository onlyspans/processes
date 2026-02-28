using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onlyspans.Processes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    raw_yaml = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    script = table.Column<string>(type: "text", nullable: true),
                    script_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    optional = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    blocking = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    on_failure = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    timeout = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_steps_processes_process_id",
                        column: x => x.process_id,
                        principalTable: "processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variables", x => x.id);
                    table.ForeignKey(
                        name: "FK_variables_processes_process_id",
                        column: x => x.process_id,
                        principalTable: "processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_processes_project_id_environment_id_release_version",
                table: "processes",
                columns: new[] { "project_id", "environment_id", "release_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_steps_process_id_order",
                table: "steps",
                columns: new[] { "process_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_variables_process_id_name",
                table: "variables",
                columns: new[] { "process_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steps");

            migrationBuilder.DropTable(
                name: "variables");

            migrationBuilder.DropTable(
                name: "processes");
        }
    }
}
