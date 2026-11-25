using FluentAssertions;
using PanelForge.Application.Features.Auth.Queries.CheckEmail;
using PanelForge.Domain.Entities.Identity;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Auth;

public class CheckEmailQueryTests
{
    [Theory]
    [InlineData("reader@test.com", true)]
    [InlineData("  READER@Test.com ", true)]
    [InlineData("new@test.com", false)]
    public async Task ReportsWhetherEmailHasAccount_CaseAndSpaceInsensitive(string email, bool expected)
    {
        await using var db = InMemoryDb.Create();
        db.Users.Add(User.Create("reader@test.com", "Reader"));
        await db.SaveChangesAsync();

        var result = await new CheckEmailQueryHandler(db).Handle(new CheckEmailQuery(email), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Exists.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task InvalidEmail_Fails(string email)
    {
        await using var db = InMemoryDb.Create();
        var result = await new CheckEmailQueryHandler(db).Handle(new CheckEmailQuery(email), default);
        result.IsSuccess.Should().BeFalse();
    }
}
