using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmVoucherGateway.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Vouchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key_Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Key_Prefix = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Section = table.Column<string>(type: "TEXT", nullable: true),
                    Allowance = table.Column<int>(type: "INTEGER", nullable: false),
                    Consumed = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Lifetime_CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Lifetime_ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_Key_Hash",
                table: "Vouchers",
                column: "Key_Hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Vouchers");
        }
    }
}
