#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "Đang dừng toàn bộ hệ thống FanHubPlus..."
docker compose -f docker-compose.prod.yml down
echo "Đã dừng thành công."
