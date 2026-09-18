# Kiến Trúc Clean Architecture & Quy Ước Đặt Tên trong Dự Án PanelForge

> **Dự án**: PanelForge (AI-Assisted Manga Production Management Platform)  
> **Phiên bản tài liệu**: 1.0  
> **Ngày lập**: 2026-09-17  
> **Vị trí lưu trữ**: `docs/markdown/clean-architecture-structure.md`

---

## Mục Lục
1. [Tổng Quan Kiến Trúc (Architecture Overview)](#1-tổng-quan-kiến-trúc-architecture-overview)
2. [Cây Thư Mục Toàn Dự Án (Project Directory Tree)](#2-cây-thư-mục-toàn-dự-án-project-directory-tree)
3. [Phân Tích Chi Tiết Từng Tầng (Layer-by-Layer Breakdown)](#3-phân-tích-chi-tiết-từng-tầng-layer-by-layer-breakdown)
   - [3.1 Tầng Domain (PanelForge.Domain)](#31-tầng-domain-panelforgedomain)
   - [3.2 Tầng Application (PanelForge.Application)](#32-tầng-application-panelforgeapplication)
   - [3.3 Tầng Infrastructure (PanelForge.Infrastructure)](#33-tầng-infrastructure-panelforgeinfrastructure)
   - [3.4 Tầng Presentation / API (PanelForge.API)](#34-tầng-presentation--api-panelforgeapi)
   - [3.5 Tầng Test (PanelForge.UnitTests)](#35-tầng-test-panelforgeunittests)
4. [Bảng Tra Cứu Quy Ước Đặt Tên (Naming & Placement Conventions)](#4-bảng-tra-cứu-quy-ước-đặt-tên-naming--placement-conventions)
5. [Cơ Chế Lưu Trữ Lai: Event Sourcing + Relational (Hybrid Persistence)](#5-cơ-chế-lưu-trữ-lai-event-sourcing--relational-hybrid-persistence)
6. [Quy Trình Chuẩn Khi Code Thêm Tính Năng Mới (Step-by-Step Guide)](#6-quy-trình-chuẩn-khi-code-thêm-tính-năng-mới-step-by-step-guide)

---

## 1. Tổng Quan Kiến Trúc (Architecture Overview)

Dự án **PanelForge** được xây dựng dựa trên nền tảng **.NET 9** theo mô hình **Clean Architecture** (Onion / Hexagonal Architecture) kết hợp với **Domain-Driven Design (DDD)**, **CQRS (Command Query Responsibility Segregation)** và **Event Sourcing (Marten)**.

### 1.1 Nguyên tắc phụ thuộc (Dependency Rule)

Quy tắc cốt lõi: **Các tầng bên trong không bao giờ biết hoặc phụ thuộc vào các tầng bên ngoài.**

```text
       ┌──────────────────────────────────────────────────┐
       │                 PanelForge.API                   │  Presentation / Entry Point
       └─────────────────────────┬────────────────────────┘
                                 │
                                 ↓
       ┌──────────────────────────────────────────────────┐
       │             PanelForge.Application               │  Use Cases, CQRS, Interfaces
       └──────────────────┬───────────────────────────────┘
                          │                      ↑
                          ↓                      │ Implements Interfaces
       ┌──────────────────────────────┐          │
       │      PanelForge.Domain       │          │
       │ (Entities, Aggregates, Enums)│          │
       └──────────────────────────────┘          │
                          ↑                      │
                          │                      │
       ┌──────────────────┴──────────────────────┴────────┐
       │             PanelForge.Infrastructure            │  Database, EF Core, Marten,
       │                                                  │  Firebase, BCrypt, JWT, Mail
       └──────────────────────────────────────────────────┘
```

- **Domain**: Độc lập tuyệt đối, không reference bất kỳ project nào hay thư viện bên ngoài (trừ C# Standard Library).
- **Application**: Chỉ phụ thuộc vào `Domain` và thư viện xử lý CQRS/Validation (`MediatR`). Định nghĩa các Contract/Interface để tầng ngoài cài đặt.
- **Infrastructure**: Phụ thuộc vào `Application` và `Domain`. Hiện thực hóa tất cả các Interface (EF Core DbContext, Marten Event Store, Firebase Auth, BCrypt, Email, JWT).
- **API**: Entry Point của Web API, phụ thuộc vào `Application` và `Infrastructure` để cấu hình Dependency Injection (DI), Middleware, Controllers.

---

## 2. Cây Thư Mục Toàn Dự Án (Project Directory Tree)

Cấu trúc thực tế đang áp dụng trong repository:

```text
PanelForge_FA26SE251/
├── docs/                                 # Tài liệu dự án
│   ├── database/
│   ├── markdown/                         # Chứa tài liệu cấu trúc & hướng dẫn
│   │   └── clean-architecture-structure.md
│   ├── implementation-qa.md
│   ├── project-plan.md
│   └── requirements.md
├── src/
│   ├── PanelForge.Domain/                # LAYER 1: Core Domain & Business Rules
│   │   ├── Common/                       # Base classes (AggregateRoot, BaseEntity, IDomainEvent)
│   │   ├── Entities/                     # Rich Domain Entities
│   │   │   ├── Auth/                     # User, StudioWorkspace, WorkspaceMember, ExternalPreviewLink
│   │   │   └── Content/                  # Series, Chapter, Scene, ScriptLine, Page, Panel, Element
│   │   ├── Enums/                        # Các kiểu liệt kê của hệ thống
│   │   └── Events/                       # Domain Events (cho Event Sourcing & Notifications)
│   │
│   ├── PanelForge.Application/           # LAYER 2: Business Logic & Use Cases
│   │   ├── Common/                       # Shared Result Pattern & Interfaces
│   │   │   ├── Interfaces/
│   │   │   │   ├── Authentication/       # IJwtTokenGenerator, IPasswordHasher
│   │   │   │   ├── Persistence/          # IPanelForgeDbContext, IPageRepository
│   │   │   │   └── Services/             # IAuthService, IEmailService, IWorkspaceService, ...
│   │   │   └── Result.cs                 # Result<T> pattern cho xử lý lỗi business
│   │   ├── Features/                     # Vertical Slice theo từng Module/Feature
│   │   │   ├── Auth/                     # Models DTOs (Login, Register, 2FA, Password...)
│   │   │   ├── Elements/                 # CQRS (AddElement, MoveElement, RemoveElement, GetPageSnapshot)
│   │   │   ├── Pages/                    # CQRS (CreatePage, DeletePage, GetPageById, GetPagesByChapter, UpdateCanvas)
│   │   │   ├── Users/                    # CQRS (Commands: SoftDelete, UpdateProfile; Queries: GetUserProfile; Models)
│   │   │   └── Workspaces/               # Models DTOs (CreateWorkspace, UpdateRole, WorkspaceDto...)
│   │   └── DependencyInjection.cs        # Đăng ký MediatR cho toàn bộ Application
│   │
│   ├── PanelForge.Infrastructure/        # LAYER 3: External Services & Data Persistence
│   │   ├── Authentication/               # JwtTokenGenerator, BcryptPasswordHasher, JwtSettings
│   │   ├── ExternalServices/             # Dịch vụ ngoài (Firebase, Email, Workspace Authorization)
│   │   ├── Persistence/                  # Cơ sở dữ liệu
│   │   │   ├── Configurations/           # EF Core Fluent API Configurations (Auth, Content)
│   │   │   ├── Migrations/               # EF Core Code-First Migrations
│   │   │   ├── Repositories/             # MartenPageRepository (Event Sourcing)
│   │   │   ├── PanelForgeDbContext.cs    # EF Core DbContext chính
│   │   │   └── PanelForgeDbContextFactory.cs
│   │   ├── Services/                     # Cài đặt AuthService, WorkspaceService
│   │   └── DependencyInjection.cs        # Đăng ký DbContext, Marten, Services, JWT vào DI container
│   │
│   └── PanelForge.API/                   # LAYER 4: Presentation & Web API Controllers
│       ├── Common/
│       │   └── Attributes/               # Custom Attributes & Action Filters (RequireWorkspaceRoleAttribute)
│       ├── Controllers/                  # REST API Controllers (AuthController, PagesController, ...)
│       ├── appsettings.json              # Cấu hình Database, JWT, Firebase, Logging
│       └── Program.cs                    # Entry Point: Middleware pipeline, Swagger, DI, Auth
│
└── test/
    └── PanelForge.UnitTests/             # Unit Tests
        ├── Common/                       # Mocks & Test Helpers (DbSetMockHelper, TestAsyncQueryProvider)
        ├── Domain/                       # Unit Test cho Domain Aggregates & Entities
        └── Infrastructure/               # Unit Test cho Services & Auth
```

---

## 3. Phân Tích Chi Tiết Từng Tầng (Layer-by-Layer Breakdown)

### 3.1 Tầng Domain (`PanelForge.Domain`)

Tầng trung tâm chứa toàn bộ quy tắc nghiệp vụ cốt lõi, không chứa bất kỳ logic giao diện, database hay third-party SDK nào.

#### Thư mục và File:
1. **`Common/`**:
   - `BaseEntity.cs`: Chứa thuộc tính định danh cơ bản `public Guid Id { get; protected set; }`. Mọi Entity thông thường đều kế thừa class này.
   - `AggregateRoot.cs`: Base class cho mô hình **Event Sourcing** (`Page`). Quản lý danh sách `_uncommittedEvents`, `Version` (cho Optimistic Concurrency), method `RaiseEvent(IDomainEvent)` và abstract method `Apply(IDomainEvent)`.
   - `IDomainEvent.cs`: Interface đánh dấu cho tất cả Domain Event (`EventId`, `OccurredAt`).

2. **`Entities/`**: Chia theo Bounded Context:
   - `Auth/`:
     - `User.cs`: Thực thể tài khoản, hỗ trợ mật khẩu băm, Firebase UID, Two-Factor Authentication (2FA), Email Verification.
     - `StudioWorkspace.cs`: Không gian làm việc của studio/nhóm sáng tác.
     - `WorkspaceMember.cs`: Thành viên trong workspace kèm quyền hạn (`WorkspaceRole`).
     - `ExternalPreviewLink.cs`: Link xem trước dành cho đối tác/khách hàng bên ngoài.
   - `Content/`:
     - Phân cấp nội dung Manga:
       `Series` ➔ `Chapter` ➔ `Scene` ➔ `ScriptLine` ➔ `Page` (AggregateRoot) ➔ `Panel` ➔ `Element`.
     - `ElementState.cs`: Giá trị trạng thái in-memory của Element trên Page khi replay Event Stream.

3. **`Enums/`**:
   - `ElementType.cs`: Kiểu phần tử (Dialogue, SFX, Balloon, ArtLayer, Narration...).
   - `LayoutFormat.cs`: Định dạng layout trang (StandardPage, WebtoonStrip...).
   - `ReadingDirection.cs`: Hướng đọc truyện (RightToLeft, LeftToRight, TopToBottom).
   - `SystemRole.cs`: Quyền hệ thống cấp cao (Admin, User).
   - `WorkspaceRole.cs`: Quyền trong Studio (Owner, Producer, Writer, Artist, Letterer, Editor, Viewer).

4. **`Events/`**:
   - Các sự kiện miền (Domain Events) bất biến phục vụ Event Sourcing: `PageCreatedEvent`, `PageCanvasUpdatedEvent`, `PageReorderedEvent`, `ElementAddedEvent`, `ElementMovedEvent`, `ElementRemovedEvent`.

> **Quy tắc thiết kế Domain**:
> - **Encapsulation**: Thuộc tính chỉ cho phép `private set` hoặc `protected set`.
> - **Static Factory Method**: Dùng `public static Entity Create(...)` để khởi tạo thực thể, thực hiện validation ngay lúc tạo thay vì dùng public constructor trống.
> - **Rich Domain Model**: Hành vi nghiệp vụ được viết trực tiếp bên trong Entity/Aggregate (ví dụ: `page.AddElement(...)`, `user.UpdateProfile(...)`) chứ không để Entity bị anemic (chỉ có getter/setter).

---

### 3.2 Tầng Application (`PanelForge.Application`)

Tầng điều phối luồng nghiệp vụ (Orchestration), hiện thực hóa các Use Case thông qua mẫu hình **CQRS** kết hợp thư viện **MediatR**.

#### Thư mục và File:
1. **`Common/`**:
   - `Result.cs`: Generic class `Result<T>` đóng gói kết quả trả về (`IsSuccess`, `Value`, `ErrorMessage`). Tránh lạm dụng throw Exception cho các lỗi nghiệp vụ dự đoán được.
   - `Interfaces/Persistence/`:
     - `IPanelForgeDbContext.cs`: Interface cho Entity Framework Core DbContext (để Application truy vấn dữ liệu mà không cần phụ thuộc trực tiếp vào package EF Core Npgsql).
     - `IPageRepository.cs`: Interface cho repository nạp/lưu Page Aggregate qua Marten Event Store.
   - `Interfaces/Authentication/`:
     - `IJwtTokenGenerator.cs`: Ký và sinh JWT token.
     - `IPasswordHasher.cs`: Băm và kiểm tra mật khẩu.
   - `Interfaces/Services/`:
     - `IAuthService.cs`, `IWorkspaceService.cs`, `IEmailService.cs`, `IWorkspaceAuthorizationService.cs`.

2. **`Features/`**: Tổ chức theo tính năng (Feature-based / Vertical Slice):
   - **`Auth/Models/`**: Các DTO request/response liên quan đến Auth (`LoginRequest`, `RegisterRequest`, `AuthResponse`, `ChangePasswordRequest`, `VerifyTwoFactorRequest`...).
   - **`Workspaces/Models/`**: Các DTO quản lý không gian làm việc (`CreateWorkspaceRequest`, `WorkspaceDto`, `AddWorkspaceMemberRequest`...).
   - **`Users/`**:
     - `Commands/`: Mỗi command đặt trong thư mục riêng:
       - `SoftDeleteUser/SoftDeleteUserCommand.cs` & `SoftDeleteUserCommandHandler.cs`
       - `UpdateUserProfile/UpdateUserProfileCommand.cs` & `UpdateUserProfileCommandHandler.cs`
     - `Queries/`:
       - `GetUserProfile/GetUserProfileQuery.cs` & `GetUserProfileQueryHandler.cs`
     - `Models/`: `UserProfileDto.cs`, `UpdateUserProfileRequest.cs`
   - **`Pages/`**:
     - `CreatePage/CreatePageCommand.cs` & `CreatePageCommandHandler.cs`
     - `DeletePage/DeletePageCommand.cs` & `DeletePageCommandHandler.cs`
     - `GetPageById/GetPageByIdQuery.cs` & `GetPageByIdQueryHandler.cs`
     - `GetPagesByChapter/GetPagesByChapterQuery.cs` & `GetPagesByChapterQueryHandler.cs`
     - `UpdateCanvasSettings/UpdateCanvasSettingsCommand.cs` & `UpdateCanvasSettingsCommandHandler.cs`
     - `Models/PageDto.cs`
   - **`Elements/`**:
     - `AddElement/`, `MoveElement/`, `RemoveElement/`, `GetPageSnapshot/` (Command và Handler tương ứng).

3. **`DependencyInjection.cs`**:
   - Sử dụng `services.AddMediatR(...)` để tự động quét toàn bộ Assembly và đăng ký các Handler.

---

### 3.3 Tầng Infrastructure (`PanelForge.Infrastructure`)

Tầng chịu trách nhiệm giao tiếp với hệ thống bên ngoài: Database (PostgreSQL qua EF Core & Marten), Firebase Admin SDK, BCrypt, SMTP Mail Server, JWT.

#### Thư mục và File:
1. **`Persistence/`**:
   - `PanelForgeDbContext.cs`: Kế thừa `DbContext` của EF Core, hiện thực hóa interface `IPanelForgeDbContext`.
   - `Configurations/`: Sử dụng Fluent API tách biệt từng Entity:
     - `Auth/`: `UserConfiguration.cs`, `StudioWorkspaceConfiguration.cs`, `WorkspaceMemberConfiguration.cs`, `ExternalPreviewLinkConfiguration.cs`.
     - `Content/`: `SeriesConfiguration.cs`, `ChapterConfiguration.cs`, `SceneConfiguration.cs`, `ScriptLineConfiguration.cs`, `PageConfiguration.cs`, `PanelConfiguration.cs`, `ElementConfiguration.cs`.
   - `Migrations/`: Toàn bộ các file migration sinh ra bởi EF Core.
   - `Repositories/`:
     - `MartenPageRepository.cs`: Hiện thực `IPageRepository` sử dụng **Marten** để quản lý stream sự kiện của Page Aggregate.

2. **`Authentication/`**:
   - `BcryptPasswordHasher.cs`: Hiện thực `IPasswordHasher` sử dụng thư viện `BCrypt.Net-Next`.
   - `JwtTokenGenerator.cs`: Tạo Bearer Token có chứa Claims (`sub`, `email`, `role`, `user_id`).
   - `JwtSettings.cs`: Model ánh xạ từ config `JwtSettings` trong `appsettings.json`.

3. **`ExternalServices/`**:
   - `Firebase/FirebaseServiceExtensions.cs`: Khởi tạo FirebaseApp từ file service account credential hoặc chuỗi config.
   - `Email/EmailService.cs`: Gửi email xác thực tài khoản và đổi mật khẩu qua MailKit/SMTP.
   - `Authorization/WorkspaceAuthorizationService.cs`: Kiểm tra phân quyền thành viên trong Studio Workspace.

4. **`Services/`**:
   - `AuthService.cs`: Triển khai các luồng đăng ký, đăng nhập, quên mật khẩu, kích hoạt 2FA.
   - `WorkspaceService.cs`: Triển khai tạo workspace, mời thành viên, cập nhật role.

5. **`DependencyInjection.cs`**:
   - Extension method `AddInfrastructure(this IServiceCollection services, IConfiguration configuration)` đăng ký tập trung tất cả dịch vụ tầng này.

---

### 3.4 Tầng Presentation / API (`PanelForge.API`)

Entry Point của toàn ứng dụng, tiếp nhận request HTTP, kiểm tra Authorization và ủy quyền xử lý cho MediatR / Application Services.

#### Thư mục và File:
1. **`Controllers/`**:
   - `AuthController.cs`: Tuyến `/api/auth` (register, login, logout, 2fa, forgot-password, verify-email).
   - `UsersController.cs`: Tuyến `/api/users` (lấy profile me, cập nhật hồ sơ, xóa mềm tài khoản).
   - `WorkspacesController.cs`: Tuyến `/api/workspaces` (quản lý workspace và thành viên).
   - `PagesController.cs`: Tuyến `/api/chapters/{chapterId}/pages` và `/api/pages/{pageId}` (gọi MediatR).
   - `ElementsController.cs`: Tuyến thao tác trực tiếp trên canvas manga (thêm, di chuyển, xóa element).
   - `AdminController.cs`: Tuyến dành riêng cho Quản trị viên hệ thống.

2. **`Common/Attributes/`**:
   - `RequireWorkspaceRoleAttribute.cs`: Custom Action Filter cho phép giới hạn quyền hạn trong từng workspace cụ thể (kiểm tra `Owner`, `Producer`, `Artist`, ... theo `workspaceId` trên route).

3. **`Program.cs`**:
   - Gọi `builder.Services.AddApplication()` và `builder.Services.AddInfrastructure(builder.Configuration)`.
   - Cấu hình JWT Bearer Authentication (`AddJwtBearer`).
   - Cấu hình Swagger với SwaggerDoc v1 và SecurityDefinition Bearer Token.
   - Thiết lập Middleware: `UseSwagger`, `UseSwaggerUI`, `UseAuthentication`, `UseAuthorization`, `MapControllers`.

---

### 3.5 Tầng Test (`PanelForge.UnitTests`)

Nằm trong thư mục `test/PanelForge.UnitTests`:
- **`Common/`**: Chứa mock helper cho EF Core Async Query (`DbSetMockHelper.cs`, `TestAsyncQueryProvider.cs`).
- **`Domain/`**: Kiểm tra tính đúng đắn của logic Domain:
  - `PageAggregateTests.cs`: Kiểm tra Event Sourcing, RaiseEvent, Apply cho Element và Page.
  - `UserEntityTests.cs`, `ChapterAndSceneTests.cs`, `StudioWorkspaceTests.cs`.
- **`Infrastructure/`**:
  - `AuthServiceTests.cs`, `BcryptPasswordHasherTests.cs`, `WorkspaceAuthorizationServiceTests.cs`.

---

## 4. Bảng Tra Cứu Quy Ước Đặt Tên (Naming & Placement Conventions)

Bảng dưới đây là kim chỉ nam bắt buộc khi thêm bất kỳ class/file nào vào codebase:

| Loại Class / File | Vị trí thư mục (Directory Path) | Quy tắc đặt tên File & Class | Interface / Base Class kế thừa | Ví dụ thực tế trong dự án |
| :--- | :--- | :--- | :--- | :--- |
| **Domain Entity** | `src/PanelForge.Domain/Entities/<Context>/` | `<EntityName>.cs` (Danh từ số ít, PascalCase) | `BaseEntity` | `User.cs`, `Chapter.cs`, `Panel.cs` |
| **Aggregate Root** | `src/PanelForge.Domain/Entities/Content/` | `<AggregateName>.cs` | `AggregateRoot` | `Page.cs` |
| **Domain Event** | `src/PanelForge.Domain/Events/` | `<Entity><ActionDone>Event.cs` (Quá khứ) | `IDomainEvent` (record) | `PageCreatedEvent.cs`, `ElementAddedEvent.cs` |
| **Domain Enum** | `src/PanelForge.Domain/Enums/` | `<EnumName>.cs` (PascalCase, số ít) | `enum` | `ElementType.cs`, `WorkspaceRole.cs` |
| **Command (CQRS)** | `src/PanelForge.Application/Features/<Feature>/<Action>/` hoặc `.../Commands/<Action>/` | `<Verb><Noun>Command.cs` | `IRequest<Result<TResponse>>` (record) | `CreatePageCommand.cs`, `UpdateUserProfileCommand.cs` |
| **Command Handler** | Nằm chung thư mục với Command | `<Verb><Noun>CommandHandler.cs` | `IRequestHandler<TCommand, Result<TResponse>>` | `CreatePageCommandHandler.cs`, `UpdateUserProfileCommandHandler.cs` |
| **Query (CQRS)** | `src/PanelForge.Application/Features/<Feature>/<Action>/` hoặc `.../Queries/<Action>/` | `<Verb><Noun>Query.cs` | `IRequest<Result<TResponse>>` (record) | `GetPageByIdQuery.cs`, `GetUserProfileQuery.cs` |
| **Query Handler** | Nằm chung thư mục với Query | `<Verb><Noun>QueryHandler.cs` | `IRequestHandler<TQuery, Result<TResponse>>` | `GetPageByIdQueryHandler.cs`, `GetUserProfileQueryHandler.cs` |
| **Request DTO** | `src/PanelForge.Application/Features/<Feature>/Models/` | `<Action>Request.cs` hoặc `<Action>RequestBody.cs` | `record` hoặc `class` | `LoginRequest.cs`, `CreateWorkspaceRequest.cs` |
| **Response DTO** | `src/PanelForge.Application/Features/<Feature>/Models/` | `<Noun>Dto.cs` hoặc `<Action>Response.cs` | `record` hoặc `class` | `PageDto.cs`, `UserProfileDto.cs`, `AuthResponse.cs` |
| **Interface Persistence**| `src/PanelForge.Application/Common/Interfaces/Persistence/` | `I<Noun>Repository.cs` hoặc `I<Noun>DbContext.cs` | `interface` | `IPageRepository.cs`, `IPanelForgeDbContext.cs` |
| **Interface Service** | `src/PanelForge.Application/Common/Interfaces/Services/` | `I<Noun>Service.cs` | `interface` | `IAuthService.cs`, `IWorkspaceService.cs` |
| **EF Core Configuration**| `src/PanelForge.Infrastructure/Persistence/Configurations/<Context>/` | `<EntityName>Configuration.cs` | `IEntityTypeConfiguration<TEntity>` | `PageConfiguration.cs`, `UserConfiguration.cs` |
| **Repository Impl** | `src/PanelForge.Infrastructure/Persistence/Repositories/` | `<Tech><Entity>Repository.cs` | `I<Entity>Repository` | `MartenPageRepository.cs` |
| **Service Impl** | `src/PanelForge.Infrastructure/Services/` | `<Noun>Service.cs` | `I<Noun>Service` | `AuthService.cs`, `WorkspaceService.cs` |
| **API Controller** | `src/PanelForge.API/Controllers/` | `<PluralNoun>Controller.cs` | `ControllerBase`, `[ApiController]` | `PagesController.cs`, `UsersController.cs` |
| **API Attribute / Filter**| `src/PanelForge.API/Common/Attributes/` | `<Name>Attribute.cs` | `TypeFilterAttribute`, `ActionFilterAttribute` | `RequireWorkspaceRoleAttribute.cs` |
| **Unit Test Class** | `test/PanelForge.UnitTests/<Layer>/` | `<SubjectUnderTest>Tests.cs` | `class` kiểm thử `[Fact]`, `[Theory]` | `PageAggregateTests.cs`, `AuthServiceTests.cs` |

---

## 5. Cơ Chế Lưu Trữ Lai: Event Sourcing + Relational (Hybrid Persistence)

Một nét kiến trúc đặc trưng và nổi bật nhất của **PanelForge** là việc kết hợp đồng thời hai cơ chế lưu trữ:

### 5.1 Phân chia trách nhiệm lưu trữ

1. **Relational Data (EF Core + PostgreSQL)**:
   - Áp dụng cho: `User`, `StudioWorkspace`, `WorkspaceMember`, `ExternalPreviewLink`, `Series`, `Chapter`, `Scene`, `ScriptLine`.
   - Mục đích: Đảm bảo toàn vẹn dữ liệu quan hệ, hỗ trợ khóa ngoại (foreign key), cascade delete, và truy vấn quan hệ bảng nhiều cấp nhanh chóng.

2. **Event Sourced Data (Marten + PostgreSQL JSONB)**:
   - Áp dụng cho: `Page` và các `Element` trên trang vẽ Manga.
   - Mục đích:
     - Lưu trữ toàn bộ lịch sử biên tập của từng trang truyện (Ai đã thêm bóng thoại, ai di chuyển panel, lúc mấy giờ).
     - Hỗ trợ Undo / Redo vô hạn và Time Travel Debugging / Rollback.
     - Khả năng tái hiện trạng thái (replay stream) bất kỳ thời điểm nào trong quá khứ.

### 5.2 Luồng xử lý Dual-Write trong Command Handler

Minh họa luồng khi gọi `CreatePageCommandHandler`:

```text
HTTP POST /api/chapters/{id}/pages
           │
           ▼
     PagesController
           │  _mediator.Send(CreatePageCommand)
           ▼
 CreatePageCommandHandler
     ├── 1. Kiểm tra Chapter có tồn tại (truy vấn EF Core)
     ├── 2. Tính PageNumber tự động (truy vấn EF Core)
     ├── 3. Page.Create(...) 
     │       └── Sinh Domain Event: PageCreatedEvent
     ├── 4. [Write Model] _pageRepository.SaveAsync(page)
     │       └── Append PageCreatedEvent vào Marten stream (PostgreSQL table: mt_events)
     └── 5. [Read Model] _dbContext.Pages.Add(page) + SaveChangesAsync()
             └── Lưu bản ghi chiếu vào bảng relational "pages" để join nhanh
```

---

## 6. Quy Trình Chuẩn Khi Code Thêm Tính Năng Mới (Step-by-Step Guide)

Khi một Developer nhận task mới (ví dụ: phát triển tính năng quản lý Chapter hoặc phê duyệt bản vẽ):

### Bước 1: Định nghĩa Domain (`PanelForge.Domain`)
1. Tạo thực thể Entity trong `Entities/<Context>/<EntityName>.cs`:
   - Kế thừa `BaseEntity` hoặc `AggregateRoot`.
   - Đặt constructor rỗng `private EntityName() { }`.
   - Cung cấp `public static EntityName Create(...)` để kiểm tra validation.
   - Đặt tất cả setter là `private set`.
2. Định nghĩa các Enum liên quan trong `Enums/`.
3. Nếu tính năng thuộc phạm vi Event Sourcing, tạo Event tương ứng trong `Events/` (dùng C# `record` kế thừa `IDomainEvent`).

### Bước 2: Thiết kế Use Cases & CQRS (`PanelForge.Application`)
1. Tạo folder tính năng trong `Features/<FeatureName>/`.
2. Tạo DTOs trong `Features/<FeatureName>/Models/` (Request DTO, Response DTO).
3. Nếu là thao tác thay đổi dữ liệu (Create/Update/Delete):
   - Tạo `<Action>Command.cs` implementing `IRequest<Result<TResponse>>`.
   - Tạo `<Action>CommandHandler.cs` implementing `IRequestHandler<TCommand, Result<TResponse>>`.
4. Nếu là thao tác đọc dữ liệu:
   - Tạo `<Action>Query.cs` implementing `IRequest<Result<TResponse>>`.
   - Tạo `<Action>QueryHandler.cs` implementing `IRequestHandler<TQuery, Result<TResponse>>`.
5. Nếu cần tương tác dịch vụ ngoài, khai báo Interface tại `Common/Interfaces/`.

### Bước 3: Cấu hình Persistence & Dịch vụ ngoài (`PanelForge.Infrastructure`)
1. Nếu có Entity mới của EF Core:
   - Thêm `DbSet<EntityName>` vào `IPanelForgeDbContext` và `PanelForgeDbContext`.
   - Tạo file cấu hình `Configurations/<Context>/<EntityName>Configuration.cs` implementing `IEntityTypeConfiguration<T>`.
   - Chạy lệnh migration:
     ```bash
     dotnet ef migrations add Add<EntityName> --project src/PanelForge.Infrastructure --startup-project src/PanelForge.API
     ```
2. Nếu có Service/Repository mới:
   - Viết class hiện thực hóa interface trong `Services/` hoặc `Repositories/`.
   - Khai báo đăng ký DI trong `Infrastructure/DependencyInjection.cs`.

### Bước 4: Expose Endpoints (`PanelForge.API`)
1. Tạo hoặc bổ sung method trong Controller tương ứng tại `Controllers/<Name>Controller.cs`.
2. Sử dụng `[Authorize]` hoặc custom attribute `[RequireWorkspaceRole(...)]` nếu cần phân quyền.
3. Inject `IMediator` hoặc `ISender` để gửi Command/Query sang tầng Application.
4. Trả về mã HTTP chuẩn RESTful (`Ok(result.Value)`, `CreatedAtAction`, `BadRequest`, `NotFound`).

### Bước 5: Viết Unit Test (`PanelForge.UnitTests`)
1. Viết test cho phương thức khởi tạo và logic nghiệp vụ của Entity/Aggregate trong `Domain/`.
2. Viết test cho Command/Query Handler hoặc Service trong `Infrastructure/` / `Application/`.
3. Chạy kiểm thử:
   ```bash
   dotnet test
   ```

---
*Tài liệu được biên soạn và đồng bộ dựa trên cấu trúc thực tế của mã nguồn PanelForge.*
