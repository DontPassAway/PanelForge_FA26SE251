# Kiến Trúc Clean Architecture & Quy Ước Tổ Chức Mã Nguồn – PanelForge Backend

> **Dự án**: PanelForge – AI-Assisted Manga Production Management Platform (FA26SE251)
> **Phiên bản tài liệu**: 2.0
> **Cập nhật**: 2026-09-28
> **Vị trí lưu trữ**: `docs/architecture/clean-architecture-structure.md`
> **Nền tảng**: ASP.NET Core Web API **.NET 9**, PostgreSQL (EF Core + Marten), MediatR

Tài liệu này là **quy ước bắt buộc** khi thêm code vào Backend. Nó mô tả:
- cấu trúc hiện tại,
- cấu trúc mục tiêu theo 5 Core Flow của đề tài,
- cách mở rộng cho **Mobile Reader app** mà không phải viết lại logic.

Ký hiệu dùng trong tài liệu:
- ✅ đã có trong code
- 🔁 đã có nhưng cần chỉnh hoặc di chuyển
- 🆕 sẽ thêm khi làm Core Flow tương ứng

---

## Mục Lục
1. [Tổng quan kiến trúc](#1-tổng-quan-kiến-trúc)
2. [Quy tắc phụ thuộc giữa các project](#2-quy-tắc-phụ-thuộc-giữa-các-project)
3. [Ánh xạ Core Flow → Code](#3-ánh-xạ-core-flow--code)
4. [Cây thư mục mục tiêu](#4-cây-thư-mục-mục-tiêu)
5. [Chi tiết từng tầng: được / không được đặt gì](#5-chi-tiết-từng-tầng-được--không-được-đặt-gì)
6. [Khi nào tạo Interface / Service](#6-khi-nào-tạo-interface--service)
7. [Bảng quy ước đặt tên & vị trí file](#7-bảng-quy-ước-đặt-tên--vị-trí-file)
8. [Lưu trữ: Relational + Event Sourcing](#8-lưu-trữ-relational--event-sourcing)
9. [Mở rộng cho Mobile (Reader app)](#9-mở-rộng-cho-mobile-reader-app)
10. [Quy trình thêm một tính năng mới](#10-quy-trình-thêm-một-tính-năng-mới)
11. [Các điểm lệch chuẩn hiện tại & lộ trình refactor](#11-các-điểm-lệch-chuẩn-hiện-tại--lộ-trình-refactor)
12. [Lịch sử thay đổi tài liệu](#12-lịch-sử-thay-đổi-tài-liệu)

---

## 1. Tổng quan kiến trúc

PanelForge áp dụng **Clean Architecture** kết hợp với:
- **CQRS + MediatR**: mỗi use case tương ứng một Command hoặc Query và một Handler.
- **Rich Domain Model (DDD)**: quy tắc nghiệp vụ nằm trong Entity/Aggregate.
- **Hybrid Persistence**: dữ liệu quan hệ dùng EF Core, còn Page/Element (canvas) dùng Event Sourcing qua Marten. Cả hai chạy trên cùng một PostgreSQL.

Một Backend duy nhất phục vụ **ba loại client**:

| Client | Người dùng | Phạm vi dữ liệu | Core Flow |
|---|---|---|---|
| **Web Studio** (`Frontend/panelforge-fe`) | Admin, Producer, Writer, Artist, Letterer, Editor | Nội dung trong Workspace mà user là thành viên | CF1 → CF5 |
| **PreviewLink** (web, không tài khoản) | Reviewer bên ngoài | Đúng 1 Chapter đang `Locked`, qua token | CF5 |
| **Mobile Reader app** | Reader (tài khoản `ReaderAccount` riêng) | Chỉ các Series/Chapter đã `Published` | CF5 (BR-24, E-36) |

> **Nguyên tắc vàng:** Web và Mobile chỉ là hai **cửa vào** (Presentation) khác nhau. Logic nghiệp vụ (Application và Domain) **dùng chung, không viết lại**.

---

## 2. Quy tắc phụ thuộc giữa các project

```text
        ┌────────────────────────── PanelForge.API ──────────────────────────┐
        │  Controllers (Studio / Admin / Public / Mobile), Filters, Middleware│
        └───────────────┬──────────────────────────────────┬──────────────────┘
                        │ gửi Command/Query                 │ chỉ để đăng ký DI
                        ▼                                   ▼
        ┌──────── PanelForge.Application ───────┐   ┌── PanelForge.Infrastructure ──┐
        │ Features (CQRS), Interfaces (ports),  │◄──│ EF Core, Marten, JWT, BCrypt,  │
        │ DTO, Result                           │   │ TOTP, Firebase, Email, Storage │
        └──────────────────┬────────────────────┘   └───────────────┬────────────────┘
                           ▼                                        │
                  ┌──── PanelForge.Domain ────┐◄────────────────────┘
                  │ Entities, Enums, Events   │
                  └───────────────────────────┘
```

| Project | Được tham chiếu | Package được phép | Tuyệt đối không |
|---|---|---|---|
| **Domain** | (không gì cả) | Chỉ BCL (.NET) | EF, MediatR, ASP.NET, bất kỳ SDK nào |
| **Application** | Domain | MediatR, `Microsoft.EntityFrameworkCore` (chỉ để dùng `DbSet`/LINQ qua `IPanelForgeDbContext`)* | Npgsql, Marten, Firebase, OtpNet, BCrypt, `HttpContext` |
| **Infrastructure** | Application, Domain | Mọi thư viện kỹ thuật | Controller, logic nghiệp vụ nhiều bước |
| **API** | Application, Infrastructure | ASP.NET, JwtBearer, Swagger | Inject `DbContext` vào Controller/Filter, viết LINQ, gọi thẳng SDK |

\* Đây là **thỏa hiệp có chủ đích**: Application biết `DbSet<T>` để query linh hoạt, nhưng không biết provider cụ thể (Npgsql) hay cách cấu hình.

---

## 3. Ánh xạ Core Flow → Code

Bảng này cho biết **mỗi Core Flow nằm ở đâu trong code**. Khi nhận task, xác định CF trước, sau đó tra đúng thư mục.

| Core Flow | Domain (`Entities/…`) | Application (`Features/…`) | API Controller | Trạng thái |
|---|---|---|---|---|
| **CF1** Studio, Series & Bible Setup | `Identity/`, `Workspaces/`, `Content/Series`, `Bible/`, `Workflow/Pipeline*`, `MasterData/` | `Auth`, `Users`, `Admin`, `Workspaces`, `AiConfig`, `AiUsage`, `Audit`, `Series`, `SeriesPipeline`, `SeriesPresets`, `Bible`, `TypographyPresets`, `ConsistencyRules`, `MasterData/*` | `Auth`, `Users`, `Admin`, `Workspaces`, `Series`, `SeriesPipeline`, `Bible`, `PipelineTemplates` | ✅ |
| **CF2** Chapter Production | `Content/Chapter, Scene, ScriptLine, Page, Panel, Element`, `Workflow/Assignment, StageProgress`, `Assets/`, `Ai/AiSuggestion` | `Chapters`, `Scenes`, `Pages`, `Panels`, `CanvasElements`, `PanelContentElements`, `Scripts` 🆕, `Assignments` 🆕, `Assets` 🆕, `AiSuggestions` 🆕 | `Chapters`, `Scenes`, `Pages`, `Panels`, `Elements`, `PanelElements`, … | ✅ khung nội dung, 🆕 phần còn lại |
| **CF3** Quality Control | `Review/Comment, ChangeRequest, ConsistencyFinding` | `Reviews` 🆕, `ChangeRequests` 🆕, `ConsistencyChecks` 🆕, `StageGates` 🆕 | 🆕 | 🆕 |
| **CF4** Version & Provenance *(xương sống)* | `Provenance/VersionEvent`, `Audit/AuditLog`, event của `Page` | `Versions` 🆕 (history, compare, restore), `Provenance` 🆕 (report) | 🆕 | ✅ event store Page, 🆕 phần còn lại |
| **CF5** Approval, Release & Export | `Publishing/PreviewLink, ExportJob, ReaderFeedback`, `Reader/ReaderAccount, ReaderSubscription` | `ChapterApproval` 🆕 (approve/lock/reopen/publish), `PreviewLinks` 🆕, `Exports` 🆕, `ReaderFeedback` 🆕, `Catalog` ✅, **`Reader/*` 🆕 (Mobile)** | `Catalog` ✅, 🆕 `Mobile/*` | ✅ Catalog, 🆕 phần còn lại |
| **Cross-cutting** | `Notification` | `Notifications` 🆕, `Search` 🆕 | 🆕 | 🆕 |

> CF4 là **cơ chế hệ thống**, không phải một màn hình riêng. Mọi Command làm thay đổi trạng thái ở CF1, CF2, CF3 và CF5 đều phải sinh bản ghi lịch sử (BR-18). Nếu không ghi được lịch sử thì Command bị từ chối.

---

## 4. Cây thư mục mục tiêu

Tên thư mục là **tên đích**. Mục nào có 🔁 nghĩa là code hiện tại đang nằm ở chỗ khác, xem [mục 11](#11-các-điểm-lệch-chuẩn-hiện-tại--lộ-trình-refactor). **Namespace luôn phải khớp với đường dẫn thư mục.**

```text
PanelForge_FA26SE251/
├── docs/
│   └── architecture/clean-architecture-structure.md
├── src/
│   ├── PanelForge.Domain/
│   │   ├── Common/                 ✅ BaseEntity, AggregateRoot, IDomainEvent, ISoftDelete, IAuditableEntity
│   │   ├── Entities/
│   │   │   ├── Identity/           ✅ User, UserRememberedDevice
│   │   │   ├── Workspaces/         ✅ StudioWorkspace, WorkspaceMember, WorkspaceAiConfig (số nhiều để không trùng thuộc tính `Workspace`)
│   │   │   ├── Content/            ✅ Series, Chapter, Scene, ScriptLine, Page (Aggregate), Panel, Element,
│   │   │   │                          ElementState, TypographyPreset, ConsistencyRule · SeriesPreset (deprecated, không còn map EF)
│   │   │   ├── Bible/              ✅ SeriesBible, BibleEntry, BibleEntryRevision
│   │   │   ├── Workflow/           ✅ PipelineDefinition, PipelineStage, StageTransition, Assignment,
│   │   │   │                          WorkflowTransitionLog · 🆕 StageProgress
│   │   │   ├── MasterData/         ✅ ElementTypeMasterData, ExportPreset, PipelineTemplate(+Stage)
│   │   │   ├── Ai/                 ✅ AiUsageRecord · 🆕 AiSuggestion
│   │   │   ├── Audit/              ✅ AuditLog
│   │   │   ├── Assets/             🆕 Asset (SHA-256), AssetReference
│   │   │   ├── Review/             🆕 Comment/Annotation, ChangeRequest, ConsistencyFinding
│   │   │   ├── Provenance/         🆕 VersionEvent / ProvenanceEvent
│   │   │   ├── Publishing/         ✅ ExternalPreviewLink · 🆕 ExportJob, ReaderFeedback
│   │   │   ├── Reader/             🆕 ReaderAccount, ReaderSubscription              ← Mobile
│   │   │   └── Notifications/      🆕 Notification
│   │   ├── Enums/                  ✅ ChapterStatus, WorkspaceRole, SystemRole, BibleEntryCategory, …
│   │   ├── Events/                 ✅ PageCreatedEvent, ElementAddedEvent, … (event của Page aggregate)
│   │   ├── Exceptions/             🆕 DomainException (vi phạm invariant)
│   │   └── ValueObjects/           🆕 (khi cần: Sha256Hash, ReadingOrder, …)
│   │
│   ├── PanelForge.Application/
│   │   ├── Common/
│   │   │   ├── Interfaces/
│   │   │   │   ├── Persistence/    ✅ IPanelForgeDbContext, IPageRepository, IUnitOfWork
│   │   │   │   ├── Authentication/ ✅ IJwtTokenGenerator, IPasswordHasher, ITotpService, IExternalIdentityVerifier
│   │   │   │   ├── Services/       ✅ IEmailService, ICurrentUserService, IWorkspaceAuthorizationService,
│   │   │   │   │                      IAiUsageService, IAiKeyProtector, IAiProviderValidator
│   │   │   │   │                   ✅ IAuthService, IWorkspaceService đã xóa (2026-09-28), logic chuyển sang Features/Auth, Features/Workspaces
│   │   │   │   ├── Storage/        🆕 IAssetStorage (upload, signed URL)
│   │   │   │   ├── Ai/             🆕 IAiScriptBreakdownClient, IAiConsistencyClient
│   │   │   │   ├── Notifications/  🆕 IPushNotificationSender (Mobile), INotificationPublisher
│   │   │   │   └── Payments/       🆕 IPaymentGateway (Reader subscription – extended scope)
│   │   │   ├── Models/             ✅ PagedResult · 🔁 Result (đang ở Common/)
│   │   │   ├── Behaviors/          🆕 ValidationBehavior, LoggingBehavior (MediatR pipeline)
│   │   │   └── Utilities/          🔁 CsvWriter (đang ở Common/)
│   │   ├── Features/               ← mỗi Feature = 1 thư mục, chia theo CF như mục 3
│   │   │   └── <Feature>/
│   │   │       ├── Commands/<Action>/<Action>Command.cs (+Handler, +Validator)
│   │   │       ├── Queries/<Action>/<Action>Query.cs    (+Handler)
│   │   │       ├── Models/          Request/Response DTO của feature
│   │   │       └── Common/          helper/factory chỉ dùng trong feature (không interface)
│   │   └── DependencyInjection.cs  ✅
│   │
│   ├── PanelForge.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── PanelForgeDbContext.cs, PanelForgeDbContextFactory.cs   ✅
│   │   │   ├── Configurations/<Nhóm khớp Domain>/                      ✅ Identity, Workspaces, Ai, Audit, Publishing, Content, Bible, Workflow, MasterData
│   │   │   ├── Migrations/                                              ✅
│   │   │   ├── EventStore/          ✅ MartenPageRepository, Projections/PageProjection
│   │   │   ├── Seeders/             ✅ MasterDataSeeder
│   │   │   └── AuditChangeSerializer.cs                                ✅
│   │   ├── Authentication/          ✅ JwtTokenGenerator, JwtSettings, BcryptPasswordHasher
│   │   │                            ✅ OtpNetTotpService (FirebaseIdentityVerifier ở ExternalServices/Firebase/)
│   │   ├── Services/                ✅ HttpCurrentUserService, AiUsageService, AiKeyProtector, AiProviderValidator
│   │   │                            ✅ WorkspaceAuthorizationService
│   │   ├── ExternalServices/        (chỉ những gì GỌI RA NGOÀI hệ thống)
│   │   │   ├── Email/               ✅ EmailService
│   │   │   ├── Firebase/            ✅ FirebaseServiceExtensions · 🆕 FcmPushNotificationSender
│   │   │   ├── Storage/             🆕 (S3/MinIO/Firebase Storage) AssetStorage
│   │   │   ├── Ai/                  🆕 các AI provider client
│   │   │   └── Payments/            🆕 payment gateway
│   │   ├── BackgroundJobs/          🆕 ExportWorker, AiJobWorker (xử lý async – NFR-05, A-01)
│   │   └── DependencyInjection.cs   ✅
│   │
│   └── PanelForge.API/
│       ├── Controllers/
│       │   ├── Studio/              🔁 Workspaces, Series, Chapters, Pages, … (Web Studio – đang ở gốc Controllers/)
│       │   ├── Admin/               🔁 AdminController
│       │   ├── Public/              🔁 Catalog · 🆕 PreviewLinks (không cần đăng nhập)
│       │   └── Mobile/              🆕 Reader*Controller – route /api/mobile/v1/...
│       ├── Filters/                 🔁 RequireWorkspaceRoleAttribute (đang ở Common/Attributes/)
│       ├── Middleware/              🆕 GlobalExceptionHandler (ProblemDetails)
│       ├── Extensions/              🆕 AddApiAuthentication(), AddSwaggerDocs(), ResultExtensions (Result → IActionResult)
│       ├── Program.cs               ✅
│       └── appsettings*.json        ✅ (không commit secret, firebase-key.json đã ignore)
│
└── test/
    └── PanelForge.UnitTests/        ✅ chia thư mục giống src/: Domain/, Application/<Feature>/, Infrastructure/, API/
```

> **Di chuyển Controller sang thư mục con không làm đổi route**, vì route khai báo bằng `[Route]` và `[HttpGet]`. Frontend không bị ảnh hưởng.

---

## 5. Chi tiết từng tầng: được / không được đặt gì

### 5.1 Domain – "Quy tắc nghiệp vụ không đổi dù đổi DB hay framework"

**Được đặt:**
- Entity/Aggregate có **hành vi**: `chapter.Approve(...)`, `chapter.Lock()`, `page.AddElement(...)`, `bibleEntry.AddRevision(...)`.
- Invariant của Business Rule, ví dụ:
  - BR-03 (phiếu V1.2): cây vật lý Series → Chapter → Page → Panel → Element song song với Scene → ScriptLine; Panel thuộc một Page, tối đa một Scene và tham chiếu ScriptLine. Code hiện còn `Page.SceneId` và cần đổi (xem `HANDOFF.md`).
  - BR-05: mỗi ScriptLine gắn tối đa một text Element.
  - BR-06, BR-12: không sửa khi Chapter đang `Locked`.
- Enum, Domain Event, Value Object, DomainException.

**Quy ước:**
- Setter là `private set`. Có constructor rỗng `private` cho EF.
- Tạo đối tượng qua `public static X Create(...)`, trong đó validate ngay.
- Chuyển trạng thái bằng method có tên nghiệp vụ, không set trực tiếp `Status = ...`.
- **Append-only (BR-01, C-01):** dữ liệu có phiên bản (Bible, Version) chỉ **thêm revision mới**, không có method `Update` ghi đè. Xóa luôn là **soft delete** (`ISoftDelete`).

**Không được:** attribute của EF, DTO, `HttpContext`, MediatR, `DateTime` lấy từ service ngoài.

### 5.2 Application – "Hệ thống làm được gì (use case)"

**Được đặt:**
- Command/Query + Handler: điều phối Domain, gọi các interface, trả về `Result<T>`.
- DTO request/response trong `Features/<Feature>/Models/`.
- Interface (port) cho mọi thứ cần từ bên ngoài, đặt trong `Common/Interfaces/`.
- Helper, factory, mapper `static` dùng chung trong một feature, đặt trong `Features/<Feature>/Common/`.

**Quy ước:**
- **Một use case tương ứng một Command/Query và một Handler.** Handler cùng file với Command, hoặc file riêng `…Handler.cs` cùng thư mục. Chọn một kiểu cho cả feature.
- Luôn dùng cấu trúc `Commands/<Action>/` và `Queries/<Action>/`. **Không** dồn nhiều handler vào một file `XxxCommands.cs`.
- Lỗi nghiệp vụ dự đoán được thì trả `Result<T>.Failure(message, ResultErrorCodes.X)`, không throw.
- Kiểm tra quyền trong Workspace qua `IWorkspaceAuthorizationService`.
- Command nào làm thay đổi dữ liệu có phiên bản thì phải ghi lịch sử (CF4, BR-18).

**Không được:** `using` Npgsql, Marten, Firebase, OtpNet, BCrypt, `Microsoft.AspNetCore.*`. Không tạo `XxxService` theo tên Entity hay Core Flow (xem mục 6).

### 5.3 Infrastructure – "Chi tiết kỹ thuật"

**Được đặt:**
- Class hiện thực của các interface trong Application.
- Cấu hình EF, migration, Marten, projection, seeder.
- Client gọi dịch vụ ngoài: Email, Firebase, Storage, AI, Payment.
- Worker nền: export, AI job.

**Phân biệt hai thư mục:**
- `ExternalServices/`: code **gọi ra ngoài** hệ thống qua mạng hoặc SDK.
- `Services/`: code hiện thực nội bộ, ví dụ đọc DB, mã hóa khóa, lấy user hiện tại.

**Không được:** use case nhiều bước, ví dụ luồng đăng ký gồm tạo user, gửi mail, tạo token.

### 5.4 API – "Cửa vào HTTP"

**Controller phải mỏng:** nhận request → dựng Command/Query → `ISender.Send(...)` → map `Result` sang HTTP status.

**Chia thư mục theo client:** `Studio/`, `Admin/`, `Public/`, `Mobile/`.

**Phân quyền:**
- `[Authorize]` cho user đã đăng nhập.
- `[RequireWorkspaceRole(...)]` cho vai trò trong Workspace.
- Policy riêng cho Reader (xem mục 9).

**Không được:** inject `IPanelForgeDbContext` hay repository, viết LINQ, gọi SDK.

`Program.cs` chỉ gọi các extension và cấu hình middleware.

---

## 6. Khi nào tạo Interface / Service

**Quy tắc:** chức năng nghiệp vụ dùng **Handler**. Chỉ tạo interface cho các nhóm trong bảng dưới.

| Nhóm | Dấu hiệu | Ví dụ | Interface? | Implementation đặt ở |
|---|---|---|:---:|---|
| **1. Cổng ra công nghệ ngoài** | Cần SDK/thư viện hoặc gọi mạng | `IEmailService`, `IJwtTokenGenerator`, `IPasswordHasher`, `ITotpService`, `IExternalIdentityVerifier`, `IAssetStorage`, `IPushNotificationSender`, `IPaymentGateway`, `IPageRepository` | **Có** | Infrastructure |
| **2. Ngữ cảnh runtime** | Lấy từ request hoặc môi trường | `ICurrentUserService` (+ thông tin Reader cho Mobile) | **Có** | Infrastructure/API |
| | Thời gian | `TimeProvider` có sẵn trong .NET, **không** tự viết `IClock` | Dùng sẵn | — |
| **3. Năng lực dùng chung của Application** | Nhiều handler cần, nhỏ, một trách nhiệm | `IWorkspaceAuthorizationService`, `IAiUsageService` (quota) | Có thể | Ưu tiên Application |
| **4. Helper / Factory / Mapper** | Hàm thuần, không phụ thuộc gì | `AuthResponseFactory`, mapper Entity → DTO | **Không** (class `static`) | `Features/<Feature>/Common/` |

**Cách nhận biết nhanh:**
- Service mang **tên một thực thể** (`SeriesService`, `ChapterService`, `StudioService`) → **sai**, chuyển sang Handler.
- Service mang **tên một năng lực** (`PasswordHasher`, `EmailSender`, `WorkspaceAuthorization`) → thường đúng.
- Quy tắc trạng thái của một Entity (ví dụ Chapter chỉ Publish được khi đang Locked) → **đặt trong method của Domain**, không đặt trong service.

---

## 7. Bảng quy ước đặt tên & vị trí file

| Loại | Vị trí | Tên file/class | Kế thừa / kiểu | Ví dụ |
|---|---|---|---|---|
| Entity | `Domain/Entities/<Nhóm>/` | `<Entity>.cs` (số ít) | `BaseEntity` | `Chapter.cs` |
| Aggregate (event-sourced) | `Domain/Entities/<Nhóm>/` | `<Aggregate>.cs` | `AggregateRoot` | `Page.cs` |
| Domain Event | `Domain/Events/` | `<Entity><Việc đã xảy ra>Event.cs` | `record : IDomainEvent` | `ElementMovedEvent.cs` |
| Enum | `Domain/Enums/` | `<Name>.cs` | `enum` | `ChapterStatus.cs` |
| Command | `Application/Features/<F>/Commands/<Action>/` | `<Verb><Noun>Command.cs` | `record : IRequest<Result<T>>` | `CreateChapterCommand.cs` |
| Command Handler | cùng thư mục | `<Verb><Noun>CommandHandler` | `IRequestHandler<,>` | `CreateChapterCommandHandler` |
| Query (+Handler) | `Application/Features/<F>/Queries/<Action>/` | `Get<Noun>[By<X>]Query.cs` | `record : IRequest<Result<T>>` | `GetChaptersBySeriesQuery.cs` |
| Validator 🆕 | cùng thư mục với Command | `<Verb><Noun>CommandValidator.cs` | (khi thêm FluentValidation) | `CreateChapterCommandValidator.cs` |
| Request DTO | `Application/Features/<F>/Models/` | `<Action>Request.cs` | `record` | `CreateWorkspaceRequest.cs` |
| Response DTO | `Application/Features/<F>/Models/` | `<Noun>Dto.cs` / `<Action>Response.cs` | `record` | `ChapterDto.cs`, `AuthResponse.cs` |
| DTO riêng cho Mobile | `Application/Features/Reader/<F>/Models/` | `Reader<Noun>Dto.cs` | `record` | `ReaderChapterDto.cs` |
| Interface (port) | `Application/Common/Interfaces/<Nhóm>/` | `I<Năng lực>.cs` | `interface` | `IAssetStorage.cs` |
| EF Configuration | `Infrastructure/Persistence/Configurations/<Nhóm khớp Domain>/` | `<Entity>Configuration.cs` | `IEntityTypeConfiguration<T>` | `ChapterConfiguration.cs` |
| Repository | `Infrastructure/Persistence/EventStore/` | `<Tech><Entity>Repository.cs` | `I<Entity>Repository` | `MartenPageRepository.cs` |
| Implementation ra ngoài | `Infrastructure/ExternalServices/<Nhà cung cấp>/` | `<Tech><Năng lực>.cs` | interface tương ứng | `FcmPushNotificationSender.cs` |
| Controller Web | `API/Controllers/Studio/` | `<PluralNoun>Controller.cs` | `ControllerBase` | `ChaptersController.cs` |
| Controller Mobile | `API/Controllers/Mobile/` | `Reader<PluralNoun>Controller.cs` | `ControllerBase` | `ReaderLibraryController.cs` |
| Filter/Attribute | `API/Filters/` | `<Name>Attribute.cs` | `TypeFilterAttribute` | `RequireWorkspaceRoleAttribute.cs` |
| Test | `test/PanelForge.UnitTests/<Layer>/<Feature>/` | `<Subject>Tests.cs` | xUnit | `CreateChapterCommandTests.cs` |

**Quy ước đặt tên Action:**
- **Bám theo route và nghiệp vụ đã có.** Ví dụ: dùng `AddMember`, không dùng `InviteMember`, vì API hiện tại thêm thành viên trực tiếp chứ không có luồng mời. Dùng `Workspaces`, không dùng `Studios`, vì route là `/api/workspaces`.
- Dữ liệu có phiên bản dùng `Add<X>Revision`, không dùng `Update<X>`.
- Các trạng thái của Chapter: `ApproveChapter`, `LockChapter`, `ReopenChapter`, `PublishChapter`.

---

## 8. Lưu trữ: Relational + Event Sourcing

| Cơ chế | Dữ liệu | Lý do |
|---|---|---|
| **EF Core (bảng quan hệ)** | User, Workspace, Series, Chapter, Scene, ScriptLine, Panel, Bible, Workflow, MasterData, AuditLog, … | Khóa ngoại, truy vấn nhiều cấp, phân trang |
| **Marten Event Store** (`mt_events`) | `Page` aggregate và các Element trên canvas | Lịch sử chỉnh sửa đầy đủ, replay, optimistic concurrency (`Version`) |

### 8.1 Quy tắc ghi dữ liệu (bắt buộc với code mới)

1. **Không ghi hai nơi mà không có transaction chung.**
   Gọi `_pageRepository.SaveAsync(...)` (Marten tự commit) rồi mới gọi `_dbContext.SaveChangesAsync()` sẽ dẫn đến lệch dữ liệu nếu bước thứ hai lỗi. Đây là lỗi đã biết, xem [mục 11](#11-các-điểm-lệch-chuẩn-hiện-tại--lộ-trình-refactor). Có hai hướng xử lý:
   - **Ngắn hạn:** Marten session dùng chung connection và transaction với EF Core, để một lần commit áp dụng cho cả hai.
   - **Dài hạn:** Marten là nguồn dữ liệu gốc, còn bảng `pages` được cập nhật bằng **projection `Inline`**. Handler chỉ ghi vào Marten.
2. **Xóa một aggregate event-sourced** thì phải phát event (ví dụ `PageDeletedEvent`) vào stream, không được chỉ xóa bảng phía EF.
3. **Append-only (BR-01, BR-08):** "Restore" nghĩa là tạo **phiên bản mới** chép lại trạng thái cũ, không xóa hay sửa bản cũ.

### 8.2 Hai loại "event"

| Loại | Mục đích | Hiện tại |
|---|---|---|
| **Event Sourcing event** | Dựng lại trạng thái aggregate (`Page.Apply(...)`) | ✅ 6 event trong `Domain/Events/` |
| **Domain event để thông báo** | Báo cho phần khác xử lý, ví dụ gửi Notification khi được giao việc hoặc đẩy push cho Reader khi có chapter mới | 🆕 chưa có |

Khi bắt đầu cần loại thứ hai (CF2 Notification, CF5 Publish), tách riêng thành `Domain/Events/Notifications/` với interface marker riêng, và dispatch sau khi `SaveChanges` thành công.

---

## 9. Mở rộng cho Mobile (Reader app)

### 9.1 Phạm vi nghiệp vụ (theo BR-24, US-30, E-36, CF5 bước 10–12)

Reader trên Mobile có thể:
- **Đăng ký và đăng nhập** bằng `ReaderAccount`. Đây là tài khoản **tách biệt** với `User` của Studio.
- **Duyệt catalog công khai** các Series đã Published. API `GET /api/catalog/...` ✅ đã có. *(Tính năng thêm ngoài phạm vi đăng ký V1.2: phiếu ghi xuất bản làm ngoài hệ thống và không có Public Reader; nhóm quyết định giữ phần này, không thuộc nghiệm thu.)*
- **Đăng ký theo dõi (subscribe)** một Series. Nếu Series yêu cầu trả phí thì cần subscription còn hiệu lực (phần mở rộng).
- **Đọc** các Chapter `Published`, theo `ReadingDirection` của Series.
- **Gửi feedback** ở cấp chapter. Feedback này đi vào luồng triage giống PreviewLink (CF5 bước 6).

Reader **không bao giờ** được truy cập:
- nội dung trong Workspace,
- Chapter đang `Locked` hoặc `InProduction` (BR-07, NFR-06).

### 9.2 Nguyên tắc thiết kế

| Nguyên tắc | Cách làm |
|---|---|
| **Dùng chung Application** | Mobile chỉ thêm Controller trong `API/Controllers/Mobile/`. Logic nằm trong `Application/Features/Reader/*`. Logic dùng chung với Web (ví dụ lọc Published) phải gọi chung một query hoặc một method Domain, **không copy**. |
| **Route có version** | Tiền tố `/api/mobile/v1/...`. App mobile không bắt người dùng cập nhật được, nên khi đổi contract phải tạo `v2` song song thay vì sửa `v1`. Route Web giữ nguyên. |
| **Tách danh tính Reader** | JWT của Reader có claim `actor_type=reader` (hoặc audience riêng). Controller Mobile dùng `[Authorize(Policy = "Reader")]`. Controller Studio dùng policy `StudioUser`. **Token Reader bị từ chối ở mọi API Studio.** |
| **DTO gọn cho mobile** | Tạo `Reader*Dto` riêng: ít field, có sẵn URL ảnh, **phân trang bắt buộc** (`PagedResult`). Không trả Entity hay DTO nội bộ của Studio. |
| **Ảnh qua signed URL** | Ảnh trang lấy qua `IAssetStorage.GetSignedUrl(...)`, URL có thời hạn ngắn (BR-04, BR-13). Không trả đường dẫn file cố định. |
| **Kiểm tra quyền đọc ở Application** | Handler đọc chapter kiểm tra: Chapter `Published`, cộng thêm subscription nếu Series yêu cầu. Viết thành method Domain, ví dụ `series.CanBeReadBy(subscription)`, để Web và Mobile dùng chung. |
| **Tích hợp ngoài qua interface** | Push dùng `IPushNotificationSender` (FCM). Thanh toán dùng `IPaymentGateway`. Class hiện thực đặt ở `Infrastructure/ExternalServices/…`. |

### 9.3 Cấu trúc thư mục cho Mobile

```text
Domain/Entities/Reader/
├── ReaderAccount.cs
└── ReaderSubscription.cs

Application/Features/Reader/
├── Auth/        Commands/{RegisterReader, LoginReader, RefreshReaderToken} · Models/
├── Library/     Queries/{GetMySubscriptions, GetReadingProgress} · Commands/{SubscribeSeries, UnsubscribeSeries}
├── Reading/     Queries/{GetPublishedChapter, GetChapterPages}          (dùng chung quy tắc Published)
├── Feedback/    Commands/{SubmitReaderFeedback}
└── Common/      ReaderAccessPolicy (static helper, nếu cần)

Application/Features/Catalog/   ✅ dùng chung cho Web (user chưa có Workspace) và Mobile

API/Controllers/Mobile/
├── ReaderAuthController.cs      [Route("api/mobile/v1/auth")]
├── ReaderCatalogController.cs   [Route("api/mobile/v1/catalog")]  → gửi query của Features/Catalog
├── ReaderLibraryController.cs   [Route("api/mobile/v1/library")]
└── ReaderReadingController.cs   [Route("api/mobile/v1/series/{seriesId}/chapters")]
```

### 9.4 Khi cần tách hẳn một host riêng cho Mobile

Nếu sau này cần deploy hoặc scale Mobile độc lập, tạo project `src/PanelForge.MobileApi/` tham chiếu `Application` và `Infrastructure`, rồi chuyển `Controllers/Mobile/` sang đó. **Domain, Application và Infrastructure không phải sửa gì.** Đây là lý do bắt buộc Controller phải mỏng.

---

## 10. Quy trình thêm một tính năng mới

**Bước 0: Xác định vị trí**
- Tính năng thuộc CF nào, feature nào (xem mục 3)?
- Client nào gọi: Studio, Admin, Public hay Mobile?
- Đọc `HANDOFF.md` ở thư mục gốc để xem Frontend có yêu cầu nào đang `WAITING_BE`.

**Bước 1: Domain**
1. Tạo hoặc bổ sung Entity trong `Entities/<Nhóm>/`, kèm factory `Create(...)` và method nghiệp vụ.
2. Đặt invariant của BR tương ứng vào trong method. Viết test ở `test/.../Domain/`.

**Bước 2: Application**
1. Tạo `Features/<F>/Commands|Queries/<Action>/` gồm Command/Query và Handler.
2. Tạo DTO trong `Features/<F>/Models/`.
3. Chỉ khi cần công nghệ ngoài: khai báo interface trong `Common/Interfaces/<Nhóm>/` (theo mục 6).
4. Nếu thay đổi dữ liệu có phiên bản: ghi lịch sử (CF4).

**Bước 3: Infrastructure**
1. Entity mới: thêm `DbSet` vào `IPanelForgeDbContext` và `PanelForgeDbContext`, tạo `Configurations/<Nhóm>/<Entity>Configuration.cs`, rồi tạo migration:
   ```bash
   dotnet ef migrations add Add<EntityName> --project src/PanelForge.Infrastructure --startup-project src/PanelForge.API
   ```
2. Interface mới: viết class hiện thực vào đúng thư mục và đăng ký trong `DependencyInjection.cs`.

**Bước 4: API**
1. Thêm action vào Controller trong đúng thư mục client (`Studio/`, `Admin/`, `Public/`, `Mobile/`).
2. Gắn `[Authorize]`, `[RequireWorkspaceRole(...)]` hoặc `[Authorize(Policy = "Reader")]`.
3. Gọi `ISender.Send(...)` và map `Result` sang status code (`200/201/400/403/404/409`).

**Bước 5: Test và bàn giao**
1. Viết test cho Handler trong `test/.../Application/<Feature>/`, rồi chạy `dotnet test`.
2. Cập nhật `HANDOFF.md`: ghi method, route, Request/Response DTO (tên field, kiểu, nullable) và yêu cầu auth. Nếu đổi contract của một API đã `DONE`, ghi vào mục "Nhật ký thay đổi breaking".

---

## 11. Các điểm lệch chuẩn hiện tại & lộ trình refactor

Danh sách từ đợt rà soát ngày 2026-09-28. **Code mới không được lặp lại các lỗi này.** Code cũ sửa dần theo thứ tự ưu tiên.

| Ưu tiên | Vấn đề | Vị trí | Hướng sửa |
|:---:|---|---|---|
| ✅ đã sửa 2026-09-28 | Dual-write Marten + EF không có transaction chung | `Pages/CreatePage`, `Pages/ReorderPage`, `Pages/UpdateCanvasSettings` | Đã thêm `IUnitOfWork`: Marten enlist vào transaction của EF (hướng ngắn hạn mục 8.1). Đã kiểm chứng rollback trên PostgreSQL thật. |
| ✅ đã sửa 2026-09-28 | `DeletePage` chỉ xóa phía EF, stream Marten vẫn còn | `Pages/DeletePage/DeletePageCommandHandler.cs` | Phát `PageDeletedEvent` và soft delete |
| ✅ đã sửa 2026-09-28 | Use case nằm trong Infrastructure (god service) | `Infrastructure/Services/AuthService.cs` (644 dòng), `WorkspaceService.cs` (465 dòng) | Đã tách `ITotpService`, `IExternalIdentityVerifier`; 12 handler `Features/Auth/Commands/*`, 9 handler `Features/Workspaces/*`; route và DTO giữ nguyên; đã xóa `AuthService`, `WorkspaceService`, `IAuthService`, `IWorkspaceService`. |
| ✅ đã sửa 2026-09-28 | Controller truy vấn DB trực tiếp | `API/Controllers/AdminController.cs` | Chuyển sang `Features/Admin/Commands|Queries` |
| ✅ đã sửa 2026-09-28 | Filter truy vấn DB trực tiếp | `API/Common/Attributes/RequireWorkspaceRoleAttribute.cs` | Dùng `IWorkspaceAuthorizationService` |
| ✅ đã sửa 2026-09-28 | Hai kiểu cấu trúc CQRS | `Features/Pages`, `Features/Elements` (kiểu phẳng) | Đã chuyển về `Commands/` và `Queries/`. |
| ✅ đã sửa 2026-09-28 | Nhiều handler dồn trong một file | `SeriesPipeline/SeriesPipelineHandlers.cs`, `AiUsage/AiUsageQueries.cs`, `Catalog/PublicCatalogQueries.cs`, `ConsistencyRules/Commands/ConsistencyRuleCommands.cs`, `MasterData/*/…Commands.cs` | Đã tách mỗi use case một thư mục (DTO → `Models/`, static helper → `Common/`). **Còn lại:** `Catalog/PublicCatalogQueries.cs` chưa tách vì phiên sửa lỗi Catalog đang sửa cùng file. |
| ✅ đã sửa 2026-09-28 | Request DTO đặt sai chỗ | `ConsistencyRules/Commands/ConsistencyRuleRequests.cs`, `TypographyPresets/Commands/TypographyPresetRequests.cs` | Đã chuyển về `Models/`. |
| ✅ đã sửa 2026-09-28 | Namespace không khớp thư mục | Ví dụ `IPageRepository` (`PanelForge.Application.Interfaces`), `MartenPageRepository` (`PanelForge.Infrastructure.Repositories`), `using PanelForge.Application.DTOs.*` | Mọi file trong `src/` đã khớp. Ngoại lệ có chủ đích: test `Application/Series` giữ namespace `SeriesTests` để không trùng class `Series`. |
| ✅ đã sửa 2026-09-28 | Hai feature "Element" dễ nhầm | `Features/Elements` (Marten), `Features/ContentElements` (EF) | Đã đổi tên thành `CanvasElements` và `PanelContentElements`. |
| ✅ đã sửa 2026-09-28 | Thư mục Domain `Auth/` chứa thứ không phải Auth | `StudioWorkspace`, `WorkspaceAiConfig`, `AiUsageRecord`, `AuditLog`, `ExternalPreviewLink` | Đã tách thành `Identity/`, `Workspaces/`, `Ai/`, `Audit/`, `Publishing/`. EF xác nhận không đổi schema (`has-pending-model-changes`). |
| ✅ đã sửa 2026-09-28 | Configuration không khớp nhóm Domain | `Configurations/Content/Bible*Configuration.cs` | Đã chuyển; Configurations giờ khớp nhóm Domain. |
| ✅ đã sửa 2026-09-28 | `WorkspaceAuthorizationService` sai thư mục | `Infrastructure/ExternalServices/Authorization/` | Chuyển sang `Infrastructure/Services/` |
| ✅ đã sửa 2026-09-28 | File lỗi encoding (vỡ dấu tiếng Việt) | `Infrastructure/Persistence/Repositories/MartenPageRepository.cs` | Lưu lại bằng UTF-8 |
| ✅ đã sửa 2026-09-28 | Endpoint thử nghiệm, tên file khác tên class | `API/Controllers/TestController.cs` (class `TestAuthController`) | Đã xóa (FE không dùng). |
| ✅ đã sửa 2026-09-28 (phiên riêng) | `GET /api/catalog/series` trả 500 trên PostgreSQL (LINQ không dịch được sang SQL; unit test chạy EF InMemory nên không bắt được). Phát hiện khi chạy thử API 2026-09-28 | `Features/Catalog/PublicCatalogQueries.cs` | Viết lại truy vấn để PostgreSQL dịch được (sắp xếp / tính toán trước khi projection sang record). Đây là API public dùng cho Mobile Reader. || 🔴 1 | Migration EF `20260916040132_AddEventSourcingElements` tự tạo bảng `mt_events` / `mt_streams` bằng SQL tay, lệch schema với Marten 9 → dựng database MỚI thì Marten báo lỗi schema | `Infrastructure/Persistence/Migrations/` | Để Marten tự quản lý schema `mt_*` (bỏ SQL tay khỏi migration cho DB mới, có phương án cho DB đã chạy). Cần thống nhất với nhóm vì migration đã chạy trên máy mọi người. |
| 🔴 1 | Migration EF `20260916040132_AddEventSourcingElements` tự tạo bảng `mt_events` / `mt_streams` bằng SQL tay, lệch schema với Marten 9 → dựng database MỚI thì Marten báo lỗi schema | `Infrastructure/Persistence/Migrations/` | Để Marten tự quản lý schema `mt_*` (bỏ SQL tay khỏi migration cho DB mới, có phương án cho DB đã chạy). Cần thống nhất với nhóm vì migration đã chạy trên máy mọi người. |

> Việc di chuyển file và đổi namespace **không làm đổi route hay DTO**, nên không ảnh hưởng Frontend. Nên làm theo từng feature, mỗi lần một commit, và chạy `dotnet build` cùng `dotnet test` sau mỗi bước.

---

## 12. Lịch sử thay đổi tài liệu

| Phiên bản | Ngày | Nội dung |
|---|---|---|
| 1.0 | 2026-09-17 | Bản đầu: mô tả 4 tầng và quy ước đặt tên theo code thời điểm đó. |
| 2.0 | 2026-09-28 | Thêm ánh xạ CF1–CF5 → code; cây thư mục mục tiêu có trạng thái ✅/🔁/🆕; quy tắc khi nào tạo Interface/Service (4 nhóm); quy tắc ghi dữ liệu Marten + EF và append-only; mục mở rộng cho Mobile Reader (route `/api/mobile/v1`, policy Reader, DTO riêng, signed URL); danh sách điểm lệch chuẩn và lộ trình refactor. Bỏ `AuthService`/`WorkspaceService` khỏi danh sách "đặt đúng chỗ". |

---
*Tài liệu này được đối chiếu với mã nguồn thực tế và với `docs/requirements/PanelForge_Capstone_Content.md`. Khi cấu trúc code thay đổi, cập nhật mục 4 và mục 11 trước tiên.*
