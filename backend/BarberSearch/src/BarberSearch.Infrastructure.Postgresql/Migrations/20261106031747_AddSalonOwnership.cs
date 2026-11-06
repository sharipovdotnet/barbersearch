using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddSalonOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "moderation_status",
                table: "salons",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_user_id",
                table: "salons",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "salons",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "salons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_user_id",
                table: "salons",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_salons_owner_user_id",
                table: "salons",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_salons_reviewed_by_user_id",
                table: "salons",
                column: "reviewed_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_salons_moderation_status",
                table: "salons",
                sql: "moderation_status IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_salons_rejection_reason",
                table: "salons",
                sql: "rejection_reason IS NULL OR char_length(rejection_reason) BETWEEN 1 AND 500");

            migrationBuilder.AddForeignKey(
                name: "fk_salons_users_owner_user_id",
                table: "salons",
                column: "owner_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_salons_users_reviewed_by_user_id",
                table: "salons",
                column: "reviewed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_salons_users_owner_user_id",
                table: "salons");

            migrationBuilder.DropForeignKey(
                name: "fk_salons_users_reviewed_by_user_id",
                table: "salons");

            migrationBuilder.DropIndex(
                name: "ix_salons_owner_user_id",
                table: "salons");

            migrationBuilder.DropIndex(
                name: "ix_salons_reviewed_by_user_id",
                table: "salons");

            migrationBuilder.DropCheckConstraint(
                name: "ck_salons_moderation_status",
                table: "salons");

            migrationBuilder.DropCheckConstraint(
                name: "ck_salons_rejection_reason",
                table: "salons");

            migrationBuilder.DropColumn(
                name: "moderation_status",
                table: "salons");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "salons");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "salons");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "salons");

            migrationBuilder.DropColumn(
                name: "reviewed_by_user_id",
                table: "salons");
        }
    }
}
