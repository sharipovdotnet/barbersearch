using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "avatar_moderation_status",
                table: "users",
                type: "smallint",
                nullable: false,
                defaultValue: (short)2);

            migrationBuilder.AddColumn<string>(
                name: "avatar_rejection_reason",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "avatar_reviewed_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "avatar_reviewed_by_user_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "avatar_submitted_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill: существующие кадры не перепроверяются — статус уже Approved
            // (default 2), а метка поступления в очередь берётся из updated_at: другой
            // технической метки у строки нет. Аккаунты без аватара остаются с NULL.
            migrationBuilder.Sql(
                "UPDATE users SET avatar_submitted_at = updated_at WHERE avatar_url IS NOT NULL;");

            migrationBuilder.AddColumn<short>(
                name: "moderation_status",
                table: "portfolio_photos",
                type: "smallint",
                nullable: false,
                defaultValue: (short)2);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "portfolio_photos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "portfolio_photos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_user_id",
                table: "portfolio_photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_avatar_moderation_status_submitted_at",
                table: "users",
                columns: new[] { "avatar_moderation_status", "avatar_submitted_at", "id" },
                filter: "avatar_url IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_avatar_reviewed_by_user_id",
                table: "users",
                column: "avatar_reviewed_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_avatar_moderation_status",
                table: "users",
                sql: "avatar_moderation_status IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_avatar_rejection_reason",
                table: "users",
                sql: "avatar_rejection_reason IS NULL OR char_length(avatar_rejection_reason) BETWEEN 1 AND 500");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_photos_moderation_status_created_at",
                table: "portfolio_photos",
                columns: new[] { "moderation_status", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_photos_reviewed_by_user_id",
                table: "portfolio_photos",
                column: "reviewed_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolio_photos_moderation_status",
                table: "portfolio_photos",
                sql: "moderation_status IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolio_photos_rejection_reason",
                table: "portfolio_photos",
                sql: "rejection_reason IS NULL OR char_length(rejection_reason) BETWEEN 1 AND 500");

            migrationBuilder.AddForeignKey(
                name: "fk_portfolio_photos_users_reviewed_by_user_id",
                table: "portfolio_photos",
                column: "reviewed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_users_users_avatar_reviewed_by_user_id",
                table: "users",
                column: "avatar_reviewed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_portfolio_photos_users_reviewed_by_user_id",
                table: "portfolio_photos");

            migrationBuilder.DropForeignKey(
                name: "fk_users_users_avatar_reviewed_by_user_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_avatar_moderation_status_submitted_at",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_avatar_reviewed_by_user_id",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_avatar_moderation_status",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_avatar_rejection_reason",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_portfolio_photos_moderation_status_created_at",
                table: "portfolio_photos");

            migrationBuilder.DropIndex(
                name: "ix_portfolio_photos_reviewed_by_user_id",
                table: "portfolio_photos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_photos_moderation_status",
                table: "portfolio_photos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_photos_rejection_reason",
                table: "portfolio_photos");

            migrationBuilder.DropColumn(
                name: "avatar_moderation_status",
                table: "users");

            migrationBuilder.DropColumn(
                name: "avatar_rejection_reason",
                table: "users");

            migrationBuilder.DropColumn(
                name: "avatar_reviewed_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "avatar_reviewed_by_user_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "avatar_submitted_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "moderation_status",
                table: "portfolio_photos");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "portfolio_photos");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "portfolio_photos");

            migrationBuilder.DropColumn(
                name: "reviewed_by_user_id",
                table: "portfolio_photos");
        }
    }
}
