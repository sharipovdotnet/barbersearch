using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddSalonPlaceKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_listed",
                table: "salons",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<short>(
                name: "kind",
                table: "salons",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_salons_kind",
                table: "salons",
                sql: "kind IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_salons_listed_kind",
                table: "salons",
                sql: "NOT is_listed OR kind IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_salons_kind",
                table: "salons");

            migrationBuilder.DropCheckConstraint(
                name: "ck_salons_listed_kind",
                table: "salons");

            migrationBuilder.DropColumn(
                name: "is_listed",
                table: "salons");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "salons");
        }
    }
}
