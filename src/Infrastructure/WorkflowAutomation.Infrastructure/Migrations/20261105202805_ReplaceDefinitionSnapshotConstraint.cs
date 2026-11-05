using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowAutomation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceDefinitionSnapshotConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkflowExecutions_DefinitionSnapshot_IsObject",
                table: "WorkflowExecutions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkflowExecutions_DefinitionSnapshot_NotEmpty",
                table: "WorkflowExecutions",
                sql: "\"DefinitionSnapshot\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkflowExecutions_DefinitionSnapshot_NotEmpty",
                table: "WorkflowExecutions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkflowExecutions_DefinitionSnapshot_IsObject",
                table: "WorkflowExecutions",
                sql: "jsonb_typeof(\"DefinitionSnapshot\"::jsonb) = 'object'");
        }
    }
}
