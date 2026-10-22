using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddOverdueReminderSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OverdueRemindersEnabled",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                // Existing users start with reminders on (PRD); EF would have generated false.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverdueRemindersEnabled",
                table: "AspNetUsers");
        }
    }
}
