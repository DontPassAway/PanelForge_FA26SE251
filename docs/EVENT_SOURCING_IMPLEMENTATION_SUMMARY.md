# 📘 BÁO CÁO TỔNG HỢP TRIỂN KHAI HỆ THỐNG EVENT SOURCING & CQRS CHO DỰ ÁN PANELFORGE

> **Dự án**: PanelForge: An AI-Assisted Manga Production Management Platform  
> **Nền tảng công nghệ**: .NET 9, C# 13, PostgreSQL, Marten Event Store, MediatR (CQRS), Entity Framework Core, xUnit, Docker, GitHub Actions  
> **Nhánh thực hiện**: `test-feature`  
> **Trạng thái**: ✅ **Hoàn thành toàn diện — Build Succeeded — 64/64 Unit Tests Passed**

---

## 1. Bối cảnh & Yêu cầu cốt lõi (Non-Functional Requirements)

Hệ thống quản lý sản xuất truyện tranh Manga của PanelForge không phải là ứng dụng CRUD thông thường. Dữ liệu được quản lý theo mô hình phân cấp nội dung có cấu trúc:
$$\text{Series} \longrightarrow \text{Chapter} \longrightarrow \text{Page} \longrightarrow \text{Panel} \longrightarrow \text{Element (Artwork, Dialogue Balloon, SFX, Narration Box...)}$$

Các yêu cầu kiến trúc bắt buộc:
1. **Append-only Event Store (Event Sourcing)**:
   * Tuyệt đối **không ghi đè (overwrite)** và **không xóa cứng (hard delete)** bất kỳ phần tử nào đã lưu để bảo đảm tính toàn vẹn của lịch sử phiên bản (*Full Revision Provenance*).
   * Mọi thao tác Create, Update, Delete đều được chuyển thành các Sự kiện nghiệp vụ (*Domain Events*) và ghi nối tiếp vào Event Store.
2. **CQRS (Command Query Responsibility Segregation)**:
   * Tách biệt hoàn toàn luồng ghi (Command) và luồng đọc (Query) thông qua MediatR.
3. **Giữ vững kiến trúc Clean Architecture**:
   * Phân tách nghiêm ngặt giữa các tầng: Domain, Application, Infrastructure, API, và Test.

---

## 2. Chi tiết các thành phần đã triển khai theo từng Layer

### 🏛️ Tầng 1: Domain Layer (`PanelForge.Domain`)
Nơi chứa toàn bộ cốt lõi nghiệp vụ và quy tắc Event Sourcing thuần túy (không phụ thuộc vào bất kỳ thư viện bên ngoài nào):

* **[`IDomainEvent.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Domain/Common/IDomainEvent.cs)**:
  * Marker interface định danh cho tất cả các sự kiện Domain trong hệ thống.
* **[`AggregateRoot.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Domain/Common/AggregateRoot.cs)**:
  * Lớp cơ sở trừu tượng kế thừa từ `Entity<Guid>`.
  * Quản lý danh sách `_uncommittedEvents`, `Version` của Aggregate stream, các hàm `RaiseEvent()`, `ClearUncommittedEvents()` và phương thức ảo `Apply(IDomainEvent)`.
* **[`Page.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Domain/Entities/Content/Page.cs) (Aggregate Root)**:
  * Đóng vai trò là Aggregate Root kiểm soát toàn bộ vòng đời của các phần tử (Elements) trên trang truyện.
  * Chứa trạng thái in-memory `Dictionary<Guid, ElementState> _elements`.
  * Các nghiệp vụ Domain:
    * `AddElement(...)`: Validate thông số, phát sinh `ElementAddedEvent`.
    * `MoveElement(...)`: Validate, kiểm tra idempotent (nếu tọa độ không đổi thì không sinh event), phát sinh `ElementMovedEvent` lưu cả tọa độ cũ và mới (hỗ trợ Undo/Diff).
    * `RemoveElement(...)`: Validate, phát sinh `ElementRemovedEvent` và đánh dấu `IsRemoved = true` (xóa mềm).
    * `Apply(IDomainEvent)`: Cơ chế **Replay Stream** — nạp chuỗi sự kiện trong quá khứ để tái tạo trạng thái hiện thời của Page mà không cần query bảng quan hệ.
* **[`ElementState.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Domain/Entities/Content/ElementState.cs)**:
  * Trạng thái in-memory của phần tử: `ElementId`, `ElementType`, `X`, `Y`, `Width`, `Height`, `ZIndex`, `Content`, `AssetId`, `IsRemoved`.
* **Domain Events** (Records bất biến):
  * `ElementAddedEvent`: Ghi lại sự kiện thêm mới element.
  * `ElementMovedEvent`: Ghi lại sự kiện di chuyển/resize element (lưu previous & new positions).
  * `ElementRemovedEvent`: Ghi lại sự kiện gỡ bỏ element kèm lý do (`Reason`).
* **[`ElementType.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Domain/Enums/ElementType.cs)**:
  * Cập nhật enum chuẩn ngành Manga: `DialogueBalloon`, `NarrationBox`, `SoundEffect`, `CharacterInstance`, `ArtworkLayer`.

---

### ⚡ Tầng 2: Application Layer (`PanelForge.Application`)
Hiện thực hóa mô hình CQRS phân tách luồng Command và Query thông qua MediatR:

* **[`Result<T>.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Application/Common/Result.cs)**:
  * Generic wrapper chuẩn hóa kết quả trả về (`IsSuccess`, `Value`, `ErrorMessage`) thay cho việc throw exception bừa bãi.
* **[`IPageRepository.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Application/Interfaces/IPageRepository.cs)**:
  * Cung cấp hợp đồng tải Aggregate theo stream (`GetAsync`) và lưu các event mới phát sinh (`SaveAsync`).
* **CQRS Features (`Features/Elements/`)**:
  1. **AddElement**:
     * `AddElementCommand`: Record nhận payload thêm mới.
     * `AddElementCommandHandler`: Nạp Aggregate $\rightarrow$ gọi `page.AddElement()` $\rightarrow$ gọi `_pageRepository.SaveAsync()`.
  2. **MoveElement**:
     * `MoveElementCommand`: Nhận vị trí mới + `ExpectedPageVersion`.
     * `MoveElementCommandHandler`: Kiểm tra xung đột đồng thời (**Optimistic Concurrency Control**), di chuyển và lưu event.
  3. **RemoveElement**:
     * `RemoveElementCommand` & Handler: Ghi nhận sự kiện xóa mềm vào stream.
  4. **GetPageSnapshot**:
     * `GetPageSnapshotQuery`: Nhận `PageId` và cờ `IncludeRemoved`.
     * `GetPageSnapshotQueryHandler`: Tái hiện snapshot trang, lọc bỏ các element đã xóa (nếu không yêu cầu), sắp xếp theo `ZIndex` tăng dần và map sang DTO.
* **Dependency Injection**: Tự động scan và đăng ký toàn bộ MediatR handlers trong assembly.

---

### 🗄️ Tầng 3: Infrastructure Layer (`PanelForge.Infrastructure`)
Kết nối hệ thống với PostgreSQL thông qua Marten (Event Sourcing) song song với EF Core (Auth & Relational entities):

* **Tích hợp Marten 7.36.0**:
  * Cấu hình kết nối PostgreSQL.
  * Đăng ký serializer/deserializer cho các loại Domain Events (`ElementAddedEvent`, `ElementMovedEvent`, `ElementRemovedEvent`).
  * Bật chế độ `AutoCreateSchemaObjects = AutoCreate.All` tự động quản lý schema.
* **[`MartenPageRepository.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Infrastructure/Repositories/MartenPageRepository.cs)**:
  * Sử dụng `session.Events.AggregateStreamAsync<Page>(pageId)` để replay event stream từ database.
  * Sử dụng `session.Events.Append(page.Id, page.Version, events)` để append các sự kiện mới vào stream và kiểm tra concurrency version ở tầng DB.
* **Cấu hình EF Core Hybrid**:
  * Cấu hình `builder.Ignore(p => p.Version)`, `builder.Ignore(p => p.UncommittedEvents)`, `builder.Ignore(p => p.Elements)` trong [`PageConfiguration.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Infrastructure/Persistence/Configurations/Content/PageConfiguration.cs) để EF Core không làm lệch schema với các trường của AggregateRoot.
  * Giữ nguyên vẹn các mối quan hệ khoá ngoại phục vụ truy vấn metadata (`Chapter`, `Scene`, `Panels`).
* **Migration & Script SQL**:
  * Tạo Migration EF Core: [`20260916040132_AddEventSourcingElements.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.Infrastructure/Persistence/Migrations/20260916040132_AddEventSourcingElements.cs) nhúng câu lệnh DDL tạo bảng Event Store.
  * Tạo Script DDL độc lập: [`docs/database/04_event_sourcing_marten_schema.sql`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/docs/database/04_event_sourcing_marten_schema.sql) tạo các bảng `mt_streams` và `mt_events` phục vụ review/báo cáo đồ án.

---

### 🌐 Tầng 4: API Layer (`PanelForge.API`)
Cung cấp các RESTful Endpoints để frontend/client giao tiếp:

* **[`ElementsController.cs`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/src/PanelForge.API/Controllers/ElementsController.cs)**:
  * `POST   /api/pages/{pageId}/elements` $\rightarrow$ Thêm Element mới vào stream.
  * `PUT    /api/pages/{pageId}/elements/{elementId}/position` $\rightarrow$ Di chuyển Element (có header `If-Match` kiểm tra version).
  * `DELETE /api/pages/{pageId}/elements/{elementId}` $\rightarrow$ Xóa mềm Element (append delete event).
  * `GET    /api/pages/{pageId}/elements` $\rightarrow$ Lấy snapshot trạng thái trang hiện tại (lọc theo cờ `includeRemoved`).

---

### 🧪 Tầng 5: Testing Layer (`test/PanelForge.UnitTests`)
Tái cấu trúc và mở rộng bộ kiểm thử tự động toàn diện theo chuẩn xUnit, FluentAssertions và Moq:

```
test/PanelForge.UnitTests/
├── Common/
│   ├── DbSetMockHelper.cs                              # Helper giả lập truy vấn bất đồng bộ EF Core
│   └── TestAsyncQueryProvider.cs                       # IAsyncQueryProvider & IAsyncEnumerator
├── Domain/
│   ├── PageAggregateTests.cs                           # Event Sourcing, AggregateRoot & Replay (14 tests)
│   ├── UserEntityTests.cs                              # User Aggregate, Email, Password, 2FA (10 tests)
│   └── ChapterAndSceneTests.cs                         # VersionVector, Reorder Scene, Navigation (5 tests)
├── Application/
│   └── Elements/
│       ├── AddElementCommandHandlerTests.cs            # Thêm Element, validate kích thước (3 tests)
│       ├── MoveElementCommandHandlerTests.cs           # Di chuyển, Optimistic Concurrency (3 tests)
│       ├── RemoveElementCommandHandlerTests.cs         # Xóa mềm (Soft Delete) qua Append-only (2 tests)
│       └── GetPageSnapshotQueryHandlerTests.cs         # Lọc Active/Removed, sắp xếp Z-Index (3 tests)
└── Infrastructure/
    └── Authentication/
        ├── AuthServiceTests.cs                         # Luồng Auth đầy đủ: Register, Login, 2FA, Reset (19 tests)
        └── BcryptPasswordHasherTests.cs                # Mã hóa & kiểm tra hash BCrypt (3 tests)
```

**Kết quả chạy thực tế**: **64/64 test cases Passed 100%** trong thời gian ~700ms.

---

### 🚢 Tầng 6: CI/CD Pipeline & Docker
* **[`.github/workflows/ci.yml`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/.github/workflows/ci.yml)**:
  * Tự động khởi tạo PostgreSQL service ảo (`postgres:17-alpine`) với health-check.
  * Chạy `dotnet restore`, `dotnet build --configuration Release`.
  * Chạy tự động `dotnet test --no-build --configuration Release --verbosity normal` kiểm thử toàn bộ 64 tests.
  * Thực hiện `docker build -t panelforge-api:ci -f Dockerfile .` kiểm tra tính hợp lệ của Docker image.
* **[`Dockerfile`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/Dockerfile) & [`docker-compose.yml`](file:///d:/Program%20Files/Downloads/Project%20SEP490/Backend/PanelForge_FA26SE251/docker-compose.yml)**:
  * Multi-stage build tối ưu hóa dung lượng image và cache layer cho .NET 9 ASP.NET Core runtime.

---

## 3. Cấu trúc Database PostgreSQL sau khi áp dụng

Hệ thống hoạt động theo mô hình Hybrid Database trên cùng một cơ sở dữ liệu `panelforge`:

| Tên bảng | Cơ chế quản lý | Mục đích sử dụng |
|---|---|---|
| `users`, `studio_workspaces`, `workspace_members` | EF Core | Quản lý người dùng, phân quyền, bảo mật 2FA, workspace |
| `series`, `chapters`, `scenes`, `script_lines` | EF Core | Quản lý phân cấp kịch bản, phiên bản chương truyện |
| `pages`, `panels` | EF Core | Quản lý layout, tọa độ khung tranh polygon, metadata khổ trang |
| **`mt_streams`** | **Marten Event Store** | **Quản lý stream phiên bản của từng trang truyện (Aggregate)** |
| **`mt_events`** | **Marten Event Store** | **Append-only Event Log lưu bất biến mọi thao tác Element (Add, Move, Delete)** |

---

## 4. Hướng dẫn nhanh cho các thành viên trong nhóm

### 1. Kéo code mới về máy
```bash
git pull origin test-feature
```

### 2. Cập nhật Database
Đảm bảo PostgreSQL đang chạy (qua Docker Compose hoặc Postgres local), sau đó chạy:
```powershell
dotnet ef database update --project src/PanelForge.Infrastructure --startup-project src/PanelForge.API
```

### 3. Chạy Unit Test kiểm tra
```powershell
dotnet test
```

### 4. Khởi động API và trải nghiệm Swagger
```powershell
dotnet run --project src/PanelForge.API
```
Mở trình duyệt truy cập: `http://localhost:5000/swagger` (hoặc cổng cấu hình) để thử nghiệm cụm API `/api/pages/{pageId}/elements`.
