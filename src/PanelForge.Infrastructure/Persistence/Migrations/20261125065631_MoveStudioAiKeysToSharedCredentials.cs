using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// TASK-350: key Gemini riêng của Studio → API dùng chung (ai_provider_credentials) và gán cho Studio đó.
    /// Copy nguyên ciphertext (cùng IAiKeyProtector). Id credential suy ra từ id config để UPDATE nối lại được.
    /// Cột api_key_encrypted/provider giữ lại, drop ở migration sau.
    /// </summary>
    public partial class MoveStudioAiKeysToSharedCredentials : Migration
    {
        private const string Moved = """
            c.provider_credential_id IS NULL AND NOT c.is_deleted
            AND coalesce(c.api_key_encrypted, '') <> ''
            """;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO ai_provider_credentials
                    (id, name, provider_type, account_id, api_key_encrypted, text_model, image_model,
                     is_enabled, created_at, created_by, is_deleted)
                SELECT md5(c.id::text || 'task-350')::uuid,
                       left('Gemini – ' || coalesce(w.name, c.workspace_id::text), 100),
                       'Gemini', NULL, c.api_key_encrypted, 'gemini-3.8-flash', NULL,
                       true, now(), 'migration:TASK-350', false
                FROM workspace_ai_configs c
                LEFT JOIN studio_workspaces w ON w.id = c.workspace_id
                WHERE {Moved};

                UPDATE workspace_ai_configs c
                SET provider_credential_id = md5(c.id::text || 'task-350')::uuid
                WHERE {Moved};
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE workspace_ai_configs c SET provider_credential_id = NULL
                WHERE provider_credential_id = md5(c.id::text || 'task-350')::uuid;

                DELETE FROM ai_provider_credentials WHERE created_by = 'migration:TASK-350';
                """);
        }
    }
}
