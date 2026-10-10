using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvidenceChain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoverInboxKeysetIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EvidenceInbox_Custodian",
                table: "EvidenceInbox");

            migrationBuilder.DropIndex(
                name: "IX_EvidenceInbox_Integrity",
                table: "EvidenceInbox");

            migrationBuilder.DropIndex(
                name: "IX_EvidenceInbox_Recent",
                table: "EvidenceInbox");

            migrationBuilder.DropIndex(
                name: "IX_EvidenceInbox_Type",
                table: "EvidenceInbox");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceInbox_Custodian",
                table: "EvidenceInbox",
                columns: new[] { "CurrentCustodianId", "LastEventAtUtc", "EvidenceId" },
                descending: new[] { false, true, true })
                .Annotation("SqlServer:Include", new[] { "Code", "TypeCode", "Description", "CurrentCustodianName", "EventCount", "IntegrityStatus", "IntegrityCheckedAtUtc", "PendingTransferId", "PendingToCustodianId", "PendingSinceUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceInbox_Integrity",
                table: "EvidenceInbox",
                columns: new[] { "IntegrityStatus", "LastEventAtUtc", "EvidenceId" },
                descending: new[] { false, true, true })
                .Annotation("SqlServer:Include", new[] { "Code", "TypeCode", "Description", "CurrentCustodianId", "CurrentCustodianName", "EventCount", "IntegrityCheckedAtUtc", "PendingTransferId", "PendingToCustodianId", "PendingSinceUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceInbox_Recent",
                table: "EvidenceInbox",
                columns: new[] { "LastEventAtUtc", "EvidenceId" },
                descending: new bool[0])
                .Annotation("SqlServer:Include", new[] { "Code", "TypeCode", "Description", "CurrentCustodianId", "CurrentCustodianName", "EventCount", "IntegrityStatus", "IntegrityCheckedAtUtc", "PendingTransferId", "PendingToCustodianId", "PendingSinceUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceInbox_Type",
                table: "EvidenceInbox",
                columns: new[] { "TypeCode", "LastEventAtUtc", "EvidenceId" },
                descending: new[] { false, true, true })
                .Annotation("SqlServer:Include", new[] { "Code", "Description", "CurrentCustodianId", "CurrentCustodianName", "EventCount", "IntegrityStatus", "IntegrityCheckedAtUtc", "PendingTransferId", "PendingToCustodianId", "PendingSinceUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EvidenceInbox_Custodian",
                table: "EvidenceInbox");

            migrationBuilder.DropIndex(
                name: "IX_EvidenceInbox_Integrity",
                table: "EvidenceInbox");

            migrationBuilder.DropIndex(
                name: "IX_EvidenceInbox_Recent",
                table: "EvidenceInbox");

            migrationBuilder.DropIndex(
                name: "IX_EvidenceInbox_Type",
                table: "EvidenceInbox");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceInbox_Custodian",
                table: "EvidenceInbox",
                columns: new[] { "CurrentCustodianId", "LastEventAtUtc", "EvidenceId" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceInbox_Integrity",
                table: "EvidenceInbox",
                columns: new[] { "IntegrityStatus", "LastEventAtUtc", "EvidenceId" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceInbox_Recent",
                table: "EvidenceInbox",
                columns: new[] { "LastEventAtUtc", "EvidenceId" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceInbox_Type",
                table: "EvidenceInbox",
                columns: new[] { "TypeCode", "LastEventAtUtc", "EvidenceId" },
                descending: new[] { false, true, true });
        }
    }
}
