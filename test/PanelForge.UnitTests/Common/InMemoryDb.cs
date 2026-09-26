using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Interfaces;
using PanelForge.Infrastructure.Persistence;

namespace PanelForge.UnitTests.Common;

/// <summary>
/// PanelForgeDbContext thật trên EF InMemory: dùng để kiểm tra logic trong SaveChangesAsync
/// (audit trail, soft-delete) và các handler cần change tracking thật.
/// </summary>
public static class InMemoryDb
{
    public static PanelForgeDbContext Create(ICurrentUserService? currentUser = null, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<PanelForgeDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;
        return new PanelForgeDbContext(options, currentUser);
    }
}

public sealed class FakeCurrentUser : ICurrentUserService
{
    public FakeCurrentUser(Guid? userId = null, string? email = null, string? ipAddress = "127.0.0.1")
    {
        UserId = userId;
        Email = email;
        IpAddress = ipAddress;
    }

    public Guid? UserId { get; }
    public string? Email { get; }
    public string? IpAddress { get; }
}
