using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmVoucherGateway.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLastRequestedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRequestedAt",
                table: "Vouchers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRequestedAt",
                table: "Vouchers");
        }
    }
}
