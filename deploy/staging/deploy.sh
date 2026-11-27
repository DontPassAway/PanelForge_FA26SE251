#!/usr/bin/env bash
# Deploy: lấy commit mới nhất của nhánh $BRANCH (mặc định main), có thay đổi (hoặc --force) thì build lại và chạy.
# systemd timer panelforge-deploy.timer gọi script này mỗi 2 phút. Log: journalctl -u panelforge-deploy
set -euo pipefail
cd /opt/panelforge
BRANCH="${BRANCH:-main}"

exec 9>/run/panelforge-deploy.lock
flock -n 9 || exit 0 # lượt trước chưa xong

git -C src fetch --quiet origin "$BRANCH"
NEW=$(git -C src rev-parse "origin/$BRANCH")
OLD=$(cat .deployed 2>/dev/null || true)
[ "$NEW" = "$OLD" ] && [ "${1:-}" != "--force" ] && exit 0

echo "Deploy $BRANCH @ ${NEW:0:7} (trước: ${OLD:0:7})"
git -C src reset --quiet --hard "$NEW"
# Khi deploy/staging đã có trong nhánh stg thì cấu hình server theo repo (deploy.sh tự cập nhật qua bootstrap.sh).
if [ -d src/deploy/staging ]; then
  cp src/deploy/staging/{docker-compose.yml,Caddyfile,Dockerfile.ai} .
fi
# Ghi trước khi build: build lỗi thì không build lại liên tục, đợi commit sau hoặc --force.
echo "$NEW" > .deployed

docker compose build --pull api ai
docker compose up -d --remove-orphans

for _ in $(seq 1 60); do
  if curl -fsS -o /dev/null http://127.0.0.1:8080/swagger/v1/swagger.json; then
    echo "OK: API chạy commit ${NEW:0:7}"
    docker image prune -f > /dev/null
    exit 0
  fi
  sleep 5
done

echo "LỖI: API không trả lời sau 5 phút. Log gần nhất:"
docker compose logs --tail 80 api
exit 1
