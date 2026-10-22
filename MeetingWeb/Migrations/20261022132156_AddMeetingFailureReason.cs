using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingFailureReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailureReason",
                table: "Meetings",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Meetings_FailureReason",
                table: "Meetings",
                sql: "[FailureReason] IS NULL OR [FailureReason] IN (1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Meetings_FailureReason",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Meetings");
        }
    }
}
