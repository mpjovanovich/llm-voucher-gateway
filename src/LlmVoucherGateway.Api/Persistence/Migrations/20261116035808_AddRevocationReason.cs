using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmVoucherGateway.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevocationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "Vouchers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevocationReason",
                table: "Vouchers");
        }
    }
}
