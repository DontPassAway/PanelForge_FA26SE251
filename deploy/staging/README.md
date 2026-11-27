# Deploy BE lên VM (bản test + bản chính thức)

Một VM Ubuntu 24.04 chạy **2 môi trường**, mỗi môi trường có API, AI service và **Postgres riêng**:

| Môi trường | Nhánh | Địa chỉ (Azure hiện tại) | DB |
|---|---|---|---|
| **Bản chính thức** | `main` | `https://panelforge.koreacentral.cloudapp.azure.com` | volume `pg_main` |
| **Bản test** | `dev` | `https://panelforge.koreacentral.cloudapp.azure.com:8443` | volume `pg_dev` |

Quy trình: code mới vào `dev` → tự lên bản test → test ổn thì promote `dev` vào `main` (như nhóm đang làm) → tự lên bản chính thức. Hai DB tách hẳn: dữ liệu thử trên bản test không lẫn vào bản chính thức.

VM tự build từ mã nguồn, không dùng image GHCR (image do `ci.yml` build chỉ có x64 và có thể private).

## Luồng CI/CD

1. Push lên `dev`/`main` → GitHub Actions (`.github/workflows/ci.yml`) chạy build + test như cũ.
2. Trên VM, `panelforge-deploy.timer` chạy `deploy.sh` mỗi 2 phút. Với từng môi trường: nhánh có commit mới thì `git reset` về commit đó, build lại API + AI của môi trường đó, `up -d`, rồi đợi `/swagger/v1/swagger.json` trả 200.
3. Build lỗi thì giữ bản cũ đang chạy, ghi log, không build lại cho tới commit sau. Hai môi trường build lần lượt (chung một khoá), không chạy song song.

VM không giữ secret nào của GitHub và GitHub không cần SSH key vào VM.

## Thành phần (`docker-compose.yml`, project `panelforge`)

| Service | Vai trò |
|---|---|
| `caddy` | Reverse proxy: `MAIN_SITE` → `api-main`, `DEV_SITE` → `api-dev`; tự cấp HTTPS khi dùng tên miền |
| `translate` | LibreTranslate (model en/vi), dùng chung |
| `api-main`, `ai-main`, `postgres-main` | Bản chính thức, mã nguồn `./src-main` |
| `api-dev`, `ai-dev`, `postgres-dev` | Bản test, mã nguồn `./src-dev` |

Volume quan trọng của mỗi môi trường: `pg_*` (DB), `assets_*` (ảnh upload), `dp_*` (khoá mã hoá AI key của Studio; mất là phải nhập lại key).

## Cài VM mới

**Azure for Students** (đang dùng): Ubuntu Server 24.04 x64, cỡ `Standard_B2ls_v2` (2 vCPU, 4 GB) ở region được phép, ổ OS 64 GB Premium SSD (P6, miễn phí 12 tháng), user `azureuser` + SSH key, inbound ports 22/80/443 và thêm rule **8443** trong Networking. Dán vào ô *Custom data* (tab Advanced):

```bash
#!/bin/bash
exec > /var/log/panelforge-bootstrap.log 2>&1
set -eux
git clone --branch main https://github.com/DontPassAway/PanelForge_FA26SE251.git /opt/panelforge/src-main
git -C /opt/panelforge/src-main fetch origin main
git -C /opt/panelforge/src-main show FETCH_HEAD:deploy/staging/bootstrap.sh > /root/panelforge-bootstrap.sh
bash /root/panelforge-bootstrap.sh main
```

Tham số của `bootstrap.sh` là ref lấy thư mục `deploy/staging` (dùng nhánh có bản mới nhất của thư mục này). Sau khi tạo VM, đặt *DNS name label* cho Public IP để có tên `<label>.<region>.cloudapp.azure.com`, rồi ghi tên đó vào `.env` (xem dưới).

**Oracle Always Free** (ARM): như trên, user `ubuntu`; `bootstrap.sh` tự mở 80/443/8443 trong iptables của image Oracle, còn Security List của subnet phải thêm Ingress TCP 80, 443, 8443.

## Vận hành

```bash
ssh -i ~/.ssh/panelforge_oracle azureuser@<IP>
sudo tail -f /var/log/panelforge-bootstrap.log      # lần cài đầu
journalctl -fu panelforge-deploy                     # các lần deploy
cd /opt/panelforge && sudo docker compose ps
sudo docker compose logs -f --tail 100 api-dev       # hoặc api-main
sudo ./deploy.sh --force                             # deploy lại ngay cả 2 môi trường
sudo ENVS=dev ./deploy.sh --force                    # chỉ bản test
```

Cập nhật cấu hình server khi file trong `deploy/staging` đổi: chạy lại `bootstrap.sh <ref>` (giữ nguyên `.env`, DB và volume).

### Biến trong `/opt/panelforge/.env`

| Biến | Ghi chú |
|---|---|
| `MAIN_SITE`, `DEV_SITE` | Không có tên miền: `:80` và `:8443` (HTTP). Có tên miền: `ten.mien` và `ten.mien:8443` (HTTPS) |
| `MAIN_*`, `DEV_*` (`POSTGRES_PASSWORD`, `JWT_SECRET`, `AI_SERVICE_KEY`) | Sinh tự động, riêng từng môi trường. Không đổi `*_POSTGRES_PASSWORD` sau khi DB đã tạo |
| `MAIN_SEPAY_WEBHOOK_API_KEY` | Webhook SePay trỏ về bản chính thức, xem `docs/setup/SEPAY_SETUP.md` |
| `SMTP_SENDER_EMAIL`, `SMTP_APP_PASSWORD` | Gmail App Password, cần để gửi OTP xác thực email (dùng chung 2 môi trường) |
| `FIREBASE_PROJECT_ID` | Đăng nhập Google; file key chưa được mount |

Sửa xong: `sudo docker compose up -d` (chỉ đổi biến) hoặc `sudo ./deploy.sh --force`.

### Tạo tài khoản Admin đầu tiên (mỗi môi trường làm riêng)

Server không chạy `DevUserSeeder` (mật khẩu mặc định nằm trong repo public). Khi chưa có SMTP:

```bash
curl -X POST https://<MAIN_SITE hoặc DEV_SITE>/api/auth/register -H 'Content-Type: application/json' \
  -d '{"email":"<email>","password":"<mật khẩu>","fullName":"<tên>"}'   # có thể báo lỗi gửi mail, tài khoản vẫn được tạo
cd /opt/panelforge && sudo docker compose exec postgres-main psql -U panelforge -d panelforge \
  -c "UPDATE users SET is_email_confirmed = true, role = 'Admin' WHERE email = '<email>';"   # postgres-dev cho bản test
```

## Giới hạn đã biết

- VM 4 GB chạy cả 2 môi trường khá sát RAM; thiếu thì nâng cỡ VM (Azure: Size → `B2s_v2` 8 GB) hoặc tạm dừng bản test (`sudo docker compose stop api-dev ai-dev postgres-dev`).
- OpenCV chỉ có native x64 (`OpenCvSharp4.official.runtime.linux-x64.slim`): tách khung tranh (`SplitArtwork`) lỗi nếu chạy trên VM ARM (Oracle).
- CORS của API đang cố định `localhost`: FE deploy ở domain khác cần sửa `Program.cs`.
- Oracle thu hồi VM Always Free nhàn rỗi (CPU < 20% suốt 7 ngày) với tài khoản Free Tier.
