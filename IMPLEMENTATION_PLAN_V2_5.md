# KẾ HOẠCH TRIỂN KHAI PANELFORGE KIẾN TRÚC V2.5 (IMPLEMENTATION PLAN V2.5)

> **Dự án:** PanelForge (`SEP490_PanelForge`)  
> **Kiến trúc:** .NET 9 Backend (Clean Architecture, CQRS, Event Sourcing) + React 19 / TypeScript Frontend  
> **Mục tiêu:** Tái cơ cấu luồng nghiệp vụ truyện tranh chuyên nghiệp theo chuẩn V2.5: Đưa Writer ra ngoài luồng Assignment, luồng sản xuất Series Bible 2 tầng (Writer ↔ Artist ↔ Producer), rào chắn Chapter-Scoped Bible Gate, duyệt kịch bản thuần túy (Freeze Script) và chuyển giao sản xuất hình ảnh song song.

---

## BẢNG TIẾN ĐỘ TỔNG QUAN

| Giai đoạn | Nội dung trọng tâm | Trạng thái |
|---|---|:---:|
| **PHASE 1** | Luồng Sản Xuất Series Bible (Writer ↔ Artist ↔ Producer) & Độc Lập Hóa Writer Khỏi Assignment | [x] Hoàn thành |
| **PHASE 2** | Luồng Duyệt Kịch Bản Thuần Túy (Freeze Script) & Chuyển Giao Sang Artist Dàn Trang (Thumbnail Layout & Script Coverage Bar 100%) | [ ] Chưa làm |
| **PHASE 3** | Luồng Vẽ Tay Song Song Giữa Artist A & Artist B (Default Chapter Assignee, Scope Transfer P5-6, Stage Impact Flag ⚑) | [ ] Chưa làm |

---

## CHI TIẾT CÁC HẠNG MỤC THEO PHASE

### PHASE 1: Luồng Sản Xuất Series Bible (Writer ↔ Artist ↔ Producer) & Độc Lập Hóa Writer Khỏi Assignment

- [x] **Hạng mục 1.1: Spec Audit & Cập nhật tài liệu đặc tả**
  - [x] Rà soát và cập nhật `PanelForge_5_Core_Flows.md` (CF1 và CF2 theo V2.5).
  - [x] Rà soát và cập nhật `series-bible.md` (luồng 3 bên, vòng đời 2 tầng, Content-Addressed Storage SHA-256).
  - [x] Cập nhật bảng phân công và việc còn lại của CF1, CF2 trong tài liệu nhóm.

- [x] **Hạng mục 1.2: Độc lập hóa Writer khỏi Assignment của Producer**
  - [x] Backend: Cập nhật `ScriptLineRules.AuthorizeAsync` cho phép `WorkspaceRole.Writer` và `Producer` toàn quyền tạo/sửa/xóa ScriptLine khi kịch bản chưa đóng băng (`!IsScriptFrozen`), không bắt buộc có `Assignment`.
  - [x] Backend: Cập nhật `SceneAssignmentGate.CheckCanEditScenesAsync` mở quyền tạo/sửa Scene cho Writer mà không yêu cầu thẻ Assignment.
  - [x] Frontend: Cập nhật `permissions.ts` và logic `ScriptEditorView.tsx` đảm bảo Writer làm việc độc lập trên cây tự sự (`Story Arc -> Chapter Outline -> Scene -> ScriptLine`), ẩn bỏ hoàn toàn các trường ép Writer chia Page/Panel.

- [x] **Hạng mục 1.3: Cấp quyền cho Artist tham gia Series Bible & Upload Asset chuẩn SHA-256 (Fix P0 1.10 & 1.11)**
  - [x] Backend: Cập nhật Domain `BibleEntryRevision` bổ sung `ArtistUserId` (Guid?) đảm bảo Provenance tác giả bản vẽ.
  - [x] Backend: Đảm bảo `UploadBibleReferenceImageCommand` và `BibleController` cho phép role `Artist` upload và sinh link CAS SHA-256 (`/api/series/{seriesId}/bible/assets/{sha}`).
  - [x] Frontend: Nối luồng upload file thực tế qua `bibleService.uploadReferenceImage` vào `SeriesBibleView.tsx` và `BibleEntityDetailModal.tsx`, thay thế hoàn toàn chuỗi text dán link thủ công.

- [x] **Hạng mục 1.4: Vòng đời kiểm duyệt 2 tầng cho Series Bible (Writer/Editor Review ➔ Producer Canon Lock)**
  - [x] Backend: Định nghĩa enum `BibleRevisionStatus` (`Draft = 1`, `ConceptSubmitted = 2`, `ReworkRequired = 3`, `WriterEndorsed = 4`, `CanonLocked = 5`).
  - [x] Backend: Thêm commands & endpoints:
    - `POST .../revisions/{id}/endorse` (Writer/Editor xác nhận khớp cốt truyện).
    - `POST .../revisions/{id}/request-rework` (Writer/Editor yêu cầu Artist vẽ lại kèm lý do/feedback).
    - `POST .../revisions/{id}/lock` (Producer chốt khóa canon `🔒 CanonLocked`).
    - Khẳng định `/unlock` bị loại bỏ hoàn toàn, thay thế bằng `/supersede`.
  - [x] Frontend: Hiển thị trạng thái vòng đời 2 tầng trên `SeriesBibleView.tsx` và `BibleEntityDetailModal.tsx` kèm các nút hành động theo đúng vai trò người dùng (Duyệt thiết kế, Yêu cầu sửa kèm feedback modal).

- [x] **Hạng mục 1.5: Hàng đợi thiết kế Bible cho Artist (Bible Design Queue)**
  - [x] Backend: Thêm query `GetBibleDesignQueueQuery` trả về các mục Bible ở trạng thái `Draft` (chờ thiết kế) hoặc `ReworkRequired` (cần vẽ lại).
  - [x] Frontend: Thêm widget / banner `Bible Design Queue` trên đầu `SeriesBibleView.tsx` với khả năng tải lên bản vẽ nhanh cho Artist.

- [x] **Hạng mục 1.6: Cổng kiểm tra Series Bible theo Chương (Chapter-Scoped Bible Gate)**
  - [x] Backend: Thêm `CheckChapterBibleGateQuery` (`GET /api/chapters/{chapterId}/bible-gate`) kiểm tra toàn bộ thực thể `speakerCharacterId` và `bibleEntryIds` trong Chapter N. Nếu có thực thể chưa có revision `CanonLocked` có hiệu lực tại Chapter N, trả về danh sách cảnh báo `CHARACTER_NOT_LOCKED` / `ENTRY_NOT_LOCKED`.
  - [x] Frontend: Hiển thị nút kiểm tra Bible Gate trên thanh công cụ `ScriptEditorView.tsx`, hiển thị banner cảnh báo và modal chi tiết các nhân vật/bối cảnh chưa lock.

- [x] **Hạng mục 1.7: Nút Producer Duyệt Kịch Bản & Đưa Vào Sản Xuất (Freeze Script & Hand-off Gate)**
  - [x] Backend: Bổ sung cờ `IsScriptFrozen`, `ScriptFrozenAt`, `ScriptFrozenById` trên `Chapter`.
  - [x] Backend: Thêm `FreezeChapterScriptCommand` (`POST /api/chapters/{id}/freeze-script`) và `UnfreezeChapterScriptCommand`: Kiểm tra Bible Gate, đóng băng kịch bản chỉ đọc đối với Writer, tự động mở khóa stage hình ảnh đầu tiên (`Thumbnail / Layout`) cho Artist.
  - [x] Frontend: Thêm nút "Duyệt kịch bản & Bắt đầu sản xuất" cho Producer và trạng thái "🔒 Kịch bản đã đóng băng" (chỉ đọc) trên giao diện Writer.

- [x] **Hạng mục 1.8: Kiểm chứng chất lượng (Verification Loop Phase 1)**
  - [x] Backend: `dotnet build` và `dotnet test` vượt qua 100% (1080/1080 tests passed).
  - [x] Frontend: `npm run typecheck` (0 errors) và `vitest run` vượt qua 100% (52/52 tests passed).

---

### PHASE 2: Luồng Duyệt Kịch Bản Thuần Túy & Chuyển Giao Sang Artist Dàn Trang (Thumbnail Layout & Script Coverage Bar 100%)

- [ ] **Hạng mục 2.1:** Script Coverage Bar 100% (Đối chiếu tỷ lệ chuyển thể từ `ScriptLine` sang các khung tranh `Panel`).
- [ ] **Hạng mục 2.2:** Layout Workspace chuyên dụng cho Artist: Trực quan hóa danh sách `ScriptLine` chỉ đọc bên cạnh bàn vẽ Canvas.
- [ ] **Hạng mục 2.3:** Kéo thả gắn `ScriptLine` vào Panel với cảnh báo chống sót thoại và chống gán trùng.
- [ ] **Hạng mục 2.4:** Cơ chế mở lại kịch bản có kiểm soát (`Reopen Script`) kèm Stage Impact Flag `⚑` cảnh báo tới Artist khi kịch bản bị sửa đổi sau khi đã đóng băng.

---

### PHASE 3: Luồng Vẽ Tay Song Song Giữa Artist A & Artist B (Default Chapter Assignee, Scope Transfer, Stage Impact Flag ⚑)

- [ ] **Hạng mục 3.1:** Cấu hình Default Chapter Assignee (Họa sĩ chính của Chương tự động nhận các task hình ảnh khi kịch bản được duyệt).
- [ ] **Hạng mục 3.2:** Phân chia phạm vi vẽ song song (Artist A vẽ Pages 1–4, Artist B vẽ Pages 5–8) với kiểm tra chồng lấn Scope Overlap.
- [ ] **Hạng mục 3.3:** Bàn giao phạm vi công việc (`TransferAssignmentScopeCommand`) hỗ trợ tách/gộp trang giữa các Artist.
- [ ] **Hạng mục 3.4:** Đánh cờ ảnh hưởng chéo (Stage Impact Flag `⚑`) khi một bên cập nhật layout hoặc thoại làm ảnh hưởng đến các trang liên quan.
