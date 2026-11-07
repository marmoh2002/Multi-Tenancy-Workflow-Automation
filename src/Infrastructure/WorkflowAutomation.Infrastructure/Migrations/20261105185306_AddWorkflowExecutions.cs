using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowAutomation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    DefinitionSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    DefinitionSnapshot = table.Column<string>(type: "text", nullable: false),
                    DefinitionHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduledOccurrenceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedStepId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    CancellationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsCancellable = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowExecutions", x => x.Id);
                    table.UniqueConstraint("AK_WorkflowExecutions_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_WorkflowExecutions_CancellationRequest_RequiresReason", "\"CancellationRequestedAt\" IS NULL OR (\"CancellationReason\" IS NOT NULL AND btrim(\"CancellationReason\", E' \\t\\n\\r\\f\\v') <> '')");
                    table.CheckConstraint("CK_WorkflowExecutions_Cancelled_RequiresTimestamps", "\"Status\" <> 5 OR (\"CancelledAt\" IS NOT NULL AND \"CancellationRequestedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_WorkflowExecutions_Completed_RequiresCompletedAt", "\"Status\" <> 3 OR \"CompletedAt\" IS NOT NULL");
                    table.CheckConstraint("CK_WorkflowExecutions_DefinitionHash_Sha256Hex", "\"DefinitionHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_WorkflowExecutions_DefinitionSchemaVersion_Positive", "\"DefinitionSchemaVersion\" > 0");
                    table.CheckConstraint("CK_WorkflowExecutions_DefinitionSnapshot_IsObject", "jsonb_typeof(\"DefinitionSnapshot\"::jsonb) = 'object'");
                    table.CheckConstraint("CK_WorkflowExecutions_Failed_RequiresCompletedAtAndReason", "\"Status\" <> 4 OR (\"CompletedAt\" IS NOT NULL AND \"FailureReason\" IS NOT NULL AND btrim(\"FailureReason\", E' \\t\\n\\r\\f\\v') <> '')");
                    table.CheckConstraint("CK_WorkflowExecutions_FailedStepId_OnlyOnFailed", "\"Status\" = 4 OR \"FailedStepId\" IS NULL");
                    table.CheckConstraint("CK_WorkflowExecutions_Running_RequiresStartedAt", "\"Status\" <> 2 OR \"StartedAt\" IS NOT NULL");
                    table.CheckConstraint("CK_WorkflowExecutions_Source_Enum_Valid", "\"Source\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_WorkflowExecutions_Status_Enum_Valid", "\"Status\" IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_WorkflowExecutions_VersionNumber_Positive", "\"VersionNumber\" > 0");
                    table.CheckConstraint("CK_WorkflowExecutions_WorkflowName_TrimmedAndNotEmpty", "\"WorkflowName\" = btrim(\"WorkflowName\", E' \\t\\n\\r\\f\\v') AND \"WorkflowName\" <> ''");
                    table.ForeignKey(
                        name: "FK_WorkflowExecutions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_ScheduleId_ScheduledOccurrenceId",
                table: "WorkflowExecutions",
                columns: new[] { "ScheduleId", "ScheduledOccurrenceId" },
                unique: true,
                filter: "\"ScheduleId\" IS NOT NULL AND \"ScheduledOccurrenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_WorkflowVersionId",
                table: "WorkflowExecutions",
                column: "WorkflowVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_WorkspaceId_Status_CreatedAt",
                table: "WorkflowExecutions",
                columns: new[] { "WorkspaceId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_WorkspaceId_WorkflowId_CreatedAt",
                table: "WorkflowExecutions",
                columns: new[] { "WorkspaceId", "WorkflowId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_WorkspaceId_WorkflowId_Status",
                table: "WorkflowExecutions",
                columns: new[] { "WorkspaceId", "WorkflowId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowExecutions");
        }
    }
}
