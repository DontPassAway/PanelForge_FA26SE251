# Chiến lược lưu trữ của PanelForge: ảnh, cốt truyện, canvas, luồng và theo từng role

> **Cập nhật:** 04/10/2026
> **Nguồn:** code backend đã đọc (Domain, Infrastructure, Application), `requirements.md`, `storage-architecture.md`, `role-flow-examples.md`, cộng với research ngoài (nguồn ở cuối).
> **Phân biệt:** mục 1 là **hiện trạng đọc được từ code**; các mục còn lại là **đề xuất thiết kế**. Mình chưa chạy thử bất kỳ thành phần lưu trữ nào.

---

## 0. Tóm tắt các quyết định

| Loại dữ liệu | Nơi lưu | Lý do ngắn |
|---|---|---|
| Tài khoản, workspace, series, chapter, scene, page, panel, pipeline, assignment | **PostgreSQL** (EF Core, quan hệ) | Cần ràng buộc khóa ngoại và truy vấn |
| Phần tử trên canvas (balloon, chữ, layer, vị trí) | **PostgreSQL qua Marten event store** (đang dùng) | Mỗi thao tác kéo thả là một sự kiện, tua lại được |
| Cốt truyện (script, thoại, người nói) | **PostgreSQL**: bảng `ScriptLine` (bản làm việc) + `ContentRevision` (bản chụp bất biến có hash) | Vừa truy vấn được từng dòng, vừa giữ lịch sử |
| Series Bible | **PostgreSQL**: `BibleEntry` + `BibleEntryRevision` | Đã có, bất biến, có hash |
| Ảnh gốc (artwork, ảnh tham chiếu, avatar, ảnh AI, ảnh tách nền) | **Object storage S3-tương thích** (bucket private) | Ảnh lớn không nên nằm trong DB |
| Metadata của ảnh (hash, kích thước, chủ sở hữu, nguồn gốc) | **PostgreSQL**: bảng `Assets` | Truy vấn, hạn mức, phân quyền |
| Ảnh hiển thị và thumbnail | Object storage (sinh lại được từ ảnh gốc) | Nhẹ hơn cho canvas và danh sách |
| Chú thích khoanh vùng, comment, change request | **PostgreSQL** (tọa độ lưu dạng JSON/vector, không lưu thành ảnh) | Nhẹ, gắn được vào đúng phiên bản panel |
| Kết quả và provenance của AI | **PostgreSQL** (đã có `AiWorkflowRun`, `AiSuggestion`, `AiUsageRecord`) | Đã có, giữ nguyên |
| File xuất (PDF, CBZ, JSON) | Object storage, thư mục riêng, có hạn lưu | Sinh lại được từ dữ liệu có cấu trúc |
| Nhật ký kiểm toán | **PostgreSQL** (`AuditLog`) | Đã có |
| Secret (khóa AI) | PostgreSQL, **mã hóa**; JWT/SMTP/ServiceKey ở user-secrets hoặc biến môi trường | Không đưa vào repo |

**Một câu:** dữ liệu có cấu trúc đi vào **PostgreSQL** (không cần thêm database nào khác), file nhị phân đi vào **object storage**, và hai bên nối nhau bằng `AssetId` kèm mã băm SHA-256.

---

## 1. Hiện trạng đọc được từ code

### Đã có
- **PostgreSQL 17** là kho duy nhất. `docker-compose.yml` chỉ có `postgres` và `panelforge-api`; **chưa có dịch vụ lưu trữ file nào**.
- **Hai kiểu lưu cùng tồn tại trong một DB:**
  - EF Core (quan hệ) cho Series, Chapter, Scene, Panel, ScriptLine, Bible, Assignment, AuditLog, AI…
  - **Marten event store** cho aggregate `Page`: `PageCreated`, `PageCanvasUpdated`, `PageReordered`, `ElementAdded`, `ElementMoved`, `ElementRemoved`, `PageDeleted`. Xóa là xóa mềm, event được giữ lại.
- **Lịch sử bất biến đã có ở hai nơi:**
  - `ContentRevision` (script, layout, image prompt): `VersionNumber` tăng dần, `PayloadJson`, `ContentHash`, `PreviousRevisionId`, `SourceSuggestionId`. Append-only.
  - `BibleEntryRevision`: `SnapshotJson`, `ContentHash`, `AuthorId`, có hiệu lực từ chương nào.
- **`AuditLog`:** người làm, thời điểm, hành động, thực thể, thay đổi, IP.
- **Firebase** hiện được dùng để xác minh danh tính (`FirebaseIdentityVerifier`), không dùng để lưu file.

### Chưa có
- Không có entity `Asset`, không có object storage, không có API tải ảnh lên.
- Ảnh hiện chỉ được tham chiếu bằng **chuỗi tự do**:

| Nơi | Trường | Vấn đề |
|---|---|---|
| `BibleEntry` | `ReferenceImageUrl` (string) | API `BibleController` nhận URL bất kỳ từ client, kể cả URL ngoài; không có kiểm soát quyền hay lưu bản sao |
| `User` | `AvatarUrl` (string) | Tương tự |
| `Assignment` | `ReferenceAssetUrlsJson` (JSON chuỗi URL) | Tương tự |
| `ElementState` / `ElementAddedEvent` | `AssetId` (string, không có khóa ngoại) | Trỏ tới thứ chưa tồn tại |

---

## 2. Nguyên tắc chọn nơi lưu

1. **Dữ liệu có thể truy vấn → PostgreSQL. File nhị phân lớn → object storage.** Không lưu ảnh dưới dạng BLOB hoặc base64 trong DB.
2. **Bất biến cho mọi thứ đã commit** (`requirements.md` 4.4, 6.1): ảnh tên theo hash nội dung; script và Bible có revision; canvas là event append-only.
3. **Workspace là ranh giới tenant**: mọi bản ghi file thuộc một Workspace; kiểm tra quyền phía server (`panelforge-security`).
4. **Không có URL công khai cố định**: bucket private, xem ảnh qua URL ký ngắn hạn sau khi kiểm quyền.
5. **Cái gì sinh lại được thì không cần bảo vệ như bản gốc**: thumbnail, ảnh hiển thị, file xuất. Cái gì không sinh lại được (ảnh gốc, lịch sử, Bible) thì phải sao lưu.
6. **Chưa thêm công nghệ mới khi PostgreSQL làm được** (tìm kiếm văn bản dùng full-text của PostgreSQL, không cần Elasticsearch ở giai đoạn này).

---

## 3. Ảnh: lưu như thế nào

### 3.1 Mô hình hai lớp

```
Object storage (S3-tương thích, bucket private)         PostgreSQL
┌──────────────────────────────────────────┐           ┌──────────────────────────────┐
│ assets/original/{ws}/{h[0..2]}/{h}.{ext} │ ◄──────── │ Assets                       │
│ assets/display/{ws}/{h}.webp             │  StorageKey│  Id, WorkspaceId, Sha256Hash │
│ assets/thumb/{ws}/{h}.webp               │           │  MimeType, SizeBytes, W, H   │
│ exports/{ws}/{series}/{job}.pdf          │           │  SourceAssetId, Origin       │
└──────────────────────────────────────────┘           │  UploadedByUserId            │
                                                       └──────────────────────────────┘
        Tham chiếu từ nơi khác chỉ bằng AssetId (Guid), không dùng URL
```

- **Ảnh gốc:** giữ nguyên định dạng (PNG hoặc TIFF), tên theo SHA-256, không bao giờ ghi đè. Cần cho in ấn và xuất bản.
- **Ảnh hiển thị:** WebP, tối đa khoảng 2048 px chiều dài, dùng cho canvas.
- **Thumbnail:** WebP 256 hoặc 512 px, dùng cho danh sách và bảng việc.
- **Tách khóa theo Workspace** (`{ws}`) và dedup chỉ trong Workspace, để giữ ranh giới tenant (xem `storage-architecture.md` mục 3.2).
- File nguồn của phần mềm vẽ (PSD, CLIP, ZIP) có thể lưu như một loại asset "nguồn", không sinh variant. Đây là tùy chọn, cần giới hạn dung lượng.

### 3.2 Chọn nơi chạy object storage

Điểm cần chỉnh so với `storage-architecture.md`: tài liệu đó đề xuất **MinIO** cho môi trường dev, nhưng theo các nguồn tìm được, bản community của MinIO **vào chế độ maintenance từ 12/2025 và repository bị archive từ 02/2026**: chỉ còn vá bảo mật, không phát hành bản mới, không có binary chính thức, phải tự build. Không nên chọn nó cho dự án mới.

| Lựa chọn | Dùng cho | Ưu | Nhược |
|---|---|---|---|
| **Ổ đĩa cục bộ** (cài đặt `IObjectStorageService` ghi vào thư mục) | Dev và test của từng thành viên | Không thêm container, không thêm phụ thuộc, chạy ngay | Không có URL ký như S3 (backend phải tự phát file qua endpoint có kiểm quyền); không kiểm được code S3 thật |
| **Garage** (AGPLv3) | Dev/staging tự host | Đang được duy trì; nguồn so sánh cho thấy hỗ trợ presigned URL tin cậy | Cần cấu hình layout lần đầu; chưa được mình chạy thử |
| **SeaweedFS** (Apache 2.0) | Dev/staging, quy mô lớn hơn | Giấy phép dễ chịu, tính năng nhiều | Có báo cáo presigned URL cần gỡ rối cấu hình |
| **Cloudflare R2** | Production (và demo trên cloud) | Tương thích S3 (dùng `AWSSDK.S3`), băng thông ra miễn phí | Là dịch vụ ngoài, cần tài khoản; nội dung chưa phát hành nằm ở nhà cung cấp ngoài |

**Gợi ý cho đồ án:** hai cài đặt cho cùng một port `IObjectStorageService` (cổng nằm ở `Application/Common/Interfaces`, đúng quy ước dự án):
- **Dev và test:** cài đặt ghi ổ đĩa cục bộ, để không ai phải chạy thêm container.
- **Demo/production:** cài đặt S3 trỏ tới **Cloudflare R2** hoặc Garage tự host.

Như vậy đổi nhà cung cấp chỉ là đổi cấu hình và một lớp cài đặt, không đụng domain.

### 3.3 Chi phí ước tính (R2)

Bảng giá R2 tham khảo: lưu trữ chuẩn khoảng 0,015 USD/GB-tháng, Class A khoảng 4,50 USD/triệu yêu cầu, Class B khoảng 0,36 USD/triệu, **băng thông ra miễn phí**, gói miễn phí gồm 10 GB và 1 triệu Class A/tháng.

Ước tính của mình (giả định rõ ràng, không phải số đo thật):

| Kịch bản | Giả định | Dung lượng | Chi phí lưu trữ |
|---|---|---|---|
| Bộ dữ liệu demo | 3 chương × 20 trang × 4 phiên bản (Thumbnail, Pencil, Ink, Color) × 20 MB | khoảng 5 GB | Nằm trong gói miễn phí 10 GB |
| Một series dài | 100 chương × 20 trang × 4 phiên bản × 20 MB | khoảng 156 GB | khoảng 2–3 USD/tháng |

Chi phí lưu trữ nhỏ; phần đáng cân nhắc hơn là **quyền riêng tư** (nội dung chưa phát hành nằm trên dịch vụ ngoài) và **hạn mức theo Workspace** mà Admin cấu hình (`requirements.md` 3.1).

### 3.4 Quy tắc tải ảnh

- Kiểm tra loại file bằng **nội dung (magic bytes)**, không tin đuôi file hoặc `Content-Type` do client gửi.
- Giới hạn dung lượng và số điểm ảnh tối đa để tránh ảnh nén bom làm tràn bộ nhớ.
- Server tự tính lại SHA-256 sau khi tải, không tin hash của client.
- Cân nhắc loại bỏ metadata EXIF nhạy cảm (vị trí, thiết bị) ở bản hiển thị.
- Asset tải dở không được gắn vào revision nào sẽ bị dọn theo chính sách lưu giữ; asset **đã được revision tham chiếu thì không bao giờ xóa**.

---

## 4. Cốt truyện: lưu như thế nào

Cốt truyện không nên là một khối văn bản. Yêu cầu cần biết "câu thoại nào thuộc panel nào, ai nói, nhân vật nào xuất hiện" (`requirements.md` 1.1), nên cần lưu **có cấu trúc**, ở ba lớp:

| Lớp | Bảng | Vai trò | Tính chất |
|---|---|---|---|
| Bản làm việc | `ScriptLine` (SceneId, LineOrder, SpeakerCharacterId, DialogueText, StageDirection) | Truy vấn từng dòng, hiển thị, gắn vào balloon | Sửa được |
| Lịch sử | `ContentRevision` (Kind = Script / Layout / ImagePrompt, PayloadJson, ContentHash, PreviousRevisionId) | Xem lại, so sánh, khôi phục | **Bất biến**, chỉ thêm |
| Ngữ cảnh | `BibleEntry` + `BibleEntryRevision` | Nhân vật, địa điểm, thuật ngữ, plot fact có hiệu lực theo chương | Bất biến theo phiên bản |

Mối quan hệ giữa các tầng nội dung:

```
Scene ──< ScriptLine ──(sourceScriptLineId)──> Panel ──< Element (trong Marten, có ScriptLineId, SpeakerId)
  │                                              │
  └── ContentRevision(Script)                    └── ContentRevision(Layout, ImagePrompt)
```

**Tìm kiếm:** dùng full-text của PostgreSQL (kiểu `tsvector`) trên `ScriptLine`, tên Bible và comment; chưa cần công cụ ngoài.

**Xuất/nhập:** lược đồ JSON mở (`requirements.md` 6.8) là định dạng trao đổi, sinh ra từ dữ liệu có cấu trúc khi xuất. Không dùng nó làm nơi lưu chính.

### Điểm cần chú ý

Hiện chỉ có **một nơi** tạo `ContentRevision` và sửa `ScriptLine`: lệnh duyệt đề xuất AI (`ReviewSuggestionCommand`). Mình không thấy controller nào cho Writer **sửa script bằng tay**. Khi thêm API đó, mỗi lần lưu phải **tạo `ContentRevision` mới trong cùng một transaction** với việc cập nhật `ScriptLine`, nếu không sẽ có chỗ sửa mà không có lịch sử, trái với yêu cầu 4.4 và mục "script history" của Writer.

---

## 5. Layout và canvas

| Dữ liệu | Nơi lưu | Ghi chú |
|---|---|---|
| Khung trang (kích thước, DPI, chiều đọc) | Marten (`PageCreated`, `PageCanvasUpdated`) | Đang có |
| Thứ tự trang | Marten (`PageReordered`) | Đang có |
| Phần tử (balloon, chữ, layer artwork, nhân vật) | Marten (`ElementAdded`, `ElementMoved`, `ElementRemoved`) | Đang có; `ElementMoved` lưu cả vị trí cũ để hoàn tác và so sánh |
| Khung panel (hộp bao, thứ tự đọc) | EF Core `Panel` (`BoundingBox` JSON, `ReadingOrder`) | Đang có |
| Layer ảnh của phần tử | Object storage, tham chiếu bằng `AssetId` | Cần chuyển `AssetId` từ chuỗi sang Guid có kiểm tra tồn tại |

Lưu ý vận hành với Marten (theo hướng dẫn của Marten): không thêm snapshot cho tới khi thực sự gặp vấn đề hiệu năng; khi stream của các trang cũ không còn hoạt động có thể **archive stream**, và có thể phân vùng bảng để bảng sự kiện đang hoạt động không phình ra.

---

## 6. Các luồng lưu trữ

### 6.1 Tải artwork lên

```
Artist chọn file
  → Frontend tính SHA-256 (gợi ý nhanh) → POST /api/assets/prepare-upload (tên, dung lượng, hash)
  → Backend kiểm quyền Workspace + hạn mức lưu trữ
  → Đã có asset cùng hash TRONG Workspace? Có: trả AssetId hiện có (không tải lại)
  → Chưa: Backend cấp URL ký PUT ngắn hạn (hoặc, ở dev, endpoint tải trực tiếp)
  → Artist tải file thẳng lên object storage
  → POST /api/assets/confirm-upload
  → Job nền: kiểm magic bytes, tính lại SHA-256, đọc kích thước, tạo ảnh hiển thị và thumbnail
  → Ghi bản ghi Assets → gắn AssetId vào panel/element, ghi vào pipeline stage hiện tại
  → Ghi AuditLog (ai, khi nào, asset nào, stage nào)
```

### 6.2 Phiên bản artwork và khôi phục

```
Mỗi lần Artist tải bản mới cho một panel ở một stage:
  → Asset mới (tên theo hash mới), không động vào asset cũ
  → Event append vào Marten (ElementAdded hoặc cập nhật layer trỏ AssetId mới)
Khôi phục: tua stream về mốc trước, hoặc trỏ lại AssetId cũ (file cũ vẫn còn)
```

Vì tên file là hash nội dung, không ai sửa được nội dung mà giữ nguyên hash, nên lịch sử không bị phá.

### 6.3 Tách nền (derived asset)

```
Asset gốc ──► Job nền gọi dịch vụ tách nền ──► Asset mới (Origin = BackgroundRemoved, SourceAssetId = gốc)
Artist xem và chấp nhận ──► mới được gắn vào panel
```

Asset gốc giữ nguyên; asset mới thuộc cùng Workspace và tính vào hạn mức. Ghi tên công cụ/model đã dùng.

### 6.4 Ảnh do AI tạo

```
Yêu cầu (prompt + tham chiếu Bible) ──► Job nền ──► provider ảnh do Admin cấu hình
  → AiSuggestion (provenance: model, tham số, input, output)
  → Asset (Origin = AiGenerated, AiSuggestionId = ...)
  → Người dùng Accept / Edit / Reject; chỉ khi Accept mới gắn vào panel
```

### 6.5 Lưu script

```
Writer lưu (hoặc Accept đề xuất AI)
  → Một transaction: cập nhật ScriptLine + tạo ContentRevision(Script, v+1, hash, PreviousRevisionId, SourceSuggestionId)
  → AuditLog
  → Nếu phiên bản nguồn đã cũ: trả 409, không ghi đè
```

### 6.6 Review và change request

```
Editor khoanh vùng trên đúng phiên bản panel
  → Lưu comment: (panelId, assetId/phiên bản, vùng = JSON hình học chuẩn hóa theo kích thước ảnh, nội dung)
  → Vùng và nét vẽ lưu dạng vector JSON, KHÔNG lưu thành ảnh mới
  → Tạo change request → Assignment: ReworkRequired + LastFeedbackComment
```

Lưu vùng theo tọa độ chuẩn hóa (0–1) giúp comment vẫn đúng dù ảnh hiển thị đổi kích thước.

### 6.7 Xem ảnh trong workspace

```
Frontend: GET /api/assets/{id}/view-url
  → Backend kiểm tra: thành viên Workspace, quyền với series/chapter/panel, chương không bị chặn
  → Trả URL ký 15–30 phút (hoặc phát qua endpoint có kiểm quyền ở dev)
```

### 6.8 Xem thử cho người ngoài

```
Producer tạo ExternalPreviewLink (hạn, thu hồi được)
Người ngoài mở /preview/{token}
  → Backend kiểm hạn và thu hồi; ghi nhật ký truy cập
  → Ảnh được đóng dấu chìm động (email + thời gian) khi phát; chỉ cache ngắn hạn, không lưu lâu bản đã đóng dấu
```

### 6.9 Xuất file

```
Producer kích hoạt → Job nền: kiểm tra → dựng ảnh → đóng gói (PDF/CBZ/Webtoon/JSON)
  → Lưu vào exports/{ws}/{series}/{job} với hạn lưu → thông báo Producer
Xuất lỗi: báo lỗi, không làm hỏng dữ liệu có cấu trúc
```

### 6.10 Xóa, giữ lại và sao lưu

| Thành phần | Chính sách đề xuất |
|---|---|
| Ảnh gốc đã được revision tham chiếu | **Không xóa**; chỉ khôi phục về trạng thái cũ |
| Upload bỏ dở, chưa gắn vào đâu | Dọn sau thời hạn do Admin đặt |
| Ảnh hiển thị, thumbnail | Sinh lại được; có thể xóa và tạo lại |
| File xuất | Xóa sau thời hạn |
| PostgreSQL (Marten, revision, Bible, AuditLog) | Sao lưu thường xuyên; **đây là phần không thể thiếu** |
| Object storage (ảnh gốc) | Sao lưu hoặc bật phiên bản/nhân bản của nhà cung cấp |
| Xóa mềm thực thể | Đã dùng (`ISoftDelete`); không xóa cứng dữ liệu có lịch sử |

---

## 7. Bảng theo role: mỗi role tạo gì và dữ liệu nằm ở đâu

| Role | Việc tạo ra dữ liệu | Lưu ở đâu | Ảnh/file | Đã có trong code? |
|---|---|---|---|---|
| **Admin** (`SystemRole`) | Tài khoản, quyền tạo Studio | PostgreSQL (`User`, ...) | Không | ✅ |
| | Cấu hình AI (provider, model, khóa, quota) | PostgreSQL (`WorkspaceAiConfig`), khóa **mã hóa** | Không | ✅ |
| | Hạn mức lưu trữ, chính sách lưu giữ, lịch sao lưu | PostgreSQL (cấu hình) | Không | 📋 chưa thấy |
| | Template pipeline, element type, export preset | PostgreSQL (master data) | Không | ✅ |
| | Xem audit và usage | Đọc `AuditLog`, `AiUsageRecord` | Không | ✅ |
| **Producer** | Series, chapter, pipeline, thành viên | PostgreSQL (EF) | Ảnh bìa series: asset | ✅ (ảnh bìa 📋) |
| | Assignment, hạn chót, dependency | PostgreSQL (`Assignment`) | Tài liệu tham chiếu: asset, không phải URL | 🟡 |
| | Series Bible (nhân vật, địa điểm, thuật ngữ, plot fact) | PostgreSQL (`BibleEntry` + `BibleEntryRevision`) | **Ảnh model sheet: asset** (hiện là URL chuỗi) | ✅ (ảnh 🟡) |
| | Link xem thử cho người ngoài | PostgreSQL (`ExternalPreviewLink`) | Ảnh phát qua URL ký + watermark | 🟡 |
| | Phê duyệt, khóa chương | PostgreSQL (trạng thái chương, `AuditLog`) | Không | 🟡 |
| | Xuất bản | Job nền, kết quả vào `exports/` | **PDF/CBZ/JSON trong object storage** | 📋 |
| **Writer** | Script, thoại, người nói | PostgreSQL (`ScriptLine` + `ContentRevision`) | Không | ✅ (qua AI) / 📋 (sửa tay) |
| | Liên kết nội dung với Bible | PostgreSQL (khóa tham chiếu) | Không | 🟡 |
| | Quyết định với đề xuất AI | PostgreSQL (`AiSuggestionDecision`) | Không | ✅ |
| **Artist** | Layout trang, khung panel | PostgreSQL (`Panel.BoundingBox`) + Marten (canvas) | Không | ✅ |
| | Artwork Thumbnail/Pencil/Ink/Color | PostgreSQL (`Assets` + tham chiếu trong layer) | **Ảnh gốc, display, thumb trong object storage** | 📋 |
| | Ảnh tách nền | `Assets` (Origin = BackgroundRemoved) | Object storage | 📋 |
| | Prompt ảnh cho panel | PostgreSQL (`ContentRevision` kind ImagePrompt) | Không | ✅ |
| | Ảnh AI tạo | `Assets` (Origin = AiGenerated) + `AiSuggestion` | Object storage | 📋 |
| | Nộp panel review | PostgreSQL (`Assignment.Submitted`) | Không | 🟡 |
| **Letterer** | Balloon, khung dẫn, hiệu ứng chữ | **Marten** (`ElementAdded`, `ElementMoved`), có `ScriptLineId`, `SpeakerId` | Không (chữ là dữ liệu, không phải ảnh) | ✅ nền / 🟡 liên kết |
| | Typography preset áp dụng | PostgreSQL (`TypographyPreset`) | Font (nếu tải lên): asset | ✅ |
| | Đề xuất rút gọn thoại (AI) | PostgreSQL (`ContentRevision` Script mới) | Không | ✅ |
| **Editor** | Comment, vùng khoanh, vẽ chú thích | PostgreSQL (hình học JSON chuẩn hóa) | Không lưu thành ảnh | 📋 chưa xác minh |
| | Change request | PostgreSQL (`Assignment` rework + comment) | Không | 🟡 |
| | Báo cáo Continuity/Editor (AI) | PostgreSQL (output của `AiAgentStep`) | Không | ✅ |
| | Duyệt hoặc trả về | PostgreSQL (trạng thái, `AuditLog`) | Không | 🟡 |
| **Reader / người ngoài** | Phản hồi theo chương/trang | PostgreSQL (feedback gắn với link) | Không | 📋 |
| | Nhật ký truy cập | PostgreSQL (audit của link) | Không | 📋 |
| **User** (mọi tài khoản) | Hồ sơ, 2FA, thiết bị nhớ, refresh token | PostgreSQL (`User`, `RefreshToken`, `UserRememberedDevice`) | **Avatar: asset** (hiện là URL chuỗi) | ✅ (avatar 🟡) |
| | Thông báo, tìm kiếm | PostgreSQL (full-text) | Không | 📋 |
| **Hệ thống / AI (không phải role)** | Run, step, suggestion, usage, provenance | PostgreSQL (`AiWorkflowRun`, `AiAgentStep`, `AiSuggestion`, `AiUsageRecord`) | Ảnh AI: object storage | ✅ (ảnh 📋) |

---

## 8. Việc cần làm / sửa so với hiện trạng

Sắp theo thứ tự nên làm:

1. **Thêm nền Asset**: entity `Asset` (đúng quy ước domain, xem `storage-architecture.md` mục 4), cổng `IObjectStorageService` + `IImageProcessingService` trong `Application/Common/Interfaces`, migration `AddAssetsTable`. Chưa cần chọn nhà cung cấp cuối cùng nếu có cài đặt ổ đĩa cục bộ.
2. **Thay các chuỗi URL ảnh bằng `AssetId`**:
   - `BibleEntry.ReferenceImageUrl`, `User.AvatarUrl`, `Assignment.ReferenceAssetUrlsJson`, `ElementState.AssetId` (chuỗi → Guid có kiểm tra tồn tại).
   - Hiện `BibleController` nhận URL bất kỳ từ client; chuyển sang `AssetId` loại bỏ được việc nhúng ảnh từ nguồn ngoài và làm quyền truy cập nhất quán. Đây là thay đổi hợp đồng API, cần ghi vào `HANDOFF.md` mục breaking.
3. **Khi làm API sửa script bằng tay:** một transaction cập nhật `ScriptLine` và tạo `ContentRevision` (mục 4).
4. **Marten `AutoCreateSchemaObjects = AutoCreate.All`** (`DependencyInjection.cs` dòng 71) đang không có điều kiện môi trường, dù comment ghi "Development". Với production nên dùng cách tạo schema có kiểm soát (migration/`CreateOnly`/`None`) để tránh tự đổi schema khi chạy.
5. **Đổi khuyến nghị MinIO** trong `storage-architecture.md` (xem mục 3.2 ở trên).
6. **Thêm cấu hình hạn mức và chính sách lưu giữ** cho Admin (`requirements.md` 3.1).
7. Về sau: archive stream Marten cho trang cũ; phân vùng bảng nếu dữ liệu lớn.

---

## 9. Rủi ro và điểm chưa kiểm chứng

- **Mình chưa chạy thử** Garage, SeaweedFS hay R2 với `AWSSDK.S3`. Nhận định về presigned URL của từng loại lấy từ bài so sánh trên web, cần thử với dữ liệu thật trước khi chốt.
- **Chi phí ở mục 3.3 là ước tính** với kích thước file giả định 20 MB; ảnh thật có thể nhỏ hơn hoặc lớn hơn nhiều.
- **R2 là dịch vụ ngoài:** nội dung chưa phát hành nằm ngoài hạ tầng của nhóm. Nếu yêu cầu quyền riêng tư nghiêm hơn, dùng Garage tự host.
- **Dedup theo Workspace** làm tốn thêm dung lượng nếu hai Workspace có cùng file; đây là đánh đổi có chủ đích để giữ ranh giới tenant.
- **Chưa xác minh** phần comment/annotation, thông báo, tìm kiếm, API link xem thử ở tầng Application; bảng mục 7 ghi 📋 hoặc 🟡 ở những chỗ đó.
- Dữ liệu event Marten tăng dần theo mỗi lần kéo thả; nếu mỗi lần di chuyển chuột tạo một sự kiện thì cần gộp (debounce) phía client hoặc ghi sự kiện khi thả chuột.

---

## Nguồn tham khảo

- MinIO community edition bị archive và chuyển maintenance: [bài tổng hợp 2026](https://stormdevelopments.ca/blog/minio-s-community-edition-is-archived-what-still-runs-in-2026/), [MinIO in Maintenance Mode: Open Source Alternatives](https://bizety.com/2025/12/06/minio-in-maintenance-mode-open-source-alternatives/), [Best MinIO Alternatives in 2026](https://dev.to/ethan-carter/best-minio-alternatives-in-2026-6-options-that-actually-work-17p2)
- So sánh Garage, SeaweedFS, RustFS và presigned URL: [RustFS vs SeaweedFS vs Garage](https://blog.elest.io/rustfs-vs-seaweedfs-vs-garage-which-minio-alternative-should-you-pick/), [Local S3 Storage with SeaweedFS & Garage in Docker Compose](https://www.sitepoint.com/local-s3-storage-without-minio-seaweedfs-garage-docker-compose/), [Garage S3 compatibility](https://garagehq.deuxfleurs.fr/documentation/reference-manual/s3-compatibility/), [SeaweedFS presigned upload discussion](https://github.com/seaweedfs/seaweedfs/discussions/4401)
- Giá Cloudflare R2: [Cloudflare R2 pricing (tài liệu chính thức)](https://developers.cloudflare.com/r2/pricing), [Cloudflare R2 Pricing 2026](https://egresscost.com/cloudflare/)
- Marten, event store, snapshot và archive stream: [Marten – giới thiệu](https://martendb.io/introduction.html), [Understanding Event Sourcing with Marten](https://martendb.io/events/learning), [Making Event Sourcing with Marten Go Faster](https://jeremydmiller.com/2025/06/02/making-event-sourcing-with-marten-go-faster/)
