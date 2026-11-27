#!/usr/bin/env bash
# Cài BE PanelForge trên VM Ubuntu 24.04 (Azure x64 hoặc Oracle Ampere A1 ARM): 2 môi trường main (chính thức) + dev (test).
# Chạy bằng root; chạy lại được, cũng là cách cập nhật cấu hình server khi file trong deploy/staging đổi.
# Dùng: bash bootstrap.sh [ref chứa deploy/staging, mặc định main]
set -euxo pipefail
APP=/opt/panelforge
REPO=https://github.com/DontPassAway/PanelForge_FA26SE251.git
DEPLOY_REF="${1:-main}"
ENVS="main dev"

# 1) Chỉ Ubuntu image của Oracle chặn mọi cổng vào trừ 22 (có netfilter-persistent); Azure mở cổng bằng NSG ngoài VM.
#    Mở cổng và lưu lại TRƯỚC khi cài Docker (lưu sau khi có Docker sẽ ghi cả rule của Docker, khởi động lại bị trùng rule).
if command -v netfilter-persistent > /dev/null && ! command -v docker > /dev/null; then
  for port in 80 443 8443; do
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

# 4) Mã nguồn: mỗi môi trường một bản clone theo nhánh cùng tên.
mkdir -p "$APP"
systemctl stop panelforge-deploy.timer panelforge-deploy.service 2>/dev/null || true
# Bản cài 1 môi trường trước đây (project panelforge-stg, ./src): dừng nó, dùng lại bản clone làm src-main.
if [ -d "$APP/src/.git" ] && [ ! -d "$APP/src-main" ]; then
  (cd "$APP" && docker compose -p panelforge-stg down) || true
  docker image rm panelforge-api:stg panelforge-ai:stg 2>/dev/null || true
  mv "$APP/src" "$APP/src-main"
  rm -f "$APP/.deployed"
fi
for env in $ENVS; do
  [ -d "$APP/src-$env/.git" ] || git clone --branch "$env" "$REPO" "$APP/src-$env"
done
git -C "$APP/src-main" fetch --quiet origin "$DEPLOY_REF"
for f in docker-compose.yml Caddyfile Dockerfile.ai deploy.sh; do
  git -C "$APP/src-main" show "FETCH_HEAD:deploy/staging/$f" > "$APP/$f.new"
  mv "$APP/$f.new" "$APP/$f"
done
chmod 755 "$APP/deploy.sh"

# 5) Secret sinh ngẫu nhiên ngay trên VM, không rời máy; chỉ thêm biến còn thiếu, giữ nguyên giá trị đã có.
#    Biến trống: điền sau (xem README) rồi chạy deploy.sh --force.
(umask 077 && touch "$APP/.env")
add_env() { grep -q "^$1=" "$APP/.env" || echo "$1=$2" >> "$APP/.env"; }
add_env MAIN_SITE ":80"
add_env DEV_SITE ":8443"
for env in MAIN DEV; do
  add_env "${env}_POSTGRES_PASSWORD" "$(openssl rand -hex 24)"
  add_env "${env}_JWT_SECRET" "$(openssl rand -hex 48)"
  add_env "${env}_AI_SERVICE_KEY" "$(openssl rand -hex 32)"
done
add_env MAIN_SEPAY_WEBHOOK_API_KEY ""
add_env SMTP_SENDER_EMAIL ""
add_env SMTP_APP_PASSWORD ""
add_env FIREBASE_PROJECT_ID ""

# 6) Tự deploy: timer kiểm tra nhánh main và dev mỗi 2 phút.
cat > /etc/systemd/system/panelforge-deploy.service <<EOF
[Unit]
Description=PanelForge deploy (main + dev)
After=docker.service network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=$APP/deploy.sh
TimeoutStartSec=60min
EOF
cat > /etc/systemd/system/panelforge-deploy.timer <<EOF
[Unit]
Description=Kiem tra nhanh main va dev moi 2 phut

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
