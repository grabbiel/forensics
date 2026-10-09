using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvidenceChain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustodySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tracer rows have no custodians; refuse rather than invent them or silently delete evidence.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.Evidence)
                    THROW 50001, N'Evidence holds tracer rows from before the custody schema. Empty the database (locally: docker compose down -v) and migrate again.', 1;
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "CapturedAtUtc",
                table: "Evidence",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CurrentCustodianId",
                table: "Evidence",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EventCount",
                table: "Evidence",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "HeadMac",
                table: "Evidence",
                type: "binary(32)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InitialCustodianId",
                table: "Evidence",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemoFixture",
                table: "Evidence",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAtUtc",
                table: "Evidence",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "RegisteredById",
                table: "Evidence",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Evidence",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "DailyCounter",
                columns: table => new
                {
                    TypeCode = table.Column<string>(type: "char(3)", unicode: false, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    LastNo = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCounter", x => new { x.TypeCode, x.Day });
                    table.CheckConstraint("CK_DailyCounter_LastNo", "[LastNo] BETWEEN 1 AND 9999");
                });

            migrationBuilder.CreateTable(
                name: "EvidenceContent",
                columns: table => new
                {
                    EvidenceId = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ByteLength = table.Column<int>(type: "int", nullable: false),
                    MediaType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceContent", x => x.EvidenceId);
                    table.CheckConstraint("CK_EvidenceContent_Length", "[ByteLength] = DATALENGTH([Bytes])");
                    table.ForeignKey(
                        name: "FK_EvidenceContent_Evidence_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "Evidence",
                        principalColumn: "EvidenceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceInbox",
                columns: table => new
                {
                    EvidenceId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TypeCode = table.Column<string>(type: "char(3)", unicode: false, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CodeDateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentCustodianId = table.Column<int>(type: "int", nullable: false),
                    CurrentCustodianName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    LastEventAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IntegrityStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    IntegrityCheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IntegrityCheckedThroughSeq = table.Column<int>(type: "int", nullable: true),
                    PendingTransferId = table.Column<long>(type: "bigint", nullable: true),
                    PendingToCustodianId = table.Column<int>(type: "int", nullable: true),
                    PendingSinceUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceInbox", x => x.EvidenceId);
                    table.CheckConstraint("CK_EvidenceInbox_IntegrityStatus", "[IntegrityStatus] IN ('Unverified', 'Valid', 'Invalid')");
                    table.ForeignKey(
                        name: "FK_EvidenceInbox_Evidence_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "Evidence",
                        principalColumn: "EvidenceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeedRuns",
                columns: table => new
                {
                    SeedRunId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Seed = table.Column<long>(type: "bigint", nullable: false),
                    AnchorUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Profile = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Evidences = table.Column<int>(type: "int", nullable: false),
                    Events = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Arguments = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedRuns", x => x.SeedRunId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "varchar(254)", unicode: false, maxLength: 254, nullable: false),
                    Role = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                    table.CheckConstraint("CK_Users_Role", "[Role] IN ('Investigador', 'Custodio', 'Supervisor')");
                });

            migrationBuilder.CreateTable(
                name: "CustodyTransfers",
                columns: table => new
                {
                    TransferId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvidenceId = table.Column<long>(type: "bigint", nullable: false),
                    FromCustodianId = table.Column<int>(type: "int", nullable: false),
                    ToCustodianId = table.Column<int>(type: "int", nullable: false),
                    RequestedById = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedById = table.Column<int>(type: "int", nullable: true),
                    DecisionNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    DecisionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionFingerprint = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustodyTransfers", x => x.TransferId);
                    table.UniqueConstraint("AK_CustodyTransfers_TransferId_EvidenceId", x => new { x.TransferId, x.EvidenceId });
                    table.CheckConstraint("CK_CustodyTransfers_Decision", "([Status] = 'Pending' AND [DecidedAtUtc] IS NULL AND [DecidedById] IS NULL AND [DecisionKey] IS NULL AND [DecisionFingerprint] IS NULL) OR ([Status] <> 'Pending' AND [DecidedAtUtc] IS NOT NULL AND [DecidedAtUtc] >= [RequestedAtUtc] AND [DecidedById] IS NOT NULL AND [DecisionKey] IS NOT NULL AND [DecisionFingerprint] IS NOT NULL)");
                    table.CheckConstraint("CK_CustodyTransfers_DifferentRecipient", "[FromCustodianId] <> [ToCustodianId]");
                    table.CheckConstraint("CK_CustodyTransfers_RecipientDecides", "[DecidedById] IS NULL OR [DecidedById] = [ToCustodianId]");
                    table.CheckConstraint("CK_CustodyTransfers_Status", "[Status] IN ('Pending', 'Accepted', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_CustodyTransfers_Evidence_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "Evidence",
                        principalColumn: "EvidenceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustodyTransfers_Users_DecidedById",
                        column: x => x.DecidedById,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustodyTransfers_Users_FromCustodianId",
                        column: x => x.FromCustodianId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustodyTransfers_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustodyTransfers_Users_ToCustodianId",
                        column: x => x.ToCustodianId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustodyEvents",
                columns: table => new
                {
                    CustodyEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvidenceId = table.Column<long>(type: "bigint", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    TransferId = table.Column<long>(type: "bigint", nullable: true),
                    FromCustodianId = table.Column<int>(type: "int", nullable: true),
                    ToCustodianId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentSha256 = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    ContentLength = table.Column<int>(type: "int", nullable: true),
                    MediaType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    KeyId = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    PrevMac = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    Mac = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CanonicalVersion = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustodyEvents", x => x.CustodyEventId);
                    table.CheckConstraint("CK_CustodyEvents_Kind", "[Kind] IN ('EvidenceRegistered', 'TransferRequested', 'TransferAccepted', 'TransferRejected')");
                    table.CheckConstraint("CK_CustodyEvents_Shape", "([Seq] = 1 AND [Kind] = 'EvidenceRegistered' AND [PrevMac] IS NULL AND [TransferId] IS NULL AND [FromCustodianId] IS NULL AND [ToCustodianId] IS NOT NULL AND [ContentSha256] IS NOT NULL AND [ContentLength] >= 0 AND [MediaType] IS NOT NULL) OR ([Seq] > 1 AND [Kind] <> 'EvidenceRegistered' AND [PrevMac] IS NOT NULL AND [TransferId] IS NOT NULL AND [FromCustodianId] IS NOT NULL AND [ToCustodianId] IS NOT NULL AND [ContentSha256] IS NULL AND [ContentLength] IS NULL AND [MediaType] IS NULL)");
                    table.ForeignKey(
                        name: "FK_CustodyEvents_CustodyTransfers_TransferEvidence",
                        columns: x => new { x.TransferId, x.EvidenceId },
                        principalTable: "CustodyTransfers",
                        principalColumns: new[] { "TransferId", "EvidenceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustodyEvents_Evidence_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "Evidence",
                        principalColumn: "EvidenceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustodyEvents_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustodyEvents_Users_FromCustodianId",
                        column: x => x.FromCustodianId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustodyEvents_Users_ToCustodianId",
                        column: x => x.ToCustodianId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_CurrentCustodianId",
                table: "Evidence",
                column: "CurrentCustodianId");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_InitialCustodianId",
                table: "Evidence",
                column: "InitialCustodianId");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_RegisteredById",
                table: "Evidence",
                column: "RegisteredById");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Evidence_CapturedBeforeRegistered",
                table: "Evidence",
                sql: "[CapturedAtUtc] <= [RegisteredAtUtc]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Evidence_CodeDateIsRegistrationDate",
                table: "Evidence",
                sql: "[CodeDateUtc] = CONVERT(date, [RegisteredAtUtc])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Evidence_Head",
                table: "Evidence",
                sql: "([EventCount] = 0 AND [HeadMac] IS NULL) OR ([EventCount] > 0 AND [HeadMac] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CustodyEvents_ActorId",
                table: "CustodyEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_CustodyEvents_FromCustodianId",
                table: "CustodyEvents",
                column: "FromCustodianId");

            migrationBuilder.CreateIndex(
                name: "IX_CustodyEvents_ToCustodianId",
                table: "CustodyEvents",
                column: "ToCustodianId");

            migrationBuilder.CreateIndex(
                name: "IX_CustodyEvents_TransferId_EvidenceId",
                table: "CustodyEvents",
                columns: new[] { "TransferId", "EvidenceId" });

            migrationBuilder.CreateIndex(
                name: "UX_CustodyEvents_EvidenceSeq",
                table: "CustodyEvents",
                columns: new[] { "EvidenceId", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustodyTransfers_Evidence",
                table: "CustodyTransfers",
                columns: new[] { "EvidenceId", "TransferId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustodyTransfers_FromCustodianId",
                table: "CustodyTransfers",
                column: "FromCustodianId");

            migrationBuilder.CreateIndex(
                name: "IX_CustodyTransfers_RecipientStatus",
                table: "CustodyTransfers",
                columns: new[] { "ToCustodianId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_CustodyTransfers_DecisionKey",
                table: "CustodyTransfers",
                columns: new[] { "DecidedById", "DecisionKey" },
                unique: true,
                filter: "[DecisionKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CustodyTransfers_OnePendingPerEvidence",
                table: "CustodyTransfers",
                column: "EvidenceId",
                unique: true,
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "UX_CustodyTransfers_RequestKey",
                table: "CustodyTransfers",
                columns: new[] { "RequestedById", "ClientRequestId" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "UX_EvidenceInbox_Code",
                table: "EvidenceInbox",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Evidence_Users_CurrentCustodianId",
                table: "Evidence",
                column: "CurrentCustodianId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Evidence_Users_InitialCustodianId",
                table: "Evidence",
                column: "InitialCustodianId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Evidence_Users_RegisteredById",
                table: "Evidence",
                column: "RegisteredById",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            // CREATE TRIGGER must be alone in its batch; each Sql() call is one batch.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_CustodyEvents_AppendOnly ON dbo.CustodyEvents
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, N'CustodyEvents is append-only: rows cannot be updated or deleted.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_Evidence_InitialCustodianImmutable ON dbo.Evidence
                AFTER UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF UPDATE(InitialCustodianId) AND EXISTS (
                        SELECT 1 FROM inserted AS i JOIN deleted AS d ON d.EvidenceId = i.EvidenceId
                        WHERE i.InitialCustodianId <> d.InitialCustodianId)
                        THROW 51001, N'Evidence.InitialCustodianId cannot change after registration.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_Evidence_InitialCustodianImmutable;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_CustodyEvents_AppendOnly;");

            migrationBuilder.DropForeignKey(
                name: "FK_Evidence_Users_CurrentCustodianId",
                table: "Evidence");

            migrationBuilder.DropForeignKey(
                name: "FK_Evidence_Users_InitialCustodianId",
                table: "Evidence");

            migrationBuilder.DropForeignKey(
                name: "FK_Evidence_Users_RegisteredById",
                table: "Evidence");

            migrationBuilder.DropTable(
                name: "CustodyEvents");

            migrationBuilder.DropTable(
                name: "DailyCounter");

            migrationBuilder.DropTable(
                name: "EvidenceContent");

            migrationBuilder.DropTable(
                name: "EvidenceInbox");

            migrationBuilder.DropTable(
                name: "SeedRuns");

            migrationBuilder.DropTable(
                name: "CustodyTransfers");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Evidence_CurrentCustodianId",
                table: "Evidence");

            migrationBuilder.DropIndex(
                name: "IX_Evidence_InitialCustodianId",
                table: "Evidence");

            migrationBuilder.DropIndex(
                name: "IX_Evidence_RegisteredById",
                table: "Evidence");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Evidence_CapturedBeforeRegistered",
                table: "Evidence");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Evidence_CodeDateIsRegistrationDate",
                table: "Evidence");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Evidence_Head",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "CapturedAtUtc",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "CurrentCustodianId",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "EventCount",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "HeadMac",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "InitialCustodianId",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "IsDemoFixture",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "RegisteredAtUtc",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "RegisteredById",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Evidence");
        }
    }
}
