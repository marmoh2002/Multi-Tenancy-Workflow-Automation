using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowAutomation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowPersistenceConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Workflows_WorkflowVersions_Id_ActiveVersionId",
                table: "Workflows");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "WorkflowVersions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Workflows",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkflowVersions_DefinitionSchemaVersion_Positive",
                table: "WorkflowVersions",
                sql: "\"DefinitionSchemaVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkflowVersions_PublicationTimestamp_Consistency",
                table: "WorkflowVersions",
                sql: "(\"IsPublished\" = false AND \"PublishedAt\" IS NULL) OR (\"IsPublished\" = true AND \"PublishedAt\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkflowVersions_Published_NoValidationErrors",
                table: "WorkflowVersions",
                sql: "NOT \"IsPublished\" OR \"ValidationErrorsJson\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkflowVersions_SerializedDefinition_IsObject",
                table: "WorkflowVersions",
                sql: "jsonb_typeof(\"SerializedDefinition\") = 'object'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkflowVersions_ValidationErrorsJson_IsArrayOrNull",
                table: "WorkflowVersions",
                sql: "\"ValidationErrorsJson\" IS NULL OR jsonb_typeof(\"ValidationErrorsJson\") = 'array'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkflowVersions_VersionNumber_Positive",
                table: "WorkflowVersions",
                sql: "\"VersionNumber\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_WorkspaceId_IsActive",
                table: "Workflows",
                columns: new[] { "WorkspaceId", "IsActive" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Workflows_CreatedByUserId_NotEmpty",
                table: "Workflows",
                sql: "\"CreatedByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Workflows_IsActive_RequiresActiveVersionId",
                table: "Workflows",
                sql: "NOT \"IsActive\" OR \"ActiveVersionId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Workflows_Name_TrimmedAndNotEmpty",
                table: "Workflows",
                sql: "\"Name\" = btrim(\"Name\", E' \\t\\n\\r\\f\\v') AND \"Name\" <> ''");

            migrationBuilder.AddForeignKey(
                name: "FK_Workflows_WorkflowVersions_Id_ActiveVersionId",
                table: "Workflows",
                columns: new[] { "Id", "ActiveVersionId" },
                principalTable: "WorkflowVersions",
                principalColumns: new[] { "WorkflowId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Workflows_WorkflowVersions_Id_ActiveVersionId",
                table: "Workflows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkflowVersions_DefinitionSchemaVersion_Positive",
                table: "WorkflowVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkflowVersions_PublicationTimestamp_Consistency",
                table: "WorkflowVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkflowVersions_Published_NoValidationErrors",
                table: "WorkflowVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkflowVersions_SerializedDefinition_IsObject",
                table: "WorkflowVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkflowVersions_ValidationErrorsJson_IsArrayOrNull",
                table: "WorkflowVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkflowVersions_VersionNumber_Positive",
                table: "WorkflowVersions");

            migrationBuilder.DropIndex(
                name: "IX_Workflows_WorkspaceId_IsActive",
                table: "Workflows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Workflows_CreatedByUserId_NotEmpty",
                table: "Workflows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Workflows_IsActive_RequiresActiveVersionId",
                table: "Workflows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Workflows_Name_TrimmedAndNotEmpty",
                table: "Workflows");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "WorkflowVersions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Workflows");

            migrationBuilder.AddForeignKey(
                name: "FK_Workflows_WorkflowVersions_Id_ActiveVersionId",
                table: "Workflows",
                columns: new[] { "Id", "ActiveVersionId" },
                principalTable: "WorkflowVersions",
                principalColumns: new[] { "WorkflowId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
