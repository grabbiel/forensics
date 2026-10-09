using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EvidenceChain.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTracer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Evidence",
                columns: table => new
                {
                    EvidenceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TypeCode = table.Column<string>(type: "char(3)", unicode: false, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CodeDateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    DailyNo = table.Column<short>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false, computedColumnSql: "CONCAT([TypeCode], CONVERT(char(8), [CodeDateUtc], 112), RIGHT(CONCAT('000', [DailyNo]), 4)) COLLATE Latin1_General_100_BIN2", stored: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evidence", x => x.EvidenceId);
                    table.CheckConstraint("CK_Evidence_DailyNo", "[DailyNo] BETWEEN 1 AND 9999");
                    table.CheckConstraint("CK_Evidence_TypeCode", "[TypeCode] IN ('LOG', 'CSV', 'EML')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_Recent",
                table: "Evidence",
                columns: new[] { "CodeDateUtc", "EvidenceId" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "UX_Evidence_Code",
                table: "Evidence",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Evidence_TypeDateNo",
                table: "Evidence",
                columns: new[] { "TypeCode", "CodeDateUtc", "DailyNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Evidence");
        }
    }
}
