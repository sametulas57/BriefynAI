using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MeetingPoints_Kind",
                table: "MeetingPoints");

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Meetings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MeetingSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZoomMeetingKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingSeries_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MeetingSeries_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_SeriesId",
                table: "Meetings",
                column: "SeriesId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MeetingPoints_Kind",
                table: "MeetingPoints",
                sql: "[Kind] IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingSeries_CreatedByUserId",
                table: "MeetingSeries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingSeries_WorkspaceId_ZoomMeetingKey",
                table: "MeetingSeries",
                columns: new[] { "WorkspaceId", "ZoomMeetingKey" },
                unique: true,
                filter: "[ZoomMeetingKey] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Meetings_MeetingSeries_SeriesId",
                table: "Meetings",
                column: "SeriesId",
                principalTable: "MeetingSeries",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Meetings_MeetingSeries_SeriesId",
                table: "Meetings");

            migrationBuilder.DropTable(
                name: "MeetingSeries");

            migrationBuilder.DropIndex(
                name: "IX_Meetings_SeriesId",
                table: "Meetings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MeetingPoints_Kind",
                table: "MeetingPoints");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Meetings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MeetingPoints_Kind",
                table: "MeetingPoints",
                sql: "[Kind] IN (1, 2)");
        }
    }
}
