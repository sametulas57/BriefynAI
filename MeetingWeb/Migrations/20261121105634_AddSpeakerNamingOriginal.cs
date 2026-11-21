using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakerNamingOriginal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalText",
                table: "MeetingTranscripts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakerNames",
                table: "MeetingTranscripts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalText",
                table: "MeetingTranscripts");

            migrationBuilder.DropColumn(
                name: "SpeakerNames",
                table: "MeetingTranscripts");
        }
    }
}
