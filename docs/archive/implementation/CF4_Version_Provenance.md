# Đặc tả thay đổi: Core Flow 4 (CF4): Version Control & Provenance

> **Trạng thái (rà 2026-11-12):** Đã merge, tài liệu giữ làm lịch sử thay đổi. Hai việc còn mở được chuyển sang kế hoạch nội bộ: gắn Snapshot vào `ExportJob` (§7) → `REL-05`, kiểm thử trình duyệt mục F (§8) → `REL-10`. Checklist merge §7 không còn hiệu lực.

> **Người thực hiện:** Huỳnh Công Hoà · **Ngày:** 17/10/2026
> **Nhánh:** BE `feat/cf4` (tách từ `test-feature` @ `63259c0`) · FE `feat/cf4` (tách từ `trungkien` @ `205c6c6`)
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
| — | FE: modal Lịch sử & nguồn gốc (bản duyệt, Provenance Report, lịch sử AI), canvas/scene gửi `expectedVersion` | Xong | (FE) |
| 4.3 (trang) | Lịch sử, xem lại, so sánh và khôi phục canvas Page (UC-18, US-12) | Xong | `feat/cf4` 22/10 |
| 4.6 | Xuất JSON kèm provenance (`ExportJob` định dạng `OPEN_JSON`) | Xong | `feat/cf4` 22/10 |
| 4.7 (mở rộng) | Sửa/xoá thoại hoặc đổi khung Panel sau khi duyệt → việc đã duyệt của đúng Panel đó làm lại | Xong | `feat/cf4` 22/10 |
| 4.8 | Chốt Q4, Q6, `expectedVersion` (mục 2.10) | Xong, chờ GVHD xác nhận | `feat/cf4` 22/10 |
| — | FE: tab "Lịch sử trang" trong modal, định dạng "JSON kèm nguồn gốc" ở màn Xuất | Xong | FE `CongHoa` 22/10 |

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

`PanelDto`, `PanelDetailDto`, `SceneDto`, `SceneDetailDto` trả thêm `version`. Version chỉ tăng khi giá trị thật sự đổi (lưu lại y nguyên không làm người khác bị xung đột). Dùng version riêng thay vì `Chapter.VersionVector` để kéo một panel không làm đề xuất AI đang chờ thành cũ, và hai người sửa hai panel khác nhau không xung đột. Mã lỗi là `CONFLICT`, không phải `CHAPTER_READ_ONLY` (theo ghi chú của Danh).

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

### 2.7. Lịch sử canvas Page (phần trang của 4.3)

Version = vị trí event trong stream Marten của Page (1 = `PageCreated`). Khôi phục thêm event mới, không xoá event nào: element đang có mà bản cũ không có → `ElementRemovedEvent` (lý do `RESTORE_TO_VERSION_n`); element khác bản cũ hoặc đã xoá mềm → `ElementRestoredEvent` mới (mang đủ trạng thái element). Xoá trước rồi mới dựng lại nên ràng buộc một ScriptLine – một element (BR-05) giữ đúng như bản cũ. Version của element tiếp tục tăng, không quay về số cũ.

| Endpoint | Quyền | Ghi chú |
|---|---|---|
| `GET /api/pages/{pageId}/history?page=1&pageSize=50` | Thành viên | Mới nhất trước, tối đa 100/trang |
| `GET /api/pages/{pageId}/versions/{version}?includeRemoved=` | Thành viên | Dùng lại `GetPageSnapshotQuery` (như `GET .../elements?version=`) |
| `GET /api/pages/{pageId}/compare?left=&right=` | Thành viên | Khớp theo `elementId`: `Added`, `Removed`, `Modified` (+ `changedFields`) |
| `POST /api/pages/{pageId}/versions/{version}/restore` | Producer, Artist | Body `{ expectedPageVersion }` **bắt buộc**; lệch → 409 `CONFLICT`; chương không InProduction → 409 `CHAPTER_READ_ONLY`; `version` phải < version hiện tại → 400 |

### 2.8. Xuất JSON kèm provenance (4.6)

Dùng chung `ExportJob` (preset `OPEN_JSON` đã seed sẵn), không làm endpoint riêng: tạo `POST /api/chapters/{chapterId}/exports` body `{ "presetCode": "OPEN_JSON" }`, theo dõi và tải như các định dạng khác. Tạo job đòi chương Approved/Published, đủ artwork như cũ, **và có `ChapterSnapshot`** (không có → 409). Worker gọi lại `GetProvenanceReportQuery` qua MediatR (không tổng hợp lại), theo người yêu cầu tại lúc chạy: người đó không còn là Producer/Editor thì job `Failed`.

File (`application/json`, `<Series> - Chuong <n> - provenance.json`):

| Trường | Nội dung |
|---|---|
| `schema`, `schemaVersion` | `panelforge.open-json`, `1.0` |
| `series`, `chapter` | id, tiêu đề, số chương, `chapterVersion` của job |
| `snapshot` | id, số bản duyệt, người duyệt, `manifestHash`, `manifestJson` (chuỗi gốc), `manifest` (object để đọc) |
| `provenance` | Nguyên Provenance Report: `stats`, `nodes`, `edges`, `timeline` |
| `verification` | Cách kiểm chứng (bên dưới) |

Kiểm chứng ngoài nền tảng: SHA-256 (hex thường) của bytes UTF-8 của `snapshot.manifestJson` phải bằng `snapshot.manifestHash`; SHA-256 của cả file phải bằng `sha256` của job trong `GET /api/chapters/{chapterId}/exports` (màn Xuất hiện 12 ký tự đầu, rê chuột xem đủ). Manifest hỏng hash thì job `Failed`, không xuất.

Tải file: Producer tải mọi định dạng; **Editor chỉ tải `OPEN_JSON`** (Q4), định dạng khác → 403 `FORBIDDEN`.

### 2.9. Vô hiệu phê duyệt khi nguồn đổi (mở rộng 4.7)

Dùng chung lõi với 4.7 (`StageProgressRules.ReworkForSourceChangeAsync`): việc **đã duyệt** bao phủ Panel liên quan → `ReworkRequired`, dòng tiến độ `Approved` → `ChangesRequested`, người làm được báo (`AssignmentChangesRequested`). Ghi cùng lần SaveChanges với thao tác gốc (audit theo SaveChanges). Việc chưa duyệt không đổi: chữ lỗi thời đã chặn nộp (BR-05).

| Thao tác | Điều kiện | Việc bị trả về | Panel bị ảnh hưởng |
|---|---|---|---|
| `PUT /api/chapters/{id}/script-lines/{lineId}` (sửa dòng) | Thoại/chỉ dẫn hoặc speaker đổi thật | Lettering (Letterer) đã duyệt | Panel có bóng gắn dòng đó: `PanelId` của element → Panel neo dòng → các Panel cùng trang với bóng |
| `DELETE /api/chapters/{id}/script-lines/{lineId}` | Luôn (dòng đã đặt chữ bị xoá) | như trên | như trên |
| `PUT /api/panels/{panelId}/coordinates` | `boundingBox` đổi thật (chỉ đổi thứ tự đọc thì không) | Artwork (Artist) + Lettering (Letterer) đã duyệt | Đúng Panel đó |

### 2.10. Quyết định 4.8

| Câu hỏi | Quyết định | Ghi chú |
|---|---|---|
| Q4: Editor xem Provenance Report? | **Có**, chỉ đọc; tải được file JSON provenance | Như code và mục Decision #69 của `PanelForge_Capstone_Content.md`; ghi thêm #88. Chờ GVHD xác nhận |
| Q6: Xung đột phiên bản 409 hay 412? | **409 `CONFLICT`** | Chương chỉ đọc cũng 409 nhưng mã `CHAPTER_READ_ONLY` để FE phân biệt (#89) |
| E: bắt buộc `expectedVersion`? | **Không** với Panel/Scene (null = không kiểm) | Hàng chờ ngoại tuyến cố ý không gửi; nhánh FE khác còn gọi bản cũ. Endpoint mới (khôi phục Page) bắt buộc (#90) |

Không có thay đổi breaking cho API đã `DONE`: chỉ thêm endpoint, thêm định dạng `OPEN_JSON`, thêm quyền Editor tải JSON.

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

- **Unit BE:** 944/944 (nền `test-feature` @ `63259c0`: 926). Mới: `ChapterPublishFlowTests` +4 (snapshot), `HistoryProvenanceTests` 7, `ContentConcurrencyTests` 5, `PreviewLinkTests` +2 (4.7).
- **Unit BE đợt 22/10:** 1052/1052 (nền `test-feature` @ `a824a39`: 1039). Mới: `ExportJobTests` +4 (JSON: cần snapshot, tính lại hash file và `manifestHash` khớp, manifest hỏng / mất quyền → Failed, Editor chỉ tải JSON, người ngoài 403), `PageVersionHistoryTests` 4 (lịch sử + phân trang, compare, khôi phục giữ lịch sử, 409/400/chương chỉ đọc), `SourceChangeReworkTests` 5 (sửa/xoá thoại, đổi khung, lưu y nguyên không ảnh hưởng, Panel khác không bị ảnh hưởng).
- **Unit FE (vitest):** 595/595. Mới: `provenanceRules.test.ts`, `ChapterProvenanceModal.test.tsx`, `splitPanel.test.ts` +1.
- **Unit FE đợt 22/10:** 600/600. Mới: `PageHistoryPanel.test.tsx` 4, `provenanceRules.test.ts` +1.
- **API trên DB thật:** dữ liệu mẫu "Studio Hoàng Hôn"; các API CF4 trả đúng dữ liệu. **Chưa** kiểm giao diện bằng trình duyệt (mục F, checklist ở mục 8).

## 5. Migration

`AddChapterSnapshots`, `AddPanelSceneVersion` (thêm cột `version int default 0` cho `panels`, `scenes`). Chạy sau `AddBibleRevisionStageRange`.

## 6. Phối hợp

- **Danh:** đã có `ChapterSnapshot` cho 5.5 và 5.6 (thay danh sách artwork của PreviewLink/ExportJob bằng `ChapterSnapshotId`). Handler Provenance Report dùng được cho export JSON (5.3b).
- **FE (đã làm trên `feat/cf4`):** `ChapterProvenanceModal` (shared) mở từ danh sách chương của Producer, bàn Publisher (chỉ tab Bản duyệt + Lịch sử AI) và màn task (thay `ProvenanceModals.tsx` dữ liệu giả). Artist gửi `expectedVersion` khi lưu/kéo/chia khung (hàng chờ ngoại tuyến không gửi để các lần sửa nối tiếp không tự xung đột), Writer gửi khi sửa phân cảnh; 409 `CONFLICT` thì nạp lại, không tự gửi lại. Xem/so sánh/khôi phục script dùng `ScriptHistoryModal` có sẵn của Writer.

## 7. Checklist khi merge

- [x] Merge BE `feat/cf4` vào `test-feature`; chạy 2 migration ở mục 5.
- [ ] Báo Danh mục 6 (5.5, 5.6, 5.3b) và 2.8 (JSON đã dùng `ExportJob`; khi 5.6 thêm `ChapterSnapshotId` vào `ExportJob` thì thay `OpenJsonExport.SnapshotOfAsync` bằng id đó).
- [x] FE dùng `expectedVersion` cho Panel/Scene (không bật kiểm tra bắt buộc, xem 2.10).
- [ ] Merge đợt 22/10 (BE `feat/cf4`, FE `CongHoa`). Không có migration EF mới; event Marten mới `ElementRestoredEvent` đã đăng ký trong `DependencyInjection`.
- [ ] Chép route/DTO ở mục 2.7–2.9 vào `HANDOFF.md` (không có trong repo), đánh dấu `DONE`.

## 8. Kiểm thử bằng trình duyệt (mục F, chưa chạy)

| # | Bước | Kỳ vọng | Kết quả |
|---|---|---|---|
| 1 | Producer duyệt chương | Snapshot mới ở tab "Bản duyệt" của modal Lịch sử & nguồn gốc, hash khớp | |
| 2 | Hai tab cùng sửa một Panel (kéo khung) hoặc một Scene | Tab sau nhận 409, nạp lại, không ghi đè | |
| 3 | Writer so sánh rồi khôi phục script (ScriptHistoryModal) | Revision mới, nội dung về bản cũ | |
| 4 | Tab Provenance | Đủ chuỗi Run → Step → Suggestion → Decision → Revision → Snapshot | |
| 5 | Writer/Artist mở report | Tab ẩn; gọi thẳng API → 403 | |
| 6 | Tab "Lịch sử trang": so sánh rồi khôi phục (Artist) | Thêm event, canvas về bản cũ, lịch sử cũ còn | |
| 7 | Màn Xuất: "JSON kèm nguồn gốc" | Tải được; tính SHA-256 file khớp, `manifestHash` khớp | |
| 8 | Writer sửa thoại đã đặt chữ (Lettering đã duyệt) | Việc Lettering của Panel đó → cần làm lại, Letterer nhận thông báo | |
