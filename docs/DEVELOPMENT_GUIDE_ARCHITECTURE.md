# 📖 HƯỚNG DẪN KIẾN TRÚC & QUY TRÌNH PHÁT TRIỂN CODE (PANELFORGE)

> **Dành cho các thành viên nhóm phát triển Backend PanelForge**  
> **Kiến trúc áp dụng**: Clean Architecture + CQRS (MediatR) + Event Sourcing (Marten) & EF Core Read Model  
> **Mục tiêu**: Giúp mọi thành viên hiểu rõ cấu trúc từng tầng, biết chính xác cần tạo file gì, ở đâu và code như thế nào khi phát triển một tính năng mới.

---

## PHẦN 1: Ý NGHĨA VÀ VAI TRÒ CỦA TỪNG TẦNG (LAYERS)

Hệ thống được chia làm **4 tầng chính** theo nguyên tắc phụ thuộc một chiều (Dependency Inversion):

$$\text{Domain (Lõi cốt lõi)} \longleftarrow \text{Application} \longleftarrow \text{Infrastructure} \longleftarrow \text{API (Cửa ngõ)}$$

```
┌─────────────────────────────────────────────────────────────┐
│ 1. API LAYER (PanelForge.API)                              │
│    Controllers, Middlewares, Swagger, Dependency Injection  │
└──────────────────────────────┬──────────────────────────────┘
                               │ (gọi qua MediatR)
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ 2. APPLICATION LAYER (PanelForge.Application)               │
│    Commands, Queries, Handlers (Services), DTOs, Interfaces │
└──────────────┬───────────────────────────────┬──────────────┘
               │                               │
               ▼                               ▼
┌──────────────────────────────┐ ┌─────────────────────────────┐
│ 3. DOMAIN LAYER              │ │ 4. INFRASTRUCTURE LAYER     │
│    (PanelForge.Domain)       │ │    (PanelForge.Infrastructure)│
│    Aggregates, Entities,     │ │    Marten Event Store,      │
│    Domain Events, Enums      │ │    EF Core DbContext,       │
│    (Trái tim nghiệp vụ)      │ │    BCrypt, Email, Firebase  │
└──────────────────────────────┘ └─────────────────────────────┘
```

---

### 1. Tầng Domain (`PanelForge.Domain`) — Trái tim nghiệp vụ
* **Ý nghĩa**: Chứa các quy tắc bất biến của thế giới thực Manga (ví dụ: kích thước trang không được âm, toạ độ không được vượt ra ngoài canvas, xóa mềm chứ không xóa cứng).
* **Quy tắc vàng**: **Tuyệt đối KHÔNG phụ thuộc vào bất kỳ thư viện hay database nào**. Không import EF Core, không import ASP.NET Core, không quan tâm dữ liệu lưu vào đâu.
* **Gồm các loại file**:
  * **Aggregate Root / Entity** (ví dụ: `Page.cs`, `User.cs`, `Chapter.cs`): Chứa dữ liệu nghiệp vụ và các hàm thay đổi dữ liệu (`AddElement()`, `MoveElement()`, `UpdateCanvasSettings()`).
  * **Domain Events** (ví dụ: `PageCreatedEvent.cs`, `ElementAddedEvent.cs`): Các sự kiện nghiệp vụ bất biến ghi nhận việc gì vừa xảy ra.
  * **Enums** (ví dụ: `ElementType.cs`, `LayoutFormat.cs`).

---

### 2. Tầng Application (`PanelForge.Application`) — Nhạc trưởng điều phối
* **Ý nghĩa**: Tiếp nhận yêu cầu từ API Controller, xác thực tính hợp lệ, gọi Domain để thực thi nghiệp vụ, và lưu/đọc qua Repository/DbContext.
* **Tư duy CQRS (Thay thế cho Service to tướng truyền thống)**:
  * **DTO (Data Transfer Object)**: Khuôn dữ liệu chỉ chứa các trường cần hiển thị để gửi về cho Client xem (ví dụ: `PageDto.cs`). Không bao giờ gửi trực tiếp Entity Domain ra ngoài API.
  * **Command**: Định nghĩa dữ liệu Client gửi lên để **Ghi / Thay đổi** (Thêm, Sửa, Xóa).
  * **Query**: Định nghĩa dữ liệu Client gửi lên để **Đọc / Lọc** dữ liệu.
  * **Handler (CHÍNH LÀ CODE SERVICE)**: Thay vì gom 20 hàm vào 1 file `PageService.cs`, mỗi hàm được tách thành 1 file Handler độc lập (`CreatePageCommandHandler.cs`, `GetPagesByChapterQueryHandler.cs`).
  * **Interfaces** (`IPageRepository.cs`, `IPanelForgeDbContext.cs`): Hợp đồng định nghĩa Application cần gì, để tầng Infrastructure cài đặt.

---

### 3. Tầng Infrastructure (`PanelForge.Infrastructure`) — Giao tiếp hạ tầng & Database
* **Ý nghĩa**: Nơi duy nhất "nói chuyện" trực tiếp với hệ điều hành, dịch vụ bên ngoài và Database.
* **Gồm các loại file**:
  * **Repositories** (ví dụ: `MartenPageRepository.cs`): Viết code tương tác với Marten Event Store (Append event, Replay stream).
  * **Persistence (EF Core)** (`PanelForgeDbContext.cs`, `Configurations/`): Cấu hình bảng quan hệ PostgreSQL, migrations, quan hệ khóa ngoại (One-to-Many, Foreign Keys).
  * **Services công nghệ**: Mã hóa BCrypt (`BcryptPasswordHasher.cs`), gửi mail SMTP (`EmailService.cs`), xác thực Firebase.

---

### 4. Tầng API (`PanelForge.API`) — Cửa ngõ giao tiếp HTTP
* **Ý nghĩa**: Tiếp nhận HTTP Request (GET, POST, PUT, DELETE) từ Frontend/Postman/Swagger và trả về HTTP Response tương ứng (`200 OK`, `201 Created`, `400 BadRequest`, `404 NotFound`).
* **Đặc điểm "Thin Controller" (Controller siêu mỏng)**:
  * Controller **không chứa logic tính toán**.
  * Controller chỉ làm 3 bước:
    1. Nhận dữ liệu HTTP từ Client.
    2. Đóng gói thành `Command` hoặc `Query` và bắn qua MediatR: `await _mediator.Send(command)`.
    3. Trả về kết quả cho Client.

---

### 5. So sánh trực quan: Mô hình Service cũ vs. CQRS Clean Architecture hiện tại

| Thành phần trong suy nghĩ cũ | Tương ứng trong dự án PanelForge | Nằm ở đâu? | Vai trò cụ thể |
|---|---|---|---|
| **Model / DTO** | **DTO** (`PageDto.cs`, `SeriesDto.cs`) | `PanelForge.Application/DTOs/` | Định hình dữ liệu JSON trả về cho Frontend xem. |
| **Request Model** | **Command / Query** (`CreatePageCommand.cs`) | `PanelForge.Application/Features/...` | Định hình dữ liệu Client gửi lên Server. |
| **Interface Service** (`IPageService`) | **`IRequestHandler<TCommand, TResult>`** | Thư viện MediatR cấp sẵn | Mỗi Handler là 1 nghiệp vụ, không cần viết Interface thủ công nữa! |
| **Class Service** (`PageService.cs`) | **Handler** (`CreatePageCommandHandler.cs`) | `PanelForge.Application/Features/...` | **Chính là Service**: Nơi viết toàn bộ logic xử lý nghiệp vụ, kiểm tra dữ liệu và lưu DB. |
| **Repository Interface** | **`IPageRepository.cs`**, **`IPanelForgeDbContext.cs`** | `PanelForge.Application/Interfaces/` | Hợp đồng giao tiếp dữ liệu do Application định nghĩa. |
| **Repository Implementation** | **`MartenPageRepository.cs`**, **`PanelForgeDbContext.cs`** | `PanelForge.Infrastructure/` | Cài đặt thực tế bằng Marten hoặc EF Core. |
| **Controller** | **Controller** (`PagesController.cs`) | `PanelForge.API/Controllers/` | Chỉ nhận URL, chuyển việc cho Handler xử lý qua `_mediator.Send()` và trả HTTP status. |

---

## PHẦN 2: QUY TRÌNH 5 BƯỚC KHI TẠO TÍNH NĂNG MỚI (RECIPE)

Giả sử bạn cần làm một tính năng mới: **"Tạo bộ truyện mới (Create Series)"**. Hãy làm chuẩn theo 5 bước sau:

---

### BƯỚC 1: Tạo DTO (Dữ liệu trả về cho Client)
* **Thư mục**: `src/PanelForge.Application/DTOs/Content/SeriesDto.cs`
* **Mục đích**: Chỉ chứa các trường cần trả về cho Client hiển thị.

```csharp
namespace PanelForge.Application.DTOs.Content;

public sealed record SeriesDto(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    string? Synopsis,
    string ReadingDirection,
    DateTime CreatedAt
);
```

---

### BƯỚC 2: Tạo Command / Query (Dữ liệu đầu vào)
* **Thư mục**: `src/PanelForge.Application/Features/Series/CreateSeries/CreateSeriesCommand.cs`
* **Mục đích**: Chứa các thông tin Client cần gửi lên để tạo Series. Kế thừa `IRequest<Result<T>>`.

```csharp
using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;

namespace PanelForge.Application.Features.Series.CreateSeries;

// Khai báo dữ liệu gửi vào, và mong muốn nhận về Result<SeriesDto>
public sealed record CreateSeriesCommand(
    Guid WorkspaceId,
    string Title,
    string? Synopsis = null,
    string ReadingDirection = "RightToLeft"
) : IRequest<Result<SeriesDto>>;
```

---

### BƯỚC 3: Tạo Handler (Viết logic Service xử lý)
* **Thư mục**: `src/PanelForge.Application/Features/Series/CreateSeries/CreateSeriesCommandHandler.cs`
* **Mục đích**: Cài đặt interface `IRequestHandler<CreateSeriesCommand, Result<SeriesDto>>`. Đây chính là nơi viết code kiểm tra, tính toán và lưu database.

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.DTOs.Content;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Series.CreateSeries;

public sealed class CreateSeriesCommandHandler 
    : IRequestHandler<CreateSeriesCommand, Result<SeriesDto>>
{
    private readonly IPanelForgeDbContext _dbContext;

    // Inject DbContext hoặc Repository cần thiết
    public CreateSeriesCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SeriesDto>> Handle(
        CreateSeriesCommand command, 
        CancellationToken cancellationToken)
    {
        // 1. Kiểm tra nghiệp vụ (Validate)
        if (string.IsNullOrWhiteSpace(command.Title))
            return Result<SeriesDto>.Failure("Tên bộ truyện không được để trống.");

        var workspaceExists = await _dbContext.StudioWorkspaces
            .AnyAsync(w => w.Id == command.WorkspaceId, cancellationToken);
        if (!workspaceExists)
            return Result<SeriesDto>.Failure($"Workspace {command.WorkspaceId} không tồn tại.");

        // 2. Parse enum
        if (!Enum.TryParse<ReadingDirection>(command.ReadingDirection, true, out var direction))
            direction = ReadingDirection.RightToLeft;

        // 3. Gọi Domain Entity để tạo đối tượng
        var series = Domain.Entities.Content.Series.Create(
            command.WorkspaceId,
            command.Title,
            direction,
            command.Synopsis
        );

        // 4. Lưu vào Database
        _dbContext.Series.Add(series);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 5. Trả về kết quả thành công kèm DTO
        return Result<SeriesDto>.Success(new SeriesDto(
            series.Id,
            series.WorkspaceId,
            series.Title,
            series.Synopsis,
            series.ReadingDirection.ToString(),
            series.CreatedAt
        ));
    }
}
```

---

### BƯỚC 4: Tạo Controller Endpoint (API tiếp nhận HTTP)
* **Thư mục**: `src/PanelForge.API/Controllers/SeriesController.cs`
* **Mục đích**: Đón đường dẫn URL `/api/workspaces/{workspaceId}/series`, đóng gói Command và bắn qua MediatR.

```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.DTOs.Content;
using PanelForge.Application.Features.Series.CreateSeries;

namespace PanelForge.API.Controllers;

[ApiController]
[Route("api/workspaces/{workspaceId:guid}/series")]
public sealed class SeriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SeriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SeriesDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSeries(
        [FromRoute] Guid workspaceId,
        [FromBody] CreateSeriesRequestBody body,
        CancellationToken cancellationToken)
    {
        // 1. Đóng gói dữ liệu thành Command
        var command = new CreateSeriesCommand(
            WorkspaceId:      workspaceId,
            Title:            body.Title,
            Synopsis:         body.Synopsis,
            ReadingDirection: body.ReadingDirection ?? "RightToLeft"
        );

        // 2. Bắn sang MediatR (tự động tìm đến CreateSeriesCommandHandler để chạy)
        var result = await _mediator.Send(command, cancellationToken);

        // 3. Map sang HTTP Response chuẩn
        if (!result.IsSuccess)
            return BadRequest(new { error = result.ErrorMessage });

        return CreatedAtAction(nameof(CreateSeries), new { id = result.Value!.Id }, result.Value);
    }
}

public sealed record CreateSeriesRequestBody(
    string Title,
    string? Synopsis,
    string? ReadingDirection
);
```

---

### BƯỚC 5: Viết Unit Test (Bắt buộc để pass CI/CD)
* **Thư mục**: `test/PanelForge.UnitTests/Application/Series/CreateSeriesCommandHandlerTests.cs`
* **Mục đích**: Kiểm thử các trường hợp thành công và thất bại với Moq & FluentAssertions.

```csharp
using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Series.CreateSeries;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Content;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Series;

public class CreateSeriesCommandHandlerTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock = new();
    private readonly CreateSeriesCommandHandler _handler;

    public CreateSeriesCommandHandlerTests()
    {
        _handler = new CreateSeriesCommandHandler(_dbContextMock.Object);
    }

    [Fact]
    public async Task Handle_WhenValidInput_ShouldCreateSeriesAndReturnSuccess()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Workspace", Guid.NewGuid());
        typeof(StudioWorkspace).GetProperty("Id")!.SetValue(workspace, workspaceId);

        var wsDbSet = DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace });
        var seriesList = new List<Domain.Entities.Content.Series>();
        var seriesDbSet = DbSetMockHelper.CreateDbSetMock(seriesList);

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(wsDbSet);
        _dbContextMock.Setup(db => db.Series).Returns(seriesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var command = new CreateSeriesCommand(workspaceId, "One Piece", "Demo manga");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("One Piece");
        seriesList.Should().ContainSingle(s => s.Title == "One Piece");
    }
}
```

---

## 🎯 BẢNG TÓM TẮT ĐỐI CHIẾU NHANH

| Bạn muốn làm gì? | Bạn cần tạo file ở đâu? | Kế thừa / Cài đặt cái gì? |
|---|---|---|
| **Định nghĩa dữ liệu trả về cho Frontend** | `src/PanelForge.Application/DTOs/` | Dùng C# `record` (ví dụ `PageDto`) |
| **Định nghĩa dữ liệu gửi lên để Ghi** | `src/PanelForge.Application/Features/{ChứcNăng}/{Tên}/` | Kế thừa `IRequest<Result<TResult>>` |
| **Định nghĩa dữ liệu gửi lên để Đọc/Lọc** | `src/PanelForge.Application/Features/{ChứcNăng}/{Tên}/` | Kế thừa `IRequest<Result<TResult>>` |
| **Viết code xử lý logic (Service)** | `src/PanelForge.Application/Features/{ChứcNăng}/{Tên}/` | Cài đặt `IRequestHandler<TCommand, Result<TResult>>` |
| **Tạo cổng API đón đường dẫn URL** | `src/PanelForge.API/Controllers/` | Kế thừa `ControllerBase`, inject `IMediator` |
| **Viết test kiểm tra tự động** | `test/PanelForge.UnitTests/Application/{ChứcNăng}/` | Dùng `[Fact]`, `[Theory]`, `FluentAssertions`, `Moq` |
