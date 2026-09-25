# GPS Location Service - Architecture & Implementation Plan

## 1. Phân tích Yêu cầu (Từ SRS & DBML)
- **Tính năng**: 
  - Khám phá các sự kiện (Fan conventions, cosplay meetups, screening events) lân cận dựa trên vị trí (Location-Aware Event Discovery).
  - Tích hợp Bản đồ & GPS (Map and GPS Integration).
- **Cơ sở dữ liệu hiện tại (DBML)**: 
  - Dự án đang sử dụng mô hình Database chia theo Service (Microservices Database per Service).
  - Bảng `LOCATION` đang nằm trong `EVENT DB` (SQL Server) bao gồm các trường: `location_id`, `name`, `latitude`, `longitude`.

## 2. Phương án Kiến trúc Tối ưu (Optimal Solution)

Vì việc truy vấn "Tìm kiếm các điểm lân cận trong bán kính X km" (Geospatial Query) trên SQL Server thông thường (nếu không dùng kiểu `geography`) sẽ rất chậm khi lượng dữ liệu lớn, phương án tối ưu cho Microservice này như sau:

### 2.1. Lựa chọn Công nghệ
- **Framework**: Node.js + Fastify (Tối ưu throughput, nhanh gấp 2-3 lần Express).
- **Database / Cache cho Tọa độ**:
  - **Phương án đề xuất (Tối ưu nhất)**: Sử dụng **Redis (Redis GEO)**. Redis lưu trữ tọa độ cực kỳ nhẹ và truy vấn khoảng cách bằng `GEORADIUS` hoặc `GEOSEARCH` với tốc độ bộ nhớ đệm (in-memory).
  - **Phương án thay thế**: Sử dụng **MongoDB** (với `2dsphere index`) cho phép truy vấn không gian ($near, $geoWithin) rất mạnh mẽ nếu service này cần mở rộng lưu thêm nhiều metadata của địa điểm.
- **Data Sync (Event-Driven)**: Khi một Sự kiện/Địa điểm mới được tạo ở `Event Service` (C#/.NET), hệ thống sẽ bắn một message qua **RabbitMQ / Kafka**. `gps-location-service` sẽ consume message này và cập nhật tọa độ vào Redis/MongoDB.

### 2.2. Luồng hoạt động (Workflow)
1. **Sync Data**: `Event Service` -> [RabbitMQ] -> `gps-location-service` -> Lưu `{eventId, lat, lng}` vào Redis.
2. **Truy vấn**: User mở app -> Cấp quyền GPS -> Frontend gọi API `GET /api/v1/locations/nearby?lat=...&lng=...&radius=10` -> `gps-location-service` query Redis GEO -> Trả về danh sách `eventId` kèm khoảng cách -> (Tùy chọn) Gọi gộp Event Service để lấy thông tin chi tiết sự kiện và trả về cho Client.

### 2.3. Cấu trúc Thư mục Đề xuất (Đã khởi tạo)
```text
gps-location-service/
├── src/
│   ├── config/          # Cấu hình biến môi trường, kết nối Redis, DB, RabbitMQ
│   ├── controllers/     # Xử lý request từ client (LocationController)
│   ├── middleware/      # Validate tọa độ, xác thực Auth token
│   ├── models/          # Schema MongoDB (Nếu dùng Mongo)
│   ├── routes/          # Khai báo endpoints (/api/v1/locations)
│   ├── services/        # Business logic xử lý tìm kiếm không gian (GeoService)
│   └── index.js         # Entry point (Express App)
├── package.json
└── README.md
```

## 3. Các API Endpoints Chính

| Method | Endpoint | Mô tả |
|--------|----------|-------|
| `GET`  | `/api/v1/locations/nearby` | Tìm sự kiện quanh 1 tọa độ (`?lat=&lng=&radius=km`) |
| `GET`  | `/api/v1/locations/:eventId` | Lấy tọa độ cụ thể của một sự kiện |
| `POST` | `/api/v1/locations/sync`   | API nội bộ (hoặc webhook) để đồng bộ tọa độ (Nếu không dùng RabbitMQ) |

## 4. Các bước triển khai tiếp theo
1. Cài đặt các dependencies: `npm install express cors dotenv ioredis amqplib`
2. Triển khai cấu hình kết nối Redis / MongoDB trong thư mục `src/config`.
3. Xây dựng logic query tọa độ bằng `Redis.geosearch` hoặc `Mongoose.$near` trong `src/services`.
4. Viết các Controllers & Routes và gắn vào `index.js`.
