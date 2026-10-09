using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvidenceChain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropUnusedIndexesAndInboxColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Evidence_Recent",
                table: "Evidence");

            migrationBuilder.DropIndex(
                name: "IX_CustodyTransfers_RecipientStatus",
                table: "CustodyTransfers");

            migrationBuilder.DropColumn(
                name: "CodeDateUtc",
                table: "EvidenceInbox");

            migrationBuilder.DropColumn(
                name: "RegisteredAtUtc",
                table: "EvidenceInbox");

            migrationBuilder.CreateIndex(
                name: "IX_CustodyTransfers_ToCustodianId",
                table: "CustodyTransfers",
                column: "ToCustodianId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustodyTransfers_ToCustodianId",
                table: "CustodyTransfers");

            migrationBuilder.AddColumn<DateOnly>(
                name: "CodeDateUtc",
                table: "EvidenceInbox",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAtUtc",
                table: "EvidenceInbox",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_Recent",
                table: "Evidence",
                columns: new[] { "CodeDateUtc", "EvidenceId" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_CustodyTransfers_RecipientStatus",
                table: "CustodyTransfers",
                columns: new[] { "ToCustodianId", "Status" });
        }
    }
}
