using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddMyReviewsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_reviews_user_id_created_at_id",
                table: "reviews",
                columns: new[] { "user_id", "created_at", "id" },
                descending: new[] { false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_user_id_created_at_id",
                table: "reviews");
        }
    }
}
