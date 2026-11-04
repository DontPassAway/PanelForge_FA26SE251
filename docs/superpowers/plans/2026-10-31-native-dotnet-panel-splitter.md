# Native .NET Manga Panel Splitter (OpenCvSharp4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chuyển tính năng tách panel và bong bóng thoại từ microservice Python (`PanelForge.PanelSplitter`, cổng 5066) sang chạy native trong tiến trình .NET 9 bằng `OpenCvSharp4`, **cho kết quả giống hệt bản Python**, đồng thời chuẩn hoá use case theo CQRS của PanelForge.

**Architecture:** `OpenCvPanelSplitter` cài đặt `IPanelSplitter` trong `PanelForge.Infrastructure`. Use case đi qua MediatR `SplitArtworkQuery` trong `PanelForge.Application`; `PagesController` chỉ kiểm tra file upload và dispatch. Quyền Workspace/Series và việc Page tồn tại đã do `[RequireWorkspaceRole(Producer, Artist)]` kiểm tra từ `pageId` trên route.

**Tech Stack:** .NET 9, OpenCvSharp4 `4.13.0.20260627` (đã xác nhận có trên NuGet), MediatR, EF Core (`IPanelForgeDbContext`), xUnit/FluentAssertions.

**Spec:** Thay `src/PanelForge.PanelSplitter/app.py` và `PanelSplitterClient.cs`. Response JSON (`PanelSplitResult`) giữ nguyên shape để Frontend không đổi.

## Global Constraints
- Target Framework: .NET 9.0 (`net9.0`).
- Clean Architecture: Domain không phụ thuộc gì; Application chỉ phụ thuộc Domain; Infrastructure cài đặt `IPanelSplitter`; Controller chỉ dispatch qua `_mediator.Send(...)`.
- Cross-platform runtime: Windows (`OpenCvSharp4.runtime.win`) và container Linux (`OpenCvSharp4.official.runtime.linux-x64`) trên image `mcr.microsoft.com/dotnet/aspnet:9.0` (Debian). **Linux phải được kiểm chứng bằng Docker thật (Task 5)**, không chỉ build.
- **Parity trước, tối ưu sau:** bản .NET phải cho cùng số panel, cùng thứ tự đọc, cùng bbox và cùng bong bóng như `app.py` trên cùng ảnh. Không co ảnh (downscale) trong plan này (xem "Ngoài phạm vi").
- Tọa độ trả về (`Bbox = [x, y, w, h]`, `Polygon`) theo pixel của ảnh gốc.
- Không đổi route, request hay shape response thành công. Thay đổi mã lỗi phải ghi vào `HANDOFF.md` (Task 6).

## Review Focus
1. Stream ảnh rỗng/không hợp lệ: ném `InvalidImageException` (lỗi nghiệp vụ, map 400), không để OpenCV crash unmanaged.
2. Unmanaged memory: mọi `Mat`, `Mat[]` contour, `InputArray` phải được dispose (`using`), kể cả trên nhánh lỗi.
3. Thứ tự đọc RTL/LTR: gom theo hàng `y` (ngưỡng `p.y > row[0].y + row[0].h * 0.5` như Python), trong hàng sắp theo `x`, đảo nếu `rightToLeft`.
4. Parity với Python: ngưỡng 100/200, diện tích panel ≥ 4%, bong bóng ≥ 0.3%, `contour.Length >= 5`, tỉ lệ elip `0.85 < area / (π·ma·mi/4) < 1.15`, `ApproxPolyDP(c, 2, true)`, loại bong bóng > 50% diện tích panel, khử viền ngoài khi có viền trong lồng nhau, sắp bong bóng theo `(panelOrder, y)`.
5. Xử lý CPU nặng (ảnh tới 20MB) chạy trong `Task.Run` để không chiếm thread request, và tôn trọng `CancellationToken`.

---

### Task 1: Cấu hình gói OpenCvSharp4 trong `PanelForge.Infrastructure`

**Files:**
- Modify: `src/PanelForge.Infrastructure/PanelForge.Infrastructure.csproj`

- [ ] **Step 1: Thêm PackageReference**
  `OpenCvSharp4` 4.13.0.20260627, `OpenCvSharp4.runtime.win` 4.13.0.20260627, `OpenCvSharp4.official.runtime.linux-x64` 4.13.0.20260627 (cùng phiên bản).
- [ ] **Step 2: Build**
  `dotnet build src/PanelForge.Infrastructure/PanelForge.Infrastructure.csproj` → Build succeeded, 0 errors.

---

### Task 2: Cài đặt `OpenCvPanelSplitter` (port 1:1 từ `app.py`)

**Files:**
- Create: `src/PanelForge.Infrastructure/ExternalServices/OpenCvPanelSplitter.cs`
- Modify: `src/PanelForge.Application/Common/Interfaces/Services/IPanelSplitter.cs` (doc comment + exception mới)

**Interfaces:**
- Giữ nguyên: `Task<PanelSplitResult> SplitAsync(Stream image, string fileName, bool rightToLeft, CancellationToken cancellationToken = default)`.
- Thêm vào cùng file `IPanelSplitter.cs`: `public sealed class InvalidImageException(string message) : Exception(message);`
- Sửa doc comment: bỏ "service Python"; `<exception cref="InvalidImageException">` khi ảnh không giải mã được. Bỏ `<exception cref="InvalidOperationException">` (không còn service ngoài để bị sập).

- [ ] **Step 1: Đọc Stream sang `Mat`**
  Copy stream vào `MemoryStream`, `Cv2.ImDecode(bytes, ImreadModes.Color)`. Bytes rỗng hoặc `mat.Empty()` → `throw new InvalidImageException("File không phải ảnh hợp lệ.")`. Không bắt exception khác.
- [ ] **Step 2: Phát hiện panel và thứ tự đọc** (giống `split_page` dòng 15–32 của `app.py`)
  `CvtColor BGR2GRAY` → `Threshold(gray, 100, 255, BinaryInv)` → `FindContours(External, ApproxSimple)` → `BoundingRect` → giữ `w*h >= 0.04*W*H` → sắp theo `y`, gom hàng, trong hàng sắp theo `x` (đảo nếu RTL).
- [ ] **Step 3: Phát hiện bong bóng** (giống dòng 34–58 của `app.py`)
  `Threshold(gray, 200, 255, Binary)` → `FindContours(CComp, ApproxSimple)` → lọc `ContourArea >= 0.003*W*H` và `Length >= 5` → `FitEllipse` kiểm tỉ lệ (0.85, 1.15) → tìm panel chứa tâm bbox, bỏ nếu không có hoặc `w*h > 0.5*panelArea` → `ApproxPolyDP(c, 2, true)` → khử bong bóng chứa bong bóng khác (giữ cái nằm trong) → sắp theo `(panelOrder, y)`.
- [ ] **Step 4: Bọc CPU-bound**
  `SplitAsync` = `await Task.Run(() => Split(bytes, rightToLeft, ct), ct)`; gọi `ct.ThrowIfCancellationRequested()` giữa bước panel và bước bong bóng. Mọi `Mat` dùng `using`.
- [ ] **Step 5: Build** Infrastructure → 0 errors.

---

### Task 3: Use case `SplitArtworkQuery` trong `PanelForge.Application`

**Files:**
- Create: `src/PanelForge.Application/Features/Pages/Queries/SplitArtwork/SplitArtworkQuery.cs` (query + handler cùng file, theo quy ước một use case một thư mục)

- [ ] **Step 1: Query**
  ```csharp
  public sealed record SplitArtworkQuery(Guid PageId, Stream ImageStream, string FileName, bool RightToLeft)
      : IRequest<Result<PanelSplitResult>>;
  ```
- [ ] **Step 2: Handler** (phụ thuộc `IPanelForgeDbContext`, `IPanelSplitter`)
  - Page phải tồn tại: `await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == query.PageId, ct)`; không thì `Failure("Page … không tồn tại.", ResultErrorCodes.NotFound)`. **Không** dùng `IPageRepository.GetAsync` (dựng lại cả event stream Marten chỉ để kiểm tồn tại). Quyền Workspace đã do attribute kiểm.
  - Gọi `_panelSplitter.SplitAsync(...)`.
  - **Chỉ** bắt `InvalidImageException` → `Failure(ex.Message, ResultErrorCodes.Validation)`. Không `catch (Exception)`: lỗi native thật phải nổi lên middleware (500) và được log.
- [ ] **Step 3: Build** Application → 0 errors.

---

### Task 4: DI và Controller trong `PanelForge.API`

**Files:**
- Modify: `src/PanelForge.Infrastructure/DependencyInjection.cs`
- Modify: `src/PanelForge.API/Controllers/PagesController.cs`

- [ ] **Step 1: DI**
  Thay `AddHttpClient<IPanelSplitter, PanelSplitterClient>(...)` bằng `services.AddSingleton<IPanelSplitter, OpenCvPanelSplitter>();` (stateless). Bỏ cấu hình URL/cổng 5066 của Python khỏi appsettings nếu có.
- [ ] **Step 2: Controller `SplitArtwork`**
  - Giữ nguyên route, `[RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Artist)]`, giới hạn 20MB và kiểm tra content-type ở controller (kiểm tra upload thuộc tầng API).
  - Bỏ `[FromServices] IPanelSplitter`; gửi `new SplitArtworkQuery(pageId, stream, form.File.FileName, form.RightToLeft)` qua `_mediator`.
  - Map: Success → 200; `NotFound` → 404; `Validation` (ảnh hỏng) → 400 `{ error }`. Bỏ nhánh 503 `PANEL_SPLITTER_UNAVAILABLE` và `[ProducesResponseType(503)]`.
- [ ] **Step 3: Build** API → 0 errors.

---

### Task 5: Kiểm thử (unit, parity, Docker)

**Files:**
- Create: `test/PanelForge.UnitTests/Infrastructure/OpenCvPanelSplitterTests.cs`
- Create: `test/PanelForge.UnitTests/Application/Pages/SplitArtworkQueryTests.cs`
- Create: `test/PanelForge.UnitTests/TestData/panel-splitter/` (ảnh mẫu + `expected.json`)

- [ ] **Step 1: Test `OpenCvPanelSplitter` bằng ảnh tổng hợp**
  Vẽ trong RAM 3 khung viền đen + 1 elip trắng viền đen trong khung 2. Kiểm: đúng 3 panel, bong bóng thuộc panel 2, thứ tự LTR và RTL đúng, bytes rỗng/không phải ảnh → `InvalidImageException`.
- [ ] **Step 2: Test parity với Python**
  Chọn 2–3 ảnh trang thật (có 1 ảnh > 4000px). Chạy `app.py` một lần (`py -m uvicorn app:app --port 5066`, POST `/split` với `rtl=false` và `rtl=true`), lưu output thành `expected.json`. Test .NET phải cho **cùng** `width/height`, số panel, thứ tự, bbox panel và số bong bóng; bbox bong bóng lệch tối đa ±1px (khác biệt làm tròn giữa hai binding).
- [ ] **Step 3: Test handler**
  Page không tồn tại → `NotFound`, không gọi splitter. Splitter ném `InvalidImageException` → `Validation`. Hợp lệ → Success với đúng kết quả của splitter.
- [ ] **Step 4: Chạy toàn bộ unit test**
  `dotnet test test/PanelForge.UnitTests/PanelForge.UnitTests.csproj` → tất cả `Passed!` (hiện có 1107 test).
- [ ] **Step 5: Kiểm chứng native runtime trong Docker**
  `docker build -t panelforge-api-ocv .` rồi chạy container và gọi `POST /api/pages/{pageId}/split-artwork` với một ảnh mẫu (tài khoản Artist/Producer dev). Kỳ vọng 200 với cùng kết quả như test parity.
  Nếu gặp `DllNotFoundException`/`TypeInitializationException` cho `OpenCvSharpExtern`: chạy `ldd` trên `libOpenCvSharpExtern.so` trong container để tìm thư viện thiếu, thêm `apt-get install -y --no-install-recommends <libs>` vào stage `runtime` của `Dockerfile`, build lại cho tới khi gọi được.

---

### Task 6: Dọn dẹp và tài liệu

**Files:**
- Remove: `src/PanelForge.Infrastructure/ExternalServices/PanelSplitterClient.cs`
- Modify: `src/PanelForge.PanelSplitter/README.md` (hoặc tạo) — ghi đã chuyển sang native .NET, giữ `app.py` làm nguồn đối chiếu parity
- Modify: `../../HANDOFF.md`

- [ ] **Step 1: Chỉ xoá `PanelSplitterClient.cs` sau khi Task 5 Step 5 (Docker) đạt.**
- [ ] **Step 2: Cập nhật `HANDOFF.md`** (mục "Backend → Frontend", và "Nhật ký thay đổi breaking" cho phần mã lỗi):
  `POST /api/pages/{pageId}/split-artwork` — request và response 200 không đổi. Mã lỗi: ảnh không giải mã được → **400** `{ error }` (trước đây có thể là 503); **bỏ** 503 `PANEL_SPLITTER_UNAVAILABLE` (không còn service Python cần chạy riêng). FE nên bỏ thông báo "service tách panel chưa chạy" nếu có.
- [ ] **Step 3: Full build + test lần cuối**
  `dotnet build PanelForge.slnx` và `dotnet test PanelForge.slnx` → 0 errors, toàn bộ test pass.

---

## Ngoài phạm vi (làm sau, plan riêng)
- **Downscale ảnh lớn để tăng tốc.** Co ảnh làm viền panel mảnh bị nhoè (xám > 100 → mất panel) và làm epsilon `ApproxPolyDP` 2px thành ~4px khi phóng ngược. Chỉ làm khi đã có số đo thời gian thực tế trên ảnh > 4000px, có cờ bật/tắt, và test parity (Task 5 Step 2) vẫn đạt trên bộ ảnh mẫu.
- Phát hiện panel không viền / panel rất nhỏ (cần model kiểu Magi) — giới hạn đã ghi trong `app.py`.
