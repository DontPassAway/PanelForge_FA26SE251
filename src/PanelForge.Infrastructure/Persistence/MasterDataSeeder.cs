using Microsoft.EntityFrameworkCore;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;

namespace PanelForge.Infrastructure.Persistence;

/// <summary>
/// Seeds initial master data required by CF1 on first startup (NFR-08, Task 5, Task 3).
/// Idempotent: only inserts if data doesn't already exist.
/// </summary>
public static class MasterDataSeeder
{
    public static async Task SeedAsync(PanelForgeDbContext dbContext, CancellationToken ct = default)
    {
        await SeedElementTypesAsync(dbContext, ct);
        await SeedExportPresetsAsync(dbContext, ct);
        await SeedDefaultPipelineTemplateAsync(dbContext, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    // ─── E-30: Element Types ────────────────────────────────────────────────────

    private static async Task SeedElementTypesAsync(PanelForgeDbContext dbContext, CancellationToken ct)
    {
        if (await dbContext.ElementTypes.AnyAsync(ct)) return;

        var elementTypes = new[]
        {
            ("SPEECH_BALLOON",     "Speech Balloon",     "Standard dialogue balloon",                 """{"shapes":["round","square","cloud"],"hasPointer":true}"""),
            ("THOUGHT_BALLOON",    "Thought Balloon",    "Character thought / internal monologue",    """{"shapes":["cloud","bubble"],"hasPointer":true}"""),
            ("NARRATION_BOX",      "Narration Box",      "Rectangular narration or caption box",      """{"shapes":["rect"],"hasPointer":false}"""),
            ("SOUND_EFFECT",       "Sound Effect",       "Onomatopoeia or SFX lettering",             """{"hasOutline":true,"canRotate":true}"""),
            ("CHARACTER_INSTANCE", "Character Instance", "A placed character asset on the panel",     """{"hasSkinColorOverride":true,"hasOutfit":true}"""),
            ("ARTWORK_LAYER",      "Artwork Layer",      "Background or foreground artwork layer",    """{"blendModes":["normal","multiply","screen"]}"""),
            ("PANEL_BORDER",       "Panel Border",       "Panel frame/border element",                """{"borderStyles":["solid","double","none"]}"""),
            ("PROP",               "Prop",               "Object/prop placed in a scene",             """{}"""),
        };

        foreach (var (code, name, desc, props) in elementTypes)
        {
            dbContext.ElementTypes.Add(ElementTypeMasterData.Create(code, name, desc, props));
        }
    }

    // ─── E-32: Export Presets ───────────────────────────────────────────────────

    private static async Task SeedExportPresetsAsync(PanelForgeDbContext dbContext, CancellationToken ct)
    {
        if (await dbContext.ExportPresets.AnyAsync(ct)) return;

        var presets = new[]
        {
            ("PDF",          "PDF Document",       "pdf",  """{"resolution":"300dpi","colorSpace":"CMYK","bleed":"3mm"}"""),
            ("CBZ",          "Comic Book Archive", "cbz",  """{"imageFormat":"png","quality":95}"""),
            ("WEBTOON",      "Webtoon Strip",      "jpg",  """{"stripWidth":800,"quality":85,"orientation":"vertical"}"""),
            ("PAGE_IMAGES",  "Individual Pages",   "png",  """{"resolution":"150dpi","colorSpace":"RGB"}"""),
            ("OPEN_JSON",    "Open PanelForge JSON","json","""{"version":"1.0","includeAssets":false}"""),
        };

        foreach (var (code, name, fmt, opts) in presets)
        {
            dbContext.ExportPresets.Add(ExportPreset.Create(code, name, fmt, opts));
        }
    }

    // ─── Task 5: Default Standard Manga Pipeline Template ──────────────────────

    private static async Task SeedDefaultPipelineTemplateAsync(PanelForgeDbContext dbContext, CancellationToken ct)
    {
        if (await dbContext.PipelineTemplates.AnyAsync(t => t.Code == "STANDARD_MANGA", ct)) return;

        var template = PipelineTemplate.Create(
            code: "STANDARD_MANGA",
            name: "Standard Manga Pipeline",
            description: "Canonical 8-stage manga production pipeline with guarded review gates. " +
                         "Script → Thumbnail → Pencil → Ink → Color → Letter → Review → Approved",
            isDefault: true
        );

        dbContext.PipelineTemplates.Add(template);

        // Add stages — must save before adding stages to get template.Id
        await dbContext.SaveChangesAsync(ct);

        var stages = new[]
        {
            ("SCRIPT",    "Script",    1, WorkspaceRole.Writer,   (string?)null,       true,  """{"colorCode":"#6B7280","estimatedDays":3}"""),
            ("THUMBNAIL", "Thumbnail", 2, WorkspaceRole.Artist,   null,                true,  """{"colorCode":"#8B5CF6","estimatedDays":2}"""),
            ("PENCIL",    "Pencil",    3, WorkspaceRole.Artist,   null,                true,  """{"colorCode":"#3B82F6","estimatedDays":4}"""),
            ("INK",       "Ink",       4, WorkspaceRole.Artist,   null,                true,  """{"colorCode":"#10B981","estimatedDays":3}"""),
            ("COLOR",     "Color",     5, WorkspaceRole.Artist,   null,                true,  """{"colorCode":"#F59E0B","estimatedDays":3}"""),
            ("LETTER",    "Letter",    6, WorkspaceRole.Letterer, null,                true,  """{"colorCode":"#EC4899","estimatedDays":2}"""),
            ("REVIEW",    "Review",    7, WorkspaceRole.Editor,   "Approval",          true,  """{"colorCode":"#EF4444","estimatedDays":1}"""),
            ("APPROVED",  "Approved",  8, WorkspaceRole.Editor,   null,                false, """{"colorCode":"#059669","estimatedDays":0}"""),
        };

        foreach (var (code, name, order, role, gate, isRequired, cfg) in stages)
        {
            dbContext.PipelineTemplateStages.Add(
                PipelineTemplateStage.Create(template.Id, code, name, order, role, gate, isRequired, cfg));
        }
    }
}
