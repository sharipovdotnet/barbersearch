using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewModerationUpdatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_moderation_created_at_id",
                table: "reviews");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_moderation_updated_at_id",
                table: "reviews",
                columns: new[] { "updated_at", "id" },
                filter: "is_published = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_moderation_updated_at_id",
                table: "reviews");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_moderation_created_at_id",
                table: "reviews",
                columns: new[] { "created_at", "id" },
                filter: "is_published = false");
        }
    }
}
