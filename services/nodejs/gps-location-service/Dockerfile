FROM node:20-alpine AS builder

WORKDIR /app
COPY package*.json ./
RUN npm install

COPY . .

FROM node:20-alpine

WORKDIR /app
COPY --from=builder /app/package*.json ./
COPY --from=builder /app/node_modules ./node_modules
COPY --from=builder /app/src ./src
COPY --from=builder /app/index.js ./index.js

# Cấu hình bảo mật: Không chạy dưới quyền root
RUN addgroup -S fanhub && adduser -S fanhub -G fanhub
USER fanhub

EXPOSE 3001

CMD ["npm", "start"]
