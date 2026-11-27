#!/usr/bin/env bash
# Cài staging BE PanelForge lần đầu trên VM Ubuntu 24.04 (Azure x64 hoặc Oracle Ampere A1 ARM). Chạy bằng root, chạy lại được.
# Dùng: bash bootstrap.sh [ref chứa deploy/staging, mặc định stg]
set -euxo pipefail
APP=/opt/panelforge
REPO=https://github.com/DontPassAway/PanelForge_FA26SE251.git
DEPLOY_REF="${1:-stg}"

# 1) Chỉ Ubuntu image của Oracle chặn mọi cổng vào trừ 22 (có netfilter-persistent); Azure mở cổng bằng NSG ngoài VM.
#    Mở 80/443 và lưu lại TRƯỚC khi cài Docker (lưu sau khi có Docker sẽ ghi cả rule của Docker, khởi động lại bị trùng rule).
if command -v netfilter-persistent > /dev/null; then
  for port in 80 443; do
    if ! iptables -C INPUT -p tcp --dport "$port" -m state --state NEW -j ACCEPT 2>/dev/null; then
      n=$(iptables -L INPUT --line-numbers -n | awk '$2=="REJECT" {print $1; exit}')
      iptables -I INPUT "${n:-1}" -p tcp --dport "$port" -m state --state NEW -j ACCEPT
    fi
  done
  netfilter-persistent save
fi

# 2) Swap 4 GB đỡ lúc build .NET.
if ! swapon --show | grep -q /swapfile; then
  fallocate -l 4G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

# 3) Docker (gói của Ubuntu).
export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get install -y -q docker.io docker-compose-v2 docker-buildx git curl openssl
systemctl enable --now docker
for u in ubuntu azureuser; do usermod -aG docker "$u" 2>/dev/null || true; done # user SSH mặc định của Oracle / Azure

# 4) Mã nguồn nhánh stg + file deploy lấy từ DEPLOY_REF.
mkdir -p "$APP"
[ -d "$APP/src/.git" ] || git clone --branch stg "$REPO" "$APP/src"
git -C "$APP/src" fetch --quiet origin "$DEPLOY_REF"
for f in docker-compose.yml Caddyfile Dockerfile.ai deploy.sh; do
  git -C "$APP/src" show "FETCH_HEAD:deploy/staging/$f" > "$APP/$f.new"
  mv "$APP/$f.new" "$APP/$f"
done
chmod 755 "$APP/deploy.sh"

# 5) Secret sinh ngẫu nhiên ngay trên VM, không rời máy. Biến trống: điền sau (xem README) rồi chạy deploy.sh --force.
if [ ! -f "$APP/.env" ]; then
  (umask 077 && cat > "$APP/.env" <<EOF
SITE_ADDRESS=:80
POSTGRES_PASSWORD=$(openssl rand -hex 24)
JWT_SECRET=$(openssl rand -hex 48)
AI_SERVICE_KEY=$(openssl rand -hex 32)
SEPAY_WEBHOOK_API_KEY=
SMTP_SENDER_EMAIL=
SMTP_APP_PASSWORD=
FIREBASE_PROJECT_ID=
EOF
  )
fi

# 6) Tự deploy: timer kiểm tra nhánh stg mỗi 2 phút.
cat > /etc/systemd/system/panelforge-deploy.service <<EOF
[Unit]
Description=PanelForge staging deploy (nhanh stg)
After=docker.service network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=$APP/deploy.sh
TimeoutStartSec=45min
EOF
cat > /etc/systemd/system/panelforge-deploy.timer <<EOF
[Unit]
Description=Kiem tra nhanh stg moi 2 phut

[Timer]
OnBootSec=2min
OnUnitActiveSec=2min

[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable panelforge-deploy.timer
systemctl start --no-block panelforge-deploy.service
systemctl start panelforge-deploy.timer
echo "Bootstrap xong. Theo dõi deploy: journalctl -fu panelforge-deploy"
