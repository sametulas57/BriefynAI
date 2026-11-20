using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CalendarInvitesEnabled",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                // Existing users start with calendar invites on (SPEC.md assumption 3); EF would have generated false.
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "CalendarInvites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActionItemId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Uid = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SendPending = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarInvites", x => x.Id);
                    table.CheckConstraint("CK_CalendarInvites_Status", "[Status] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_CalendarInvites_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarInvites_ActionItemId_UserId",
                table: "CalendarInvites",
                columns: new[] { "ActionItemId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarInvites_Status",
                table: "CalendarInvites",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarInvites_Uid",
                table: "CalendarInvites",
                column: "Uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarInvites_UserId",
                table: "CalendarInvites",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarInvites");

            migrationBuilder.DropColumn(
                name: "CalendarInvitesEnabled",
                table: "AspNetUsers");
        }
    }
}
