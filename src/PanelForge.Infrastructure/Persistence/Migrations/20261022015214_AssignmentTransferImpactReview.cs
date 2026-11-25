using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentTransferImpactReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "script_revision_ids",
                table: "submissions",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                table: "assignments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "impact_review_reason",
                table: "assignments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "impact_review_requested_at",
                table: "assignments",
                type: "timestamptz",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "script_revision_ids",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "impact_review_reason",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "impact_review_requested_at",
                table: "assignments");
        }
    }
}
