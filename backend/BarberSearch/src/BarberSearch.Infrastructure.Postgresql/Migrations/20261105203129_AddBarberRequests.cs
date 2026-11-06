using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddBarberRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "barber_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    years_of_experience = table.Column<int>(type: "integer", nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    about = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    has_salon = table.Column<bool>(type: "boolean", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barber_requests", x => x.id);
                    table.CheckConstraint("ck_barber_requests_about", "about IS NULL OR char_length(about) BETWEEN 1 AND 2000");
                    table.CheckConstraint("ck_barber_requests_city", "char_length(city) BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_barber_requests_display_name", "char_length(display_name) BETWEEN 1 AND 120");
                    table.CheckConstraint("ck_barber_requests_rejection_reason", "rejection_reason IS NULL OR char_length(rejection_reason) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_barber_requests_status", "status IN (1, 2, 3)");
                    table.CheckConstraint("ck_barber_requests_years_of_experience", "years_of_experience BETWEEN 1 AND 70");
                    table.ForeignKey(
                        name: "fk_barber_requests_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_barber_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_barber_requests_created_at_id",
                table: "barber_requests",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_barber_requests_reviewed_by_user_id",
                table: "barber_requests",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_barber_requests_user_id_created_at",
                table: "barber_requests",
                columns: new[] { "user_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ux_barber_requests_pending_user",
                table: "barber_requests",
                column: "user_id",
                unique: true,
                filter: "status = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "barber_requests");
        }
    }
}
