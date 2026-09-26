# Tối ưu kích thước image bằng Multi-stage build
FROM node:20-alpine AS builder

WORKDIR /app
COPY package*.json ./
RUN npm install

COPY . .

# Giai đoạn production (chỉ chứa các code cần chạy, giảm kích thước Image)
FROM node:20-alpine

WORKDIR /app

# Copy các dependencies production từ builder
COPY --from=builder /app/package*.json ./
COPY --from=builder /app/node_modules ./node_modules
COPY --from=builder /app/src ./src
COPY --from=builder /app/index.js ./index.js

# Đảm bảo thư mục storage tồn tại và có quyền ghi (vì Docker sẽ chạy user non-root)
RUN mkdir -p /app/src/storage && chown -R node:node /app/src/storage

# Chạy dưới quyền user node có sẵn trong image alpine để tăng bảo mật
USER node

EXPOSE 3002

CMD ["npm", "start"]
