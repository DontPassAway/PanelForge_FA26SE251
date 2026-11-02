using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Commands.LinkBibleEntries;

/// <summary>
/// V2.5 & Module A: Gắn liên kết Dynamic @-mentions từ kịch bản vào các thực thể Series Bible.
/// Tự động tạo Stub Entity khi thực thể chưa tồn tại trong Series Bible (Write First, Bible Later).
/// </summary>
public sealed record LinkBibleEntriesCommand(
    Guid UserId,
    Guid ChapterId,
    Guid ScriptLineId,
    IReadOnlyList<ScriptMentionItemDto> Mentions
) : IRequest<Result<IReadOnlyList<ScriptLineBibleLinkDto>>>;

public sealed class LinkBibleEntriesCommandHandler : IRequestHandler<LinkBibleEntriesCommand, Result<IReadOnlyList<ScriptLineBibleLinkDto>>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public LinkBibleEntriesCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<IReadOnlyList<ScriptLineBibleLinkDto>>> Handle(LinkBibleEntriesCommand cmd, CancellationToken ct)
    {
        var line = await _db.ScriptLines.FirstOrDefaultAsync(l => l.Id == cmd.ScriptLineId, ct);
        if (line is null)
            return Result<IReadOnlyList<ScriptLineBibleLinkDto>>.Failure($"ScriptLine {cmd.ScriptLineId} không tồn tại.", ResultErrorCodes.NotFound);

        var authResult = await ScriptLineRules.AuthorizeAsync(_db, _auth, cmd.UserId, cmd.ChapterId, null, ct);
        if (!authResult.IsSuccess)
            return Result<IReadOnlyList<ScriptLineBibleLinkDto>>.Failure(authResult.ErrorMessage!, authResult.ErrorCode);

        var (chapter, _) = authResult.Value;

        var seriesBible = await _db.SeriesBibles
            .Include(b => b.Entries)
                .ThenInclude(e => e.Revisions)
            .FirstOrDefaultAsync(b => b.SeriesId == chapter.SeriesId, ct);

        if (seriesBible is null)
        {
            var workspaceId = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
            seriesBible = SeriesBible.Create(chapter.SeriesId, workspaceId, "Series Bible", "Series Bible mặc định");
            _db.SeriesBibles.Add(seriesBible);
            await _db.SaveChangesAsync(ct);
        }

        var currentLinks = await _db.ScriptLineBibleLinks
            .Include(l => l.BibleEntry)
            .Include(l => l.ResolvedRevision)
            .Where(l => l.ScriptLineId == cmd.ScriptLineId)
            .ToListAsync(ct);

        var chapterNumberInt = (int)Math.Floor(chapter.ChapterNumber);
        var resultingLinks = new List<ScriptLineBibleLink>();

        foreach (var mention in cmd.Mentions)
        {
            BibleEntry? targetEntry = null;

            if (mention.BibleEntryId.HasValue && mention.BibleEntryId.Value != Guid.Empty)
            {
                targetEntry = seriesBible.Entries.FirstOrDefault(e => e.Id == mention.BibleEntryId.Value && e.IsActive);
            }

            if (targetEntry is null && !string.IsNullOrWhiteSpace(mention.RawText))
            {
                var cleanName = mention.RawText.Trim().TrimStart('@').Trim();
                targetEntry = seriesBible.Entries.FirstOrDefault(e =>
                    e.IsActive && string.Equals(e.Name, cleanName, StringComparison.OrdinalIgnoreCase));

                // Nếu chưa có, tự động tạo Stub Entity
                if (targetEntry is null)
                {
                    var category = mention.CategoryHint ?? BibleEntryCategory.Character;
                    targetEntry = BibleEntry.CreateStub(seriesBible.Id, category, cleanName);

                    var snapshotObj = new
                    {
                        targetEntry.Name,
                        targetEntry.Category,
                        targetEntry.Description,
                        IsStub = true,
                        EffectiveFromChapterNumber = 1
                    };
                    var snapshotJson = JsonSerializer.Serialize(snapshotObj);
                    var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));

                    var stubRev = targetEntry.AddRevision(
                        summary: $"Stub khởi tạo tự động từ kịch bản cho '@{cleanName}'",
                        snapshotJson: snapshotJson,
                        contentHash: contentHash,
                        authorId: cmd.UserId,
                        associatedChapterId: cmd.ChapterId,
                        effectiveFromChapterNumber: 1,
                        isInitialVersion: true
                    );

                    _db.BibleEntries.Add(targetEntry);
                    _db.BibleEntryRevisions.Add(stubRev);
                    seriesBible.Entries.Add(targetEntry);
                }
            }

            if (targetEntry is null) continue;

            // Xác định revision có hiệu lực tại chapter này
            var effectiveRev = targetEntry.EffectiveRevisionAt(chapterNumberInt, canonOnly: false);
            var resolvedRevId = mention.ResolvedRevisionId ?? effectiveRev?.Id;

            var existingLink = currentLinks.FirstOrDefault(l => l.BibleEntryId == targetEntry.Id);
            if (existingLink != null)
            {
                existingLink.UpdateResolvedRevision(resolvedRevId);
                resultingLinks.Add(existingLink);
            }
            else
            {
                var newLink = ScriptLineBibleLink.Create(
                    cmd.ScriptLineId,
                    targetEntry.Id,
                    mention.RawText,
                    resolvedRevId
                );
                _db.ScriptLineBibleLinks.Add(newLink);
                resultingLinks.Add(newLink);
            }
        }

        // Xóa những liên kết không còn trong danh sách mentions
        var resultingEntryIds = resultingLinks.Select(r => r.BibleEntryId).ToHashSet();
        foreach (var oldLink in currentLinks.Where(l => !resultingEntryIds.Contains(l.BibleEntryId)))
        {
            _db.ScriptLineBibleLinks.Remove(oldLink);
        }

        await _db.SaveChangesAsync(ct);

        // Nạp lại thông tin để sinh DTO đầy đủ
        var updatedLinks = await _db.ScriptLineBibleLinks
            .Include(l => l.BibleEntry)
                .ThenInclude(e => e.Revisions)
            .Include(l => l.ResolvedRevision)
            .Where(l => l.ScriptLineId == cmd.ScriptLineId)
            .ToListAsync(ct);

        var dtos = updatedLinks.Select(l =>
        {
            var rev = l.ResolvedRevision ?? l.BibleEntry.EffectiveRevisionAt(chapterNumberInt, false);
            return new ScriptLineBibleLinkDto(
                Id: l.Id,
                ScriptLineId: l.ScriptLineId,
                BibleEntryId: l.BibleEntryId,
                RawMentionText: l.RawMentionText,
                ResolvedRevisionId: l.ResolvedRevisionId,
                EntryName: l.BibleEntry.Name,
                Category: l.BibleEntry.Category,
                IsStub: l.BibleEntry.IsStub,
                StageName: rev?.StageName,
                ReferenceImageUrl: l.BibleEntry.ReferenceImageUrl,
                IsCanonLocked: rev?.IsLocked ?? false,
                CreatedAt: l.CreatedAt
            );
        }).ToList();

        return Result<IReadOnlyList<ScriptLineBibleLinkDto>>.Success(dtos);
    }
}

