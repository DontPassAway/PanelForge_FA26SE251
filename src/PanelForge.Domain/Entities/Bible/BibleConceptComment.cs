using PanelForge.Domain.Common;

namespace PanelForge.Domain.Entities.Bible;

/// <summary>
/// Góp ý và thảo luận có trả lời (1 cấp) gắn với một phiên bản ConceptArt cụ thể (V2.6 - Q20).
/// </summary>
public class BibleConceptComment : BaseEntity
{
    public Guid RevisionId { get; private set; }
    public Guid ConceptEventId { get; private set; }
    public Guid? ParentCommentId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Body { get; private set; } = default!;
    /// <summary>Vùng khoanh trên ảnh: JSON {x,y,w,h} chuẩn hoá 0..1 (nếu có).</summary>
    public string? Region { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public Guid? ResolvedBy { get; private set; }

    public BibleEntryRevision Revision { get; internal set; } = default!;
    public BibleRevisionEvent ConceptEvent { get; internal set; } = default!;
    public BibleConceptComment? ParentComment { get; internal set; }
    public ICollection<BibleConceptComment> Replies { get; internal set; } = [];

    private BibleConceptComment() { }

    public static BibleConceptComment Create(
        Guid revisionId,
        Guid conceptEventId,
        Guid authorId,
        string body,
        string? region = null,
        Guid? parentCommentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        var trimmed = body.Trim();
        if (trimmed.Length is < 1 or > 4000)
            throw new ArgumentOutOfRangeException(nameof(body), "Nội dung nhận xét phải từ 1 đến 4000 ký tự.");

        return new BibleConceptComment
        {
            Id = Guid.NewGuid(),
            RevisionId = revisionId,
            ConceptEventId = conceptEventId,
            ParentCommentId = parentCommentId,
            AuthorId = authorId,
            Body = trimmed,
            Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }

    public void Resolve(Guid userId)
    {
        ResolvedAt = DateTime.UtcNow;
        ResolvedBy = userId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unresolve()
    {
        ResolvedAt = null;
        ResolvedBy = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Delete(Guid? userId = null)
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = userId?.ToString();
        UpdatedAt = DateTime.UtcNow;
    }
}
