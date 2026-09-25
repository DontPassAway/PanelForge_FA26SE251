# PanelForge — Core Flow 1 (CF1): Phase 1 Implementation Report

> **Session Date:** 2026-09-25  
> **Build Status:** ✅ `Build succeeded. 0 Warning(s). 0 Error(s).`  
> **Test Status:** ✅ `Passed! - Failed: 0, Passed: 156, Skipped: 0, Total: 156`  
> **Target Scope:** Phase 1 — Security & Bug Fixes (Bảo mật & Sửa lỗi CF1)  
> **References:** `docs/markdown/PanelForge_Capstone_Content.md` (BR-07, BR-22, BR-23, UC-03, NFR-08)  

---

## 1. Mục tiêu & Phạm vi thực hiện (Scope of Phase 1)

Giai đoạn 1 tập trung vào việc khắc phục các lỗ hổng bảo mật, hoàn thiện cơ chế phân quyền (Two-Level Access Control - BR-07), đồng bộ DTO giữa Backend và Frontend, và bảo vệ các endpoint thao tác tài nguyên:

| # | Hạng mục công việc | Mô tả & Quy tắc nghiệp vụ | Trạng thái |
|---|-------------------|---------------------------|------------|
| **1.1** | Bảo mật `SeriesController` | Thêm `[Authorize]` cấp class (401 khi thiếu token). Gắn `[RequireWorkspaceRole(WorkspaceRole.Producer)]` cho các thao tác tạo/sửa/xóa Series, TypographyPreset, ConsistencyRule. Gắn `[RequireWorkspaceRole]` cho các thao tác đọc (GET). Mở rộng filter tự động resolve `WorkspaceId` từ `SeriesId` (403 khi thiếu quyền). | ✅ Đã hoàn thành |
| **1.2** | Đồng bộ `PipelineTemplateId` | Đổi tên `PipelineDefinitionId` → `PipelineTemplateId` trong `CreateSeriesRequest` DTO, `SeriesController.CreateSeries`, và interface frontend `panelforge-fe/src/services/series.service.ts`. | ✅ Đã hoàn thành |
| **1.3** | Phân quyền `WorkspacesController` | Bảo vệ `GET {id}/audit-logs` (chỉ WorkspaceRole.Producer) và `GET {id}/ai-config` (chỉ thành viên Workspace). Không tin cậy workspaceId từ client khi chưa thẩm thực qua filter. | ✅ Đã hoàn thành |
| **1.4** | Triệt để quy tắc BR-07 | Administrator không được có dòng `workspace_members`. Chặn thêm Admin vào workspace trong `WorkspaceService.AddMemberAsync`. Chặn cấp `CanCreateStudio` cho Admin trong `GrantStudioCreationPermissionCommand`. Viết unit test cho cả 2 trường hợp. | ✅ Đã hoàn thành |

---

## 2. Chi tiết kỹ thuật các thay đổi (Implementation Details)

### 2.1 Mở rộng `RequireWorkspaceRoleAttribute` & Filter
* **File:** `src/PanelForge.API/Common/Attributes/RequireWorkspaceRoleAttribute.cs`
* **Cải tiến:**
  * Inject `IPanelForgeDbContext` vào `RequireWorkspaceRoleFilter` thông qua DI container của ASP.NET Core (`TypeFilterAttribute`).
  * Tự động nhận diện tham số `{seriesId:guid}` trên route: nếu có, filter sẽ truy vấn cơ sở dữ liệu để lấy `WorkspaceId` của Series đó. Nếu Series không tồn tại, trả về ngay **HTTP 404 Not Found**.
  * Hỗ trợ khai báo không kèm tham số vai trò `[RequireWorkspaceRole]`: khi `_allowedRoles.Length == 0`, filter sẽ kiểm tra xem người dùng có phải là thành viên hợp lệ của Workspace hay không thông qua `IWorkspaceAuthorizationService.IsMemberAsync`.
  * Trả về **HTTP 401 Unauthorized** khi thiếu hoặc không parse được Claim danh tính người dùng.
  * Trả về **HTTP 403 Forbidden** với thông báo tiếng Việt rõ ràng khi người dùng không đủ quyền.

```csharp
// Xử lý gián tiếp qua seriesId
else if (context.RouteData.Values.TryGetValue("seriesId", out var seriesVal) && seriesVal is string seriesStr && Guid.TryParse(seriesStr, out var parsedSeriesId))
{
    var seriesWsId = await _dbContext.Series
        .Where(s => s.Id == parsedSeriesId)
        .Select(s => (Guid?)s.WorkspaceId)
        .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

    if (seriesWsId == null)
    {
        context.Result = new NotFoundObjectResult(new { message = "Không tìm thấy Series tương ứng." });
        return;
    }

    workspaceId = seriesWsId.Value;
}
```

### 2.2 Phân quyền toàn diện trên `SeriesController`
* **File:** `src/PanelForge.API/Controllers/SeriesController.cs`
* Bổ sung `[Authorize]` ở cấp controller.
* Áp dụng quyền **Producer** (`[RequireWorkspaceRole(WorkspaceRole.Producer)]`):
  * `POST /api/workspaces/{workspaceId:guid}/series`
  * `PUT /api/series/{seriesId:guid}`
  * `DELETE /api/series/{seriesId:guid}`
  * `POST /api/series/{seriesId:guid}/presets`
  * `PUT /api/series/{seriesId:guid}/presets/{presetId:guid}`
  * `DELETE /api/series/{seriesId:guid}/presets/{presetId:guid}`
  * `POST /api/series/{seriesId:guid}/consistency-rules`
  * `PUT /api/series/{seriesId:guid}/consistency-rules/{ruleId:guid}`
  * `DELETE /api/series/{seriesId:guid}/consistency-rules/{ruleId:guid}`
* Áp dụng quyền **Thành viên Workspace** (`[RequireWorkspaceRole]`):
  * `GET /api/workspaces/{workspaceId:guid}/series`
  * `GET /api/series/{seriesId:guid}`
  * `GET /api/series/{seriesId:guid}/presets`
  * `GET /api/series/{seriesId:guid}/consistency-rules`

### 2.3 Đồng bộ DTO `PipelineTemplateId`
* **Backend:**
  * `src/PanelForge.Application/Features/Series/Models/SeriesDtos.cs`: Thuộc tính `PipelineDefinitionId` trong record `CreateSeriesRequest` được đổi thành:
    ```csharp
    public sealed record CreateSeriesRequest(
        string Title,
        string? Synopsis,
        ReadingDirection ReadingDirection = ReadingDirection.RightToLeft,
        Guid? PipelineTemplateId = null,
        string Genre = "Action",
        string Format = "Manga",
        string? ReleaseScheduleJson = null
    );
    ```
  * `SeriesController.CreateSeries`: truyền trực tiếp `PipelineTemplateId: body.PipelineTemplateId` sang `CreateSeriesCommand`.
* **Frontend:**
  * `panelforge-fe/src/services/series.service.ts`: Cập nhật interface `CreateSeriesRequest`:
    ```typescript
    export interface CreateSeriesRequest {
      title: string;
      synopsis?: string;
      readingDirection?: number | string;
      pipelineTemplateId?: string;
      genre?: string;
      format?: string;
      releaseScheduleJson?: string;
    }
    ```

### 2.4 Bảo vệ Endpoint trên `WorkspacesController`
* **File:** `src/PanelForge.API/Controllers/WorkspacesController.cs`
* Thêm `[RequireWorkspaceRole]` cho `GET /api/workspaces/{id:guid}/ai-config`: Ngăn chặn người dùng đọc cấu hình AI của các Workspace mà họ không tham gia.
* Thêm `[RequireWorkspaceRole(WorkspaceRole.Producer)]` cho `GET /api/workspaces/{id:guid}/audit-logs`: Chỉ Producers của Workspace mới được xem nhật ký kiểm toán nội bộ.

### 2.5 Tuân thủ triệt để Quy tắc BR-07 (Tách biệt Administrator)
* **`WorkspaceService.AddMemberAsync`:**
  * Vị trí: `src/PanelForge.Infrastructure/Services/WorkspaceService.cs`
  * Thêm kiểm tra: Nếu `targetUser.Role == SystemRole.Admin`, hệ thống ném `InvalidOperationException`: *"Administrator không được phép tham gia Workspace với tư cách thành viên (BR-07)."*
* **`GrantStudioCreationPermissionCommand`:**
  * Vị trí: `src/PanelForge.Application/Features/Users/Commands/GrantStudioPermission/GrantStudioCreationPermissionCommand.cs`
  * Thêm kiểm tra: Nếu người dùng mục tiêu có `Role == SystemRole.Admin`, từ chối cấp quyền và trả về `Result.Failure("Không thể cấp quyền tạo Studio cho tài khoản Administrator (BR-07, BR-22).")`.
* **Unit Testing:**
  * `test/PanelForge.UnitTests/Infrastructure/Services/WorkspaceServiceTests.cs`: Thêm test case `AddMemberAsync_WhenTargetUserIsAdmin_ShouldThrowInvalidOperationException`.
  * `test/PanelForge.UnitTests/Application/Users/GrantStudioCreationPermissionCommandHandlerTests.cs`: Tạo mới bộ unit test kiểm thử lệnh cấp quyền `CanCreateStudio` cho Admin và User thường.
  * `test/PanelForge.UnitTests/Application/Series/SeriesCommandTests.cs`: Cập nhật test case tạo Series tương thích với `PipelineTemplateId` và mock `PipelineTemplateStage`.

---

## 3. Danh sách các file thay đổi & tạo mới

| STT | File Path | Layer | Loại thay đổi | Mô tả ngắn |
|:---:|-----------|:-----:|:-------------:|------------|
| 1 | `src/PanelForge.API/Common/Attributes/RequireWorkspaceRoleAttribute.cs` | Presentation / API | **Modified** | Hỗ trợ resolve `WorkspaceId` từ `seriesId`, kiểm tra membership rỗng, inject DbContext. |
| 2 | `src/PanelForge.API/Controllers/SeriesController.cs` | Presentation / API | **Modified** | Thêm `[Authorize]`, gắn `[RequireWorkspaceRole]` cho toàn bộ endpoint, map `PipelineTemplateId`. |
| 3 | `src/PanelForge.API/Controllers/WorkspacesController.cs` | Presentation / API | **Modified** | Gắn phân quyền cho `GET ai-config` và `GET audit-logs`. |
| 4 | `src/PanelForge.Application/Features/Series/Models/SeriesDtos.cs` | Application | **Modified** | Đổi tên `PipelineDefinitionId` → `PipelineTemplateId` trong `CreateSeriesRequest`. |
| 5 | `panelforge-fe/src/services/series.service.ts` | Frontend (FE) | **Modified** | Đổi `pipelineDefinitionId` → `pipelineTemplateId` trong interface `CreateSeriesRequest`. |
| 6 | `src/PanelForge.Infrastructure/Services/WorkspaceService.cs` | Infrastructure | **Modified** | Chặn Admin làm thành viên Workspace trong `AddMemberAsync` (BR-07). |
| 7 | `src/PanelForge.Application/Features/Users/Commands/GrantStudioPermission/GrantStudioCreationPermissionCommand.cs` | Application | **Modified** | Chặn cấp cờ `CanCreateStudio` cho tài khoản Admin (BR-07, BR-22). |
| 8 | `test/PanelForge.UnitTests/Infrastructure/Services/WorkspaceServiceTests.cs` | Test | **Modified** | Thêm test case từ chối thêm Admin vào Workspace. |
| 9 | `test/PanelForge.UnitTests/Application/Series/SeriesCommandTests.cs` | Test | **Modified** | Fix mock data cho `PipelineTemplate` và `PipelineTemplateId`. |
| 10 | `test/PanelForge.UnitTests/Application/Users/GrantStudioCreationPermissionCommandHandlerTests.cs` | Test | **Created** | Tạo unit test xác thực logic cấp quyền StudioCreation cho Admin/User. |

---

## 4. Báo cáo Kết quả Biên dịch và Kiểm thử

### 4.1 Kết quả Biên dịch (`dotnet build`)
```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  PanelForge.Domain -> E:\Panel-forge\PanelForge_FA26SE251\src\PanelForge.Domain\bin\Debug\net9.0\PanelForge.Domain.dll
  PanelForge.Application -> E:\Panel-forge\PanelForge_FA26SE251\src\PanelForge.Application\bin\Debug\net9.0\PanelForge.Application.dll
  PanelForge.Infrastructure -> E:\Panel-forge\PanelForge_FA26SE251\src\PanelForge.Infrastructure\bin\Debug\net9.0\PanelForge.Infrastructure.dll
  PanelForge.UnitTests -> E:\Panel-forge\PanelForge_FA26SE251\test\PanelForge.UnitTests\bin\Debug\net9.0\PanelForge.UnitTests.dll
  PanelForge.API -> E:\Panel-forge\PanelForge_FA26SE251\src\PanelForge.API\bin\Debug\net9.0\PanelForge.API.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.29
```

### 4.2 Kết quả Kiểm thử tự động (`dotnet test`)
```text
Test run for E:\Panel-forge\PanelForge_FA26SE251\test\PanelForge.UnitTests\bin\Debug\net9.0\PanelForge.UnitTests.dll (.NETCoreApp,Version=v9.0)
VSTest version 17.14.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   156, Skipped:     0, Total:   156, Duration: 992 ms - PanelForge.UnitTests.dll (net9.0)
```

---

## 5. Kế hoạch tiếp theo: PHASE 2 (UC-02 AI Config & UC-15 Audit)

Sẵn sàng triển khai khi có lệnh:
1. **2.1 Chuyển quyền cấu hình AI sang Administrator (UC-02):**
   * Bổ sung `GET/PUT /api/admin/workspaces/{workspaceId}/ai-config` trong `AdminController`.
   * Chuyển endpoint `PUT /api/workspaces/{id}/ai-config` ở `WorkspacesController` thành chỉ đọc hoặc vô hiệu hóa đối với Producer.
2. **2.2 Mã hóa API Key trước khi lưu trữ:**
   * Sử dụng ASP.NET Core Data Protection, định nghĩa interface `IAiKeyProtector` và implementation ở Infrastructure.
   * DTO chỉ trả `HasApiKey` (boolean), tuyệt đối không bao giờ trả key thô ra ngoài API.
3. **2.3 Kiểm tra xác thực Provider khi lưu (UC-02 Alternate Flow):**
   * Tạo interface `IAiProviderValidator` kiểm tra tính hợp lệ của API Key trước khi lưu cấu hình.
   * Từ chối lưu và trả lỗi tiếng Việt nếu API Key không hợp lệ.
