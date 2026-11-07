using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowAutomation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStepExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowStepExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    IsRetryable = table.Column<bool>(type: "boolean", nullable: false),
                    OutputMetadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStepExecutions", x => x.Id);
                    table.CheckConstraint("CK_WorkflowStepExecutions_AttemptNumber_Positive", "\"AttemptNumber\" > 0");
                    table.CheckConstraint("CK_WorkflowStepExecutions_Cancelled_RequiresCancelledAt", "\"Status\" <> 5 OR \"CancelledAt\" IS NOT NULL");
                    table.CheckConstraint("CK_WorkflowStepExecutions_Completed_RequiresTimestamps", "\"Status\" <> 3 OR (\"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_WorkflowStepExecutions_ErrorMessage_OnlyOnFailed", "\"Status\" = 4 OR \"ErrorMessage\" IS NULL");
                    table.CheckConstraint("CK_WorkflowStepExecutions_Failed_RequiresTimestampsAndReason", "\"Status\" <> 4 OR (\"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND \"ErrorMessage\" IS NOT NULL AND btrim(\"ErrorMessage\", E' \\t\\n\\r\\f\\v') <> '')");
                    table.CheckConstraint("CK_WorkflowStepExecutions_FinishTimestamps_Exclusive", "\"CompletedAt\" IS NULL OR \"CancelledAt\" IS NULL");
                    table.CheckConstraint("CK_WorkflowStepExecutions_IsRetryable_OnlyOnFailed", "\"Status\" = 4 OR NOT \"IsRetryable\"");
                    table.CheckConstraint("CK_WorkflowStepExecutions_OutputMetadata_OnlyOnCompleted", "\"Status\" = 3 OR \"OutputMetadata\" IS NULL");
                    table.CheckConstraint("CK_WorkflowStepExecutions_Pending_NoTimestamps", "\"Status\" <> 1 OR (\"StartedAt\" IS NULL AND \"CompletedAt\" IS NULL AND \"CancelledAt\" IS NULL)");
                    table.CheckConstraint("CK_WorkflowStepExecutions_Running_RequiresStartedAt", "\"Status\" <> 2 OR (\"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL AND \"CancelledAt\" IS NULL)");
                    table.CheckConstraint("CK_WorkflowStepExecutions_Skipped_RequiresCompletedAt", "\"Status\" <> 6 OR (\"StartedAt\" IS NULL AND \"CompletedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_WorkflowStepExecutions_Status_Enum_Valid", "\"Status\" IN (1, 2, 3, 4, 5, 6)");
                    table.CheckConstraint("CK_WorkflowStepExecutions_StepId_TrimmedAndNotEmpty", "\"StepId\" = btrim(\"StepId\", E' \\t\\n\\r\\f\\v') AND \"StepId\" <> ''");
                    table.ForeignKey(
                        name: "FK_WorkflowStepExecutions_Executions_WorkspaceId_ExecutionId",
                        columns: x => new { x.WorkspaceId, x.ExecutionId },
                        principalTable: "WorkflowExecutions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowStepExecutions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepExecutions_ExecutionId_Status",
                table: "WorkflowStepExecutions",
                columns: new[] { "ExecutionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepExecutions_ExecutionId_StepId_AttemptNumber",
                table: "WorkflowStepExecutions",
                columns: new[] { "ExecutionId", "StepId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepExecutions_WorkspaceId_ExecutionId",
                table: "WorkflowStepExecutions",
                columns: new[] { "WorkspaceId", "ExecutionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowStepExecutions");
        }
    }
}
