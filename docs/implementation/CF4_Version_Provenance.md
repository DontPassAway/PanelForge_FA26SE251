# Đặc tả thay đổi: Core Flow 4 (CF4): Version Control & Provenance

> **Người thực hiện:** Huỳnh Công Hoà · **Ngày:** 17/10/2026
> **Nhánh:** BE `feat/cf4` (tách từ `test-feature` @ `63259c0`) · FE chưa làm
> **Căn cứ:** `docs/requirements/PanelForge_5_Core_Flows.md` mục 4, ghi chú phối hợp ở `CF5_Approval_Export.md` mục 6.

## 1. Tổng quan

| # | Việc | Trạng thái | Commit BE |
|---|---|---|---|
| 4.1 | `ChapterSnapshot`: manifest bất biến khi Producer duyệt chương + API xem | Xong | `e2e5829`, `81f0e18` |
| 4.2 | Chống ghi đè Panel và Scene bằng version riêng (BR-11) | Xong | `b28ddc2` |
| 4.3 | Lịch sử AI run, so sánh hai revision (UC-18) | Xong | `48beee8`, `fab27c5` |
| 4.4 | Khôi phục: dùng `RestoreScriptRevisionCommand` có sẵn, bỏ bản trùng | Xong | `c156cec` |
| 4.5 | Provenance Report của chương (UC-19, BR-10) | Xong | `48beee8`, `fab27c5` |
| 4.7 | Trả việc stage trước về làm lại thì vô hiệu các stage sau của cùng Panel | Xong | `17b0f48` |
| — | FE: màn lịch sử, so sánh, khôi phục, Provenance Report, danh sách snapshot | **Chưa** | — |

## 2. Chi tiết thay đổi

### 2.1. `ChapterSnapshot` (4.1)

Khi `ApproveChapter` thành công, hệ thống ghi một snapshot **cùng lần Save** với việc duyệt (BR-18). Mở lại rồi duyệt lại thì tạo snapshot mới; snapshot cũ không bị sửa hay xoá (BR-01). Bảng `chapter_snapshots`, unique `(chapter_id, snapshot_number)`, không xoá mềm.

Manifest chỉ lưu tham chiếu, không chép ảnh:

| Trường | Nội dung |
|---|---|
| `pages[]` | `pageId`, `pageNumber`, `canvasStreamVersion` (Marten, `-1` = chưa có event), `artworkReferenceId` + `artworkSha256` theo `FinalArtwork` (null = trang chưa có ảnh cả trang) |
| `scriptRevisionIds` | Revision script mới nhất của từng scene |
| `bibleRevisionIds` | Revision Bible đã khoá có hiệu lực tại chương (phạm vi From/To; chồng lấn thì giai đoạn From lớn hơn thắng) |

`manifestHash` = SHA-256 của manifest.

| Endpoint | Quyền | Ghi chú |
|---|---|---|
| `GET /api/chapters/{chapterId}/snapshots` | Thành viên | Mới nhất trước; `isLatest` = bản duyệt hiện hành |
| `GET /api/chapters/{chapterId}/snapshots/{snapshotId}` | Thành viên | `manifest` + `hashVerified`; snapshot khác chương → 404 |

### 2.2. Chống ghi đè Panel và Scene (4.2)

`Panel.Version` tăng khi đổi khung, thứ tự đọc hoặc scene; `Scene.Version` tăng khi đổi nội dung (đổi số thứ tự cảnh không tăng). Cả hai là concurrency token.

| Endpoint | Body thêm | Kết quả |
|---|---|---|
| `PUT /api/panels/{panelId}/coordinates` | `expectedVersion?` | Lệch → **409 `CONFLICT`**; `null` → không kiểm (client cũ) |
| `PUT /api/scenes/{sceneId}` | `expectedVersion?` | như trên |

`PanelDto`, `PanelDetailDto`, `SceneDto` trả thêm `version`. Dùng version riêng thay vì `Chapter.VersionVector` để kéo một panel không làm đề xuất AI đang chờ thành cũ, và hai người sửa hai panel khác nhau không xung đột. Mã lỗi là `CONFLICT`, không phải `CHAPTER_READ_ONLY` (theo ghi chú của Danh).

### 2.3. Lịch sử và so sánh (4.3)

| Endpoint | Quyền | Ghi chú |
|---|---|---|
| `GET /api/series/{seriesId}/ai/runs?chapterId&sceneId&status&workflowType&page&pageSize` | Thành viên | Mới nhất trước, tối đa 50/trang, kèm số step/đề xuất/accept/reject |
| `GET /api/series/{seriesId}/ai/scenes/{sceneId}/revisions/compare?left=&right=` | Thành viên | Hai revision phải cùng loại (khác loại → 400) |

So sánh script theo `LineId` (đọc được cả payload AI `{lines}` và payload sửa tay dạng mảng): `Added`, `Removed`, `Modified`, `Moved` (chỉ đổi thứ tự). Loại khác so theo khoá/chỉ mục JSON. Luôn trả `leftPayload`/`rightPayload` để FE tự render sâu hơn.

### 2.4. Khôi phục (4.4)

Dùng endpoint có sẵn: `POST /api/chapters/{chapterId}/scenes/{sceneId}/script-revisions/{revisionId}/restore` (Producer/Writer, body `{ expectedChapterVersion }`). Endpoint `POST .../ai/scenes/{sceneId}/revisions/{revisionId}/restore` trong bản nháp đã **bỏ**: nó chỉ thêm dòng revision mà không áp dụng lại `ScriptLines`, không chặn chương chỉ đọc, không tăng version và không giới hạn quyền.

### 2.5. Provenance Report (4.5)

`GET /api/series/{seriesId}/ai/chapters/{chapterId}/provenance?sceneId=`, quyền **Producer, Editor** (Writer/Artist → 403).

Trả `stats`, `nodes`, `edges`, `timeline` (cũ nhất trước):

| Node | `details` |
|---|---|
| `Run` | workflow, provider/model, người chạy, version chương/script nền, revision Bible |
| `Step` | agent, vòng, model, `promptVersion`, `promptRef`, tham số, token, thời gian (không trả input/output: xem `GET /ai/runs/{runId}`) |
| `Suggestion` | loại, trạng thái, hash payload, revision script/Bible đầu vào |
| `Decision` | quyết định, người quyết, lý do, `ModificationLevel` |
| `Revision` | loại, version, hash, nguồn `AI`/`Human`, mapping |
| `Snapshot` | số bản duyệt, người duyệt, hash manifest |

Cạnh: `produced`, `inputFor`, `dependsOn`, `basedOn`, `decided`, `created`, `previousVersion`, `originatedFrom`, `restoredFrom`, `includedIn` (Revision → Snapshot). Trả lời được "agent nào, từ revision script và Bible nào, tạo ra nội dung này, ai duyệt, có nằm trong bản duyệt nào không".

### 2.6. Vô hiệu các stage sau (4.7)

Khi việc **đã duyệt** ở stage S bị trả về qua "phản hồi xem thử → yêu cầu sửa" (5.7), với các Panel việc đó bao phủ:

- Việc ở stage sau đã duyệt → `ReworkRequired`, dòng tiến độ `Approved` → `ChangesRequested`, người làm được báo (`AssignmentChangesRequested`).
- Việc ở stage sau đang làm/chờ duyệt: giữ trạng thái, chỉ được báo.
- BR-19 có sẵn chặn việc sau bắt đầu lại cho tới khi stage S được duyệt lại.
- Trang/Panel khác và việc cấp Chapter không bị ảnh hưởng.

## 3. Business rule liên quan

| BR | Cách giữ |
|---|---|
| BR-01 | Snapshot, revision chỉ thêm; khôi phục tạo revision mới; không xoá lịch sử |
| BR-06 | Sửa stage đã đóng chỉ qua trả về; stage sau tự làm lại theo (4.7) |
| BR-07 | Kiểm quyền ở server; snapshot/revision/scene phải thuộc đúng chương/series trên route |
| BR-10 | Report trả đủ model, prompt, tham số, revision đầu vào, người quyết, mức sửa |
| BR-11 | `expectedVersion` cho Panel/Scene, 409 khi lệch, concurrency token khi lưu đồng thời |
| BR-18 | Snapshot và vô hiệu stage sau lưu cùng SaveChanges với thao tác gốc (có audit) |

## 4. Kiểm thử

- **Unit BE:** 943/943 (nền `test-feature` @ `63259c0`: 926). Mới: `ChapterPublishFlowTests` +4 (snapshot), `HistoryProvenanceTests` 7, `ContentConcurrencyTests` 4, `PreviewLinkTests` +2 (4.7).
- **Chưa:** chạy API trên DB thật, giao diện.

## 5. Migration

`AddChapterSnapshots`, `AddPanelSceneVersion` (thêm cột `version int default 0` cho `panels`, `scenes`). Chạy sau `AddBibleRevisionStageRange`.

## 6. Phối hợp

- **Danh:** đã có `ChapterSnapshot` cho 5.5 và 5.6 (thay danh sách artwork của PreviewLink/ExportJob bằng `ChapterSnapshotId`). Handler Provenance Report dùng được cho export JSON (5.3b).
- **FE:** canvas gửi `expectedVersion` khi kéo/đổi panel và sửa scene, giữ `version` từ response; 409 `CONFLICT` thì tải lại. Màn lịch sử/so sánh/khôi phục/Provenance Report thay cho `ProvenanceModals.tsx` đang là dữ liệu giả. Tên field lấy từ `docs/api/openapi.json` (đã xuất lại).

## 7. Checklist khi merge

- [ ] Merge BE `feat/cf4` vào `test-feature`; chạy 2 migration ở mục 5.
- [ ] Báo Danh mục 6 (5.5, 5.6, 5.3b).
- [ ] FE dùng `expectedVersion` cho Panel/Scene trước khi bật kiểm tra bắt buộc.
