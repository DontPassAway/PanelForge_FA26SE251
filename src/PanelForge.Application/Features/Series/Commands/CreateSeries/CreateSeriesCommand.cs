using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Series.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Bible;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.Series.Commands.CreateSeries;

public sealed record CreateSeriesCommand(
    Guid WorkspaceId,
    Guid UserId,
    string Title,
    string? Synopsis,
    ReadingDirection ReadingDirection,
    Guid? PipelineTemplateId,
    string Genre = "Action",
    string Format = "Manga",
    string? ReleaseScheduleJson = null
) : IRequest<Result<SeriesDto>>;

/// <summary>
/// Task 7: Creates a Series by cloning stages from a PipelineTemplate (DB-backed).
/// Falls back to the default active template if PipelineTemplateId is not specified.
/// </summary>
public sealed class CreateSeriesCommandHandler : IRequestHandler<CreateSeriesCommand, Result<SeriesDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public CreateSeriesCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SeriesDto>> Handle(CreateSeriesCommand command, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra Workspace có tồn tại không
        var workspaceExists = await _dbContext.StudioWorkspaces
            .AnyAsync(w => w.Id == command.WorkspaceId, cancellationToken);

        if (!workspaceExists)
            return Result<SeriesDto>.Failure($"Workspace {command.WorkspaceId} không tồn tại.");

        // 2. Kiểm tra User có phải thành viên Workspace không
        var isMember = await _dbContext.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == command.WorkspaceId && m.UserId == command.UserId, cancellationToken);
        if (!isMember)
            return Result<SeriesDto>.Failure("Bạn không phải thành viên của Workspace này.");

        // 3. Task 7: Resolve PipelineTemplate từ DB
        var template = command.PipelineTemplateId.HasValue
            ? await _dbContext.PipelineTemplates
                .Include(t => t.Stages)
                .FirstOrDefaultAsync(t => t.Id == command.PipelineTemplateId.Value && t.IsActive, cancellationToken)
            : await _dbContext.PipelineTemplates
                .Include(t => t.Stages)
                .FirstOrDefaultAsync(t => t.IsDefault && t.IsActive, cancellationToken);

        if (template is null)
        {
            return Result<SeriesDto>.Failure(
                command.PipelineTemplateId.HasValue
                    ? $"PipelineTemplate {command.PipelineTemplateId.Value} không tồn tại hoặc đã bị vô hiệu hóa."
                    : "Không tìm thấy PipelineTemplate mặc định đang hoạt động. Vui lòng cấu hình template trong Admin.");
        }

        // 4. Tạo Series entity
        DomainSeries series;
        try
        {
            series = DomainSeries.Create(
                workspaceId: command.WorkspaceId,
                title: command.Title,
                readingDirection: command.ReadingDirection,
                synopsis: command.Synopsis,
                genre: command.Genre,
                format: command.Format,
                releaseScheduleJson: command.ReleaseScheduleJson
            );

            series.CreatedBy = command.UserId.ToString();
        }
        catch (ArgumentException ex)
        {
            return Result<SeriesDto>.Failure(ex.Message);
        }

        // 5. Clone PipelineTemplate -> Series PipelineDefinition (Task 7)
        var pipeline = Domain.Entities.Workflow.PipelineDefinition.Create(
            workspaceId: command.WorkspaceId,
            name: $"{series.Title} — {template.Name}",
            description: $"Cloned from template: {template.Code}",
            seriesId: series.Id,
            isDefault: false
        );

        // Clone stages theo thứ tự từ template
        foreach (var templateStage in template.Stages.OrderBy(s => s.Order))
        {
            pipeline.AddStage(
                name: templateStage.Name,
                slug: templateStage.Code.ToLowerInvariant(),
                stageOrder: templateStage.Order,
                colorCode: null,
                allowedRole: templateStage.RequiredRole,
                isApprovalGate: templateStage.GateType == "Approval",
                isInitial: templateStage.Order == template.Stages.Min(s => s.Order),
                isTerminal: templateStage.Order == template.Stages.Max(s => s.Order)
            );
        }

        series.SetPipelineDefinition(pipeline.Id);
        _dbContext.PipelineDefinitions.Add(pipeline);

        // 6. Khởi tạo mặc định SeriesBible cho bộ truyện mới (CF1 Step 7)
        var bible = SeriesBible.Create(series.Id, command.WorkspaceId, $"{series.Title} - Bible");
        series.AttachBible(bible);

        _dbContext.Series.Add(series);
        _dbContext.SeriesBibles.Add(bible);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<SeriesDto>.Success(new SeriesDto(
            Id: series.Id,
            WorkspaceId: series.WorkspaceId,
            Title: series.Title,
            Synopsis: series.Synopsis,
            ReadingDirection: series.ReadingDirection,
            PipelineDefinitionId: series.PipelineDefinitionId,
            Genre: series.Genre,
            Format: series.Format,
            ReleaseScheduleJson: series.ReleaseScheduleJson,
            CreatedAt: series.CreatedAt,
            UpdatedAt: series.UpdatedAt,
            CreatedBy: series.CreatedBy,
            ChaptersCount: 0
        ));
    }
}
