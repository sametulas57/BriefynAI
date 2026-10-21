using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TitleId",
                table: "WorkspaceMembers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkspaceTitles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceTitles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceTitles_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_TitleId",
                table: "WorkspaceMembers",
                column: "TitleId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceTitles_WorkspaceId_Name",
                table: "WorkspaceTitles",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkspaceMembers_WorkspaceTitles_TitleId",
                table: "WorkspaceMembers",
                column: "TitleId",
                principalTable: "WorkspaceTitles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkspaceMembers_WorkspaceTitles_TitleId",
                table: "WorkspaceMembers");

            migrationBuilder.DropTable(
                name: "WorkspaceTitles");

            migrationBuilder.DropIndex(
                name: "IX_WorkspaceMembers_TitleId",
                table: "WorkspaceMembers");

            migrationBuilder.DropColumn(
                name: "TitleId",
                table: "WorkspaceMembers");
        }
    }
}
