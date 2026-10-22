using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddZoomAutoImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoImportWorkspaceId",
                table: "ZoomConnections",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZoomConnections_AutoImportWorkspaceId",
                table: "ZoomConnections",
                column: "AutoImportWorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ZoomConnections_ZoomUserId",
                table: "ZoomConnections",
                column: "ZoomUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ZoomConnections_Workspaces_AutoImportWorkspaceId",
                table: "ZoomConnections",
                column: "AutoImportWorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ZoomConnections_Workspaces_AutoImportWorkspaceId",
                table: "ZoomConnections");

            migrationBuilder.DropIndex(
                name: "IX_ZoomConnections_AutoImportWorkspaceId",
                table: "ZoomConnections");

            migrationBuilder.DropIndex(
                name: "IX_ZoomConnections_ZoomUserId",
                table: "ZoomConnections");

            migrationBuilder.DropColumn(
                name: "AutoImportWorkspaceId",
                table: "ZoomConnections");
        }
    }
}
