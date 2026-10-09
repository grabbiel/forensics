using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvidenceChain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InboxIntegrityCheckedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_EvidenceInbox_IntegrityChecked",
                table: "EvidenceInbox",
                columns: new[] { "IntegrityCheckedAtUtc", "EvidenceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EvidenceInbox_IntegrityChecked",
                table: "EvidenceInbox");
        }
    }
}
