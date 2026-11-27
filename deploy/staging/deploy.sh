#!/usr/bin/env bash
# Deploy 2 môi trường: main (bản chính thức) và dev (bản test), mỗi môi trường theo dõi nhánh cùng tên.
# Nhánh có commit mới (hoặc --force) thì build lại API + AI của môi trường đó rồi chạy lại.
# systemd timer panelforge-deploy.timer gọi script này mỗi 2 phút. Log: journalctl -u panelforge-deploy
set -euo pipefail
cd /opt/panelforge
ENVS="${ENVS:-main dev}"
declare -A PORT=([main]=8080 [dev]=8081)

exec 9>/run/panelforge-deploy.lock
flock -n 9 || exit 0 # lượt trước chưa xong

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

  if ! docker compose build --pull "api-$env" "ai-$env"; then
    echo "[$env] LỖI build, giữ bản đang chạy"
    status=1
    continue
  fi
  docker compose up -d --no-build caddy translate "postgres-$env" "ai-$env" "api-$env"

  ok=""
  for _ in $(seq 1 60); do
    if curl -fsS -o /dev/null "http://127.0.0.1:${PORT[$env]}/swagger/v1/swagger.json"; then ok=1; break; fi
    sleep 5
  done
  if [ -n "$ok" ]; then
    echo "[$env] OK: API chạy commit ${new:0:7}"
  else
    echo "[$env] LỖI: API không trả lời sau 5 phút. Log gần nhất:"
    docker compose logs --tail 80 "api-$env"
    status=1
  fi
done

docker image prune -f > /dev/null
exit $status
