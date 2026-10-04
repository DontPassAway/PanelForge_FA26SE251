# Thiết Kế Kiến Trúc Lưu Trữ Ảnh & Tài Sản Đồ Họa (Asset Storage Architecture)

> **Dự án:** PanelForge — Manga Production Management Platform  
> **Tài liệu kỹ thuật:** Chiến lược & kiến trúc lưu trữ ảnh nhị phân cho hệ thống  
> **Tác giả:** Đội ngũ kiến trúc hệ thống

---

## 1. Bối Cảnh & Thách Thức Đặc Thù Của PanelForge

Trong một nền tảng sản xuất manga/webtoon cộng tác như PanelForge, **ảnh không phải là tài liệu đính kèm thông thường** mà là trung tâm của sản phẩm:
1. **Dung lượng lớn và độ phân giải cao:** Bản vẽ manga phục vụ in ấn thường có độ phân giải từ 300 đến 600 DPI, kích thước file thô từ 10MB đến 50MB+ cho mỗi trang.
2. **Nhiều layer & nhiều phiên bản (Revisions):** Một trang (Page) gồm nhiều khung (Panel), mỗi khung có nhiều lớp (ArtworkLayer: phác thảo, nét ink, màu, đổ bóng). Qua từng công đoạn (Thumbnail → Pencil → Ink → Color), số lượng file ảnh sinh ra tăng theo cấp số nhân.
3. **Tính bất biến trong Event Sourcing & Provenance:** Toàn bộ lịch sử chỉnh sửa được lưu dạng Event Store. Hệ thống cam kết có thể quay ngược thời gian (rollback) để xem lại trang truyện ở bất kỳ thời điểm nào. **Nếu một file ảnh bị sửa đổi hoặc ghi đè, tính toàn vẹn của lịch sử phiên bản sẽ bị phá hủy hoàn toàn.**
4. **Bảo mật bản quyền (Chống rò rỉ tác phẩm chưa phát hành):** Bản vẽ của các chương truyện chưa xuất bản là tài sản thương mại tối mật. Cần ngăn chặn việc lộ đường dẫn tĩnh (public URL) và cần có cơ chế đóng dấu bản quyền (Watermarking) cho các liên kết xem thử (Preview links).

---

## 2. So Sánh Các Giải Pháp Lưu Trữ

| Tiêu chí | Lưu vào CSDL (BLOB / Base64) | Lưu Ổ Cứng Server (Local File System) | Lưu Object Storage Thường (Tên file ngẫu nhiên) | **Content-Addressable Storage (CAS) trên Object Storage (Đề xuất)** |
|---|:---:|:---:|:---:|:---:|
| **Khả năng mở rộng (Scalability)** | ❌ Rất kém (DB phình to, backup chậm) | ❌ Kém (chết khi scale đa server / Docker) | ✅ Tốt (S3 scale không giới hạn) | ✅ **Rất tốt** |
| **Chống trùng lặp (Deduplication)** | ❌ Không | ❌ Không | ❌ Trùng lặp lớn giữa các version | ✅ **Tuyệt đối (100% không lưu trùng)** |
| **Tính bất biến & Toàn vẹn (Immutability)** | 🟡 Tùy cấu hình DB | ❌ Dễ bị ghi đè / mất file | 🟡 Có thể bị ghi đè nếu trùng key | ✅ **Bất biến tuyệt đối theo Hash SHA-256** |
| **Bảo mật & Chống Leak** | 🟡 Thông qua API | 🟡 Phải viết file stream qua API | ✅ Hỗ trợ Pre-signed URL | ✅ **Hỗ trợ Pre-signed URL + Watermark** |
| **Tải & Render trên Canvas** | ❌ Chậm chạp, nghẽn RAM | 🟡 Tốn băng thông máy chủ | 🟡 Phải tự quản lý thumbnail | ✅ **Multi-resolution (Gốc + WebP Display + Thumb)** |

---

## 3. Kiến Trúc Đề Xuất: Content-Addressable Storage (CAS) + S3-Compatible

Mô hình này lấy cảm hứng từ cơ chế lưu trữ của **Git LFS** và **Hệ thống phân tán Content-Addressed**: **Tên và định danh của file chính là mã băm SHA-256 nội dung của file đó.**

```
                                      ┌────────────────────────────────────────┐
                                      │        Client (FE Canvas Editor)       │
                                      └───────┬────────────────────────▲───────┘
                                              │ 1. Hash SHA-256        │ 4. Direct Upload
                                              │    & Check existence   │    via Pre-signed PUT URL
                                              ▼                        │
                         ┌────────────────────────────────────────┐    │
                         │          PanelForge Backend            │    │
                         │  - Check Hash in DB (Deduplication)    │    │
                         │  - Generate Pre-signed URLs            │    │
                         │  - RBAC / Watermarking Service         │    │
                         └───────┬────────────────────────┬───────┘    │
                                 │                        │            │
            2. Metadata Query    │                        │ 3. Sign    │
            & Insert Entity      ▼                        ▼            ▼
                         ┌──────────────┐         ┌────────────────────────────┐
                         │  PostgreSQL  │         │   Object Storage (S3/R2)   │
                         │  (Assets DB) │         │                            │
                         └──────────────┘         │  assets/                   │
                                                  │   └── 3a/                  │
                                                  │       └── f8/              │
                                                  │           └── 3af8...png   │
                                                  │  variants/                 │
                                                  │   └── 3af8..._thumb.webp   │
                                                  │   └── 3af8..._display.webp │
                                                  └────────────────────────────┘
```

### 3.1. Nguyên Lý Content-Addressable Storage (CAS)

1. Khi người dùng chọn tải lên một ảnh (ví dụ: `sketch_panel_1.png`):
   - Client hoặc Server tính mã băm `SHA-256` của nội dung nhị phân:  
     `hash = "3af8d9c2e14b...89a0"`
2. Đường dẫn lưu trữ trên Object Storage (S3 Storage Key) được chuẩn hóa theo cấu trúc sharding để tối ưu cây thư mục:  
   `assets/original/{hash[0..2]}/{hash[2..4]}/{hash}.{ext}`  
   *Ví dụ:* `assets/original/3a/f8/3af8d9c2e14b...89a0.png`
3. **Lợi ích cốt lõi:**
   - **Deduplication trong phạm vi một Workspace:** Nếu 2 artist **cùng Workspace** tải lên cùng một bản vẽ, hoặc khi nhân bản một Panel có chung hình nền, hệ thống kiểm tra thấy Hash đã tồn tại trong Workspace đó -> **không cần upload lại**, chỉ cần trỏ `AssetId` về bản ghi cũ.
   - **Bảo toàn toàn vẹn lịch sử (Provenance & Event Sourcing):** Mỗi phiên bản trong quá khứ trỏ tới một Hash cụ thể. Do tên file gắn liền với nội dung, không một ai có thể sửa nội dung mà giữ nguyên được Hash.
   - **Xác minh tính toàn vẹn (Integrity Verification):** Bất cứ khi nào tải file, hệ thống có thể băm lại để đảm bảo file trên cloud không bị hỏng hóc hay can thiệp.

### 3.2. Phạm Vi Deduplication: Theo Workspace, Không Toàn Cục

Workspace là ranh giới tenant của hệ thống. Vì vậy deduplication và kiểm tra hash chỉ thực hiện **trong cùng một Workspace**:

- Chỉ mục unique là `(WorkspaceId, Sha256Hash)`, không phải `Sha256Hash` toàn cục.
- Nếu dedup toàn cục, bước `prepare-upload` trả `alreadyExists: true` sẽ tiết lộ cho Workspace A rằng một Workspace khác đã có đúng file đó. Kèm với việc hash do client tự khai, người dùng có thể khai một hash bất kỳ để nhận `AssetId` của người khác mà không chứng minh được mình có file.
- Hạn mức lưu trữ (Storage Quota) tính theo Workspace nên mỗi Workspace phải có bản ghi `Asset` của riêng mình.
- Chi phí đánh đổi: cùng một file ở hai Workspace khác nhau sẽ được lưu hai lần. Đây là lựa chọn có chủ đích để giữ ranh giới tenant.

Với asset nhỏ hơn ngưỡng cấu hình, hoặc khi cần chắc chắn, server nên tự tính lại SHA-256 sau khi upload (đã có trong bước `confirm-upload`) thay vì tin hash của client.

---

## 4. Thiết Kế Cơ Sở Dữ Liệu (Asset Entity)

Trong PostgreSQL (quản lý bởi EF Core), chúng ta cần bổ sung bảng `Assets` để lưu trữ siêu dữ liệu (metadata) của các file.

Entity theo đúng quy ước của domain hiện tại: kế thừa `BaseEntity` (đã có sẵn `Id` với `protected set`, `CreatedAt`, trường audit và soft-delete, nên **không khai báo lại**), setter `private`, tạo qua factory `Create(...)`:

```csharp
namespace PanelForge.Domain.Entities.Storage;

public class Asset : BaseEntity
{
    /// <summary>Workspace sở hữu asset (ranh giới tenant, dùng cho Storage Quota).</summary>
    public Guid WorkspaceId { get; private set; }

    /// <summary>
    /// SHA-256 nội dung nhị phân (Content-Addressable Storage & Deduplication).
    /// Unique theo cặp (WorkspaceId, Sha256Hash), KHÔNG unique toàn cục (xem 3.2).
    /// </summary>
    public string Sha256Hash { get; private set; } = default!;

    public string OriginalFileName { get; private set; } = default!;

    /// <summary>MIME: image/png, image/jpeg, image/webp...</summary>
    public string MimeType { get; private set; } = default!;

    public long SizeBytes { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Khóa file gốc trên Object Storage (bất biến).</summary>
    public string StorageKey { get; private set; } = default!;

    /// <summary>Khóa ảnh hiển thị WebP tối ưu cho Canvas Editor.</summary>
    public string? DisplayStorageKey { get; private set; }

    /// <summary>Khóa ảnh Thumbnail WebP (256 hoặc 512 px).</summary>
    public string? ThumbnailStorageKey { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    /// <summary>
    /// Asset dẫn xuất (tách nền, ảnh AI tạo): trỏ về asset nguồn. Null nếu là file người dùng tải lên trực tiếp.
    /// Asset nguồn luôn được giữ nguyên.
    /// </summary>
    public Guid? SourceAssetId { get; private set; }

    /// <summary>Cách asset này ra đời; mặc định là người dùng tải lên.</summary>
    public AssetOrigin Origin { get; private set; } = AssetOrigin.Upload;

    /// <summary>Tên tool/model đã tạo asset dẫn xuất (ví dụ rembg + tên model). Null với Upload.</summary>
    public string? DerivationTool { get; private set; }

    /// <summary>Liên kết tới bản ghi provenance AI (AiSuggestion/AiUsageRecord) khi Origin = AiGenerated.</summary>
    public Guid? AiSuggestionId { get; private set; }

    private Asset() { }
}

public enum AssetOrigin
{
    Upload = 1,
    BackgroundRemoved = 2,
    AiGenerated = 3
}
```

> Đây là thiết kế đề xuất, chưa có trong code. Các trường dẫn xuất (`SourceAssetId`, `Origin`, `DerivationTool`, `AiSuggestionId`) phục vụ yêu cầu 5.8 và 5.9 trong `requirements.md`.
> Không dùng `ReferenceCount` làm cơ sở xóa file: bản đã commit trong lịch sử phiên bản không được xóa (`requirements.md` 4.4, 6.1). Dọn dẹp chỉ áp dụng cho asset chưa từng được gắn vào bất kỳ revision nào (ví dụ upload bị bỏ dở), theo Retention Policy do Admin cấu hình.

---

## 5. Quy Trình Xử Lý Tải Lên (Upload Flow) & Phân Phối Ảnh

### 5.1. Quy Trình Direct Upload Tối Ưu Băng Thông (Pre-signed URL)

Thay vì để client tải file 50MB qua Backend API rồi Backend mới đẩy lên S3 (gây nghẽn RAM và băng thông máy chủ .NET), hệ thống sử dụng quy trình **Pre-signed URL**:

```mermaid
sequenceDiagram
    autonumber
    actor User as Artist (Frontend)
    participant API as PanelForge API
    participant DB as PostgreSQL
    participant S3 as Object Storage (MinIO/R2)

    User->>API: POST /api/assets/prepare-upload (fileName, size, sha256Hash)
    API->>DB: Kiểm tra (WorkspaceId, Sha256Hash) đã tồn tại chưa?
    alt Đã tồn tại (Deduplication)
        DB-->>API: Trả về AssetId hiện có
        API-->>User: HTTP 200 { alreadyExists: true, assetId: "..." } (Instant Upload!)
    else Chưa tồn tại
        API->>S3: Sinh Pre-signed PUT URL (Hạn 15 phút)
        S3-->>API: preSignedUrl
        API-->>User: HTTP 200 { alreadyExists: false, uploadUrl: preSignedUrl, storageKey: "..." }
        User->>S3: PUT [Binary file] trực tiếp lên uploadUrl
        User->>API: POST /api/assets/confirm-upload (storageKey, sha256Hash, ...)
        API->>API: Đưa job vào Background Queue (Verify hash, extract size, tạo WebP thumbnail)
        API->>DB: Lưu Asset Record mới
        API-->>User: HTTP 201 { assetId: "..." }
    end
```

### 5.2. Đường Ống Xử Lý Ảnh Đa Kích Thước (Image Processing Pipeline)

Khi file gốc được tải lên thành công:
1. **Master Asset (Original):** Giữ nguyên vẹn 100% định dạng gốc (PNG/TIFF không nén mất dữ liệu) để phục vụ xuất bản in ấn (Print Export) sau này.
2. **Display Variant (Tối ưu cho Editor):** Chuyển đổi sang định dạng `.webp` với độ phân giải giới hạn (ví dụ tối đa 2048px chiều dài, quality 85%). Dùng variant này để load lên **Canvas Editor**, giúp thao tác kéo thả mượt mà, không giật lag.
3. **Thumbnail Variant (Tối ưu cho Dashboard):** Chuyển đổi sang `.webp` kích thước 256px hoặc 512px để hiển thị danh sách trang, task board, preview nhỏ.
4. **Công cụ xử lý khuyến nghị:** Sử dụng thư viện **`SixLabors.ImageSharp`** hoặc **`SkiaSharp`** chạy trực tiếp trong C# .NET Core mà không cần cài đặt ImageMagick ngoài hệ thống.

### 5.3. Asset Dẫn Xuất: Tách Nền và Ảnh AI Tạo

Hai thao tác sau không ghi đè asset hiện có mà **luôn sinh asset mới** có `SourceAssetId` (hoặc `AiSuggestionId`) trỏ về nguồn:

```text
Asset gốc (Origin = Upload)
        │  Artist bấm "Tách nền"
        ▼
Job nền (bất đồng bộ) ──► service tách nền (ví dụ rembg tự host)
        ▼
Asset mới (Origin = BackgroundRemoved, SourceAssetId = asset gốc, DerivationTool = tên tool/model)
        ▼
Artist xem và chấp nhận ──► dùng làm ARTWORK_LAYER hoặc element nhân vật
```

```text
Yêu cầu tạo ảnh (prompt + reference từ Series Bible)
        ▼
Job nền ──► provider ảnh do Admin cấu hình (ví dụ ComfyUI tự host)
        ▼
AiSuggestion (kèm provenance: model, tham số, input, output)
        ▼
Asset mới (Origin = AiGenerated, AiSuggestionId = ...)
        ▼
Người dùng Accept / Edit / Reject ──► chỉ khi Accept mới gắn vào Panel
```

Nguyên tắc:

- Kết quả đi qua cùng pipeline của mục 5.1 và 5.2 (hash, lưu object, tạo variant), và thuộc cùng Workspace với asset nguồn.
- Chưa được Accept thì asset AI/tách nền không xuất hiện trong artwork của Panel, không vào export.
- Asset dẫn xuất tính vào Storage Quota của Workspace.
- Cả hai thao tác ghi provenance theo `requirements.md` mục 6.2 và 8.

---

## 6. Bảo Mật & Phân Phối Ảnh (Security & Delivery)

1. **Storage Bucket ở chế độ Private 100%:** Tuyệt đối không bật Public Read cho Bucket chứa truyện. Mọi truy cập trực tiếp từ bên ngoài đều bị từ chối (`403 Forbidden`).
2. **Xem ảnh trong Workspace:** 
   - Khi Artist/Editor xem một trang trên hệ thống, Frontend gọi API `GET /api/assets/{assetId}/view-url`.
   - Backend kiểm tra xem User có quyền truy cập vào Workspace/Series/Chapter đó không (thông qua `WorkspaceAuthorizationService`).
   - Nếu hợp lệ, Backend sinh **Pre-signed GET URL** có hạn ngắn (15–30 phút). Trình duyệt dùng URL này để tải ảnh hiển thị.
3. **External Reviewer Preview (Xem trước bên ngoài):**
   - Với các đối tác hoặc reviewer không có tài khoản nội bộ: Sử dụng liên kết chia sẻ tạm thời (`ExternalPreviewLink`).
   - Hình ảnh khi xem qua link này sẽ được Backend đóng dấu chìm (Dynamic Watermarking): in mờ email người xem + timestamp lên từng panel để chống chụp màn hình tuồn ra ngoài.
   - Ghi nhật ký truy cập (Audit Log) mỗi lần link được mở.

---

## 7. Hạ Tầng Công Nghệ Lưu Trữ Cụ Thể

Hệ thống sử dụng chuẩn giao tiếp **S3-Compatible API**, cho phép chuyển đổi linh hoạt giữa các môi trường mà không cần sửa code:

### 7.1. Môi trường Local Development & Test

> **Lưu ý (cập nhật 04/10/2026):** bản trước của tài liệu này đề xuất MinIO. Theo các nguồn tìm được, bản community của MinIO đã vào chế độ maintenance từ 12/2025 và repository bị archive từ 02/2026 (chỉ vá bảo mật, không phát hành bản mới, không có binary chính thức). Không nên chọn cho dự án mới. Xem `storage-strategy.md` mục 3.2.

Khuyến nghị: hai cài đặt cho cùng cổng `IObjectStorageService`:
- **Dev và test:** cài đặt ghi vào **thư mục cục bộ**, không cần thêm container.
- **Staging/demo:** **Garage** (tự host, đang được duy trì, hỗ trợ presigned URL) hoặc SeaweedFS chạy bằng Docker Compose, hoặc dùng thẳng Cloudflare R2.
- Nhận định về Garage/SeaweedFS lấy từ bài so sánh trên web, **chưa được thử với `AWSSDK.S3` trong dự án này**.

### 7.2. Môi trường Production / Cloud: Cloudflare R2 (Khuyến nghị số 1)
- **Lý do chọn Cloudflare R2:**
  - **Miễn phí 100% chi phí Egress Bandwidth:** Đối với nền tảng truyện tranh, dung lượng tải ảnh về cho hàng nghìn lượt xem/vẽ sẽ tốn hàng trăm GB băng thông mỗi tháng. Trên AWS S3, chi phí bandwidth này rất đắt; trên Cloudflare R2, **băng thông tải ra là 0 đồng**.
  - Tương thích 100% chuẩn S3 API (dùng chung thư viện `AWSSDK.S3` của .NET).
  - Tốc độ CDN toàn cầu cực nhanh.

---

## 8. Lộ Trình Triển Khai Cho Team

### Bước 1: Hạ tầng (BE)
- Dev/test: cài đặt `IObjectStorageService` ghi vào thư mục cục bộ (không cần container). Staging/demo: thêm Garage hoặc SeaweedFS vào `docker-compose.yml`, hoặc dùng Cloudflare R2 (xem 7.1).
- Cài đặt package `AWSSDK.S3` và `SixLabors.ImageSharp` vào `PanelForge.Infrastructure`.

### Bước 2: Domain & Persistence (BE1)
- Tạo Entity `Asset` trong `PanelForge.Domain.Entities.Storage`.
- Cấu hình EF Core DbContext và tạo Migration `AddAssetsTable`.

### Bước 3: Service Layer (BE2 / BE3)
- Tạo interface `IObjectStorageService` (UploadAsync, GeneratePreSignedPutUrl, GeneratePreSignedGetUrl, DeleteAsync).
- Tạo interface `IImageProcessingService` (GenerateVariantsAsync, ExtractMetadataAsync).
- Triển khai implementation tương ứng trong Infrastructure.

### Bước 4: API Layer
- Xây dựng `AssetsController`:
  - `POST /api/assets/prepare-upload`: Kiểm tra hash & cấp Pre-signed PUT URL.
  - `POST /api/assets/confirm-upload`: Xác nhận upload thành công & trigger xử lý ảnh.
  - `GET /api/assets/{id}/view-url`: Cấp Pre-signed GET URL có kiểm tra quyền.

### Bước 5: Frontend Integration (FE1)
- Xây dựng tiện ích tính SHA-256 hash trên trình duyệt (dùng Web Crypto API `crypto.subtle.digest`).
- Tích hợp direct upload vào Canvas Editor và Avatar/Series Cover Upload.
