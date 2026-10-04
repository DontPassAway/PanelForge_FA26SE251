# Chiến Lược Lưu Trữ Ảnh Hợp Lý Cho Dự Án PanelForge

> **Dự án:** PanelForge: An AI-Assisted Manga Production Management Platform  
> **Vấn đề giải quyết:** Cách lưu trữ và quản lý tài sản hình ảnh (bản vẽ, layer, thumbnail, annotation) tối ưu, an toàn và đồng bộ với kiến trúc Event Sourcing.

---

## 1. Bản Chất Của Vấn Đề Lưu Trữ Ảnh Trong PanelForge

Trong PanelForge, ảnh **không phải là avatar hay tệp đính kèm đơn giản**, mà là lõi của quy trình sáng tác với các đặc tính:
- **Dung lượng lớn:** Bản vẽ in ấn từ 300–600 DPI, kích thước file từ 10MB đến 50MB/trang.
- **Nhiều phiên bản (Revisions):** Mỗi panel trải qua nhiều công đoạn (Sketch → Pencil → Ink → Color), mỗi công đoạn lại có các lần chỉnh sửa theo yêu cầu của Editor.
- **Yêu cầu bất biến (Immutability & Event Sourcing):** Khi một phiên bản trong quá khứ được lưu vào Event Store, file ảnh tại thời điểm đó **tuyệt đối không được phép bị ghi đè hay thay đổi âm thầm**.
- **Chống rò rỉ (Leak Protection):** Chương truyện chưa phát hành là bí mật thương mại, không thể dùng đường link public thông thường.

---

## 2. Vì Sao Các Cách Lưu Trữ Thông Thường Sẽ Thất Bại?

1. **Lưu trực tiếp vào CSDL (dạng BLOB / Base64):**
   - ❌ Làm Database phình to hàng trăm Gigabyte rất nhanh.
   - ❌ Gây nghẽn CPU/RAM khi query, thời gian backup/restore CSDL trở thành thảm họa.
2. **Lưu vào thư mục Local trên Server (ví dụ `uploads/images/...`):**
   - ❌ Mất file ngay khi restart Docker container hoặc triển khai nhiều máy chủ (không scale được).
   - ❌ Làm nghẽn băng thông mạng của máy chủ Backend API khi tải/gửi các file dung lượng lớn.
3. **Lưu trên Cloud Storage (S3) theo tên ngẫu nhiên/thư mục:**
   - ❌ **Trùng lặp khổng lồ (Deduplication = 0):** Khi artist re-upload lại hoặc nhân bản các panel có chung background, hệ thống sẽ lưu file mới hoàn toàn, lãng phí chi phí lưu trữ.
   - ❌ Khó đảm bảo tính toàn vẹn nếu bị ghi đè file cùng tên.

---

## 3. Giải Pháp Tối Ưu Nhất: Content-Addressable Storage (CAS) trên Object Storage

Mô hình này hoạt động tương tự như cơ chế quản lý file của **Git LFS** hay **IPFS**: **Tên của file trên hệ thống lưu trữ chính là mã băm SHA-256 nội dung của chính file đó.**

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

### 3.1. Điểm Vượt Trội Của Content-Addressable Storage (CAS)
- **Tự Động Chống Trùng Lặp 100% (Instant Deduplication):**
  - Trước khi upload, Client tính mã băm SHA-256 của file.
  - Nếu mã băm này đã tồn tại trong Database (do đã có panel khác hoặc version trước dùng chung), hệ thống lập tức tái sử dụng `AssetId` cũ mà **không cần tải lên lại một byte nào**.
- **Bất Biến Tuyệt Đối (Immutability):**
  - Không bao giờ có chuyện "ghi đè file". Nội dung thay đổi thì Hash sẽ đổi thành file mới. Điều này bảo vệ 100% tính toàn vẹn của Event Sourcing.
- **Xác Minh Toàn Vẹn (Integrity Verification):**
  - Bất kỳ lúc nào, hệ thống cũng có thể verify xem dữ liệu trên Cloud có đúng là file gốc của tác giả hay không bằng cách tính lại SHA-256.

---

## 4. Mô Hình Lưu Trữ Phân Tầng Cụ Thể (Storage Architecture)

Hệ thống nên tổ chức làm **3 tầng** rõ ràng:

### Tầng 1: CSDL Quản Lý Metadata (`Assets` Table trong PostgreSQL)
Lưu thông tin về file ảnh, không lưu nội dung nhị phân:
- `Id`: Mã định danh Asset (UUID).
- `Sha256Hash`: Chuỗi Hash SHA-256 (Unique Index để tra cứu chống trùng lặp).
- `StorageKey`: Đường dẫn thực tế trên S3 (ví dụ: `assets/3a/f8/3af8d9c2...png`).
- `Width`, `Height`, `SizeBytes`, `MimeType`: Thông số kỹ thuật ảnh.
- `DisplayStorageKey`: Đường dẫn ảnh nén WebP phục vụ kéo thả trên Canvas Editor.
- `ThumbnailStorageKey`: Đường dẫn ảnh nhỏ (256x256 WebP) hiển thị ở thanh danh sách/dashboard.
- `WorkspaceId`: Thuộc workspace nào (để kiểm soát dung lượng lưu trữ Storage Quota).

### Tầng 2: Hạ Tầng Object Storage (S3-Compatible)
Sử dụng chuẩn giao thức S3:
- **Khi chạy máy Local / Dev / CI:** Dùng **MinIO** tích hợp trong `docker-compose.yml`. Hoàn toàn miễn phí, độc lập, không phụ thuộc mạng.
- **Khi triển khai Cloud / Production:** Dùng **Cloudflare R2** (Khuyến nghị số 1 vì **miễn phí 100% phí băng thông tải ra - Egress Bandwidth**) hoặc AWS S3 / Wasabi.

### Tầng 3: Pipeline Tối Ưu Ảnh (Image Processing Pipeline)
Khi một file ảnh vẽ gốc tải lên:
1. **Master Asset:** Giữ nguyên gốc chất lượng cao nhất (PNG/TIFF) để sau này dùng cho chức năng **Export PDF / In ấn**.
2. **Display Variant (Tối ưu cho Editor):** Chuyển sang định dạng **WebP** giới hạn kích thước (tối đa 2048px, dung lượng ~300KB-800KB). Màn hình Canvas Editor sẽ load ảnh này để thao tác mượt, không giật lag.
3. **Thumbnail Variant:** WebP 256px hoặc 512px phục vụ thanh công cụ hoặc trang tổng quan.

---

## 5. Quy Trình Upload & Bảo Mật Ảnh (Direct Upload + Pre-signed URL)

### 5.1. Direct Upload (Không đi qua RAM của Backend API)
1. **Bước 1 (Check):** Frontend gửi Hash SHA-256 lên API kiểm tra xem file đã có chưa.
2. **Bước 2 (Sign):** Nếu chưa có, Backend tạo ra một đường dẫn đặc biệt **Pre-signed PUT URL** từ S3 có hạn 15 phút và trả về cho Frontend.
3. **Bước 3 (Upload):** Frontend đẩy trực tiếp file nhị phân từ trình duyệt lên S3 thông qua URL đó. (Server Backend không phải gánh traffic nặng).
4. **Bước 4 (Confirm & Process):** Frontend báo cho Backend rằng upload đã xong. Backend đưa vào Background Queue để sinh Thumbnail/Display Variant bằng thư viện `SixLabors.ImageSharp`.

### 5.2. Bảo Mật Chống Rò Rỉ Bản Quyền (Leak Protection)
- **Bucket hoàn toàn Private:** Không ai có thể truy cập trực tiếp bằng đường dẫn tĩnh.
- **Pre-signed GET URL:** Khi thành viên mở một trang truyện, Backend xác thực quyền xem rồi mới sinh URL tạm thời (hạn 15–30 phút).
- **Watermarked Preview (Xem trước đối tác ngoài):** Đối với các link xem trước tạm thời gửi cho đối tác/độc giả thử nghiệm (`ExternalPreviewLink`), hệ thống tự động đóng dấu chìm (Watermark) mờ tên người xem + thời gian lên ảnh để chống chụp lén tuồn ra ngoài.

---

## 6. Các Bước Triển Khai Thực Tế Cho Team PanelForge

1. **Bổ sung MinIO vào `docker-compose.yml`:** Để cả team có thể test upload/download ảnh S3 ngay trên máy local.
2. **Tạo Entity `Asset` trong Domain:** Đăng ký bảng `Assets` trong `PanelForgeDbContext` và tạo migration.
3. **Tạo Interface & Service trong Infrastructure:**
   - `IObjectStorageService` sử dụng thư viện `AWSSDK.S3`.
   - `IImageProcessingService` sử dụng `SixLabors.ImageSharp` để nén WebP & sinh thumbnail.
4. **Viết `AssetsController`:** Cung cấp các endpoint `prepare-upload`, `confirm-upload`, và `view-url`.
5. **Tích hợp vào Frontend Editor:** Sử dụng Web Crypto API để tính SHA-256 trước khi gửi request.
