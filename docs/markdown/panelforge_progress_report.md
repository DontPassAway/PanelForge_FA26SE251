# PanelForge — Báo Cáo Tiến Độ Codebase & Phân Công Team

> **Ngày kiểm tra:** 18/09/2026  
> **Team:** 5 người (3 Backend, 2 Frontend)  
> **Tech Stack:** .NET 9 / ASP.NET Core · PostgreSQL · EF Core · Marten (Event Store) · Firebase · MediatR · CQRS

---

## 1. Tổng Quan Kiến Trúc Đã Xây Dựng

Dự án được tổ chức theo kiến trúc **Clean Architecture 4 lớp** + **Event Sourcing / CQRS**:

```
PanelForge_FA26SE251/
├── src/
│   ├── PanelForge.Domain          ← Entities, Domain Events, Aggregates
│   ├── PanelForge.Application     ← CQRS Commands/Queries, Interfaces
│   ├── PanelForge.Infrastructure  ← EF Core, Marten, Auth, Email, Firebase
│   └── PanelForge.API             ← REST Controllers, Swagger
├── test/
│   └── PanelForge.UnitTests       ← Unit tests (Domain + Application + Infrastructure)
├── docs/                          ← Tài liệu thiết kế
├── Dockerfile + docker-compose.yml
└── .github/workflows/ci.yml       ← CI/CD pipeline
```

---

## 2. Những Gì ĐÃ Làm Xong ✅

### 2.1 Nền Tảng Kiến Trúc (Domain Layer)

| Thành phần | File | Trạng thái |
|---|---|---|
| Base `AggregateRoot` (Event Sourcing pattern) | `Domain/Common/AggregateRoot.cs` | ✅ Hoàn thiện |
| `IDomainEvent` interface | `Domain/Common/IDomainEvent.cs` | ✅ Hoàn thiện |
| `BaseEntity` | `Domain/Common/BaseEntity.cs` | ✅ Hoàn thiện |

**Domain Entities — Content Hierarchy (Series → Chapter → Scene → Page → Panel → Element):**

| Entity | File | Trạng thái |
|---|---|---|
| `Series` | `Entities/Content/Series.cs` | ✅ Hoàn thiện (schema) |
| `Chapter` | `Entities/Content/Chapter.cs` | ✅ Hoàn thiện (schema) |
| `Scene` | `Entities/Content/Scene.cs` | ✅ Hoàn thiện (schema) |
| `Page` (**Aggregate Root**) | `Entities/Content/Page.cs` | ✅ Hoàn thiện — **Event Sourced**, đầy đủ domain methods |
| `Panel` | `Entities/Content/Panel.cs` | ✅ Hoàn thiện (schema) |
| `Element` + `ElementState` | `Entities/Content/Element.cs`, `ElementState.cs` | ✅ Hoàn thiện |
| `ScriptLine` | `Entities/Content/ScriptLine.cs` | ✅ Hoàn thiện (schema) |

**Domain Entities — Auth/Workspace:**

| Entity | File | Trạng thái |
|---|---|---|
| `User` (đầy đủ auth fields) | `Entities/Auth/User.cs` | ✅ Hoàn thiện |
| `StudioWorkspace` | `Entities/Auth/StudioWorkspace.cs` | ✅ Hoàn thiện |
| `WorkspaceMember` | `Entities/Auth/WorkspaceMember.cs` | ✅ Hoàn thiện |
| `ExternalPreviewLink` | `Entities/Auth/ExternalPreviewLink.cs` | ✅ Hoàn thiện (schema) |

**Domain Enums:**

| Enum | Trạng thái |
|---|---|
| `ElementType` | ✅ (DialogueBalloon, NarrationBox, SoundEffect, CharacterInstance, ArtworkLayer...) |
| `LayoutFormat` | ✅ |
| `ReadingDirection` | ✅ |
| `SystemRole` | ✅ (Admin, Moderator, User) |
| `WorkspaceRole` | ✅ (Producer, Writer, Artist, Letterer, Editor, Viewer) |

**Domain Events (Event Store — 6 events):**

| Event | Trạng thái |
|---|---|
| `PageCreatedEvent` | ✅ |
| `PageCanvasUpdatedEvent` | ✅ |
| `PageReorderedEvent` | ✅ |
| `ElementAddedEvent` | ✅ |
| `ElementMovedEvent` | ✅ (lưu cả vị trí cũ — audit trail) |
| `ElementRemovedEvent` | ✅ (Soft Delete — record vẫn tồn tại trong stream) |

---

### 2.2 Application Layer (CQRS với MediatR)

#### Features/Elements — Event Sourced

| Command/Query | Handler | Trạng thái |
|---|---|---|
| `AddElementCommand` | `AddElementCommandHandler` | ✅ |
| `MoveElementCommand` | `MoveElementCommandHandler` | ✅ (Optimistic Concurrency `ExpectedPageVersion`) |
| `RemoveElementCommand` | `RemoveElementCommandHandler` | ✅ (Soft Delete) |
| `GetPageSnapshotQuery` | `GetPageSnapshotQueryHandler` | ✅ (rebuild từ Event Store, hỗ trợ `includeRemoved`) |

#### Features/Pages — CQRS

| Command/Query | Handler | Trạng thái |
|---|---|---|
| `CreatePageCommand` | `CreatePageCommandHandler` | ✅ (tạo stream mới trên Marten) |
| `DeletePageCommand` | `DeletePageCommandHandler` | ✅ |
| `GetPageByIdQuery` | `GetPageByIdQueryHandler` | ✅ |
| `GetPagesByChapterQuery` | `GetPagesByChapterQueryHandler` | ✅ |
| `UpdateCanvasSettingsCommand` | `UpdateCanvasSettingsCommandHandler` | ✅ |

#### Features/Users — CQRS

| Command/Query | Handler | Trạng thái |
|---|---|---|
| `GetUserProfileQuery` | `GetUserProfileQueryHandler` | ✅ (theo Guid hoặc Firebase UID) |
| `UpdateUserProfileCommand` | `UpdateUserProfileCommandHandler` | ✅ |
| `SoftDeleteUserCommand` | `SoftDeleteUserCommandHandler` | ✅ |

#### Features/Auth & Workspaces — Models/DTOs

| Feature | Trạng thái |
|---|---|
| Auth models (Register, Login, VerifyEmail, ForgotPassword, ResetPassword, 2FA, ExternalLogin) | ✅ |
| Workspace models (Create, Update, AddMember, UpdateMemberRole) | ✅ |

---

### 2.3 Infrastructure Layer

#### Persistence (EF Core — PostgreSQL)

| Thành phần | Trạng thái |
|---|---|
| `PanelForgeDbContext` | ✅ |
| EF Configurations cho tất cả entities (User, Workspace, WorkspaceMember, ExternalPreviewLink, Series, Chapter, Scene, Page, Panel, Element, ScriptLine) | ✅ |
| **6 Database Migrations** (InitialCreate → AddUserPasswordHash → AddUserAuthAndTwoFactorFields → AddAuthTables → AddSystemRoleToUser → AddEventSourcingElements) | ✅ |

#### Event Store (Marten — PostgreSQL `mt_events`)

| Thành phần | Trạng thái |
|---|---|
| `MartenPageRepository` (IPageRepository) | ✅ — Append-only, FetchForWriting, AggregateStream, Optimistic Concurrency |
| Marten cấu hình event types + AutoCreateSchemaObjects | ✅ |

#### Authentication & Security

| Thành phần | Trạng thái |
|---|---|
| JWT Token Generator | ✅ |
| BCrypt Password Hasher | ✅ |
| Firebase Integration (External Login) | ✅ |
| `WorkspaceAuthorizationService` (RBAC cấp workspace) | ✅ |

#### Services

| Service | Trạng thái |
|---|---|
| `AuthService` | ✅ — Register, Login, VerifyEmail, ForgotPassword, ResetPassword, ChangePassword, ResendVerification, ExternalLogin (Firebase), Enable2FA, Verify2FA |
| `WorkspaceService` | ✅ — Create, Get, Update, Delete Workspace; Get/Add/Update/Remove Members |
| `EmailService` | ✅ (SMTP) |

---

### 2.4 API Layer (REST Controllers)

| Controller | Endpoints đã có | Trạng thái |
|---|---|---|
| `AuthController` | POST register, login, logout, change-password, forgot-password, reset-password, verify-email, resend-verification, external-login, enable-2fa, verify-2fa; GET me | ✅ 12 endpoints |
| `UsersController` | GET me, GET {id}, GET firebase/{uid}, PUT {id}, PUT firebase/{uid}, DELETE {id}, DELETE firebase/{uid} | ✅ 7 endpoints |
| `WorkspacesController` | POST /, GET /, GET {id}, PUT {id}, DELETE {id}, GET {id}/members, POST {id}/members, PUT {id}/members/{userId}, DELETE {id}/members/{userId} | ✅ 9 endpoints |
| `PagesController` | POST chapters/{id}/pages, GET chapters/{id}/pages, GET pages/{id}, PUT pages/{id}/canvas-settings, DELETE pages/{id} | ✅ 5 endpoints |
| `ElementsController` | GET pages/{id}/elements (snapshot), POST pages/{id}/elements, PUT pages/{id}/elements/{eid}/position, DELETE pages/{id}/elements/{eid} | ✅ 4 endpoints |
| `AdminController` | GET users (paged), POST assign-role, GET system-stats | ✅ 3 endpoints |

**Middleware & Cross-cutting:**
- JWT Bearer Authentication ✅
- CORS (AllowFrontend `localhost:3000`) ✅
- Swagger/OpenAPI với Bearer auth ✅
- `RequireWorkspaceRole` custom attribute ✅

---

### 2.5 DevOps & CI/CD

| Thành phần | Trạng thái |
|---|---|
| `Dockerfile` (multi-stage build) | ✅ |
| `docker-compose.yml` (API + PostgreSQL) | ✅ |
| GitHub Actions CI (`ci.yml`) | ✅ (build + test tự động) |

---

### 2.6 Unit Tests

| Test Suite | Số lượng | Trạng thái |
|---|---|---|
| **Domain Tests** | | |
| `PageAggregateTests` | ✅ đầy đủ |
| `ChapterAndSceneTests` | ✅ |
| `UserEntityTests` | ✅ |
| `StudioWorkspaceTests` | ✅ |
| **Application Tests** | | |
| `AddElementCommandHandlerTests` | ✅ |
| `MoveElementCommandHandlerTests` | ✅ |
| `RemoveElementCommandHandlerTests` | ✅ |
| `GetPageSnapshotQueryHandlerTests` | ✅ |
| `CreatePageCommandHandlerTests` | ✅ |
| `GetPagesByChapterQueryHandlerTests` | ✅ |
| `UpdateCanvasSettingsCommandHandlerTests` | ✅ |
| `GetUserProfileQueryHandlerTests` | ✅ |
| `UpdateUserProfileCommandHandlerTests` | ✅ |
| `SoftDeleteUserCommandHandlerTests` | ✅ |
| **Infrastructure Tests** | | |
| `AuthServiceTests` | ✅ (đầy đủ các flows) |
| `BcryptPasswordHasherTests` | ✅ |
| `WorkspaceAuthorizationServiceTests` | ✅ |

**Tổng: ~17 test classes, ước tính 60–80+ test cases.**

---

### 2.7 Tài Liệu

| Tài liệu | Trạng thái |
|---|---|
| `docs/requirements.md` | ✅ |
| `docs/project-plan.md` | ✅ |
| `docs/implementation-qa.md` | ✅ |
| `docs/database/` | ✅ |
| README.md (16KB) | ✅ |

---

## 3. Những Gì CHƯA Làm ❌

### 3.1 Domain / Data Model (chưa có)

| Hạng mục | Mức độ ưu tiên |
|---|---|
| `SeriesBible` entity (Characters, Locations, Props, Terminology, StyleRules, PlotFacts) | 🔴 **CRITICAL** |
| `PipelineStage` / Workflow state machine entity | 🔴 **CRITICAL** |
| `Assignment` (task queue cho Artist/Writer/Letterer) | 🔴 **CRITICAL** |
| `Review` / `ChangeRequest` / `Annotation` entities | 🔴 **CRITICAL** |
| `ProvenanceRecord` / AI usage log entity | 🔴 **CRITICAL** |
| `Notification` entity | 🟡 Quan trọng |
| `ActivityFeed` entity | 🟡 Quan trọng |

### 3.2 Backend Features (chưa implement)

| Hạng mục | Mức độ ưu tiên |
|---|---|
| Series CRUD (Create, Update, Get, Delete) | 🔴 **CRITICAL** |
| Chapter CRUD (Create, Update, Get, Delete) | 🔴 **CRITICAL** |
| Scene CRUD | 🟡 Quan trọng |
| Panel CRUD (hiện chỉ có schema, chưa có API) | 🔴 **CRITICAL** |
| **Series Bible** — CRUD + versioning | 🔴 **CRITICAL** |
| **Production Pipeline** — workflow engine, state transitions | 🔴 **CRITICAL** |
| **Assignment management** — assign/deadline/dependency | 🔴 **CRITICAL** |
| **Review & Annotation** — region-anchored comments | 🔴 **CRITICAL** |
| **Version history** — diff, rollback, side-by-side compare | 🔴 **CRITICAL** |
| **Provenance subsystem** — AI contribution logging | 🔴 **CRITICAL** |
| **External Preview Link** (watermarked, time-limited) | 🟡 Quan trọng |
| **Export pipeline** (PDF, CBZ, PNG, webtoon) | 🟡 Quan trọng |
| Asset/Object Storage integration (S3 / MinIO) | 🟡 Quan trọng |
| Search across scripts, panels, bible entries | 🟡 Quan trọng |
| Notification system (in-app + email) | 🟡 Quan trọng |

### 3.3 AI Services (chưa có gì)

| Hạng mục | Mức độ ưu tiên |
|---|---|
| Script-to-structure breakdown (LLM) | 🟡 Quan trọng |
| Series Bible grounded consistency checking (RAG + vector DB) | 🟡 Quan trọng |
| Character continuity tracking | 🟢 Extended scope |
| Panel layout suggestion | 🟢 Extended scope |
| Balloon-fit analysis | 🟢 Extended scope |

### 3.4 Frontend (chưa có gì)

| Hạng mục | Mức độ ưu tiên |
|---|---|
| Toàn bộ giao diện người dùng | 🔴 **CRITICAL** |
| Structured script editor | 🔴 **CRITICAL** |
| Panel canvas & element editor | 🔴 **CRITICAL** |
| Thumbnail / page layout planner | 🔴 **CRITICAL** |
| Lettering canvas | 🟡 Quan trọng |
| Region-anchored annotation layer | 🔴 **CRITICAL** |
| Side-by-side version comparison | 🔴 **CRITICAL** |
| Producer dashboard (panel-level progress) | 🔴 **CRITICAL** |
| Personal task queue | 🟡 Quan trọng |
| Reading / Preview mode | 🟡 Quan trọng |
| Series Bible UI | 🔴 **CRITICAL** |
| Auth flows (Login, Register, 2FA) | 🔴 **CRITICAL** |
| Workspace management UI | 🔴 **CRITICAL** |

---

## 4. Đề Xuất Phân Công (3 BE + 2 FE)

> **Nguyên tắc:** WP1 (Domain + Schema) và WP2 (Event Store + Provenance) phải xong trước để các WP còn lại xây dựng trên nền tảng đó.

### 👤 BE1 — Domain Engineer (WP2 mở rộng + WP1 bổ sung)

**Việc đã làm:** Đây là người đã xây dựng phần lớn Domain + Event Sourcing hiện tại.

**Việc cần làm tiếp:**

- [ ] Thiết kế và implement `SeriesBible` aggregate (Characters, Locations, Props, Terminology, StyleRules, PlotFacts) với versioning
- [ ] Thiết kế entities `Review`, `ChangeRequest`, `Annotation` (region-anchored)
- [ ] Thiết kế và implement `ProvenanceRecord` entity — lưu model, parameters, input context, accepting user, degree of human modification
- [ ] Extend Event Store: thêm domain events cho Series Bible changes, Review actions
- [ ] Version diff & rollback cho Page, Element, ScriptLine
- [ ] EF Migrations cho toàn bộ entities mới
- [ ] Unit tests cho tất cả aggregates mới

**Output:** Series → Element hierarchy hoàn chỉnh với full provenance; Series Bible domain model

---

### 👤 BE2 — Production Pipeline & Access Control (WP3)

**Việc đã làm:** Kế thừa WorkspaceService + WorkspaceAuthorizationService đã có.

**Việc cần làm:**

- [ ] **Workflow Engine:** Configurable pipeline stages (Script → Thumbnail → Pencil → Ink → Color → Letter → Review → Approved) — state machine với guarded transitions
- [ ] **Assignment system:** Assign Panel/Page cho Artist/Writer/Letterer, set deadline, track dependency
- [ ] **Review workflow:** Submit → Review → Change Request → Approved/Rejected, track resolution
- [ ] **Notification system:** In-app + email triggers (assignment, mention, review, deadline)
- [ ] **ABAC:** Attribute-Based Access Control — time-limited external reviewer links
- [ ] **Asset access logging** — audit trail cho từng lần xem/download asset
- [ ] **External Preview Link** — watermarked, expiring links
- [ ] **Export pipeline** — render Page data thành PNG, PDF, CBZ, webtoon scroll
- [ ] API endpoints cho toàn bộ tính năng trên
- [ ] Unit + Integration tests

**Output:** Production pipeline hoàn chỉnh; Notification system; Export pipeline

---

### 👤 BE3 — Series/Chapter CRUD + AI Services (WP5 + WP1 API)

**Việc đã làm:** Kế thừa codebase sạch, CI/CD đã setup.

**Việc cần làm:**

- [ ] **Series CRUD API:** Create, Get, Update, Delete Series + SeriesController
- [ ] **Chapter CRUD API:** Create, Get, Update, Delete Chapter
- [ ] **Scene + Panel CRUD API:** Hoàn thiện Panel controller (hiện chỉ có schema)
- [ ] **ScriptLine CRUD API** (Structured script editor backend)
- [ ] **Search API:** Cross-entity search (scripts, panels, bible entries, comments)
- [ ] **AI Service:** Script-to-structure breakdown (LLM integration — OpenAI/Gemini)
  - Nhận script text → phân tách thành Scenes, Pages, Panels, Elements (suggestion chờ human accept/reject)
  - Log provenance: model, prompt, timestamp, accepting user
- [ ] **Series Bible RAG:** Vector embedding + consistency checking
- [ ] Tích hợp với object storage (Cloudflare R2 / AWS S3 / MinIO) cho asset upload
- [ ] Integration tests, deployment scripts
- [ ] Mở rộng CI/CD để bao gồm integration tests + Docker build

**Output:** Series/Chapter/Panel API hoàn chỉnh; AI service layer với provenance

---

### 👤 FE1 — Production Workspace & Core Editor (WP4 phần 1)

**Việc đã làm:** Chưa có gì — bắt đầu từ đầu.

**Việc cần làm:**

- [ ] **Setup Frontend project** (Next.js 14 + TypeScript + Tailwind CSS)
- [ ] **Auth flows:** Login, Register, Email Verification, 2FA, Forgot/Reset Password — kết nối API đã có
- [ ] **Workspace management:** Create/List/Update workspace, Invite/Manage members
- [ ] **Series & Chapter management:** Create/List/View Series, Chapter timeline
- [ ] **Panel Canvas Editor:** 
  - Drag-drop elements (DialogueBalloon, NarrationBox, SoundEffect, ArtworkLayer)
  - Resize, reorder by Z-index
  - Kết nối với Elements API (Event Sourcing, Optimistic Concurrency)
- [ ] **Structured Script Editor:** Scene/dialogue/direction blocks, speaker assignment
- [ ] **Thumbnail & Page Layout Planner:** Grid-based panel planning
- [ ] **Personal Task Queue:** Danh sách công việc theo người dùng hiện tại

**Output:** Toàn bộ Production Workspace UI

---

### 👤 FE2 — Review, Dashboard & Advanced Features (WP4 phần 2)

**Việc đã làm:** Chưa có gì — bắt đầu từ đầu.

**Việc cần làm:**

- [ ] **Region-anchored Annotation Layer:** Vẽ annotation trực tiếp lên panel, gắn comment vào region cụ thể
- [ ] **Side-by-side Version Comparison:** So sánh 2 version của Page/Element
- [ ] **Review Workflow UI:** Submit, Review, Change Request, Approve/Reject
- [ ] **Producer Dashboard:** Panel-level progress tracking, overdue items, burndown chart, workload view
- [ ] **Series Bible UI:** CRUD Characters/Locations/Props, link script entities to Bible entries
- [ ] **Version History Panel:** Xem event log của từng entity, rollback UI
- [ ] **Provenance Viewer:** Xem AI contribution record cho mỗi artifact
- [ ] **Reading & Preview Mode:** Chapter reading UI, external preview link viewer (watermarked)
- [ ] **Admin Panel:** User management, assign roles, system stats
- [ ] **Notification Center:** In-app notifications, mark as read
- [ ] **Bilingual i18n:** Vietnamese / English toggle

**Output:** Review system UI; Dashboard; Admin panel; Series Bible UI; i18n

---

## 5. Bảng Tổng Hợp Tiến Độ Theo WP

| Work Package | Người phụ trách | Đã làm | Còn lại | Ước tính effort |
|---|---|---|---|---|
| **WP1** – Domain Analysis & Schema | BE1 + PM | ~60% (content model, auth, events) | Series Bible schema, Provenance schema, docs | 2–3 tuần |
| **WP2** – Event Store & Provenance | BE1 | ~40% (Page aggregate, 6 events, Marten repo) | Series Bible versioning, diff/rollback, full provenance | 3–4 tuần |
| **WP3** – Pipeline, Access Control, Export | BE2 | ~15% (RBAC workspace cơ bản) | Workflow engine, assignments, reviews, export | 4–5 tuần |
| **WP4** – Frontend Workspace | FE1 + FE2 | 0% | Toàn bộ UI | 5–6 tuần |
| **WP5** – AI Services, QA, Deployment | BE3 | ~20% (CI/CD, Docker) | AI service, vector DB, integration tests | 3–4 tuần |

---

## 6. Lưu Ý Quan Trọng

> [!IMPORTANT]
> **Thứ tự ưu tiên bắt buộc:**
> 1. Series + Chapter API (BE3) phải xong trước khi FE1 có thể làm Series/Chapter UI
> 2. Pipeline Stage entity (BE2) phải xong trước khi FE2 làm Dashboard
> 3. Series Bible entity (BE1) phải xong trước khi BE3 làm AI consistency checking

> [!WARNING]
> **Provenance không thể retrofit:** Nếu bắt đầu làm AI features mà chưa có `ProvenanceRecord` schema trong Event Store, sẽ phải rebuild persistence layer. BE1 phải thiết kế và migrate Provenance trước khi BE3 tích hợp AI.

> [!NOTE]
> **Những gì đã làm tốt và nên giữ nguyên:**
> - Kiến trúc Event Sourcing với Marten cho Page aggregate — **rất đúng hướng**, là điểm khác biệt cốt lõi của đề tài
> - Optimistic Concurrency (`ExpectedPageVersion`) — đã implement đúng
> - Soft Delete cho Elements thông qua `ElementRemovedEvent` — data không bao giờ mất
> - BCrypt + JWT + Firebase — auth stack đầy đủ và đúng chuẩn
> - Unit test coverage tốt cho phần đã làm

> [!CAUTION]
> **Rủi ro lớn nhất:** Frontend hoàn toàn chưa có. Với 2 FE và scope rất lớn (panel canvas editor, annotation layer, version comparison, dashboard), team FE cần bắt đầu ngay lập tức song song với BE. Có thể cần dùng mock API (MSW) để FE không bị block.

---

## 7. Các Endpoint API Sẵn Sàng Để FE Kết Nối Ngay

| Module | Endpoints sẵn có |
|---|---|
| **Auth** | `/api/auth/register`, `/api/auth/login`, `/api/auth/verify-email`, `/api/auth/forgot-password`, `/api/auth/reset-password`, `/api/auth/change-password`, `/api/auth/external-login`, `/api/auth/enable-2fa`, `/api/auth/verify-2fa` |
| **User** | `/api/users/me`, `/api/users/{id}`, `/api/users/firebase/{uid}`, PUT/DELETE tương ứng |
| **Workspace** | `/api/workspaces` (CRUD), `/api/workspaces/{id}/members` (CRUD) |
| **Page** | `/api/chapters/{id}/pages` (CRUD), `/api/pages/{id}/canvas-settings` |
| **Element** | `/api/pages/{id}/elements` (CRUD + snapshot với `includeRemoved`) |
| **Admin** | `/api/admin/users`, `/api/admin/assign-role`, `/api/admin/system-stats` |

**Tổng: ~40 endpoints đã có, có thể dùng ngay.**

---

*Báo cáo được tạo tự động từ việc phân tích toàn bộ codebase tại `d:\Program Files\Downloads\Project SEP490\Backend\PanelForge_FA26SE251`*
