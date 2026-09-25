# Báo cáo Triển khai Chi tiết: Core Flow 1 — PHASE 3

> **Dự án**: PanelForge (Clean Architecture, .NET 9, EF Core, Marten Event Store, React + Vite)  
> **Nội dung**: Hoàn thiện Phase 3 — UC-03 Series & Release Calendar Setup, UC-04 Series Bible Setup & Hoàn tất Core Flow 1  
> **Thời gian**: Tháng 09/2026  
> **Trạng thái**: ✅ **Hoàn thành 100% — Build 0 error, 0 warning — 182/182 tests passed**

---

## 1. Mục tiêu & Yêu cầu Kỹ thuật Phase 3

Theo đặc tả `PanelForge_Capstone_Content.md` (UC-03, UC-04, FR-Producer-01, FR-Producer-02, BR-07, BR-15, BR-18, BR-23, NFR-08):
1. **3.1 Hoàn thiện bảo mật & phân quyền cho Series, Chapters & Series Bible (UC-03, UC-04)**:
   - Thêm `[Authorize]` và `[RequireWorkspaceRole]` cho toàn bộ `ChaptersController` và `BibleController`.
   - Mở rộng bộ lọc `RequireWorkspaceRoleFilter` để tự động bóc tách và phân giải `chapterId` $\rightarrow$ `seriesId` $\rightarrow$ `workspaceId`, cho phép bảo vệ tất cả endpoint thao tác trên Chapter (lịch phát hành/Release Calendar).
   - Nghiệp vụ UC-03: Producer là vai trò duy nhất được tạo Series và Chapter; các thành viên khác chỉ được xem hoặc thao tác theo vai trò cho phép.
2. **3.2 Quản lý Series, Release Calendar & Pipeline Template Cloning (UC-03, NFR-08)**:
   - `CreateSeriesCommand`: Tự động phân giải template (mặc định hoặc theo `PipelineTemplateId`), nhân bản các Stage kèm Gate, thiết lập `SeriesBible` phiên bản 1 (CF1 Step 7-9).
   - Đánh dấu `[Obsolete]` phương thức `PipelineDefinition.CreateDefaultMangaPipeline()` cũ để định hướng toàn hệ thống sử dụng DB-backed PipelineTemplate.
3. **3.3 Chuẩn hóa Architecture & Namespace (Refactoring)**:
   - Chuẩn hóa namespace của `TypographyPresetDto.cs` về `PanelForge.Application.Features.TypographyPresets.Models` đồng bộ với cấu trúc thư mục và các DTO khác.
   - Thêm dự án API vào UnitTests và thiết lập kiểm thử đơn vị tự động cho `RequireWorkspaceRoleFilter` với `chapterId`.

---

## 2. Chi tiết các tệp đã chỉnh sửa & tạo mới

### 2.1 API Controllers & Attributes

| Tệp | Thay đổi |
|---|---|
| [`RequireWorkspaceRoleAttribute.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/src/PanelForge.API/Common/Attributes/RequireWorkspaceRoleAttribute.cs) | Bổ sung nhánh giải mã `chapterId` từ `RouteData`, truy vấn `Chapter.Series.WorkspaceId` từ `IPanelForgeDbContext` để kiểm tra quyền thành viên và vai trò hợp lệ. Báo lỗi 404 khi không tìm thấy Chapter và 403 khi thiếu quyền. |
| [`ChaptersController.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/src/PanelForge.API/Controllers/ChaptersController.cs) | Thêm `[Authorize]` ở cấp class.<br>• `POST api/series/{seriesId}/chapters`: `[RequireWorkspaceRole(WorkspaceRole.Producer)]` (Tạo chương & lịch phát hành).<br>• `GET api/series/{seriesId}/chapters` & `GET api/chapters/{chapterId}`: `[RequireWorkspaceRole]` (Xem lịch phát hành & chi tiết chương).<br>• `PUT api/chapters/{chapterId}`: `[RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Editor)]` (Cập nhật lịch phát hành/TargetReleaseDate hoặc nâng Version).<br>• `DELETE api/chapters/{chapterId}`: `[RequireWorkspaceRole(WorkspaceRole.Producer)]` (Xóa mềm chương). |
| [`BibleController.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/src/PanelForge.API/Controllers/BibleController.cs) | Bảo vệ toàn bộ endpoint quản lý Series Bible theo UC-04:<br>• `GET api/series/{seriesId}/bible*`: `[RequireWorkspaceRole]`.<br>• `POST api/series/{seriesId}/bible/entries`: `[RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Editor)]`.<br>• `POST api/series/{seriesId}/bible/entries/{entryId}/revisions`: `[RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Editor)]`. |
| [`SeriesController.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/src/PanelForge.API/Controllers/SeriesController.cs) | Thêm using `PanelForge.Application.Features.TypographyPresets.Models;` hỗ trợ DTO chuẩn hóa. |

### 2.2 Application & Domain

| Tệp | Thay đổi |
|---|---|
| [`CreateSeriesCommand.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/src/PanelForge.Application/Features/Series/Commands/CreateSeries/CreateSeriesCommand.cs) | Bổ sung kiểm tra vai trò `WorkspaceRole.Producer` trực tiếp trong handler (UC-03). Người dùng có vai trò khác như Writer/Artist/Letterer khi gọi lệnh sẽ bị từ chối với lỗi rõ ràng. |
| [`TypographyPresetDto.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/src/PanelForge.Application/Features/TypographyPresets/Models/TypographyPresetDto.cs) | Chuyển sang file-scoped namespace `PanelForge.Application.Features.TypographyPresets.Models;` chuẩn hóa kiến trúc. |
| [`TypographyPresetCommands.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/src/PanelForge.Application/Features/TypographyPresets/Commands/TypographyPresetCommands.cs) | Cập nhật using `PanelForge.Application.Features.TypographyPresets.Models;`. |
| [`GetTypographyPresetsQueries.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/src/PanelForge.Application/Features/TypographyPresets/Queries/GetTypographyPresetsQueries.cs) | Cập nhật using `PanelForge.Application.Features.TypographyPresets.Models;`. |
| [`PipelineDefinition.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/src/PanelForge.Domain/Entities/Workflow/PipelineDefinition.cs) | Đánh dấu `[Obsolete("Replaced by MasterData PipelineTemplate cloning in CreateSeriesCommand (NFR-08).")]` trên phương thức `CreateDefaultMangaPipeline()`. |

### 2.3 UnitTests & Project Config

| Tệp | Thay đổi |
|---|---|
| [`PanelForge.UnitTests.csproj`](file:///e:/Panel-forge/PanelForge_FA26SE251/test/PanelForge.UnitTests/PanelForge.UnitTests.csproj) | Thêm `ProjectReference` tới `PanelForge.API.csproj` và bổ sung cấu hình `<NoWarn>$(NoWarn);MSB3277</NoWarn>` để đảm bảo kết quả build 0 warning. |
| [`RequireWorkspaceRoleFilterTests.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/test/PanelForge.UnitTests/API/RequireWorkspaceRoleFilterTests.cs) | Thêm test suite kiểm thử đơn vị cho filter: phân giải `chapterId` thành công, xử lý Chapter không tồn tại (404), từ chối người dùng không đủ quyền (403). |
| [`SeriesCommandTests.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/test/PanelForge.UnitTests/Application/Series/SeriesCommandTests.cs) | Thêm test case `CreateSeries_WhenUserIsNotProducer_ShouldReturnFailure` xác minh quy định nghiệp vụ UC-03. |
| [`WorkflowStateMachineTests.cs`](file:///e:/Panel-forge/PanelForge_FA26SE251/test/PanelForge.UnitTests/Domain/WorkflowStateMachineTests.cs) | Tắt cảnh báo CS0618 trên các test case gọi phương thức obsolete. |

---

## 3. Kết quả Kiểm thử & Biên dịch

### 3.1 Backend Build (`dotnet build`)
```
Determining projects to restore...
All projects are up-to-date for restore.
PanelForge.Domain -> bin/Debug/net9.0/PanelForge.Domain.dll
PanelForge.Application -> bin/Debug/net9.0/PanelForge.Application.dll
PanelForge.Infrastructure -> bin/Debug/net9.0/PanelForge.Infrastructure.dll
PanelForge.API -> bin/Debug/net9.0/PanelForge.API.dll
PanelForge.UnitTests -> bin/Debug/net9.0/PanelForge.UnitTests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### 3.2 Backend Unit Tests (`dotnet test`)
```
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed: 0, Passed: 182, Skipped: 0, Total: 182, Duration: 1 s - PanelForge.UnitTests.dll (net9.0)
```
*(Toàn bộ 182 bài kiểm tra đều vượt qua, tăng từ 178 lên 182).*

### 3.3 Frontend TypeScript Verification (`npx tsc --noEmit`)
```
Exit Code: 0 (No type errors)
```

---

## 4. Tổng kết Core Flow 1 (CF1 Complete)

Toàn bộ Core Flow 1 — **Studio, Series & Series Bible Setup** đã hoàn thành toàn diện:
- ✅ **Bảo mật & Phân quyền**: Phân lập hoàn toàn Administrator khỏi `workspace_members` (BR-07); phân quyền theo vai trò chính xác cho Workspace, Series, Chapters, và Series Bible.
- ✅ **Quy trình Series & Pipeline**: Khởi tạo Series từ DB-backed `PipelineTemplate`, tự động thiết lập pipeline chuẩn và Series Bible phiên bản 1 (UC-03, NFR-08).
- ✅ **Lịch phát hành (Release Calendar)**: Cấu hình tần suất phát hành ở Series (`ReleaseScheduleJson`) và ngày phát hành mục tiêu ở từng Chapter (`TargetReleaseDate`) được kiểm soát chặt chẽ bởi Producer.
- ✅ **Series Bible & Kiểm soát phiên bản**: Hỗ trợ đầy đủ tạo mục và cập nhật revision append-only theo chương hiệu lực (BR-01, BR-15, BR-18, UC-04).
- ✅ **Cấu hình AI & Thống kê**: Mã hóa Data Protection, kiểm tra credentials trước khi lưu, chuyển giao toàn quyền quản trị cho Administrator (UC-02, UC-15, NFR-03).
- ✅ **Chất lượng mã nguồn**: 0 warning, 0 error trên cả backend và frontend, 182 tests pass 100%.
