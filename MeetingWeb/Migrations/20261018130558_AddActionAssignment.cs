using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddActionAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssigneeUserId",
                table: "ActionItem",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "ActionItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletedByUserId",
                table: "ActionItem",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "ActionItem",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionItem_AssigneeUserId",
                table: "ActionItem",
                column: "AssigneeUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActionItem_AssigneeUserId",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "ActionItem");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "ActionItem");
        }
    }
}
