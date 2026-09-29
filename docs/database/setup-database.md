# Cài đặt database cho môi trường dev

Backend có thể dùng **một trong hai** PostgreSQL. Chọn bằng khóa `Database:Target` trong `appsettings.Development.json`.

| | PostgreSQL cài trên máy (xem bằng pgAdmin 4) | PostgreSQL trong Docker |
|---|---|---|
| `Database:Target` | `Local` | `Docker` |
| Chuỗi kết nối được dùng | `ConnectionStrings:PostgreSQL` | `ConnectionStrings:PostgreSQLDocker` |
| Cổng | 5432 | 5433 (máy đã cài PostgreSQL) hoặc 5432 |
| Database | `panelforge_dev` | `panelforge` |
| File mẫu | `appsettings.Development.Example.json` | `appsettings.Example.json` |

Tất cả file cấu hình nằm trong `src/PanelForge.API/`. File thật `appsettings.Development.json` **không** được đưa lên Git (chứa mật khẩu, secret). Mỗi người tự tạo trên máy mình.

---

## Cách A: dùng PostgreSQL cài trên máy (pgAdmin 4)

### 1. Tạo file cấu hình

Trong thư mục `Backend/PanelForge_FA26SE251/src/PanelForge.API`, copy `appsettings.Development.Example.json` thành `appsettings.Development.json`.

Git Bash:

```bash
cd src/PanelForge.API
cp appsettings.Development.Example.json appsettings.Development.json
```

PowerShell:

```powershell
cd src/PanelForge.API
Copy-Item appsettings.Development.Example.json appsettings.Development.json
```

### 2. Sửa hai chỗ trong `appsettings.Development.json`

**Chỗ 1: `Database:Target` phải là `Local`** (file mẫu đã để sẵn):

```json
"Database": {
  "Target": "Local"
},
```

**Chỗ 2: mật khẩu trong `ConnectionStrings:PostgreSQL`.** Thay `YOUR_LOCAL_POSTGRES_PASSWORD` bằng mật khẩu user `postgres` trên máy bạn, tức mật khẩu đặt lúc cài PostgreSQL và dùng để đăng nhập pgAdmin:

```json
"ConnectionStrings": {
  "PostgreSQL": "Host=localhost;Port=5432;Database=panelforge_dev;Username=postgres;Password=MAT_KHAU_CUA_BAN",
  ...
}
```

Nếu PostgreSQL trên máy bạn không chạy ở cổng 5432, sửa luôn `Port=`.

Không cần tự tạo database `panelforge_dev` trong pgAdmin. Backend tự tạo khi chạy lần đầu.

### 3. Điền các secret còn lại

Các giá trị `YOUR_...` trong `JwtSettings`, `Firebase`, `EmailConfiguration`, cùng file `firebase-key.json` (đặt trong `src/PanelForge.API/`), xin trưởng nhóm gửi riêng. **Không commit** chúng lên Git.

### 4. Nếu máy đã có database `panelforge_dev` cũ

Database tạo từ các migration trước ngày 28/09/2026 không tương thích với migration hiện tại. Xoá một lần. Chạy ở thư mục `Backend/PanelForge_FA26SE251`:

```bash
dotnet ef database drop --force --project src/PanelForge.Infrastructure --startup-project src/PanelForge.API
```

Máy chưa từng có database này thì bỏ qua bước này.

### 5. Chạy backend

```bash
cd src/PanelForge.API
dotnet run
```

Lần chạy đầu, backend tự:
- tạo database `panelforge_dev` và các bảng (EF Core migration);
- seed master data và tài khoản dev (mật khẩu `Bnt123#`, ví dụ `admin@panelforge.com`, `producer@panelforge.com`).

Swagger: http://localhost:5044/swagger

### 6. Xem dữ liệu trong pgAdmin 4

Mở server PostgreSQL trên máy (cổng 5432) → **Databases** → `panelforge_dev` → **Schemas** → **public** → **Tables**. Nếu chưa thấy, chuột phải **Refresh**.

---

## Cách B: dùng PostgreSQL trong Docker

1. Bật Docker Desktop. Chạy ở thư mục `Backend/PanelForge_FA26SE251`:

   ```bash
   docker compose up -d postgres-db
   ```

   Máy đã cài sẵn PostgreSQL (chiếm cổng 5432) thì cần tạo file `docker-compose.override.yml` để đưa container ra cổng 5433:

   ```yaml
   services:
     postgres-db:
       ports: !override
         - "5433:5432"
   ```

   Máy không cài PostgreSQL thì bỏ file override, và sửa `Port=5433` thành `Port=5432` trong `PostgreSQLDocker`.

2. Copy `appsettings.Example.json` thành `appsettings.Development.json`. File mẫu này đã để `"Target": "Docker"`. Chuỗi `PostgreSQLDocker` khớp với user và mật khẩu khai báo trong `docker-compose.yml`.
3. Điền các secret như bước A.3, rồi `dotnet run`.
4. Xem bằng pgAdmin: đăng ký server mới với host `localhost`, port `5433`, database `panelforge`, user `postgres`.

---

## Đổi tạm sang database kia mà không sửa file

Giữ đủ cả hai chuỗi kết nối trong `appsettings.Development.json`, rồi chạy trong `src/PanelForge.API`:

```bash
dotnet run --launch-profile http-local    # PostgreSQL trên máy, cổng 5432
dotnet run --launch-profile http-docker   # PostgreSQL trong Docker
```

Lệnh `dotnet ef` đọc cùng khóa `Database:Target`. Có thể đổi tạm bằng biến môi trường `Database__Target=Local` hoặc `Database__Target=Docker`.

---

## Lỗi thường gặp

| Lỗi | Nguyên nhân / cách xử lý |
|---|---|
| `Connection string 'PostgreSQL' chưa được cấu hình` | Chưa tạo `appsettings.Development.json`, hoặc thiếu chuỗi tương ứng với `Target`. |
| `password authentication failed for user "postgres"` | Sai mật khẩu trong chuỗi kết nối. |
| `relation "..." already exists` khi khởi động | Database còn lịch sử migration cũ. Làm bước A.4. |
| `Failed to bind to address http://127.0.0.1:5044` | Đang có một backend khác chạy ở cổng 5044. Tắt nó trước. |
| pgAdmin không thấy dữ liệu mới | Đang xem nhầm server (5432 hay 5433), hoặc chưa **Refresh**. |
