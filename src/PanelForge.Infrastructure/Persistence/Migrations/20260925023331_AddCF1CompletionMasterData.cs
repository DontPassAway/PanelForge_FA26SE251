using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCF1CompletionMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "series_presets");

            migrationBuilder.AddColumn<bool>(
                name: "can_create_studio",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "consistency_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    rule_type = table.Column<string>(type: "varchar(100)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    pattern = table.Column<string>(type: "text", nullable: true),
                    severity = table.Column<string>(type: "varchar(30)", nullable: false, defaultValue: "Warning"),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consistency_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_consistency_rules_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "element_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    allowed_properties_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_element_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "export_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    format_name = table.Column<string>(type: "varchar(100)", nullable: false),
                    config_options_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_presets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "typography_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    font_family = table.Column<string>(type: "varchar(200)", nullable: false),
                    font_size = table.Column<int>(type: "integer", nullable: false),
                    font_weight = table.Column<int>(type: "integer", nullable: false, defaultValue: 400),
                    font_style = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "Normal"),
                    line_height = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 1.2m),
                    letter_spacing = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    text_align = table.Column<string>(type: "varchar(30)", nullable: false, defaultValue: "Left"),
                    usage_type = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "Custom"),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_typography_presets", x => x.id);
                    table.ForeignKey(
                        name: "FK_typography_presets_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_template_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    stage_order = table.Column<int>(type: "integer", nullable: false),
                    required_role = table.Column<string>(type: "varchar(30)", nullable: true),
                    gate_type = table.Column<string>(type: "varchar(50)", nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_template_stages", x => x.id);
                    table.ForeignKey(
                        name: "FK_pipeline_template_stages_pipeline_templates_pipeline_templa~",
                        column: x => x.pipeline_template_id,
                        principalTable: "pipeline_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consistency_rules_series_id",
                table: "consistency_rules",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_element_types_code",
                table: "element_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_export_presets_code",
                table: "export_presets",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_template_stages_template_order",
                table: "pipeline_template_stages",
                columns: new[] { "pipeline_template_id", "stage_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_templates_code",
                table: "pipeline_templates",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_typography_presets_series_id",
                table: "typography_presets",
                column: "series_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consistency_rules");

            migrationBuilder.DropTable(
                name: "element_types");

            migrationBuilder.DropTable(
                name: "export_presets");

            migrationBuilder.DropTable(
                name: "pipeline_template_stages");

            migrationBuilder.DropTable(
                name: "typography_presets");

            migrationBuilder.DropTable(
                name: "pipeline_templates");

            migrationBuilder.DropColumn(
                name: "can_create_studio",
                table: "users");

            migrationBuilder.CreateTable(
                name: "series_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consistency_rules_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    typography_presets_json = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_presets", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_presets_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_series_presets_series_id",
                table: "series_presets",
                column: "series_id",
                unique: true);
        }
    }
}
