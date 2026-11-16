using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmVoucherGateway.Api.Persistence.Migrations
{
    /// <summary>
    /// Records the voucher prefix growing from eight to twelve characters. SQLite stores
    /// the column as TEXT whatever the configured length, so there is nothing to alter;
    /// the migration exists to keep the model snapshot in step with the mapping.
    /// Vouchers issued before it keep their eight-character prefixes.
    /// </summary>
    public partial class LengthenVoucherPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nothing to alter: SQLite ignores the length. See the class summary.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to restore: SQLite ignores the length. See the class summary.
        }
    }
}
