using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddUserConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<IPAddress>(
                name: "consent_ip",
                table: "users",
                type: "inet",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consent_policy_version",
                table: "users",
                type: "text",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consented_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_consent",
                table: "users",
                sql: "(consent_policy_version IS NULL) = (consented_at IS NULL) AND (consent_policy_version IS NULL OR char_length(consent_policy_version) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_consent",
                table: "users");

            migrationBuilder.DropColumn(
                name: "consent_ip",
                table: "users");

            migrationBuilder.DropColumn(
                name: "consent_policy_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "consented_at",
                table: "users");
        }
    }
}
