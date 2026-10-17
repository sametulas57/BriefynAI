using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceIsPersonal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPersonal",
                table: "Workspaces",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Existing personal spaces were only recognisable by the name Register gives them.
            migrationBuilder.Sql("UPDATE Workspaces SET IsPersonal = 1 WHERE Name = N'Kişisel Alanım'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPersonal",
                table: "Workspaces");
        }
    }
}
