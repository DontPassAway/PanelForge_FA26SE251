using Microsoft.EntityFrameworkCore;
using Moq;

namespace PanelForge.UnitTests.Common;

public static class DbSetMockHelper
{
    public static DbSet<T> CreateDbSetMock<T>(List<T> sourceList) where T : class
    {
        var queryable = sourceList.AsQueryable();
        var dbSetMock = new Mock<DbSet<T>>();

        dbSetMock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));

        dbSetMock.As<IQueryable<T>>()
            .Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));

        dbSetMock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        dbSetMock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        dbSetMock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());

        dbSetMock.Setup(d => d.Add(It.IsAny<T>())).Callback<T>(sourceList.Add);
        dbSetMock.Setup(d => d.Remove(It.IsAny<T>())).Callback<T>(e => sourceList.Remove(e));

        return dbSetMock.Object;
    }
}
