#!/usr/bin/env bash
# Deploy 2 môi trường: main (bản chính thức) và dev (bản test), mỗi môi trường theo dõi nhánh cùng tên.
# Nhánh có commit mới (hoặc --force) thì build lại API + AI của môi trường đó rồi chạy lại.
# systemd timer panelforge-deploy.timer gọi script này mỗi 2 phút. Log: journalctl -u panelforge-deploy
#
# An toàn khi deploy (API tự chạy migration lúc khởi động):
#   1. Gắn tag :<env>-previous cho image đang chạy trước khi build.
#   2. Sau khi build, pg_dump DB của môi trường vào $BACKUP_DIR/<env>-<thời điểm>.dump (giữ 10 bản mới nhất).
#      Dump lỗi thì KHÔNG chạy bản mới cho môi trường đó.
#   3. Chạy bản mới, chờ API trả lời tối đa $HEALTH_TIMEOUT giây. Không lên thì chạy lại image :<env>-previous
#      và báo lỗi. DB KHÔNG tự khôi phục: in đường dẫn bản dump để khôi phục tay (xem README).
set -euo pipefail
cd /opt/panelforge
ENVS="${ENVS:-main dev}"
BACKUP_DIR="${BACKUP_DIR:-/opt/panelforge/backups}"
BACKUP_KEEP="${BACKUP_KEEP:-10}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-120}"
declare -A PORT=([main]=8080 [dev]=8081)
IMAGES=(api ai)

exec 9>/run/panelforge-deploy.lock
flock -n 9 || exit 0 # lượt trước chưa xong

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

image_exists() { docker image inspect "$1" > /dev/null 2>&1; }

# Gắn tag :<env>-previous cho image đang dùng (nếu có) để rollback được sau khi build đè tag :<env>.
tag_previous() {
  local env=$1 name
  for name in "${IMAGES[@]}"; do
    if image_exists "panelforge-$name:$env"; then
      docker tag "panelforge-$name:$env" "panelforge-$name:$env-previous"
    fi
  done
}

# Đưa image :<env>-previous về tag :<env>. Trả 1 nếu chưa có bản trước (lần deploy đầu).
restore_previous_tags() {
  local env=$1 name
  for name in "${IMAGES[@]}"; do
    image_exists "panelforge-$name:$env-previous" || return 1
  done
  for name in "${IMAGES[@]}"; do
    docker tag "panelforge-$name:$env-previous" "panelforge-$name:$env"
  done
}

# Chạy lại bản trước sau khi bản mới không lên.
rollback() {
  restore_previous_tags "$1" || return 1
  docker compose up -d --no-build "ai-$1" "api-$1"
}

# pg_dump dạng custom (-Fc) qua container Postgres của môi trường. In đường dẫn file khi thành công.
backup_db() {
  local env=$1 file tmp
  file="$BACKUP_DIR/$env-$(date -u +%Y%m%dT%H%M%SZ).dump"
  tmp="$file.part"
  docker compose up -d --wait "postgres-$env" > /dev/null
  if ! docker compose exec -T "postgres-$env" pg_dump -U panelforge -d panelforge -Fc > "$tmp" || [ ! -s "$tmp" ]; then
    rm -f "$tmp"
    return 1
  fi
  mv "$tmp" "$file"
  chmod 600 "$file"
  # Giữ $BACKUP_KEEP bản mới nhất của môi trường này.
  ls -1t "$BACKUP_DIR/$env-"*.dump 2> /dev/null | tail -n +"$((BACKUP_KEEP + 1))" | xargs -r rm -f --
  echo "$file"
}

wait_healthy() {
  local env=$1 waited=0
  while [ "$waited" -lt "$HEALTH_TIMEOUT" ]; do
    if curl -fsS -o /dev/null "http://127.0.0.1:${PORT[$env]}/swagger/v1/swagger.json"; then return 0; fi
    sleep 5
    waited=$((waited + 5))
  done
  return 1
}

status=0
for env in $ENVS; do
  src="src-$env"
  git -C "$src" fetch --quiet origin "$env"
  new=$(git -C "$src" rev-parse "origin/$env")
  old=$(cat ".deployed-$env" 2>/dev/null || true)
  [ "$new" = "$old" ] && [ "${1:-}" != "--force" ] && continue

  echo "[$env] deploy ${new:0:7} (trước: ${old:0:7})"
  git -C "$src" reset --quiet --hard "$new"
  # Ghi trước khi build: build lỗi thì không build lại liên tục, đợi commit sau hoặc --force.
  echo "$new" > ".deployed-$env"

  tag_previous "$env"
  if ! docker compose build --pull "api-$env" "ai-$env"; then
    echo "[$env] LỖI build, giữ bản đang chạy"
    status=1
    continue
  fi

  # Bản mới có thể chạy migration: phải có bản sao DB trước khi khởi động nó.
  if ! dump=$(backup_db "$env"); then
    echo "[$env] LỖI: pg_dump thất bại, KHÔNG deploy bản mới (bản đang chạy giữ nguyên)."
    # Image :<env> đã bị build đè: trả về bản trước để lần restart sau không vô tình chạy bản chưa backup.
    restore_previous_tags "$env" || true
    status=1
    continue
  fi
  echo "[$env] Đã backup DB: $dump"

  docker compose up -d --no-build caddy translate "postgres-$env" "ai-$env" "api-$env"

  if wait_healthy "$env"; then
    echo "[$env] OK: API chạy commit ${new:0:7}"
    continue
  fi

  echo "[$env] LỖI: API không trả lời sau ${HEALTH_TIMEOUT}s. Log gần nhất:"
  docker compose logs --tail 80 "api-$env"
  status=1
  if rollback "$env"; then
    if wait_healthy "$env"; then
      echo "[$env] Đã rollback về image trước, API đã chạy lại."
    else
      echo "[$env] Đã rollback về image trước nhưng API vẫn không trả lời (có thể do migration của bản mới)."
    fi
  else
    echo "[$env] Không có image trước để rollback (lần deploy đầu)."
  fi
  echo "[$env] DB KHÔNG được tự khôi phục. Bản dump trước khi deploy: $dump"
  echo "[$env] Khôi phục tay nếu cần: xem mục 'Backup và rollback' trong deploy/staging/README.md"
done

docker image prune -f > /dev/null
exit $status
