# Staging BE trên Oracle Cloud

Nhánh `stg` được deploy lên một VM Oracle Always Free (Ampere A1, ARM, Ubuntu 24.04). VM tự build từ mã nguồn, không dùng image GHCR: image do `ci.yml` build chỉ có bản x64.

## Luồng CI/CD

1. Push lên `stg` → GitHub Actions (`.github/workflows/ci.yml`) chạy build + test như cũ.
2. Trên VM, `panelforge-deploy.timer` chạy `deploy.sh` mỗi 2 phút: có commit mới trên `stg` thì `git reset` về commit đó, `docker compose build`, `up -d`, rồi đợi `/swagger/v1/swagger.json` trả 200.
3. Build lỗi thì giữ bản cũ đang chạy, ghi log, không build lại cho tới commit sau.

VM không giữ secret nào của GitHub và GitHub không cần SSH key vào VM: VM tự kéo code từ repo public.

## Thành phần (`docker-compose.yml`)

| Service | Vai trò |
|---|---|
| `caddy` | Reverse proxy cổng 80/443, tự cấp HTTPS khi `SITE_ADDRESS` là tên miền |
| `api` | PanelForge.API (Dockerfile ở gốc repo), `ASPNETCORE_ENVIRONMENT=Staging`, không chạy seeder |
| `ai` | PanelForge.AI (`Dockerfile.ai`) |
| `postgres` | PostgreSQL 17, volume `pg_data` |
| `translate` | LibreTranslate, chỉ tải model en/vi |

Volume quan trọng: `pg_data` (DB), `assets` (ảnh upload), `dp_keys` (khoá mã hoá AI key của Studio; mất là phải nhập lại key).

## Cài VM mới

Tạo instance Ubuntu 24.04 aarch64, dán vào ô cloud-init (Advanced options → Management → Initialization script):

```bash
#!/bin/bash
exec > /var/log/panelforge-bootstrap.log 2>&1
set -eux
git clone --branch stg https://github.com/DontPassAway/PanelForge_FA26SE251.git /opt/panelforge/src
git -C /opt/panelforge/src fetch origin stg
git -C /opt/panelforge/src show FETCH_HEAD:deploy/staging/bootstrap.sh > /root/panelforge-bootstrap.sh
bash /root/panelforge-bootstrap.sh stg
```

(Trước khi `deploy/staging` vào `stg`, thay `stg` ở hai dòng cuối bằng nhánh đang chứa thư mục này.)

Trên Oracle, thêm Ingress Rule TCP 80 và 443 từ `0.0.0.0/0` vào Security List của subnet. `bootstrap.sh` mở hai cổng này trong iptables của VM, cài Docker, sinh secret vào `/opt/panelforge/.env` (chmod 600) và bật timer.

## Vận hành

```bash
ssh -i ~/.ssh/panelforge_oracle ubuntu@<IP>
sudo tail -f /var/log/panelforge-bootstrap.log     # lần cài đầu
journalctl -fu panelforge-deploy                    # các lần deploy
cd /opt/panelforge && sudo docker compose ps
sudo docker compose logs -f --tail 100 api
sudo ./deploy.sh --force                            # deploy lại ngay
```

### Biến trong `/opt/panelforge/.env`

| Biến | Ghi chú |
|---|---|
| `SITE_ADDRESS` | `:80` = HTTP theo IP. Đặt tên miền (vd `panelforge-stg.duckdns.org`) để có HTTPS |
| `POSTGRES_PASSWORD`, `JWT_SECRET`, `AI_SERVICE_KEY` | Sinh tự động, không đổi `POSTGRES_PASSWORD` sau khi DB đã tạo |
| `SEPAY_WEBHOOK_API_KEY` | Xem `docs/setup/SEPAY_SETUP.md` |
| `SMTP_SENDER_EMAIL`, `SMTP_APP_PASSWORD` | Gmail App Password, cần để gửi OTP xác thực email |
| `FIREBASE_PROJECT_ID` | Đăng nhập Google; file key chưa được mount |

Sửa xong: `sudo docker compose up -d` (chỉ đổi biến) hoặc `sudo ./deploy.sh --force`.

### Tạo tài khoản Admin đầu tiên

Staging không chạy `DevUserSeeder` (mật khẩu mặc định nằm trong repo public). Khi chưa có SMTP:

```bash
curl -X POST http://<IP>/api/auth/register -H 'Content-Type: application/json' \
  -d '{"email":"<email>","password":"<mật khẩu>","fullName":"<tên>"}'   # có thể báo lỗi gửi mail, tài khoản vẫn được tạo
cd /opt/panelforge && sudo docker compose exec postgres psql -U panelforge -d panelforge \
  -c "UPDATE users SET is_email_confirmed = true, role = 'Admin' WHERE email = '<email>';"
```

Kiểm tra tên bảng/cột bằng `\d users` nếu câu lệnh báo lỗi.

## Giới hạn đã biết

- OpenCV chỉ có native x64 (`OpenCvSharp4.official.runtime.linux-x64.slim`): tách khung tranh (`SplitArtwork`) lỗi trên VM ARM tới khi thêm `OpenCvSharp4.runtime.linux-arm64`.
- CORS của API đang cố định `localhost`: FE deploy ở domain khác cần sửa `Program.cs`.
- Oracle thu hồi VM Always Free nhàn rỗi (CPU < 20% suốt 7 ngày) với tài khoản Free Tier.
