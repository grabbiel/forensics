using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvidenceChain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientId = table.Column<int>(type: "int", nullable: false),
                    TransferId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                    table.CheckConstraint("CK_Notifications_Kind", "[Kind] IN ('TransferRequested', 'TransferAccepted', 'TransferRejected')");
                    table.ForeignKey(
                        name: "FK_Notifications_CustodyTransfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "CustodyTransfers",
                        principalColumn: "TransferId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Recipient",
                table: "Notifications",
                columns: new[] { "RecipientId", "NotificationId" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "TransferId", "Kind", "CreatedAtUtc", "ReadAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TransferId",
                table: "Notifications",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Unread",
                table: "Notifications",
                column: "RecipientId",
                filter: "[ReadAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Notifications_RecipientTransferKind",
                table: "Notifications",
                columns: new[] { "RecipientId", "TransferId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
