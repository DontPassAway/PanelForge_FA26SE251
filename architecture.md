# Kiến Trúc Dự Án PanelForge (PanelForge Architecture Guide)

> **Dự án:** PanelForge — AI-Assisted Manga Production Management Platform  
> **Công nghệ cốt lõi:** .NET 9, PostgreSQL, Entity Framework Core, Marten (Document Store / Event Store), MediatR (CQRS).

---

## 1. Tổng Quan Kiến Trúc & Hiện Trạng

### 1.1. Mô hình áp dụng: Clean Architecture + CQRS (Vertical Slice trong Application)

Dự án tuân thủ nguyên lý **Dependency Inversion Principle (DIP)** của Clean Architecture:
- **`PanelForge.Domain`**: Lõi nghiệp vụ (Entities, Value Objects, Enums, Domain Events, Domain Exceptions). Không phụ thuộc vào bất kỳ thư viện bên ngoài nào.
- **`PanelForge.Application`**: Chứa use-case nghiệp vụ, điều phối dữ liệu (Commands, Queries, DTOs, Business Rules, Interfaces).
- **`PanelForge.Infrastructure`**: Hiện thực hóa kỹ thuật bên ngoài (EF Core DbContext, Marten Document Session, Firebase, BCrypt, JWT, Third-party APIs).
- **`PanelForge.API`**: Cổng giao tiếp ngoại vi (REST Controllers, Middlewares, Swagger, Dependency Injection Bootstrapping).

```
   ┌─────────────────────────────────────────────────────────┐
   │                   PanelForge.API                        │
   └───────────────┬─────────────────────────┬───────────────┘
                   │                         │
                   ▼                         │
   ┌───────────────────────────────┐         │
   │    PanelForge.Infrastructure  │         │
   └───────────────┬───────────────┘         │
                   │ (Implements)            │ (References)
                   ▼                         ▼
   ┌─────────────────────────────────────────────────────────┐
   │                 PanelForge.Application                  │
   └───────────────────────────────┬─────────────────────────┘
                                   │ (References)
                                   ▼
   ┌─────────────────────────────────────────────────────────┐
   │                   PanelForge.Domain                     │
   └─────────────────────────────────────────────────────────┘
```

---

### 1.2. Đánh giá hiện trạng & Những điểm đang gây "lẫn lộn"

Qua rà soát toàn bộ source code hiện tại, dự án đang gặp phải tình trạng **bất nhất về phong cách thiết kế**:

1. **Cấu trúc Application bị phân mảnh:**
   - Một số tính năng nằm trong `Features/Pages/`, `Features/Elements/` (theo hướng Feature Slice).
   - Nhưng chức năng User lại nằm ở root `Users/Commands/`, `Users/Queries/`.
   - Vừa có `Services/` (`IWorkspaceService`, `IAuthService`), vừa có `Commands/Queries` (MediatR).
2. **Trách nhiệm Controller không đồng bộ:**
   - `UsersController`: Dùng `ISender` (MediatR CQRS).
   - `AuthController` & `WorkspacesController`: Gọi thẳng Service (`IAuthService`, `IWorkspaceService`).
   - `AdminController`: Inject trực tiếp `IPanelForgeDbContext` ngay trong Controller (phá vỡ ranh giới Clean Architecture).
3. **Vị trí Interface bị phân tán:**
   - Có interface nằm ở `Interfaces/`, có cái ở `Interfaces/Persistence/`, `Interfaces/Authentication/`.
4. **Chưa phân biệt rõ phạm vi Repository vs Service.**

---

## 2. Tiêu Chuẩn Phân Định: Khi Nào Tạo Repository? Khi Nào Tạo Service?

Đây là câu hỏi quan trọng nhất để giữ cho code không bị phình to (bloated) và không sinh ra các file thừa thãi.

### 2.1. Khi nào tạo Repository (`IRepository`)?

**Bản chất:** Repository là một cơ chế trừu tượng hóa việc truy xuất dữ liệu, giúp tầng Application tương tác với dữ liệu như thể nó là một tập hợp đối tượng trong RAM (`Collection-like interface`).

| NÊN TẠO REPOSITORY | KHÔNG NÊN TẠO REPOSITORY |
| :--- | :--- |
| **Document Store / NoSQL (Marten):** Khi làm việc với Marten (lưu canvas JSON, script panels, layer, revision) -> **Rất nên tạo**, ví dụ: `IPageRepository`, `IScriptRepository`. | **Generic Repository rỗng tuếch trên EF Core:** Ví dụ tạo `IRepository<T>` chỉ để bọc lại `.Add()`, `.Update()`, `.GetById()` của `DbSet<T>`. Đây là Anti-pattern vì bản thân `DbSet<T>` đã là Repository và `DbContext` là Unit of Work. |
| **Truy vấn Aggregate Root phức tạp:** Khi một Entity có nhiều quan hệ lồng nhau, cần kiểm tra logic nghiệp vụ lưu trữ nguyên khối (Aggregate). | **Truy vấn đọc (Read / Query side trong CQRS):** Các API lấy danh sách, phân trang, lọc dữ liệu. Nên dùng `IPanelForgeDbContext` truy vấn trực tiếp bằng LINQ `.Select()` ra DTO để tận dụng SQL projection tối ưu hiệu năng. |
| **Dữ liệu cần chuyển đổi Data Source:** Khi một đối tượng có khả năng lưu ở Redis Cache, MongoDB hoặc File System thay vì PostgreSQL. | Không tạo Repository cho các bảng con (dependent entities). Chỉ tạo cho **Aggregate Root**. |

> **Quy tắc vàng cho dự án PanelForge:**
> - Với **Marten (Document DB)**: **Bắt buộc dùng Repository** (ví dụ: `IPageRepository`, `IScriptCanvasRepository`).
> - Với **PostgreSQL (EF Core)**: Sử dụng **`IPanelForgeDbContext`** thông qua CQRS Handlers. Chỉ tạo Repository chuyên biệt nếu Aggregate đó có các logic nạp dữ liệu quá phức tạp lặp lại trên 3 nơi.

---

### 2.2. Khi nào tạo Service? Phân loại 3 nhóm Service

Trong kiến trúc CQRS + MediatR:
> **Mỗi `IRequestHandler<TCommand, TResponse>` chính là một Single-Purpose Application Service.**  
> Vì vậy, **hạn chế tạo ra các "God Service"** như `UserService`, `WorkspaceService` chứa 20-30 phương thức CRUD vì nó triệt tiêu lợi ích của CQRS.

Chỉ tạo file Service khi thuộc 3 trường hợp sau:

#### A. Domain Service (Nằm trong `PanelForge.Domain/Services`)
- **Khi nào tạo:** Khi một quy tắc nghiệp vụ liên quan đến **nhiều Entity khác nhau** mà không thể đặt logic vào một Entity đơn lẻ.
- **Đặc điểm:** Thuần C#, không gọi Database, không gọi API bên ngoài.
- **Ví dụ:** `ChapterReleaseValidationService` (kiểm tra toàn bộ điều kiện về Script, Canvas, Annotation trước khi Producer duyệt phát hành Chapter).

#### B. Application Service (Nằm trong `PanelForge.Application/Common/Interfaces`)
- **Khi nào tạo:** Khi cần một service điều phối dùng chung cho nhiều Handler hoặc luồng kỹ thuật đặc thù (Cross-cutting / Orchestration).
- **Ví dụ:**
  - `ICurrentUserService`: Lấy thông tin user hiện tại từ HttpContext/JWT.
  - `IWorkspaceAuthorizationService`: Kiểm tra quyền hạn của user trong một workspace cụ thể.

#### C. Infrastructure Service (Nằm trong `PanelForge.Infrastructure/Services`)
- **Khi nào tạo:** Khi giao tiếp với các hệ thống/dịch vụ **bên thứ 3** hoặc kỹ thuật hạ tầng.
- **Ví dụ:**
  - `IEmailService` -> Hiện thực: `SmtpEmailService`
  - `IFirebaseAuthService` -> Hiện thực: `FirebaseAuthService`
  - `IAiProviderService` -> Hiện thực: `OpenAiService`, `ClaudeAiService`
  - `IStorageService` -> Lưu file ảnh/PSD lên AWS S3 / Cloudflare R2 / Local Disk.

---

## 3. Bản Đồ Cấu Trúc Thư Mục Chuẩn (Folder Structure)

Dưới đây là cấu trúc thư mục được tinh chỉnh đồng bộ cho toàn bộ 4 project.

### 3.1. `src/PanelForge.Domain`

```
PanelForge.Domain/
├── Common/
│   ├── BaseEntity.cs                 # Entity base (Id, CreatedAt, UpdatedAt)
│   ├── BaseAuditableEntity.cs        # Có thêm CreatedBy, UpdatedBy
│   ├── IDomainEvent.cs               # Marker interface cho Domain Event
│   └── ValueObject.cs                # Base class cho Value Objects
├── Entities/
│   ├── Auth/                         # Nhóm bảng Tài khoản & Phân quyền
│   │   ├── User.cs
│   │   ├── StudioWorkspace.cs
│   │   └── WorkspaceMember.cs
│   └── Content/                      # Nhóm bảng Nội dung truyện
│       ├── Series.cs
│       ├── Chapter.cs
│       └── Scene.cs
├── Enums/
│   ├── SystemRole.cs                 # Admin, Moderator, User
│   ├── WorkspaceRole.cs              # Producer, Writer, Penciler, ...
│   ├── PipelineStage.cs              # Script, Thumbnail, Drawing, ...
│   └── ChapterStatus.cs              # Draft, InReview, Approved, Published
├── Events/                           # Sự kiện nghiệp vụ phát sinh
│   ├── ChapterApprovedDomainEvent.cs
│   └── MemberInvitedDomainEvent.cs
└── Exceptions/                       # Lỗi nghiệp vụ Domain
    └── DomainException.cs
```

---

### 3.2. `src/PanelForge.Application` (Tổ chức theo Feature-Slice CQRS)

Gom nhóm code theo **Tính năng (Feature)** thay vì chia vụn ra khắp nơi. Mỗi feature có đầy đủ Commands, Queries, DTOs, Validators riêng.

```
PanelForge.Application/
├── Common/                           # Dùng chung cho toàn Application
│   ├── Behaviors/                    # MediatR Pipeline Behaviors
│   │   ├── LoggingBehavior.cs
│   │   ├── ValidationBehavior.cs
│   │   └── PerformanceBehavior.cs
│   ├── Exceptions/                   # ValidationException, NotFoundException,...
│   ├── Interfaces/                   # Interface dịch vụ kỹ thuật/hạ tầng
│   │   ├── Persistence/
│   │   │   ├── IPanelForgeDbContext.cs
│   │   │   └── IPageRepository.cs     # Repository cho Marten
│   │   ├── Services/
│   │   │   ├── ICurrentUserService.cs
│   │   │   ├── IEmailService.cs
│   │   │   ├── IStorageService.cs
│   │   │   └── IAiProviderService.cs
│   │   └── Security/
│   │       ├── IJwtTokenGenerator.cs
│   │       └── IPasswordHasher.cs
│   └── Models/                       # PagedResult<T>, Result<T>
├── Features/                         # MỌI USE-CASE ĐỀU NẰM Ở ĐÂY
│   ├── Auth/                         # Chức năng Auth
│   │   ├── Commands/
│   │   │   ├── Login/
│   │   │   │   ├── LoginCommand.cs
│   │   │   │   ├── LoginCommandHandler.cs
│   │   │   │   ├── LoginCommandValidator.cs
│   │   │   │   └── LoginResponse.cs
│   │   │   └── Register/
│   │   └── Queries/
│   ├── Users/                        # Quản lý User (Profile, Avatar, Roles)
│   │   ├── Commands/
│   │   │   ├── UpdateProfile/
│   │   │   └── SoftDeleteUser/
│   │   └── Queries/
│   │       ├── GetUserProfile/
│   │       └── GetUsersList/
│   ├── Workspaces/                   # Quản lý Studio Workspace & Thành viên
│   │   ├── Commands/
│   │   │   ├── CreateWorkspace/
│   │   │   ├── AddMember/
│   │   │   └── UpdateMemberRole/
│   │   └── Queries/
│   │       ├── GetWorkspaceById/
│   │       └── GetMyWorkspaces/
│   ├── Scripts/                      # Tính năng Viết kịch bản (Writer)
│   │   ├── Commands/
│   │   └── Queries/
│   └── Canvas/                       # Biên tập trang/panel truyện (Artist, Letterer)
│       ├── Commands/
│       │   ├── SavePageLayout/
│       │   └── AddCanvasElement/
│       └── Queries/
│           └── GetPageById/
└── DependencyInjection.cs            # Đăng ký MediatR, FluentValidation
```

---

### 3.3. `src/PanelForge.Infrastructure`

```
PanelForge.Infrastructure/
├── Authentication/                   # Hiện thực JWT, BCrypt, Claims
│   ├── JwtTokenGenerator.cs
│   └── PasswordHasher.cs
├── ExternalServices/                 # Các dịch vụ bên ngoài
│   ├── Ai/                           # Xử lý kết nối OpenAI / Claude / Local AI
│   │   ├── OpenAiProvider.cs
│   │   └── AiProviderFactory.cs
│   ├── Email/
│   │   └── SmtpEmailService.cs
│   ├── Firebase/
│   │   └── FirebaseAuthService.cs
│   └── Storage/
│       └── LocalFileStorageService.cs # hoặc S3StorageService
├── Persistence/                      # Lưu trữ dữ liệu
│   ├── Configurations/               # Fluent API EF Core
│   │   ├── Auth/
│   │   │   ├── UserConfiguration.cs
│   │   │   ├── StudioWorkspaceConfiguration.cs
│   │   │   └── WorkspaceMemberConfiguration.cs
│   │   └── Content/
│   ├── Migrations/                   # EF Core Migrations
│   ├── PanelForgeDbContext.cs        # Hiện thực IPanelForgeDbContext
│   └── Repositories/                 # Các Repository thực tế (Marten)
│       └── MartenPageRepository.cs
├── Services/                         # Dịch vụ nội bộ hạ tầng
│   └── CurrentUserService.cs
└── DependencyInjection.cs            # Đăng ký Service, DbContext, Marten vào DI
```

---

### 3.4. `src/PanelForge.API`

```
PanelForge.API/
├── Common/
│   ├── Attributes/                   # [RequireWorkspaceRole], [AuthorizeRole]
│   └── Middlewares/                  # ExceptionHandlingMiddleware
├── Controllers/
│   ├── ApiControllerBase.cs          # Base controller chứa Mediator helper
│   ├── AuthController.cs
│   ├── UsersController.cs
│   ├── WorkspacesController.cs
│   ├── ScriptsController.cs
│   └── CanvasController.cs
├── Program.cs
├── appsettings.json
└── appsettings.Development.json
```

---

## 4. Các Design Pattern Cần Áp Dụng Trong PanelForge

### 4.1. Pattern 1: Mediator Pattern & CQRS (Command Query Responsibility Segregation)
- **Mục tiêu:** Phân tách hoàn toàn thao tác Ghi (Command - làm thay đổi dữ liệu) và thao tác Đọc (Query - chỉ lấy dữ liệu).
- **Cách áp dụng:**
  - **Command:** `CreatePageCommand : IRequest<Guid>` -> `CreatePageCommandHandler`
  - **Query:** `GetPageDetailQuery : IRequest<PageDetailDto>` -> `GetPageDetailQueryHandler`
  - **Controller:** Rất mỏng, chỉ nhận HTTP request, đẩy vào Mediator và trả HTTP Response.

```csharp
[ApiController]
[Route("api/workspaces/{workspaceId}/pages")]
public class PagesController : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(Guid workspaceId, [FromBody] CreatePageRequest request)
    {
        var command = new CreatePageCommand(workspaceId, request.ChapterId, request.PageNumber);
        var pageId = await Mediator.Send(command);
        return CreatedAtAction(nameof(GetById), new { workspaceId, pageId }, pageId);
    }
}
```

---

### 4.2. Pattern 2: Pipeline Behavior Pattern (Decorator / Interceptor)
- **Mục tiêu:** Tự động thực thi các logic chung (Validation, Logging, đo Performance, Transaction) trước khi request đi vào Handler chính.
- **Cách áp dụng:** Triển khai qua `IPipelineBehavior<TRequest, TResponse>` của MediatR.
- **Thực thi:**
  1. `LoggingBehavior`: Log mọi command chạy với tham số gì, ai gọi.
  2. `ValidationBehavior`: Tự động quét `FluentValidation`, nếu request vi phạm data rules thì văng `ValidationException` ngay, Handler không bị dính mã kiểm tra null/empty lặp đi lặp lại.

---

### 4.3. Pattern 3: Strategy & Factory Pattern (Cho Module AI Provider)
- **Bài toán:** Yêu cầu đồ án đặt ra: *“Configure AI provider credentials, model selection and usage quota per workspace”*.
- **Thiết kế:**
  - Định nghĩa interface chiến lược: `IAiService`
  - Tạo các triển khai cụ thể: `OpenAiService`, `AnthropicClaudeService`, `LocalLlamaService`.
  - Dùng **`AiProviderFactory`** để tạo đúng provider dựa trên cấu hình của từng Workspace được lưu trong Database.

```csharp
// 1. Strategy Interface
public interface IAiService
{
    string ProviderName { get; }
    Task<ScriptBreakdownResult> BreakdownScriptAsync(string scriptContent, string model);
    Task<LayoutSuggestionResult> SuggestLayoutAsync(string sceneDescription, string model);
}

// 2. Concrete Strategies
public class OpenAiService : IAiService { ... }
public class ClaudeAiService : IAiService { ... }

// 3. Factory
public interface IAiServiceFactory
{
    IAiService GetProvider(string providerName);
}
```

---

### 4.4. Pattern 4: Repository Pattern kết hợp Document Store (Marten)
- **Bài toán:** Bảng vẽ canvas manga, panel grid, tọa độ bóng thoại, script breakdown là dạng dữ liệu cây lồng nhau phức tạp (Semi-structured). Lưu dạng Document JSON trong PostgreSQL bằng Marten hiệu quả hơn nhiều so với việc chia nhỏ ra hàng chục bảng SQL.
- **Thiết kế:** Tạo `IPageRepository` trong `Application` và hiện thực `MartenPageRepository` trong `Infrastructure`.

```csharp
// PanelForge.Application/Common/Interfaces/Persistence/IPageRepository.cs
public interface IPageRepository
{
    Task<Page?> GetByIdAsync(Guid pageId, CancellationToken cancellationToken = default);
    Task SaveAsync(Page page, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid pageId, CancellationToken cancellationToken = default);
}
```

---

### 4.5. Pattern 5: Specification Pattern (Tùy chọn cho Search/Filter)
- **Bài toán:** Tìm kiếm và lọc nâng cao trên danh sách Task của Artist, kịch bản của Writer, hoặc Audit Log của Admin với hàng chục filter động kết hợp (theo Date, Status, Assignee, Workspace, Tag).
- **Thiết kế:** Dùng `Specification<T>` để đóng gói điều kiện `Expression<Func<T, bool>>`, giúp tái sử dụng và kết hợp nhiều điều kiện `And()`, `Or()` sạch sẽ.

---

## 5. Bảng Quy Tắc Đặt Tên & Hướng Dẫn Thao Tác (Cheatsheet)

Khi chuẩn bị code một tính năng mới, hãy tra cứu bảng này:

| Muốn làm gì? | Tạo file ở đâu? | Quy tắc đặt tên file | Kế thừa / Triển khai |
| :--- | :--- | :--- | :--- |
| Thêm bảng mới vào Database | `Domain/Entities/{Module}/` | `[EntityName].cs` (vd: `Series.cs`) | `BaseEntity` |
| Thêm kiểu phân loại enum | `Domain/Enums/` | `[EnumName].cs` (vd: `PipelineStage.cs`) | `enum` |
| Thêm hành động Ghi dữ liệu (Create/Update/Delete) | `Application/Features/{Feature}/Commands/{Action}/` | `[Action]Command.cs`<br>`[Action]CommandHandler.cs`<br>`[Action]CommandValidator.cs` | `IRequest<T>`,<br>`IRequestHandler<TCmd, TRes>`,<br>`AbstractValidator<T>` |
| Thêm hành động Đọc dữ liệu (Get/Filter/Export) | `Application/Features/{Feature}/Queries/{Action}/` | `[Action]Query.cs`<br>`[Action]QueryHandler.cs`<br>`[Action]Response.cs` | `IRequest<T>`,<br>`IRequestHandler<TQuery, TRes>` |
| Thêm bảng Document JSON (Marten) | `Application/Common/Interfaces/Persistence/` | `I[Entity]Repository.cs` | Interface |
| Hiện thực lưu trữ Marten | `Infrastructure/Persistence/Repositories/` | `Marten[Entity]Repository.cs` | `I[Entity]Repository` |
| Gọi API bên ngoài (AI, Mail, Cloud) | `Application/Common/Interfaces/Services/`<br>và `Infrastructure/ExternalServices/{Service}/` | `I[Provider]Service.cs`<br>`[Provider]Service.cs` | Interface & Implementation |
| Endpoint API mới | `API/Controllers/` | `[Feature]Controller.cs` | `ApiControllerBase` (gọi Mediator) |

---

## 6. Lộ Trình Tái Cấu Trúc Đề Xuất (Action Plan)

Để dự án của bạn trở nên gọn gàng, bạn nên tiến hành từng bước sau:

1. **Bước 1 — Gom nhóm Features trong `Application`:**
   - Di chuyển thư mục `src/PanelForge.Application/Users/` vào `src/PanelForge.Application/Features/Users/`.
   - Chuyển `IWorkspaceService` và `IAuthService` thành các CQRS Commands/Queries trong `Features/Workspaces` và `Features/Auth` (hoặc giữ lại tạm nếu chưa muốn sửa API, nhưng quy hoạch lại interface vào `Common/Interfaces`).
2. **Bước 2 — Chuẩn hóa các Controllers trong `API`:**
   - Tạo `ApiControllerBase` có sẵn property `ISender Mediator => ...`.
   - Chuyển `AdminController` từ việc gọi trực tiếp `IPanelForgeDbContext` sang gửi `GetAllUsersQuery` qua MediatR.
3. **Bước 3 — Bổ sung Pipeline Behaviors:**
   - Đưa FluentValidation vào MediatR Pipeline Behavior để tự động validate dữ liệu đầu vào.
4. **Bước 4 — Triển khai AI Strategy & Factory:**
   - Chuẩn bị sẵn abstraction `IAiService` và `AiProviderFactory` khi bắt đầu làm tính năng script breakdown và panel layout.
