# Gắn Cloudflare R2 làm kho lưu ảnh cho PanelForge

> Dành cho: **người giữ tài khoản Cloudflare** (Phần 1) và **mỗi thành viên chạy Backend** (Phần 2).
> Mặc định Backend lưu ảnh trên ổ đĩa (`App_Data/assets`). Chỉ khi làm xong hướng dẫn này, ảnh mới được lưu lên R2.

---

## ⚠️ Quy tắc bảo mật (đọc trước)

- **Không bao giờ** commit key vào Git, kể cả `appsettings.json`, `appsettings.Development.json`, file `.env`.
- **Không** gửi key qua nhóm chat công khai, issue, PR, ảnh chụp màn hình. Gửi riêng từng người (tin nhắn riêng, trình quản lý mật khẩu như Bitwarden/1Password).
- Key chỉ nằm trong **user-secrets** trên máy từng người (lưu ngoài thư mục dự án).
- Lộ key → người giữ tài khoản **xoá token đó ngay** trên Cloudflare và tạo token mới (mục 1.4).

---

## Phần 1 – Người giữ tài khoản Cloudflare (làm 1 lần)

### 1.1. Tạo bucket
1. Đăng nhập https://dash.cloudflare.com → menu trái **R2 Object Storage**.
   (Lần đầu dùng R2, Cloudflare yêu cầu thêm phương thức thanh toán; gói miễn phí có 10 GB lưu trữ/tháng.)
2. Bấm **Create bucket**:
   - **Bucket name:** `panelforge-assets` (hoặc tên khác – nhớ báo cả nhóm).
   - **Location:** Automatic.
   - **Default storage class:** Standard.
3. Sau khi tạo: vào bucket → tab **Settings** → mục **Public access**: để **Disabled** (không bật `r2.dev`, không gắn custom domain).
   *Lý do:* người dùng không tải ảnh thẳng từ R2; ảnh đi qua API PanelForge để kiểm quyền và link ký hạn 5 phút (BR-16).
4. **Không cần** cấu hình CORS (trình duyệt không gọi R2 trực tiếp).

### 1.2. Lấy Account ID
Trang **R2 Object Storage** → khung bên phải **Account Details** → copy **Account ID** (chuỗi 32 ký tự).
Account ID không phải bí mật, nhưng vẫn gửi kèm key cho gọn.

### 1.3. Tạo API token
1. Trang **R2 Object Storage** → **Manage API Tokens** (hoặc **API** → **Manage API tokens**) → **Create API token**.
2. Điền:
   - **Token name:** `panelforge-dev-<tên người>` (khuyến nghị **mỗi thành viên 1 token** để thu hồi riêng từng người).
   - **Permissions:** **Object Read & Write**.
   - **Specify bucket(s):** **Apply to specific buckets only** → chọn `panelforge-assets`.
   - **TTL:** tuỳ (có thể để Forever cho môi trường dev, hoặc đặt hạn đến hết kỳ đồ án).
3. Bấm **Create API Token**. Màn hình hiện:
   - **Access Key ID**
   - **Secret Access Key** ← **chỉ hiện một lần**, copy ngay.
   - (Bỏ qua các mục "Token value" và "jurisdiction-specific endpoint", Backend không dùng.)

### 1.4. Gửi cho thành viên
Gửi riêng cho từng người 4 giá trị:

| Tên | Ví dụ (giả) |
|---|---|
| Account ID | `0123456789abcdef0123456789abcdef` |
| Bucket | `panelforge-assets` |
| Access Key ID | `AKxxxxxxxxxxxxxxxx` |
| Secret Access Key | `•••••••••••• (bí mật)` |

**Thu hồi / đổi key:** Manage API Tokens → chọn token → **Delete** (hoặc **Roll** để đổi secret) → gửi key mới cho người đó.

---

## Phần 2 – Mỗi thành viên chạy Backend

### 2.1. Yêu cầu
- Đã pull Backend có file `src/PanelForge.Infrastructure/Services/R2AssetStorage.cs`.
- Có .NET SDK 9 (`dotnet --version`).

### 2.2. Gắn key bằng user-secrets
Mở terminal ở thư mục gốc repo Backend (`PanelForge_FA26SE251`), rồi:

```bash
cd src/PanelForge.API
```

Chạy lần lượt, **thay phần trong `< >` bằng giá trị thật** người giữ tài khoản gửi:

```bash
dotnet user-secrets set "AssetStorage:Provider" "R2"
dotnet user-secrets set "AssetStorage:R2:AccountId" "<account-id>"
dotnet user-secrets set "AssetStorage:R2:Bucket" "panelforge-assets"
dotnet user-secrets set "AssetStorage:R2:AccessKeyId" "<access-key-id>"
dotnet user-secrets set "AssetStorage:R2:SecretAccessKey" "<secret-access-key>"
```

> PowerShell: nếu secret có ký tự `$` hoặc `` ` ``, dùng **dấu nháy đơn** `'...'` thay vì nháy kép.

Kiểm tra đã lưu đủ 5 khoá (lệnh này **in cả secret ra màn hình** – đừng chụp/chia sẻ):

```bash
dotnet user-secrets list
```

User-secrets lưu ở `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json` (Windows) hoặc `~/.microsoft/usersecrets/...` (macOS/Linux) – **nằm ngoài repo**, không bao giờ bị commit.

### 2.3. Chạy và thử
1. Khởi động lại API (`dotnet run` trong `src/PanelForge.API`, hoặc F5).
2. Đăng nhập FE bằng tài khoản Artist → mở một việc được giao → **tải lên 1 ảnh**.
3. Vào Cloudflare → bucket `panelforge-assets` → **Objects**: thấy object mới dạng
   `<workspaceId>/<2 ký tự đầu hash>/<sha256>`.
4. Mở lại ảnh trên FE → hiển thị bình thường = **xong**.

### 2.4. Quay lại lưu trên ổ đĩa
```bash
dotnet user-secrets set "AssetStorage:Provider" "Local"
```
(hoặc `dotnet user-secrets remove "AssetStorage:Provider"`). Các khoá R2 khác để nguyên cũng không sao.

---

## Phần 3 – Lỗi thường gặp

| Hiện tượng | Nguyên nhân / cách sửa |
|---|---|
| Upload báo lỗi `Thiếu cấu hình AssetStorage:R2:<Tên>` | Chưa đặt khoá đó. Chạy lại lệnh `dotnet user-secrets set` tương ứng (đúng thư mục `src/PanelForge.API`). |
| Lỗi `403 AccessDenied` / `SignatureDoesNotMatch` | Sai Access Key / Secret, token đã bị xoá, hoặc token không có quyền với bucket này. Xin key mới. |
| Lỗi `404 NoSuchBucket` | Sai tên bucket (phân biệt hoa thường) hoặc bucket thuộc tài khoản khác với Account ID. |
| Lỗi `InvalidAccessKeyId` | Account ID và key không cùng một tài khoản Cloudflare. |
| Ảnh **cũ** (tải trước khi bật R2) không hiện | Ảnh cũ vẫn nằm trên ổ đĩa `App_Data/assets`, chưa được chép lên R2. Chuyển `Provider` về `Local` để xem, hoặc báo Backend làm lệnh chép ảnh cũ lên R2. |
| Mỗi người thấy ảnh khác nhau | Mỗi người dùng database Postgres riêng trên máy mình; R2 dùng chung nhưng dữ liệu (bảng `assets`) thì không. Đây là bình thường ở môi trường dev. |
| `dotnet user-secrets` báo "Could not find the global property 'UserSecretsId'" | Đang đứng sai thư mục. Phải ở `src/PanelForge.API`. |

---

## Phần 4 – Khi deploy server (sau này)
Không dùng user-secrets trên server. Đặt **biến môi trường** (dấu `:` đổi thành `__`):

```
AssetStorage__Provider=R2
AssetStorage__R2__AccountId=<account-id>
AssetStorage__R2__Bucket=panelforge-assets
AssetStorage__R2__AccessKeyId=<access-key-id>
AssetStorage__R2__SecretAccessKey=<secret-access-key>
```

Nên tạo **token riêng cho server** (tên `panelforge-prod`), tách khỏi token dev của từng người.

---

## Phần 5 – Chép ảnh cũ (trên ổ đĩa) lên R2

Ảnh tải lên **trước khi** bật R2 nằm ở `src/PanelForge.API/bin/Debug/net9.0/App_Data/assets` (chạy bằng F5 / `dotnet run`) – sau khi bật R2 sẽ không hiện nếu chưa chép.
Làm sau khi đã gắn key (Phần 2). Lệnh **không** khởi động web, **không** đụng database, chạy lại nhiều lần vẫn an toàn (ảnh đã có trên R2 thì bỏ qua).

Đứng ở `src/PanelForge.API`. **Tắt API đang chạy trước** (tránh khoá file build).

1. Chạy thử – chỉ liệt kê và kiểm SHA-256, không chép, không cần key:
   ```bash
   dotnet run -- migrate-assets-to-r2 --dry-run
   ```
2. Chép thật:
   ```bash
   dotnet run -- migrate-assets-to-r2
   ```
3. Kết quả cuối có dạng `Xong: 5 đã chép, 0 sai hash, 0 lỗi.` Mỗi file được chép xong sẽ đọc lại từ R2 để xác nhận.
   - `[SAI HASH]`: nội dung file không khớp tên (file hỏng) → không chép, báo Backend.
   - `[LỖI]`: thường do key sai / mạng → sửa rồi chạy lại lệnh, các file đã chép không bị chép lại.

Ảnh ở thư mục khác (vd. từng chạy bằng `-c Release`, hoặc có đặt `AssetStorage:RootPath`): thêm `--source "<đường dẫn thư mục assets>"`.

Chép xong ảnh vẫn còn trên ổ đĩa (lệnh không xoá gì) – giữ lại làm bản dự phòng.

---

## Tham khảo kỹ thuật (cho người đọc code)
- Lớp: `src/PanelForge.Infrastructure/Services/R2AssetStorage.cs` (cài đặt `IAssetStorage`, dùng thư viện `AWSSDK.S3` vì R2 tương thích API S3).
- Chọn kho lưu: `src/PanelForge.Infrastructure/DependencyInjection.cs` theo `AssetStorage:Provider` (`Local` mặc định | `R2`).
- Khoá cấu hình mẫu (để trống): `src/PanelForge.API/appsettings.Example.json` → mục `AssetStorage`.
- Test: `test/PanelForge.UnitTests/Infrastructure/R2AssetStorageTests.cs`.
