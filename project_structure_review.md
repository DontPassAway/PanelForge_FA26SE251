# Báo Cáo Đánh Giá & Kế Hoạch Sắp Xếp Lại Cấu Trúc Toàn Bộ Dự Án PanelForge

> **Mục tiêu:** Rà soát toàn bộ file trong 4 project (`Domain`, `Application`, `Infrastructure`, `API`), chỉ ra các điểm bất cập, và cung cấp bản đồ ánh xạ chi tiết từng file từ vị trí cũ sang vị trí mới để bạn dễ dàng xem xét trước khi thực hiện.

---

## 1. Bảng Đánh Giá Hiện Trạng Của Từng Project

| Project | Hiện trạng thực tế | Vấn đề / Bất cập | Đánh giá & Hướng giải quyết |
| :--- | :--- | :--- | :--- |
| **PanelForge.Domain** | Đã phân chia khá tốt theo DDD (`Common`, `Entities/Auth`, `Entities/Content`, `Enums`, `Events`). | Thiếu folder `Exceptions` riêng cho Domain; thiếu các Entity/ValueObject cho Series Bible và Pipeline Stage. | **Tốt nhất trong 4 project.** Chỉ cần bổ sung `Exceptions/` và chuẩn hóa. |
| **PanelForge.Application** | Tồn tại **2 trường phái cấu trúc song song**: vừa có `Features/` (Elements, Pages), vừa có `Users/` ở root; vừa có `Services/` (`IAuthService`), vừa có `Interfaces/`; DTOs bị nhét hết vào 1 folder chung `DTOs/`. | 1. `Users` nằm ngoài `Features` làm cấu trúc bị lệch.<br>2. Interface bị phân tán ở 3 chỗ khác nhau.<br>3. `Workspaces` và `Auth` đang dùng Service cũ thay vì CQRS Commands/Queries như `Pages` và `Users`. | **Cần chuẩn hóa mạnh nhất.** Gom tất cả use-case vào `Features/`, chuyển DTOs về từng Feature tương ứng để code gom cụm (Cohesion), chuẩn hóa `Common/Interfaces`. |
| **PanelForge.Infrastructure** | Chứa `Persistence`, `Authentication`, `Firebase`, `Repositories`, `Services`. | Thư mục `Services/` đang chứa lẫn lộn giữa dịch vụ hạ tầng (`EmailService`) và dịch vụ nghiệp vụ (`AuthService`, `WorkspaceService`). | Tách `ExternalServices/` (Email, AI, Firebase, Storage) khỏi `Persistence/`. |
| **PanelForge.API** | Có 7 Controller: `Admin`, `Auth`, `Elements`, `Pages`, `Test`, `Users`, `Workspaces`. | 1. `AdminController` inject thẳng `IPanelForgeDbContext` (vi phạm Clean Architecture).<br>2. Cách viết Controller không đồng nhất: cái gọi Mediator, cái gọi Service, cái gọi DbContext.<br>3. Thiếu Global Exception Middleware chuẩn hóa lỗi trả về JSON. | Chuẩn hóa toàn bộ Controller kế thừa `ApiControllerBase` và gọi qua MediatR (`ISender`). Bổ sung `ExceptionHandlingMiddleware`. |

---

## 2. Cây Thư Mục Mục Tiêu (Target Folder Structure)

Dưới đây là sơ đồ cấu trúc hoàn chỉnh của cả 4 project sau khi được quy hoạch lại:

```
src/
├── PanelForge.Domain/
│   ├── Common/
│   │   ├── AggregateRoot.cs
│   │   ├── BaseEntity.cs
│   │   └── IDomainEvent.cs
│   ├── Entities/
│   │   ├── Auth/
│   │   │   ├── ExternalPreviewLink.cs
│   │   │   ├── StudioWorkspace.cs
│   │   │   ├── User.cs
│   │   │   └── WorkspaceMember.cs
│   │   └── Content/
│   │       ├── Chapter.cs
│   │       ├── Element.cs
│   │       ├── ElementState.cs
│   │       ├── Page.cs
│   │       ├── Panel.cs
│   │       ├── Scene.cs
│   │       ├── ScriptLine.cs
│   │       └── Series.cs
│   ├── Enums/
│   │   ├── ElementType.cs
│   │   ├── LayoutFormat.cs
│   │   ├── ReadingDirection.cs
│   │   ├── SystemRole.cs
│   │   └── WorkspaceRole.cs
│   ├── Events/
│   │   ├── ElementAddedEvent.cs
│   │   ├── ElementMovedEvent.cs
│   │   ├── ElementRemovedEvent.cs
│   │   ├── PageCanvasUpdatedEvent.cs
│   │   ├── PageCreatedEvent.cs
│   │   └── PageReorderedEvent.cs
│   └── Exceptions/
│       └── DomainException.cs                   [MỚI]
│
├── PanelForge.Application/
│   ├── Common/
│   │   ├── Behaviors/                           [MỚI]
│   │   │   ├── LoggingBehavior.cs
│   │   │   └── ValidationBehavior.cs
│   │   ├── Exceptions/                          [MỚI]
│   │   │   ├── NotFoundException.cs
│   │   │   ├── UnauthorizedException.cs
│   │   │   └── ValidationException.cs
│   │   ├── Interfaces/
│   │   │   ├── Authentication/
│   │   │   │   ├── IJwtTokenGenerator.cs
│   │   │   │   └── IPasswordHasher.cs
│   │   │   ├── Persistence/
│   │   │   │   ├── IPageRepository.cs
│   │   │   │   └── IPanelForgeDbContext.cs
│   │   │   └── Services/
│   │   │       ├── IEmailService.cs
│   │   │       └── IWorkspaceAuthorizationService.cs
│   │   ├── Models/                              [MỚI]
│   │   │   └── PagedResult.cs
│   │   └── Result.cs
│   │
│   ├── Features/                                [TẤT CẢ USE-CASE TẬP TRUNG TẠI ĐÂY]
│   │   ├── Auth/                                [QUY HOẠCH LẠI TỪ IAuthService]
│   │   │   ├── Commands/
│   │   │   │   ├── Login/
│   │   │   │   ├── Register/
│   │   │   │   ├── VerifyEmail/
│   │   │   │   ├── ForgotPassword/
│   │   │   │   └── EnableTwoFactor/
│   │   │   └── Models/ (Chứa DTOs của Auth)
│   │   │       ├── AuthResponse.cs
│   │   │       └── UserDto.cs
│   │   │
│   │   ├── Users/                               [DI CHUYỂN TỪ Application/Users VÀO]
│   │   │   ├── Commands/
│   │   │   │   ├── SoftDeleteUser/
│   │   │   │   └── UpdateUserProfile/
│   │   │   ├── Queries/
│   │   │   │   ├── GetUserProfile/
│   │   │   │   └── GetAllUsers/                 [MỚI - THAY THẾ AdminController TRUY VẤN DB]
│   │   │   └── Models/
│   │   │       └── UserProfileDto.cs
│   │   │
│   │   ├── Workspaces/                          [QUY HOẠCH LẠI TỪ IWorkspaceService]
│   │   │   ├── Commands/
│   │   │   │   ├── CreateWorkspace/
│   │   │   │   ├── UpdateWorkspace/
│   │   │   │   ├── AddWorkspaceMember/
│   │   │   │   └── UpdateMemberRole/
│   │   │   ├── Queries/
│   │   │   │   ├── GetWorkspaceById/
│   │   │   │   └── GetMyWorkspaces/
│   │   │   └── Models/
│   │   │       ├── WorkspaceDto.cs
│   │   │       └── WorkspaceMemberDto.cs
│   │   │
│   │   ├── Pages/                               [GIỮ NGUYÊN & ĐƯA PageDto VÀO]
│   │   │   ├── Commands/
│   │   │   ├── Queries/
│   │   │   └── Models/
│   │   │       └── PageDto.cs
│   │   │
│   │   └── Elements/                            [GIỮ NGUYÊN]
│   │       ├── AddElement/
│   │       ├── MoveElement/
│   │       └── RemoveElement/
│   │
│   ├── DependencyInjection.cs
│   └── PanelForge.Application.csproj
│
├── PanelForge.Infrastructure/
│   ├── Authentication/
│   │   ├── BcryptPasswordHasher.cs
│   │   ├── JwtSettings.cs
│   │   └── JwtTokenGenerator.cs
│   │
│   ├── ExternalServices/                       [GOM CÁC DỊCH VỤ BÊN THỨ 3]
│   │   ├── Email/
│   │   │   └── EmailService.cs
│   │   ├── Firebase/
│   │   │   └── FirebaseServiceExtensions.cs
│   │   └── Authorization/
│   │       └── WorkspaceAuthorizationService.cs
│   │
│   ├── Persistence/
│   │   ├── Configurations/
│   │   │   ├── Auth/
│   │   │   │   ├── ExternalPreviewLinkConfiguration.cs
│   │   │   │   ├── StudioWorkspaceConfiguration.cs
│   │   │   │   ├── UserConfiguration.cs
│   │   │   │   └── WorkspaceMemberConfiguration.cs
│   │   │   └── Content/
│   │   │       ├── ChapterConfiguration.cs
│   │   │       ├── ElementConfiguration.cs
│   │   │       ├── PageConfiguration.cs
│   │   │       ├── PanelConfiguration.cs
│   │   │       ├── SceneConfiguration.cs
│   │   │       ├── ScriptLineConfiguration.cs
│   │   │       └── SeriesConfiguration.cs
│   │   ├── Migrations/
│   │   ├── Repositories/
│   │   │   └── MartenPageRepository.cs          [CHUYÊN BIỆT CHO MARTEN DOCUMENT DB]
│   │   ├── PanelForgeDbContext.cs
│   │   └── PanelForgeDbContextFactory.cs
│   │
│   ├── DependencyInjection.cs
│   └── PanelForge.Infrastructure.csproj
│
└── PanelForge.API/
    ├── Common/
    │   ├── Attributes/
    │   │   └── RequireWorkspaceRoleAttribute.cs
    │   └── Middlewares/                         [MỚI]
    │       └── ExceptionHandlingMiddleware.cs
    │
    ├── Controllers/
    │   ├── ApiControllerBase.cs                 [MỚI - CHỨA Mediator HELPER]
    │   ├── AdminController.cs                   [REFACTOR - CHUYỂN QUA DÙNG Mediator]
    │   ├── AuthController.cs
    │   ├── ElementsController.cs
    │   ├── PagesController.cs
    │   ├── UsersController.cs
    │   └── WorkspacesController.cs
    │
    ├── Program.cs
    ├── appsettings.json
    └── appsettings.Development.json
```

---

## 3. Bảng Ánh Xạ Di Chuyển Chi Tiết (Migration Mapping Table)

Dưới đây là vị trí cụ thể của từng file hiện tại và nơi cần sắp xếp lại:

### 3.1. Các file trong `PanelForge.Application`

| Đường dẫn hiện tại | Đường dẫn đề xuất mới | Lý do thay đổi |
| :--- | :--- | :--- |
| `src/PanelForge.Application/Users/Commands/*` | `src/PanelForge.Application/Features/Users/Commands/*` | Gom toàn bộ tính năng về một mối trong `Features/`. |
| `src/PanelForge.Application/Users/Queries/*` | `src/PanelForge.Application/Features/Users/Queries/*` | Đồng nhất với `Features/Pages`, `Features/Elements`. |
| `src/PanelForge.Application/DTOs/Users/*` | `src/PanelForge.Application/Features/Users/Models/*` | DTO thuộc tính năng nào thì đi kèm tính năng đó. |
| `src/PanelForge.Application/DTOs/Workspaces/*` | `src/PanelForge.Application/Features/Workspaces/Models/*` | Giúp developer mở 1 folder là thấy toàn bộ request/response liên quan. |
| `src/PanelForge.Application/DTOs/Content/PageDto.cs` | `src/PanelForge.Application/Features/Pages/Models/PageDto.cs` | Tránh tạo folder rác `DTOs/Content` chỉ chứa đúng 1 file. |
| `src/PanelForge.Application/DTOs/Auth/*` | `src/PanelForge.Application/Features/Auth/Models/*` | Chuẩn hóa DTO của Auth. |
| `src/PanelForge.Application/Services/IAuthService.cs` | Chuyển dần thành các Command trong `Features/Auth/` hoặc chuyển vào `Common/Interfaces/Services/` | Không để folder `Services/` trơ trọi chỉ có 1 file interface ở root Application. |
| `src/PanelForge.Application/Interfaces/IPageRepository.cs` | `src/PanelForge.Application/Common/Interfaces/Persistence/IPageRepository.cs` | Gom chung với `IPanelForgeDbContext.cs`. |
| `src/PanelForge.Application/Interfaces/IEmailService.cs` | `src/PanelForge.Application/Common/Interfaces/Services/IEmailService.cs` | Phân loại rõ: nhóm Persistence vs nhóm Services. |
| `src/PanelForge.Application/Interfaces/IWorkspaceAuthorizationService.cs` | `src/PanelForge.Application/Common/Interfaces/Services/IWorkspaceAuthorizationService.cs` | Tránh vứt interface bừa bãi ở root `Interfaces/`. |

---

### 3.2. Các file trong `PanelForge.Infrastructure`

| Đường dẫn hiện tại | Đường dẫn đề xuất mới | Lý do thay đổi |
| :--- | :--- | :--- |
| `src/PanelForge.Infrastructure/Repositories/MartenPageRepository.cs` | `src/PanelForge.Infrastructure/Persistence/Repositories/MartenPageRepository.cs` | Gom Repository vào cùng nhóm Persistence dữ liệu. |
| `src/PanelForge.Infrastructure/Services/EmailService.cs` | `src/PanelForge.Infrastructure/ExternalServices/Email/EmailService.cs` | Tách các dịch vụ thứ 3 (Email, AI, S3) khỏi logic nội bộ. |
| `src/PanelForge.Infrastructure/Firebase/*` | `src/PanelForge.Infrastructure/ExternalServices/Firebase/*` | Đồng nhất các dịch vụ tích hợp bên ngoài. |
| `src/PanelForge.Infrastructure/Services/WorkspaceAuthorizationService.cs` | `src/PanelForge.Infrastructure/ExternalServices/Authorization/` | Tổ chức có cấu trúc rõ ràng. |

---

### 3.3. Các file trong `PanelForge.API`

| File | Tình trạng hiện tại | Hành động đề xuất |
| :--- | :--- | :--- |
| `ApiControllerBase.cs` | Chưa có | **Tạo mới**: Base Controller cung cấp sẵn `ISender Mediator => HttpContext.RequestServices.GetRequiredService<ISender>();` giúp các Controller khác không cần inject `ISender` lặp đi lặp lại. |
| `ExceptionHandlingMiddleware.cs` | Chưa có | **Tạo mới**: Bắt tập trung các lỗi `NotFoundException`, `ValidationException`, `UnauthorizedException` và trả về JSON chuẩn RFC 7807 (ProblemDetails). |
| `AdminController.cs` | Đang `inject IPanelForgeDbContext` và viết LINQ trực tiếp | **Sửa**: Tạo `GetAllUsersQuery` trong `Features/Users/Queries/GetAllUsers` và gọi qua `Mediator.Send(query)`. |
| `TestController.cs` | Controller test nội bộ | Giữ nguyên hoặc xóa khi lên Production. |

---

## 4. Các File Mới Cần Bổ Sung Để Hoàn Thiện Clean Architecture

Để dự án đạt chuẩn chuyên nghiệp, các file sau nên được bổ sung:

1. **`PanelForge.Application/Common/Behaviors/ValidationBehavior.cs`**:
   - Sử dụng MediatR Pipeline Behavior để tự động validate request qua `FluentValidation`. Bất kỳ Command nào có dữ liệu không hợp lệ sẽ bị chặn lại ngay lập tức trước khi tới Handler.
2. **`PanelForge.Application/Common/Exceptions/NotFoundException.cs` & `ValidationException.cs`**:
   - Định nghĩa exception nghiệp vụ chuẩn thay vì ném exception chung chung.
3. **`PanelForge.Application/Common/Models/PagedResult.cs`**:
   - Model chuẩn cho phân trang: `{ Items: [], PageIndex: 1, PageSize: 20, TotalCount: 100, TotalPages: 5 }`.
4. **`PanelForge.API/Controllers/ApiControllerBase.cs`**:
   - Base Controller kế thừa bởi mọi Controller khác trong dự án.
5. **`PanelForge.API/Common/Middlewares/ExceptionHandlingMiddleware.cs`**:
   - Middleware bắt lỗi toàn cục, loại bỏ hoàn toàn các khối `try-catch` lặp đi lặp lại trong Controller.

---

## 5. Lộ Trình Triển Khai Thực Hiện Đề Xuất

Để đảm bảo **không làm gãy (break) code đang chạy** của team, đề xuất thực hiện theo 3 giai đoạn:

```
┌─────────────────────────┐     ┌─────────────────────────┐     ┌─────────────────────────┐
│       Giai đoạn 1       │     │       Giai đoạn 2       │     │       Giai đoạn 3       │
│  Sắp xếp thư mục nội bộ │ ──> │ Bổ sung Core Component  │ ──> │ Chuẩn hóa Controllers   │
│  (Move Users & DTOs)    │     │ (Middleware, Behaviors) │     │ (Admin, ApiController)  │
└─────────────────────────┘     └─────────────────────────┘     └─────────────────────────┘
```

- **Giai đoạn 1 (Nhanh & An toàn nhất):**
  - Chuyển thư mục `src/PanelForge.Application/Users/` vào `src/PanelForge.Application/Features/Users/`.
  - Gom các DTOs vào thư mục `Models/` của từng Feature tương ứng.
  - Sửa lại các namespace và chạy `dotnet build` + `dotnet test` đảm bảo 75 unit test đều pass.
- **Giai đoạn 2 (Bổ sung hạ tầng):**
  - Thêm `ApiControllerBase`, `ExceptionHandlingMiddleware` vào API.
  - Thêm `ValidationBehavior` và `PagedResult` vào Application.
- **Giai đoạn 3 (Tối ưu Controller):**
  - Refactor `AdminController` để chuyển truy vấn trực tiếp DB thành MediatR Query.

---

*Bạn hãy xem xét tài liệu này. Khi bạn đồng ý với kế hoạch trên, tôi có thể bắt đầu thực hiện ngay từng giai đoạn cho bạn một cách an toàn và đảm bảo build luôn luôn pass.*
