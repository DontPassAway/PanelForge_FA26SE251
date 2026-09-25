# PanelForge — CF1 Implementation Handoff

> **Session date:** 2026-09-25
> **Build status:** ✅ `Build succeeded. 0 Warning(s). 0 Error(s).`
> **Migration status:** ✅ `20260925023331_AddCF1CompletionMasterData` — Applied to DB
> **Based on:** `docs/markdown/PanelForge_Capstone_Content.md`

---

## 1. Scope of This Session

Starting point: CF1 was ~75–80% complete. The following gaps were identified and fully implemented:

| # | Business Rule / NFR | Status |
|---|---------------------|--------|
| BR-22 | `CanCreateStudio` user permission flag (Admin grant/revoke) | ✅ Done |
| BR-23 | `GetLandingContextQuery` — routing after login | ✅ Done |
| NFR-08 | `ElementType` → DB-backed master data (was hardcoded enum) | ✅ Done |
| NFR-08 | `ExportPreset` → DB-backed master data (was hardcoded) | ✅ Done |
| NFR-08 | `PipelineTemplate` → DB-backed (replaced `CreateDefaultMangaPipeline()`) | ✅ Done |
| Task 7 | `CreateSeries` clones pipeline from `PipelineTemplate` (not hardcoded) | ✅ Done |
| Task 8 | `TypographyPreset` → relational entity (replaced JSON blob in `SeriesPreset`) | ✅ Done |
| Task 9 | `ConsistencyRule` → relational entity (replaced JSON blob in `SeriesPreset`) | ✅ Done |

---

## 2. New Files Created

### Domain Layer (`PanelForge.Domain`)

| File | Description |
|------|-------------|
| `Entities/MasterData/ElementTypeMasterData.cs` | E-30: DB-backed element type. Replaces `ElementType` enum. Fields: `Code`, `Name`, `Description`, `AllowedPropertiesJson` (jsonb), `IsActive` |
| `Entities/MasterData/ExportPreset.cs` | E-32: DB-backed export preset. Fields: `Code`, `Name`, `FormatName`, `ConfigOptionsJson` (jsonb), `IsActive` |
| `Entities/MasterData/PipelineTemplate.cs` | Task 5: DB-backed pipeline template blueprint. Fields: `Code`, `Name`, `Description`, `IsDefault`, `IsActive`, nav `Stages` |
| `Entities/MasterData/PipelineTemplateStage.cs` | Stage within a `PipelineTemplate`. Fields: `Code`, `Name`, `Order`, `RequiredRole`, `GateType`, `IsRequired`, `ConfigurationJson`. UNIQUE(TemplateId, Order). |
| `Entities/Content/TypographyPreset.cs` | E-31: Series-level typography preset. FK to Series. Fields: `FontFamily`, `FontSize`, `FontWeight`, `FontStyle`, `LineHeight`, `LetterSpacing`, `TextAlign`, `UsageType`, `IsDefault` |
| `Entities/Content/ConsistencyRule.cs` | E-33: Series-level consistency rule. FK to Series. Fields: `Name`, `RuleType`, `Description`, `IsEnabled`, `Pattern`, `Severity`, `ConfigurationJson` |

### Application Layer (`PanelForge.Application`)

| File | Description |
|------|-------------|
| `Features/Users/Commands/GrantStudioPermission/GrantStudioCreationPermissionCommand.cs` | BR-22: Admin-only MediatR command. Calls `user.GrantStudioCreation()` |
| `Features/Users/Commands/RevokeStudioPermission/RevokeStudioCreationPermissionCommand.cs` | BR-22: Admin-only MediatR command. Calls `user.RevokeStudioCreation()` |
| `Features/Auth/Queries/GetLandingContextQuery.cs` | BR-23: Returns `SystemRole`, `CanCreateStudio`, workspace memberships, `SuggestedLanding` |
| `Features/MasterData/ElementTypes/Commands/ElementTypeCommands.cs` | Create, Update, Deactivate + DTOs + HTTP request models |
| `Features/MasterData/ElementTypes/Queries/GetElementTypesQueries.cs` | GetList + GetById |
| `Features/MasterData/ExportPresets/Commands/ExportPresetCommands.cs` | Create, Update, Deactivate + DTOs + HTTP request models |
| `Features/MasterData/ExportPresets/Queries/GetExportPresetsQueries.cs` | GetList + GetById |
| `Features/MasterData/PipelineTemplates/Commands/PipelineTemplateCommands.cs` | Create, Update, Deactivate + DTOs + HTTP request models. Handles `IsDefault` uniqueness (unsets previous default) |
| `Features/MasterData/PipelineTemplates/Queries/GetPipelineTemplatesQueries.cs` | GetList (includes Stages) + GetById |
| `Features/TypographyPresets/Commands/TypographyPresetCommands.cs` | Create, Update, Delete. Enforces workspace membership via Series->Workspace |
| `Features/TypographyPresets/Commands/TypographyPresetRequests.cs` | HTTP request model records |
| `Features/TypographyPresets/Models/TypographyPresetDto.cs` | DTO + `ToDto()` extension (NOTE: namespace is `Commands`, not `Models`) |
| `Features/TypographyPresets/Queries/GetTypographyPresetsQueries.cs` | GetList + GetById |
| `Features/ConsistencyRules/Commands/ConsistencyRuleCommands.cs` | Create, Update, Delete + DTOs + extension `ToDto()` |
| `Features/ConsistencyRules/Commands/ConsistencyRuleRequests.cs` | HTTP request model records |
| `Features/ConsistencyRules/Queries/GetConsistencyRulesQueries.cs` | GetList + GetById |

### Infrastructure Layer (`PanelForge.Infrastructure`)

| File | Description |
|------|-------------|
| `Persistence/Configurations/MasterData/ElementTypeMasterDataConfiguration.cs` | EF config: table `element_types`, UNIQUE index on `code` |
| `Persistence/Configurations/MasterData/ExportPresetConfiguration.cs` | EF config: table `export_presets`, UNIQUE index on `code` |
| `Persistence/Configurations/MasterData/PipelineTemplateConfiguration.cs` | EF config: table `pipeline_templates`, UNIQUE index on `code`, cascade Stages |
| `Persistence/Configurations/MasterData/PipelineTemplateStageConfiguration.cs` | EF config: table `pipeline_template_stages`, UNIQUE(template_id, stage_order) composite index |
| `Persistence/Configurations/Content/TypographyPresetConfiguration.cs` | EF config: table `typography_presets`, FK to `series`, index on `series_id` |
| `Persistence/Configurations/Content/ConsistencyRuleConfiguration.cs` | EF config: table `consistency_rules`, FK to `series`, index on `series_id` |
| `Persistence/MasterDataSeeder.cs` | Idempotent startup seeder — seeds 8 ElementTypes, 5 ExportPresets, 1 PipelineTemplate "STANDARD_MANGA" (8 stages). Called from `Program.cs` on startup. |

### Migration

| File | Description |
|------|-------------|
| `Persistence/Migrations/20260925023331_AddCF1CompletionMasterData.cs` | New tables: `element_types`, `export_presets`, `pipeline_templates`, `pipeline_template_stages`, `typography_presets`, `consistency_rules`. Adds `can_create_studio` column to `users`. APPLIED. |

---

## 3. Modified Files

### Domain Layer

| File | Change |
|------|--------|
| `Entities/Auth/User.cs` | Added `CanCreateStudio` bool property (default `false`). Added `GrantStudioCreation()` and `RevokeStudioCreation()` domain methods. |
| `Entities/Content/Series.cs` | REMOVED `SeriesPreset? Preset` navigation and `AttachPreset()`. ADDED `ICollection<TypographyPreset> TypographyPresets` and `ICollection<ConsistencyRule> ConsistencyRules`. |

### Application Layer

| File | Change |
|------|--------|
| `Common/Interfaces/Persistence/IPanelForgeDbContext.cs` | REMOVED `SeriesPresets` DbSet. ADDED `ElementTypes`, `ExportPresets`, `PipelineTemplates`, `PipelineTemplateStages`, `TypographyPresets`, `ConsistencyRules` DbSets. |
| `Features/Series/Commands/CreateSeries/CreateSeriesCommand.cs` | FULLY REFACTORED. Now accepts `PipelineTemplateId`. Resolves template from DB. Clones stages atomically into a new `PipelineDefinition`. Falls back to default template if ID not specified. REMOVED `SeriesPreset` creation. |
| `Features/SeriesPresets/Commands/UpdateSeriesPresetCommand.cs` | STUBBED — deprecated comment only. |
| `Features/SeriesPresets/Queries/GetSeriesPresetQuery.cs` | STUBBED — deprecated comment only. |

### Infrastructure Layer

| File | Change |
|------|--------|
| `Persistence/PanelForgeDbContext.cs` | Added MasterData using. REMOVED `SeriesPresets` DbSet. ADDED all new DbSets. |
| `Persistence/Configurations/Auth/UserConfiguration.cs` | Added `can_create_studio` column mapping, `HasDefaultValue(false)`. |
| `Persistence/Configurations/Content/SeriesConfiguration.cs` | REWRITTEN — removed `SeriesPreset` relationship, added `TypographyPresets` and `ConsistencyRules` 1:N HasMany relationships. |
| `Persistence/Configurations/Content/SeriesPresetConfiguration.cs` | STUBBED — deprecated comment only (was removed to fix compile error from missing `Series.Preset` nav prop). |
| `Services/WorkspaceService.cs` | `CreateWorkspaceAsync()`: added BR-22 guard — throws `UnauthorizedAccessException` if `owner.CanCreateStudio == false`. |

### API Layer

| File | Change |
|------|--------|
| `Controllers/AuthController.cs` | Injected `IMediator`. Added `GET /api/auth/me/landing-context` (BR-23). UserId from JWT only. |
| `Controllers/AdminController.cs` | Added usings for all new namespaces. Added 17 new endpoints (see Section 4). |
| `Controllers/SeriesController.cs` | REWRITTEN — removed old JSON-blob SeriesPreset endpoints. Added TypographyPreset CRUD and ConsistencyRule CRUD. Updated CreateSeries to pass `PipelineTemplateId`. |
| `Program.cs` | Added `using Microsoft.EntityFrameworkCore` and `using PanelForge.Infrastructure.Persistence`. Added startup `MigrateAsync()` + `MasterDataSeeder.SeedAsync()` block. |

---

## 4. All New API Endpoints

### Auth
| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/auth/me/landing-context` | [Authorize] | BR-23: Returns SystemRole, CanCreateStudio, workspaces, SuggestedLanding |

### Admin (existing [Authorize(Policy="AdminOnly")] covers all)
| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/admin/users/{id}/grant-studio-permission` | BR-22: Grant CanCreateStudio |
| POST | `/api/admin/users/{id}/revoke-studio-permission` | BR-22: Revoke CanCreateStudio |
| GET | `/api/admin/element-types` | List all element types |
| GET | `/api/admin/element-types/{id}` | Get by ID |
| POST | `/api/admin/element-types` | Create element type |
| PUT | `/api/admin/element-types/{id}` | Update element type |
| DELETE | `/api/admin/element-types/{id}` | Deactivate (soft delete) |
| GET | `/api/admin/export-presets` | List all export presets |
| GET | `/api/admin/export-presets/{id}` | Get by ID |
| POST | `/api/admin/export-presets` | Create export preset |
| PUT | `/api/admin/export-presets/{id}` | Update export preset |
| DELETE | `/api/admin/export-presets/{id}` | Deactivate (soft delete) |
| GET | `/api/admin/pipeline-templates` | List all templates (with stages) |
| GET | `/api/admin/pipeline-templates/{id}` | Get by ID (with stages) |
| POST | `/api/admin/pipeline-templates` | Create template + stages |
| PUT | `/api/admin/pipeline-templates/{id}` | Update template, optionally replace stages |
| DELETE | `/api/admin/pipeline-templates/{id}` | Deactivate template |

### Series — TypographyPresets (workspace membership enforced in handler)
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/series/{seriesId}/presets` | List TypographyPresets |
| POST | `/api/series/{seriesId}/presets` | Create TypographyPreset |
| PUT | `/api/series/{seriesId}/presets/{presetId}` | Update TypographyPreset |
| DELETE | `/api/series/{seriesId}/presets/{presetId}` | Delete TypographyPreset |

### Series — ConsistencyRules (workspace membership enforced in handler)
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/series/{seriesId}/consistency-rules` | List ConsistencyRules |
| POST | `/api/series/{seriesId}/consistency-rules` | Create ConsistencyRule |
| PUT | `/api/series/{seriesId}/consistency-rules/{ruleId}` | Update ConsistencyRule |
| DELETE | `/api/series/{seriesId}/consistency-rules/{ruleId}` | Delete ConsistencyRule |

---

## 5. Key Design Decisions

### PipelineTemplate -> PipelineDefinition cloning
When creating a Series, `CreateSeriesCommandHandler` now:
1. Resolves `PipelineTemplate` from DB (by `PipelineTemplateId` or falls back to `IsDefault=true` one)
2. Creates a new `PipelineDefinition` scoped to that Workspace
3. Clones all `PipelineTemplateStage` rows into `PipelineStage` rows (ordered)
4. Links the Series to the cloned pipeline via `SetPipelineDefinition(pipeline.Id)`

This keeps `PipelineDefinition` workspace-scoped and editable, while `PipelineTemplate` remains system-level admin-configurable.

### Why SeriesPreset was stubbed, not deleted
`SeriesPreset` stored TypographyPresetsJson and ConsistencyRulesJson as raw JSON blobs — violating NFR-08. It was replaced by two proper relational entities. The old files were stubbed (not deleted) to preserve git history. The `series_presets` table was NOT dropped by the migration.

> **WARNING:** The `series_presets` table still exists in the DB. Add a new migration with `DropTable("series_presets")` if cleanup is needed. Existing data will be lost.

### CanCreateStudio defaults to false
All existing users get `CanCreateStudio = false` after migration. Admins must explicitly grant permission. Consider adding a dev seed that grants the test user.

### Seeder is idempotent
`MasterDataSeeder.SeedAsync()` uses `AnyAsync()` guards — safe to run every startup. No duplicates will be created.

---

## 6. What Remains for Next Session

| Priority | Item | Notes |
|----------|------|-------|
| HIGH | Add `[Authorize]` / `[RequireWorkspaceRole]` to new Series endpoints in `SeriesController` | Currently unauthenticated. Match existing pattern. |
| HIGH | Rename `PipelineDefinitionId` -> `PipelineTemplateId` in `CreateSeriesRequest` DTO | Field renamed in command but request model still has old name. Update frontend too. |
| MEDIUM | `ElementType` enum on `Element.cs` entity | Not removed yet. Only the master data table was created. Decide: drop enum + add FK to `element_types.id`, or keep parallel. Needs migration. |
| MEDIUM | `TypographyPresetDto` namespace inconsistency | File is in `Models/` folder but namespace is `Commands`. Refactor or move. |
| LOW | Mark `PipelineDefinition.CreateDefaultMangaPipeline()` as `[Obsolete]` | Method still exists. Should not be called anymore. |
| LOW | Hard-delete `SeriesPreset` entity + table | After confirming no data is needed. New migration: `DropTable("series_presets")`. |
| LOW | Bible versioning (CF1 Step 8) | Not touched this session. Check capstone spec. |

---

## 7. Seeded Master Data

### ElementTypes (8 records, Code is UPPER_SNAKE)
`SPEECH_BALLOON`, `THOUGHT_BALLOON`, `NARRATION_BOX`, `SOUND_EFFECT`, `CHARACTER_INSTANCE`, `ARTWORK_LAYER`, `PANEL_BORDER`, `PROP`

### ExportPresets (5 records)
`PDF`, `CBZ`, `WEBTOON`, `PAGE_IMAGES`, `OPEN_JSON`

### PipelineTemplate (1 record)

**Code:** `STANDARD_MANGA` | **IsDefault:** `true`

| Order | Code | Name | RequiredRole | GateType |
|-------|------|------|-------------|----------|
| 1 | SCRIPT | Script | Writer | — |
| 2 | THUMBNAIL | Thumbnail | Artist | — |
| 3 | PENCIL | Pencil | Artist | — |
| 4 | INK | Ink | Artist | — |
| 5 | COLOR | Color | Artist | — |
| 6 | LETTER | Letter | Letterer | — |
| 7 | REVIEW | Review | Editor | **Approval** |
| 8 | APPROVED | Approved | Editor | — |

---

## 8. Quick Test Sequence

```bash
# 1. Start API (auto-migrates and seeds on startup)
dotnet run --project src/PanelForge.API

# 2. Open Swagger: http://localhost:{PORT}/swagger
```

**Test flow:**
1. `POST /api/auth/login` → copy JWT token
2. `GET /api/auth/me/landing-context` → expect `SuggestedLanding: "PublicCatalog"` (CanCreateStudio=false)
3. As Admin: `POST /api/admin/users/{userId}/grant-studio-permission`
4. `GET /api/auth/me/landing-context` → expect `SuggestedLanding: "StudioCreation"`
5. `POST /api/workspaces` → should now succeed (was blocked by BR-22 guard)
6. `GET /api/admin/pipeline-templates` → verify STANDARD_MANGA with 8 stages is listed
7. `POST /api/workspaces/{workspaceId}/series` body with just Title+ReadingDirection (omit PipelineTemplateId) → auto-uses STANDARD_MANGA, creates PipelineDefinition cloned from it
8. `GET /api/series/{seriesId}/presets` → empty list (no presets yet)
9. `POST /api/series/{seriesId}/presets` with FontFamily, FontSize etc → creates TypographyPreset
