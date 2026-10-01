using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulseMind.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class OneRunningSessionPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_OneRunningPerUser",
                table: "StudySessions",
                column: "UserId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudySessions_OneRunningPerUser",
                table: "StudySessions");
        }
    }
}
