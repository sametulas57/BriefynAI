using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddActionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssigneeUserId",
                table: "ActionItem",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "ActionItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "ActionItem",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReminderSentFor",
                table: "ActionItem",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionItem_AssigneeUserId_IsDone",
                table: "ActionItem",
                columns: new[] { "AssigneeUserId", "IsDone" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionItem_DueDate",
                table: "ActionItem",
                column: "DueDate");

            migrationBuilder.AddForeignKey(
                name: "FK_ActionItem_AspNetUsers_AssigneeUserId",
                table: "ActionItem",
                column: "AssigneeUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActionItem_AspNetUsers_AssigneeUserId",
                table: "ActionItem");

            migrationBuilder.DropIndex(
                name: "IX_ActionItem_AssigneeUserId_IsDone",
                table: "ActionItem");

            migrationBuilder.DropIndex(
                name: "IX_ActionItem_DueDate",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "ReminderSentFor",
                table: "ActionItem");
        }
    }
}
