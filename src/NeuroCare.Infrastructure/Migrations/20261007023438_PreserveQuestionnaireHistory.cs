using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroCare.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveQuestionnaireHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefinitionSnapshotJson",
                table: "QuestionnaireResponses",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefinitionSnapshotJson",
                table: "QuestionnaireResponses");
        }
    }
}
