# Đặc tả thay đổi: sửa các hạn chế Core Flow 1 (CF1)

> **Trạng thái (rà 2026-11-12):** Đã merge, tài liệu giữ làm lịch sử thay đổi. Các mục còn mở ở §2.6 (quota ảnh) và §5 (migration, chữ giữ chỗ "Canon Spec Entity") đã được sửa trong code; checklist §6 không còn hiệu lực. Việc mới theo dõi ở `panelforge-fe/docs/TEAM_WORKBOARD.md`.

- **Nhánh:** `test-feature-cf1-limitations` (tách từ `test-feature` @ `f46c02d`)
- **Ngày:** 11/10/2026
- **Người thực hiện:** Phạm Thành Danh
- **Nguồn yêu cầu:** mục "Core Flow 1 – Thiết lập Studio & Series Bible" trong danh sách hạn chế của `TONG_KET_DU_AN_PANELFORGE.md`
- **FE đi kèm:** repo `panelforge-fe`, nhánh `tam-cf1-limitations` (tách từ `tam` @ `4e1bc2d`). FE **bắt buộc** merge cùng lúc vì endpoint `/unlock` đã bị bỏ.

## 1. Tổng quan

| # | Hạn chế ban đầu | Trạng thái | Commit BE |
|---|---|---|---|
| 1 | Tạo chương chưa nhận `outline` | Đã có từ `8524469`; bổ sung test | `6a7affd` |
| 2 | Không có mở khoá Character Sheet | Bỏ `/unlock` (trái V1.2), thay bằng **thay thế bản khoá** (`/supersede`) | `45da281` |
| 3 | AI/checker chưa chặn nhân vật chưa khoá | AI chỉ dùng bản khoá; checker cảnh báo `CHARACTER_NOT_LOCKED`; UI hiện lại nhân vật chưa khoá | `45da281` |
| 4 | Idle timeout chỉ kiểm khi refresh (trễ tới 30') | Kiểm ở **mỗi request** theo hoạt động thật; sửa bug đăng xuất người đang làm việc | `5d6ab48` |
| 5 | Producer chưa giám sát được phiên đồng thời | BE đã có từ `f35c7dd`, FE đã có từ `47e3290` (Minh, nhánh `tam`); nhánh này sửa cách đếm (bỏ phiên quá idle) | `5d6ab48` |
| 6 | Quota sinh ảnh luôn 0 | **Chưa sửa**: chưa có pipeline sinh ảnh. FE ghi chú "Chưa áp dụng" | — |

Không có migration mới. Không đổi schema.

## 2. Chi tiết thay đổi

### 2.1. Outline khi tạo chương (mục 1)

`POST /api/series/{seriesId}/chapters` đã nhận `outline` từ `8524469`, gọi cùng `Chapter.SetOutline()` với `PUT /chapters/{id}/outline`. Form tạo chương FE (`SeriesChaptersModal`) đã gửi `outline`. Bổ sung test: outline được trim, chuỗi trắng hoặc `null` lưu thành `null`.

### 2.2. Thay thế bản khoá thay cho mở khoá (mục 2) — **BREAKING**

**Vấn đề:** `87f05a3` thêm `POST .../revisions/{revisionId}/unlock`, xoá `LockedAt` và `LockedById`. Như vậy trái quy tắc V1.2 "bản khoá chỉ được thay bằng phiên bản mới" (và trái chính chú thích trong `BibleEntryRevision`). Endpoint này không ghi lý do và không có test. Nguyên nhân gốc khiến cần "mở khoá": `AddBibleEntryRevision` chặn tạo bản mới trùng chương bắt đầu của bản khoá. Vì vậy bản khoá ở chương 1 không bao giờ sửa được cho chương 1.

**Thay đổi:**
- Xoá `UnlockBibleEntryRevisionCommand`, `BibleEntryRevision.Unlock()` và endpoint `/unlock`.
- Thêm `POST /api/series/{seriesId}/bible/entries/{entryId}/revisions/{revisionId}/supersede`. Chỉ `[RequireWorkspaceRole(Producer)]` được gọi.
  - Tạo revision mới **cùng chương** với bản khoá và **khoá luôn** (người khoá = Producer gọi).
  - `VersionNumber` của bản mới lớn hơn, nên query hiệu lực (BR-15, sắp theo `EffectiveFrom` rồi `VersionNumber`) tự chọn bản mới.
  - Bản cũ giữ nguyên `LockedAt`, `LockedById`, `SnapshotJson` (BR-01).
  - Lý do bắt buộc, ghi vào `Summary` dạng `Thay thế bản khoá vN: <lý do>`. Audit log tự ghi `CREATE_BIBLEENTRYREVISION` (BR-18).
- Dùng lại `AddBibleEntryRevisionCommand` với 2 tham số mới `SupersedesRevisionId` và `SupersedeReason`. Không tạo handler mới.

**Request** (`SupersedeBibleEntryRevisionRequest`):

```json
{
  "reason": "Sai màu tóc trong bản khoá",
  "name": "An",
  "description": "An (17 tuổi), tóc đen.",
  "subtitle": null,
  "detailsJson": null,
  "referenceImageUrl": null,
  "priority": "Standard",
  "strictCheck": true,
  "expectedVersionNumber": 1
}
```

**Kết quả:**

| Trường hợp | HTTP |
|---|---|
| Thành công | 200, `BibleEntryRevisionDto` (`isLocked = true`) |
| Lý do rỗng hoặc dài hơn 400 ký tự | 400 |
| Không phải Producer | 403 |
| Revision không thuộc entry/series | 404 |
| Revision chưa khoá, hoặc `expectedVersionNumber` lệch | 409 |

`POST .../unlock` giờ trả 404/405.

**FE** (`24b708f`): nút "Mở Khoá Character Sheet" đổi thành "Thay thế bản khoá". Nút mở lại form tạo phiên bản ở chế độ thay thế:
- Chương hiệu lực bị khoá theo bản cũ.
- Nội dung điền sẵn từ snapshot của bản khoá.
- Lý do bắt buộc.

`bibleService.unlockRevision` được thay bằng `supersedeRevision`.

### 2.3. Canon lock áp đúng chỗ (mục 3)

**Vấn đề:** `6590a7e` chỉ lọc "Nhân vật phải có bản khoá" trong `GetBibleEntriesAtChapterQuery`. Đây là query của **màn hình** `GET /bible/at-chapter/{n}`.
- **AI không dùng query này.** `AiSnapshotBuilder` tự chọn revision theo cách riêng và không lọc khoá.
- **Checker cũng không dùng.** `RunConsistencyCheckCommand` không đọc trạng thái khoá.
- **Hệ quả:** AI vẫn dùng nhân vật chưa khoá, còn UI lại làm nhân vật chưa khoá **biến mất**. Writer không thấy nhân vật vừa tạo; mở khoá xong nhân vật cũng biến mất.

**Thay đổi:**
- Thêm `BibleEntry.EffectiveRevisionAt(int chapter, bool canonOnly = false)`. Đây là một chỗ duy nhất cho quy tắc BR-15. `canonOnly` chỉ áp cho mục Nhân vật.
- `AiSnapshotBuilder.BuildBibleAsync` dùng `canonOnly: true`. AI chỉ dựa trên character sheet đã khoá; nhân vật chưa khoá bị bỏ khỏi context.
- `GetBibleEntriesAtChapterQuery` dùng `canonOnly: false`. UI hiện tất cả; DTO có sẵn `IsLocked` để FE gắn nhãn.
- `ConsistencyChecker` thêm mã **`CHARACTER_NOT_LOCKED`**:
  - Mức **Warning**, nên không chặn duyệt chương (chỉ Blocking mới chặn, trả 409).
  - Báo khi dòng thoại hoặc liên kết trỏ tới nhân vật đã có hiệu lực ở chương này nhưng chưa có bản khoá nào từ chương này trở về trước.
  - Báo **một lần mỗi nhân vật mỗi chương**: `ScriptLineId = null`, evidence = tên nhân vật.
  - `CheckEntry` thêm `FirstLockedChapter`.
- **Không** thêm enum `Draft | Approved | CanonLocked`. `IsLocked` đủ cho V1.
- `AiDemoSeeder` khoá sẵn nhân vật mẫu (An, Bà Lan) để demo AI có nhân vật.

> **Lưu ý dữ liệu:** seeder chỉ chạy khi DB chưa có Studio Moonlight. Với DB dev đã seed trước đây, nhân vật chưa khoá sẽ **không** vào context AI cho tới khi Producer bấm "Duyệt & Khoá Character Sheet".

### 2.4. Idle timeout kiểm ở mỗi request (mục 4)

**Bug cũ:** idle chỉ được kiểm ở `POST /auth/refresh` và đo từ `RefreshToken.CreatedAt`, tức lần phát token. FE chỉ refresh khi gặp 401, tức sau khi JWT 30' hết hạn. Hệ quả có hai chiều:
- **Bị đăng xuất oan:** nếu idle < 30', mọi lần refresh đều bị từ chối, nên **người đang làm việc liên tục bị đăng xuất mỗi 30 phút**.
- **Hết idle mà vẫn dùng được:** máy bỏ quên vẫn thao tác được tới khi JWT hết hạn.

**Thay đổi:**
- `AccessTokenGuard`, chạy ở mỗi request trong `OnTokenValidated`:
  - Tìm token còn sống của `session_id`.
  - Từ chối nếu `now - LastActiveAt > idle`. `idle` là giá trị chặt nhất trong các Workspace của user, lấy qua `SessionPolicy.ForUserAsync`.
  - Nếu hợp lệ thì ghi `LastActiveAt = now`, tối đa **30 giây một lần** mỗi phiên.
- `RefreshTokenCommand` và `SessionPolicy.EnforceMaxSessionsAsync` đo idle từ `LastActiveAt` thay vì `CreatedAt`.
- `PanelForgeDbContext`: bản ghi `RefreshToken` chỉ đổi `LastActiveAt` (nhịp hoạt động) **không** ghi `AuditLog`, để không tạo một dòng audit mỗi 30 giây cho mỗi user. Mọi thay đổi khác vẫn được audit (BR-18).
- `SessionPolicy` chuyển từ `internal` thành `public` để API dùng chung.
- **Sai số:** phiên có thể bị coi là idle sớm tối đa 30 giây (idle tối thiểu 1 phút).
- **Lưu ý:** request chạy nền cũng được tính là hoạt động. Hiện FE chưa có polling. Nếu sau này thêm polling thông báo, cần loại các request đó ra.

### 2.5. Giám sát phiên đồng thời (mục 5)

- **BE** (đã có từ `f35c7dd`):
  - `GET /api/workspaces/{id}/sessions`.
  - `DELETE /api/workspaces/{id}/sessions/{sessionId}`: chỉ Producer, có kiểm tra thành viên và ghi audit.
  - Usage có `activeSessions`, `activeMemberCount`.
- **Sửa:** `GetWorkspaceUsage` và `GetWorkspaceActiveSessions` bỏ các phiên đã quá idle timeout của Workspace. Trước đây chúng chỉ loại token đã thu hồi hoặc hết hạn, nên đếm dư cả phiên bỏ quên nhiều ngày.
  - *Shortcut:* lọc theo policy của chính Workspace đó. User có policy chặt hơn ở Workspace khác có thể bị đếm dư cho tới request kế tiếp của họ.
- **FE:** Minh đã làm ở `47e3290` trên `tam`: đồng hồ số thiết bị, modal danh sách có tìm kiếm, thu hồi phiên, tải lại usage sau khi thu hồi. Nhánh FE này **không** thêm giao diện phiên riêng, để tránh trùng.
- **Lưu ý nghiệp vụ:** phiên thuộc về user, không theo Workspace. Producer studio A thu hồi phiên thì user bị đăng xuất khỏi thiết bị đó ở mọi studio.

### 2.6. Quota sinh ảnh (mục 6) — chưa sửa

`WorkspaceAiConfig.HasImageQuotaFor()` và `RecordImages()` đã có test nhưng không nơi nào gọi, vì chưa có pipeline sinh ảnh. Khi làm pipeline sinh ảnh nhân vật, handler phải chạy theo thứ tự:

1. Gọi `CheckAvailabilityAsync`.
2. Gọi `HasImageQuotaFor(n)`; hết quota thì trả QuotaExceeded.
3. Gọi provider sinh ảnh.
4. Gọi `RecordImages(n)`, chỉ khi sinh ảnh thành công.

Gợi ý: thêm `ImageCount` vào `AiUsageEntry` để `AiUsageService.RecordAsync` ghi cả token lẫn ảnh. Tạm thời FE (`ae8d32f`) hiện dòng "Chưa áp dụng" dưới đồng hồ quota ảnh.

## 3. Business rule liên quan

| BR | Cách giữ |
|---|---|
| BR-01 (append-only) | Bản khoá không bị sửa hay mở khoá; thay thế = thêm revision mới |
| BR-07 (quyền theo WorkspaceRole) | `/supersede` và thu hồi phiên chỉ dành cho Producer, kiểm ở server |
| BR-15 (Bible theo chương hiệu lực) | Một hàm duy nhất `EffectiveRevisionAt` |
| BR-18 (audit) | Supersede và thu hồi phiên có audit; nhịp `LastActiveAt` được loại trừ có chủ đích |
| BR-21 (triage finding) | `CHARACTER_NOT_LOCKED` đi qua cùng luồng triage |

## 4. Kiểm thử

### 4.1. Unit test BE: **850/850 pass** (838 cũ + 12 mới)

| File | Nội dung test mới |
|---|---|
| `AccessTokenGuardTests` | Chuyển sang EF InMemory. Thêm 3 test: quá idle bị từ chối; hoạt động được ghi lại mà không sinh audit; trong 30 giây thì không ghi lại |
| `SessionPolicyTests` | Thêm 1 test: refresh sau khi JWT hết hạn nhưng có hoạt động gần đây thì vẫn thành công (test hồi quy cho bug đăng xuất mỗi 30') |
| `WorkspaceUsageTests`, `WorkspaceSessionTests` | Mở rộng test có sẵn: phiên quá idle không được tính |
| `LockBibleEntryRevisionTests` | Thêm 3 test: supersede thành công (bản cũ giữ nguyên); thiếu lý do bị 400; bản chưa khoá bị 409 |
| `SeriesBibleTests` | Thêm 1 test: `EffectiveRevisionAt` với `canonOnly` |
| `BibleRevisionCurrentStateTests` | Sửa test có sẵn: UI vẫn hiện bản chưa khoá |
| `ConsistencyCheckTests` | Thêm 1 test: `CHARACTER_NOT_LOCKED` chỉ báo một lần và không chặn duyệt |
| `ChapterCommandTests` | Thêm 3 test (một Theory): outline khi tạo chương |

### 4.2. API trên server thật: **22/22 kiểm tra pass**

Chạy trên DB riêng `panelforge_cf1_test`, có seed demo AI, bằng các tài khoản dev.
- **Mục 2:**
  - Nhân vật seed đã khoá.
  - `/unlock` trả 404/405.
  - Writer gọi `/supersede` bị 403; thiếu lý do bị 400.
  - Producer supersede: 200, v2 đã khoá, cùng chương 1; bản v1 giữ nguyên; Bible chương 3 dùng v2.
- **Mục 3:**
  - Tạo nhân vật chưa khoá; nhân vật vẫn hiện ở `at-chapter`.
  - Dòng thoại của nhân vật này làm consistency check ra `CHARACTER_NOT_LOCKED` (Warning).
- **Mục 1:** tạo chương kèm outline; outline được trim.
- **Mục 5 + 4:**
  - Admin đặt idle 1 phút; Producer thấy 2 phiên; usage `activeSessions.used = 2`.
  - Sau 75 giây, phiên bỏ yên bị **401 ngay**, refresh của phiên đó cũng 401.
  - Phiên có request mỗi 20 giây vẫn 200.
  - Danh sách phiên bỏ phiên idle.
  - Thu hồi phiên Writer; Writer bị 401 ngay.

### 4.3. Giao diện (Chrome)

Được test trên bản FE trước khi rebase lên `tam` @ `4e1bc2d`. Phần Series Bible giữ nguyên code khi rebase.

- **Thẻ phiên:** đây là bản thẻ phiên riêng trước đây, nay đã bỏ để dùng bản của Minh. Test cho thấy BE trả đúng:
  - Số phiên đúng.
  - Thu hồi một phiên thì phiên đó mất ngay.
  - `lastActiveAt` tăng theo request thật.

  **Chưa test giao diện của bản `47e3290`** với BE của nhánh này.
- **Series Bible:**
  - Nhân vật chưa khoá (Nam) vẫn hiện.
  - "Thay thế bản khoá" Bà Lan: form điền sẵn và khoá chương; lưu thành công.
  - Lịch sử hiện v2 đã khoá (summary có lý do) và v1 vẫn khoá.
- Dòng "Chưa áp dụng" của quota ảnh hiện đúng.
- Console không có lỗi.

### 4.4. FE

Kết quả trên nhánh `tam-cf1-limitations` (2 commit trên `tam` @ `4e1bc2d`):
- Typecheck sạch.
- Lint sạch trên cả 4 file đã sửa.
- Vitest **397/397 pass** (48 file), gồm 2 test mới cho `supersedeRevision`.

## 5. Vấn đề phát hiện ngoài phạm vi (chưa sửa ở nhánh này)

1. **`test-feature` @ `f46c02d` không khởi động được API.**
   - Migration `20261011162000_AddMultipleAssignmentDependencies.cs` thiếu file `.Designer.cs`, và `PanelForgeDbContextModelSnapshot` chưa cập nhật.
   - EF 9 ném lỗi `PendingModelChangesWarning` ở `MigrateAsync`.
   - Unit test (InMemory) không bắt được.
   - Nhánh này kế thừa lỗi đó. Phần test runtime ở mục 4.2 được chạy trên `f46c02d~1` cộng 3 commit của nhánh này. Cần tạo lại migration bằng `dotnet ef migrations add` trước khi merge.
2. **FE lưu chữ giữ chỗ làm phụ đề:** `mapEntryToBibleEntity` gán phụ đề mặc định "Canon Spec Entity". Khi tạo hoặc thay thế phiên bản cho mục không có phụ đề, chữ này bị lưu vào DB. Lỗi có sẵn, không do nhánh này gây ra.

## 6. Việc cần làm khi merge

- [ ] Sửa migration của `f46c02d` (mục 5.1).
- [ ] Merge FE `tam-cf1-limitations` cùng lúc. FE cũ gọi `/unlock` sẽ lỗi 404.
- [ ] Thống nhất với Minh (tác giả `87f05a3`, `6590a7e`) về việc bỏ unlock và chuyển canon lock sang AI.
- [ ] Với DB dev có sẵn: Producer khoá lại các character sheet cần cho AI.
- [ ] Test giao diện giám sát phiên của Minh (`47e3290`) với BE nhánh này: số thiết bị phải bỏ phiên đã quá idle.
- [ ] Cập nhật bảng hạn chế trong `TONG_KET_DU_AN_PANELFORGE.md`.
