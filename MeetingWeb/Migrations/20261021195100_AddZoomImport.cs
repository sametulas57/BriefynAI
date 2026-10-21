using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddZoomImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Meetings_Source",
                table: "Meetings");

            migrationBuilder.AddColumn<string>(
                name: "ExternalRecordingId",
                table: "Meetings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ZoomConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ZoomUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ZoomEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AccessToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccessTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoomConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZoomConnections_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_WorkspaceId_ExternalRecordingId",
                table: "Meetings",
                columns: new[] { "WorkspaceId", "ExternalRecordingId" },
                unique: true,
                filter: "[ExternalRecordingId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Meetings_Source",
                table: "Meetings",
                sql: "[Source] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_ZoomConnections_UserId",
                table: "ZoomConnections",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZoomConnections");

            migrationBuilder.DropIndex(
                name: "IX_Meetings_WorkspaceId_ExternalRecordingId",
                table: "Meetings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Meetings_Source",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "ExternalRecordingId",
                table: "Meetings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Meetings_Source",
                table: "Meetings",
                sql: "[Source] IN (1)");
        }
    }
}
