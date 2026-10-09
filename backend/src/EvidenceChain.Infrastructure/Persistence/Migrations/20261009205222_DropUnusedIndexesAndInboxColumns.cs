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

            // Added nullable, filled from Evidence, then made required: the columns come back with their values and
            // without the default constraints a required AddColumn would leave behind.
            migrationBuilder.AddColumn<DateOnly>(
                name: "CodeDateUtc",
                table: "EvidenceInbox",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAtUtc",
                table: "EvidenceInbox",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE i SET CodeDateUtc = e.CodeDateUtc, RegisteredAtUtc = e.RegisteredAtUtc
                FROM dbo.EvidenceInbox AS i JOIN dbo.Evidence AS e ON e.EvidenceId = i.EvidenceId;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "CodeDateUtc",
                table: "EvidenceInbox",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "RegisteredAtUtc",
                table: "EvidenceInbox",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

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
