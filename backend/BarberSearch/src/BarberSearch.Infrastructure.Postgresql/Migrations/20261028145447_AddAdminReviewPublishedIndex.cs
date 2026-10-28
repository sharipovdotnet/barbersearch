using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminReviewPublishedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_reviews_published_created_at_id",
                table: "reviews",
                columns: new[] { "is_published", "created_at", "id" },
                descending: new[] { false, true, false },
                filter: "is_published = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_published_created_at_id",
                table: "reviews");
        }
    }
}
