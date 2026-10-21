using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkspaceMembers_Role",
                table: "WorkspaceMembers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkspaceMembers_Role",
                table: "WorkspaceMembers",
                sql: "[Role] IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkspaceMembers_Role",
                table: "WorkspaceMembers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkspaceMembers_Role",
                table: "WorkspaceMembers",
                sql: "[Role] IN (1, 2)");
        }
    }
}
