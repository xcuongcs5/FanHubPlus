# FanHub.AnalyticsService

Analytics & Metrics Aggregation Microservice for **FanHubPlus** (.NET 10).

## 1. Trách nhiệm (Responsibilities)
- **Event-Driven Aggregation**: Lắng nghe và gom nhặt các integration events từ RabbitMQ (`UserCreatedEvent`, `UserBannedEvent`, `BookingCreatedEvent`, `BookingPaymentResultEvent`, `BookingRefundResultEvent`, `BookingAttendeeChangedEvent`, `EventChangedEvent`).
- **Zero Direct Query Rule**: Không truy vấn trực tiếp vào database nghiệp vụ của các service khác, tuân thủ nghiêm ngặt ranh giới microservices.
- **Admin Dashboard KPI Support**: Cung cấp số liệu thời gian thực cho các tác vụ quản trị sàn:
  - Task 65: `GET /api/v1/admin/dashboard/overview` (Tổng người dùng, sự kiện, doanh thu GMV, bài viết).
  - Task 66: `GET /api/v1/admin/dashboard/stats/users` (Tăng trưởng người dùng, DAU/MAU).
  - Task 67: `GET /api/v1/admin/dashboard/stats/revenue` (Doanh thu vé & hoa hồng sàn 5%).
  - Task 68: `GET /api/v1/admin/dashboard/stats/categories` (Bảng xếp hạng Fandom / Danh mục thịnh hành).
  - Task 103: `GET /api/v1/admin/financial/reports` (Báo cáo doanh thu & đối soát hoa hồng).
- **Organizer Analytics**:
  - `GET /api/v1/analytics/organizer/events/{eventId}/summary` (Tiến độ bán vé, sức chứa, tỷ lệ check-in thực tế).
- **AI Chatbot & Telemetry**:
  - `GET /api/v1/analytics/ai/query-trends` (Xu hướng câu hỏi, tỷ lệ câu hỏi chưa giải đáp, toxic score).
  - `POST /api/v1/analytics/ai/record-interaction` (Ghi nhận hội thoại bot).
  - `POST /api/v1/analytics/telemetry/collect` (Thu thập clickstream, pageviews từ client).

## 2. Lưu trữ (Storage Architecture)
- **Primary Data Store**: SQL Server 2022 (`fanhub_analytics` database) với các bảng Fact (`fact_ticket_sales`, `fact_event_attendance`, `fact_user_registrations`, `fact_event_metadata`, `fact_telemetry_events`, `fact_chat_interactions`, `agg_daily_platform_kpis`). Có hỗ trợ chế độ In-Memory cho standalone development/test.
- **Fast Metrics & In-Memory Cache**: Redis 7:
  - HyperLogLog cho DAU/MAU (`PFADD`, `PFCOUNT`).
  - Caching các truy vấn Overview định kỳ.

## 3. Cách khởi chạy (How to run)

### Chạy cục bộ (.NET SDK):
```powershell
dotnet run --project services/dotnet/FanHub.AnalyticsService
```
Swagger UI: `http://localhost:5012/swagger` (hoặc cổng HTTP dev được cấp).

### Chạy qua Docker Compose:
```powershell
.\docker\chinhduc\Start-AnalyticsService.ps1
```

### Chạy toàn bộ bộ kiểm thử:
```powershell
dotnet test tests/FanHub.AnalyticsService.Tests
```
