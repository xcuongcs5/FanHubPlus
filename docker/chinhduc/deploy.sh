#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=========================================================="
echo "      🚀 FANHUBPLUS VPS DEPLOYMENT SCRIPT                 "
echo "=========================================================="

# 1. Chuẩn bị thư mục secrets nếu chưa tồn tại
mkdir -p ../secrets
if [ ! -f "../secrets/payment-providers.json" ]; then
    echo "[1/4] Tạo file payment-providers.json từ mẫu..."
    cp payment-providers.example.json ../secrets/payment-providers.json
fi

# 2. Kiểm tra file .env, nếu chưa có thì tự sinh mật khẩu an toàn (> 20 ký tự)
if [ ! -f ".env" ]; then
    echo "[2/4] Chưa có .env, đang tự động sinh mật khẩu bảo mật chuẩn regex..."
    cat > .env <<EOF
# --- CSDL & Message Broker (Tự sinh an toàn > 20 ký tự) ---
MSSQL_SA_PASSWORD=Fh9@$(openssl rand -hex 16)
EVENT_DB_PASSWORD=Fh9@$(openssl rand -hex 16)
BOOKING_DB_PASSWORD=Fh9@$(openssl rand -hex 16)
PAYMENT_DB_PASSWORD=Fh9@$(openssl rand -hex 16)
NOTIFICATION_DB_PASSWORD=Fh9@$(openssl rand -hex 16)
RABBITMQ_PASSWORD=Fh9@$(openssl rand -hex 16)
REDIS_PASSWORD=Fh9@$(openssl rand -hex 16)
MONGO_PASSWORD=Fh9@$(openssl rand -hex 16)

# --- Bảo mật & JWT ---
JWT_SECRET=$(openssl rand -hex 24)
JWT_ISSUER=FanHub.IdentityService
JWT_AUDIENCE=FanHub.Services

# --- Ports ---
SQL_PORT=14334
RABBITMQ_PORT=5673
RABBITMQ_MANAGEMENT_PORT=15673
REDIS_PORT=6381
MONGO_PORT=27017

IDENTITY_HTTP_PORT=5001
EVENT_HTTP_PORT=5004
BOOKING_HTTP_PORT=5010
PAYMENT_HTTP_PORT=5011
NOTIFICATION_HTTP_PORT=5012
GPS_HTTP_PORT=3001
SEARCH_HTTP_PORT=3003
BLOCKCHAIN_HTTP_PORT=3004
CHATBOT_HTTP_PORT=3005
ANALYTICS_HTTP_PORT=5015
KONG_PROXY_PORT=8080

# --- Tích hợp ngoài ---
GEMINI_API_KEY=
FIREBASE_ENABLED=false
FIREBASE_PROJECT_ID=
BLOCKCHAIN_SIGNER_ADDRESS=
EOF
    echo "      ✅ Đã tạo file .env thành công!"
else
    echo "[2/4] Đã tìm thấy file .env, tiếp tục sử dụng cấu hình hiện có."
fi

# 3. Khởi động hạ tầng CSDL trước và chạy Migrations
echo "[3/4] Đang khởi động hạ tầng Database, Broker và chạy Migrations..."
docker compose -f docker-compose.prod.yml up -d sqlserver rabbitmq redis mongodb-search qdrant

echo "      Đang chờ SQL Server sẵn sàng..."
docker compose -f docker-compose.prod.yml up db-migrate

# 4. Khởi động toàn bộ Microservices và Kong Gateway
echo "[4/4] Khởi động toàn bộ Microservices và Kong API Gateway..."
docker compose -f docker-compose.prod.yml up -d --build

echo ""
echo "=========================================================="
echo "      🎉 TRIỂN KHAI HOÀN TẤT TRÊN VPS!                     "
echo "=========================================================="
docker compose -f docker-compose.prod.yml ps
echo ""
echo "👉 Kong API Gateway đang lắng nghe tại cổng: 8080"
echo "👉 Kiểm tra log hệ thống: docker compose -f docker-compose.prod.yml logs -f"
