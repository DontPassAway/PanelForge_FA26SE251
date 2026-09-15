using System.Collections.Generic;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.DTOs.Auth;
using PanelForge.Application.Interfaces; 
using PanelForge.Application.Interfaces.Authentication;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Application.Services;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Infrastructure.Services; 
using Xunit;

namespace PanelForge.UnitTests;

public class AuthServiceTests
{
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IJwtTokenGenerator> _jwtTokenGeneratorMock;
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;
    private readonly Mock<IEmailService> _emailServiceMock; // Khai báo thêm Mock cho EmailService

    public AuthServiceTests()
    {
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _jwtTokenGeneratorMock = new Mock<IJwtTokenGenerator>();
        _dbContextMock = new Mock<IPanelForgeDbContext>();
        _emailServiceMock = new Mock<IEmailService>(); // Khởi tạo Mock
    }

    // Helper tạo Mock DbSet hỗ trợ truy vấn bất đồng bộ EF Core
    private static DbSet<T> CreateDbSetMock<T>(List<T> sourceList) where T : class
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

        return dbSetMock.Object;
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailAlreadyExists_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var existingUser = User.Create("test@example.com", "Existing User", "hashed_pwd", null);
        var usersList = new List<User> { existingUser };
        var usersDbSet = CreateDbSetMock(usersList);

        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);

        // Truyền thêm _emailServiceMock.Object vào constructor
        var service = new AuthService(_dbContextMock.Object, _passwordHasherMock.Object, _jwtTokenGeneratorMock.Object, _emailServiceMock.Object);
        var request = new RegisterRequest("test@example.com", "Password123!", "New User");

        // Act
        Func<Task> act = async () => await service.RegisterAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Email đã được đăng ký trong hệ thống.");
    }

    [Fact]
    public async Task RegisterAsync_WhenValidRequest_ShouldCreateUserAndReturnToken()
    {
        // Arrange
        var usersList = new List<User>();
        var usersDbSet = CreateDbSetMock(usersList);

        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(1);

        _passwordHasherMock.Setup(h => h.HashPassword("Password123!"))
                           .Returns("hashed_secure_password");

        var fakeExpiry = DateTime.UtcNow.AddHours(1);
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(It.IsAny<User>()))
                              .Returns(("fake_jwt_token", fakeExpiry));

        // Truyền thêm _emailServiceMock.Object vào constructor
        var service = new AuthService(_dbContextMock.Object, _passwordHasherMock.Object, _jwtTokenGeneratorMock.Object, _emailServiceMock.Object);
        var request = new RegisterRequest("tam@example.com", "Password123!", "Bui Ngoc Tam", "0123456789");

        // Act
        var result = await service.RegisterAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Token.Should().Be("fake_jwt_token");
        result.ExpiresAt.Should().Be(fakeExpiry);
        result.User.Email.Should().Be("tam@example.com");
        result.User.FullName.Should().Be("Bui Ngoc Tam");

        usersList.Should().ContainSingle(u => u.Email == "tam@example.com");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // Verify email được gọi 1 lần
        _emailServiceMock.Verify(e => e.SendEmailVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenUserNotFound_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var usersDbSet = CreateDbSetMock(new List<User>());
        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);

        // Truyền thêm _emailServiceMock.Object vào constructor
        var service = new AuthService(_dbContextMock.Object, _passwordHasherMock.Object, _jwtTokenGeneratorMock.Object, _emailServiceMock.Object);
        var request = new LoginRequest("notfound@example.com", "Password123!");

        // Act
        Func<Task> act = async () => await service.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Email hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task LoginAsync_WhenPasswordIsIncorrect_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var user = User.Create("tam@example.com", "Bui Ngoc Tam", "hashed_password", null);
        var usersDbSet = CreateDbSetMock(new List<User> { user });

        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);
        _passwordHasherMock.Setup(h => h.VerifyPassword("WrongPassword", "hashed_password"))
                           .Returns(false);

        // Truyền thêm _emailServiceMock.Object vào constructor
        var service = new AuthService(_dbContextMock.Object, _passwordHasherMock.Object, _jwtTokenGeneratorMock.Object, _emailServiceMock.Object);
        var request = new LoginRequest("tam@example.com", "WrongPassword");

        // Act
        Func<Task> act = async () => await service.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Email hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task LoginAsync_WhenCredentialsAreValid_ShouldReturnToken()
    {
        // Arrange
        var user = User.Create("tam@example.com", "Bui Ngoc Tam", "hashed_password", null);
        var usersDbSet = CreateDbSetMock(new List<User> { user });

        _dbContextMock.Setup(db => db.Users).Returns(usersDbSet);
        _passwordHasherMock.Setup(h => h.VerifyPassword("CorrectPassword", "hashed_password"))
                           .Returns(true);

        var fakeExpiry = DateTime.UtcNow.AddHours(24);
        _jwtTokenGeneratorMock.Setup(j => j.GenerateToken(user))
                              .Returns(("valid_jwt_token", fakeExpiry));

        // Truyền thêm _emailServiceMock.Object vào constructor
        var service = new AuthService(_dbContextMock.Object, _passwordHasherMock.Object, _jwtTokenGeneratorMock.Object, _emailServiceMock.Object);
        var request = new LoginRequest("tam@example.com", "CorrectPassword");

        // Act
        var result = await service.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Token.Should().Be("valid_jwt_token");
        result.User.Email.Should().Be("tam@example.com");
    }
}