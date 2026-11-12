# Đặc tả thay đổi: Core Flow 5 (CF5): Approval, Release & Export

> **Trạng thái (rà 2026-11-12):** Đã merge, tài liệu giữ làm lịch sử thay đổi. Migration `NarrationMode` và khoá hàng đợi export (`SKIP LOCKED`) ở §5 đã xong; hai điểm về ảnh trang theo Panel được chuyển sang `REL-10`. Checklist merge §7 không còn hiệu lực.

> **Người thực hiện:** Phạm Thành Danh · **Ngày:** 13/10/2026
> **Nhánh:** BE `feat/cf5` (tách từ `test-feature` @ `127287f`) · FE `feat/cf5` (tách từ `tam` @ `4ae1d35`)
> **Căn cứ:** mục CF5 trong `CoreFlow_Tasks_Con_Lai.md` (rà 11/10/2026), `docs/requirements/PanelForge_5_Core_Flows.md` mục 5.

## 1. Tổng quan

| # | Việc | Trạng thái | Commit BE | Commit FE |
|---|---|---|---|---|
| 5.7 | Phản hồi xem thử "cần sửa" → ChangeRequest (cũng là 3.7 của Minh) | Xong | `761b04c` | `017e4cb` |
| 5.11 | **Mới, file chưa ghi.** Chương đã duyệt/phát hành vẫn sửa được nội dung (BR-12) | Xong | `a1bab4c` | `e23904b` |
| 5.1 | `ExportJob` chạy nền, thông báo khi xong | Xong | `655a9ef` | — |
| 5.2 | Xuất CBZ và PDF | Xong | `655a9ef`, `cbb4e94` | — |
| 5.3a | Xuất ảnh trang (ZIP) và webtoon cuộn dọc | Xong | `655a9ef` | — |
| 5.3b | Xuất JSON theo lược đồ mở, nhập lại được | **Chưa**: phần provenance chờ việc 4.5 của Hoà | — | — |
| 5.4 | Màn `ExportView` gọi API thật | Xong | — | `2d50627` |
| 5.5 | Khoá chương (`Locked`) | **Hoãn** theo họp GVHD tuần 5, chờ 4.1 của Hoà | — | — |
| 5.6 | Gắn link xem thử và bản xuất vào `ChapterSnapshot` | **Chờ** 4.1 của Hoà | — | — |
| 5.8 | Rà màn tĩnh | Xong: `/reader` về catalog thật; `DashboardView` đã gọi API | — | `cddffa4` |
| 5.9 | Ghi lệch so với phiếu V1.2 vào báo cáo | Chưa (tài liệu) | — | — |
| 5.10 | Reader Mobile, trả phí, in chuyên nghiệp | Mở rộng, không làm | — | — |
| — | Tách `FinalArtwork` dùng chung (cho 4.1 của Hoà dùng lại) | Xong | `7df7037` | — |
| — | Sửa hai migration trùng cột làm mọi DB lỗi khi migrate | Xong | `78baa80` | — |

## 2. Chi tiết thay đổi

### 2.1. Phản hồi "cần sửa" → ChangeRequest (5.7)

**Luồng.** Reader gửi phản hồi qua link xem thử → Producer/Editor phân loại `NeedsChange` (ghi chú bắt buộc) → Producer mở lại chương (lý do bắt buộc, link cũ bị thu hồi) → chọn việc cần sửa → hệ thống tạo ChangeRequest gắn phản hồi.

- Việc **đã duyệt**: `Assignment Approved → ReworkRequired`, `StageProgress Approved → ChangesRequested` (hàm mới `StageProgress.ReopenForRework`), nên người làm bấm *Bắt đầu* lại được. Việc **chưa duyệt** chỉ nhận thêm yêu cầu sửa.
- ChangeRequest: người chịu trách nhiệm = người nhận việc; gắn `PanelId` và `AssetReferenceId` (bản người đọc đã xem); mô tả mặc định = nội dung phản hồi + ghi chú triage; `SubmissionId` để trống (không có lần nộp bị từ chối).
- Người làm được thông báo (`AssignmentChangesRequested`, BR-14). Chương chỉ duyệt lại được khi yêu cầu đã được Editor xác minh (luật sẵn có của `ApproveChapter`).
- `PreviewFeedback.ChangeRequestId` (cột mới `change_request_id`): mỗi phản hồi chuyển một lần, sau đó không phân loại lại được.

`POST /api/chapters/{chapterId}/preview-feedback/{feedbackId}/change-request`, quyền Producer hoặc Editor. Body: `{ assignmentId, description?, dueDate? }`.

| Trường hợp | HTTP |
|---|---|
| Thành công | 201, `PreviewFeedbackDto` có `changeRequestId` |
| Không phải Producer/Editor của Workspace | 403 |
| Phản hồi hoặc việc không thuộc chương | 404 |
| Chương chưa mở lại; phản hồi chưa `NeedsChange`; đã chuyển rồi; việc đang `Submitted`/`UnderReview` (trả về qua cổng CF3) | 409 |

**FE.** `PreviewLinksModal`: chương `InProduction` + phản hồi `NeedsChange` → ô chọn việc (việc đúng trang/khung lên đầu, việc đang chờ duyệt bị khoá) và nút *Tạo yêu cầu sửa*; phản hồi đã chuyển hiện nhãn và ẩn nút phân loại; gợi ý *Mở lại chương* chỉ hiện khi chương `Approved`. `SeriesChaptersModal`: nút xem thử hiện cả khi `InProduction` (*Phản hồi xem thử*); mở lại chương từ modal giữ modal mở ở trạng thái mới.

### 2.2. Chương đã duyệt chỉ đọc (BR-12)

Trước đây chỉ ScriptLine, Assignment, áp dụng đề xuất AI và frame template chặn sửa. Scene, Page, Panel, Element và sửa/xoá Chapter vẫn sửa được chương `Approved`, kể cả `Published`, nên Publisher có thể phát hành bản khác bản Producer đã duyệt.

- `ChapterEditGuard` (Application) gọi ở 21 handler: Scene ×4, Panel ×5, Page ×4, Element ×6, `UpdateChapter`, `DeleteChapter`.
- Mã lỗi mới `CHAPTER_READ_ONLY`, HTTP **409**. Tách khỏi `CONFLICT` vì FE đang coi mọi 409 trên canvas là xung đột phiên bản và mở hộp thoại tải lại.
- API: `ConflictOrNull` cho các controller còn đoán mã lỗi theo nội dung message; `ElementsController` trả `{ message, code }` không kèm field `conflict`.
- FE: `element.service` không gắn `ELEMENT_VERSION_CONFLICT` cho mã này; `LetteringWorkspace` báo *Chương chỉ đọc* thay vì mở `ElementConflictModal`.

### 2.3. Xuất chương chạy nền (5.1–5.3a, 5.4)

**`ExportJob`** (bảng `export_jobs`): `Queued → Processing → Completed | Failed | Cancelled`. Lúc tạo, job chụp artwork cuối của từng trang theo thứ tự (`FinalArtwork`), nên file luôn là bản đã duyệt dù sau đó chương mở lại. Thử lại = tạo job mới (BR-01).

| Endpoint | Quyền | Ghi chú |
|---|---|---|
| `POST /api/chapters/{chapterId}/exports` body `{ presetCode }` | Producer | 202 `ExportJobDto` (Queued). 400: preset không có, đã tắt hoặc chưa hỗ trợ. 409: chương chưa `Approved`/`Published`, hoặc trang thiếu ảnh cả trang (nêu số trang) |
| `GET /api/chapters/{chapterId}/exports` | Producer, Editor, Publisher | Mới nhất trước |
| `POST /api/chapters/{chapterId}/exports/{jobId}/cancel` | Producer | Chỉ job `Queued`, còn lại 409 |
| `GET /api/chapters/{chapterId}/exports/{jobId}/file` | Producer | Trả file qua API có kiểm quyền (không lộ đường dẫn kho, BR-16); đếm `downloadCount` (có audit) |

**Worker** `ExportWorker` (`Exports:Enabled` = true, `Exports:PollSeconds` = 5): lấy job `Queued` cũ nhất, đọc ảnh từ kho, dựng file, lưu `exports/{jobId}.{ext}`, tính SHA-256 trên bytes đã lưu, báo người yêu cầu (`ExportCompleted` / `ExportFailed`). Lỗi dữ liệu (ảnh hỏng, chương quá dài) hiện nguyên văn cho Producer.

**Định dạng** (`SkiaChapterExportRenderer`, SkiaSharp 4.153.1, MIT):

| Mã preset | File | Ghi chú |
|---|---|---|
| `CBZ` | `.cbz` | ZIP ảnh `001.png…` + `ComicInfo.xml`; `Manga = YesAndRightToLeft` khi Series đọc phải sang trái |
| `PAGE_IMAGES` | `.zip` | Ảnh gốc từng trang |
| `PDF` | `.pdf` | Mỗi trang một ảnh, khổ theo `resolution` của preset (mặc định 300 dpi); ảnh nén **không mất dữ liệu** (mặc định của Skia là JPEG, làm nhoè nét trắng đen) |
| `WEBTOON` | `.jpg` | Ghép dọc, rộng `stripWidth` (800), `quality` (85); tối đa 65 000 px chiều cao |

PNG/JPEG giữ nguyên bytes trong ZIP; WebP đổi sang PNG để trình đọc cũ mở được. Không làm CMYK/bleed (in chuyên nghiệp là mở rộng).

**FE `ExportView`** (`/workspaces/:workspaceId/series/:seriesId/export`): chọn chương `Approved`/`Published`, chọn định dạng, xếp hàng job, tự hỏi lại trạng thái khi còn job chờ, tải file, huỷ job đang chờ. Bỏ nút *Lock* giả và `ExportDeliveryModal` (không màn nào dùng).

### 2.4. `FinalArtwork` dùng chung

Quy tắc "bản đã duyệt của trang": ảnh **cả trang** (`PanelId` null) ở stage có `StageOrder` cao nhất, phiên bản mới nhất, chỉ trang còn sống. Dùng cho link xem thử và export; `ChapterSnapshot` (4.1) nên dùng lại để ba nơi cùng một định nghĩa.

### 2.5. Màn tĩnh (5.8)

`ReaderPortalView` không gọi API, `ReaderWorkspace` không được import: xoá; `/reader` chuyển về `/catalog`. Luồng Reader thật: `/catalog` (chương đã phát hành) và `/preview/:token` (link xem thử). `DashboardView` của Producer đã gọi API tiến độ nên giữ.

### 2.6. Migration trùng cột

`TempTest` (`698b0b7`) và `AddMultipleAssignmentDependencies` (`f46c02d`, Designer ở `0b3eff0`) cùng `ADD COLUMN depends_on_assignment_ids_json`. Mọi DB đều lỗi 42701 khi migrate (DB mới, DB đã chạy một trong hai). Đổi cả hai sang `ADD COLUMN IF NOT EXISTS` / `DROP COLUMN IF EXISTS`; DB đã chạy migration nào thì EF không chạy lại.

## 3. Business rule liên quan

| BR | Cách giữ |
|---|---|
| BR-01 | Thử lại xuất = job mới; phản hồi giữ nguyên, chỉ gắn thêm id yêu cầu sửa |
| BR-04 | SHA-256 của file xuất tính ở server trên đúng bytes đã lưu |
| BR-07 | Quyền kiểm ở server theo WorkspaceRole; phản hồi, việc, job phải thuộc đúng chương trên route |
| BR-12 | `ChapterEditGuard` cho mọi lệnh sửa nội dung; xuất chỉ cho chương đã duyệt |
| BR-14 | Thông báo người làm khi có yêu cầu sửa; thông báo người yêu cầu khi xuất xong/lỗi |
| BR-16 | File xuất chỉ qua API có kiểm quyền, đếm lượt tải |
| BR-18 | Mọi thay đổi trạng thái đi qua `SaveChanges` có audit |
| NFR-05 | Xuất chạy nền, không chặn request |

## 4. Kiểm thử

- **Unit BE:** 880/880 (nền `test-feature` 860). Mới: `PreviewLinkTests` +3 (5.7), `ChapterReadOnlyTests` 4 (21 lệnh + mở lại), `ExportJobTests` 5, `SkiaChapterExportRendererTests` 8 (dựng file thật bằng SkiaSharp).
- **FE (vitest):** 408/408; typecheck và lint sạch. Mới: `PreviewLinksModal` +2, `previewLink.service` +1, `LetteringWorkspace` +1, `ExportView` 3, `export.service` 3.
- **Runtime API** (DB mới `panelforge_cf5_test`, chạy đủ migration): 35/35. Dựng chương thật Writer → Artist → Editor duyệt → Producer duyệt; BR-12 (409 `CHAPTER_READ_ONLY` cho Scene/Page/Element); export chặn trang thiếu ảnh, Writer 403, preset chưa hỗ trợ 400; toàn bộ vòng 5.7 tới duyệt lại chương; 4 định dạng `Completed`, tải về khớp SHA-256, mở được PDF/CBZ/JPEG; lượt tải được ghi.
- **Giao diện** (trình duyệt, BE + FE thật): màn Xuất chương liệt kê chương đã duyệt, tạo CBZ → *Đang chờ* → *Xong*; modal phản hồi tạo yêu cầu sửa và đổi nhãn.
- **Merge thử** với `test-feature` mới (`f397450`): không xung đột, 888/888.

## 5. Phát hiện ngoài phạm vi

- **`test-feature` @ `f397450` thiếu migration** cho `NarrationMode` (Scene, ScriptLine; commit Narration Mode của Minh): BE sẽ báo `PendingModelChangesWarning` khi khởi động. Cần Minh sinh migration.
- `ReviewView` (Editor) truyền `assignment.status` vào `chapterStatus` của `PreviewLinksModal`, nên Editor không thấy form tạo yêu cầu sửa (Producer thấy).
- Reader (`GetPublicPageImage`) lấy cả ảnh theo panel làm ảnh trang, khác `FinalArtwork`.
- Trang chỉ có ảnh theo panel không xuất và không vào link xem thử được (export báo 409 kèm số trang). Muốn hỗ trợ cần ghép ảnh panel theo khung.
- Mở lại chương rồi sửa artwork ở stage trước không tự vô hiệu các stage sau (việc 4.7 của Hoà).
- Worker giả định một instance API (shortcut); chạy nhiều instance cần khoá hàng `FOR UPDATE SKIP LOCKED`.

## 6. Phối hợp

- **Hoà:** 4.1 `ChapterSnapshot` dùng `FinalArtwork`; 5.6 chỉ cần thay danh sách artwork của PreviewLink/ExportJob bằng `ChapterSnapshotId`. 4.4 Khôi phục phải gọi `ChapterEditGuard`. 4.2 nên trả xung đột phiên bản với dấu riêng (như `ELEMENT_VERSION_CONFLICT`), không dùng `CHAPTER_READ_ONLY`. 4.5 trả dữ liệu provenance để gắn vào xuất JSON (5.3b).
- **Minh:** 3.7 đã có qua endpoint 5.7. Hai việc migration ở mục 2.6 và mục 5.

## 7. Checklist khi merge

- [ ] Merge BE `feat/cf5` vào `test-feature`, FE `feat/cf5` vào `tam` cùng lúc (FE dùng `changeRequestId`, `CHAPTER_READ_ONLY`, API exports).
- [ ] Chạy migration `LinkPreviewFeedbackChangeRequest`, `AddExportJobs` (và migration `NarrationMode` khi Minh bổ sung).
- [ ] Build Docker image một lần để kiểm SkiaSharp chạy trên Linux (`SkiaSharp.NativeAssets.Linux.NoDependencies`).
- [ ] ExportPreset đã seed sẵn (`CBZ`, `PDF`, `PAGE_IMAGES`, `WEBTOON`, `OPEN_JSON`); `OPEN_JSON` trả 400 tới khi làm 5.3b.
- [ ] Báo Minh mục 2.6 và mục 5; báo Hoà mục 6.
