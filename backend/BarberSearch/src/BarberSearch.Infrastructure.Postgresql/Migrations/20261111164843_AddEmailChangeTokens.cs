using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailChangeTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_change_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_change_tokens", x => x.id);
                    table.CheckConstraint("ck_email_change_tokens_new_email", "position('@' in new_email) > 1");
                    table.CheckConstraint("ck_email_change_tokens_new_email_length", "char_length(new_email) BETWEEN 3 AND 254");
                    table.CheckConstraint("ck_email_change_tokens_token_hash", "char_length(token_hash) = 64");
                    table.ForeignKey(
                        name: "fk_email_change_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_change_tokens_expires_at",
                table: "email_change_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_email_change_tokens_active",
                table: "email_change_tokens",
                column: "user_id",
                unique: true,
                filter: "consumed_at IS NULL AND revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_email_change_tokens_hash",
                table: "email_change_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_change_tokens");
        }
    }
}
