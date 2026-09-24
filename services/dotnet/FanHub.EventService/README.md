# EventService — task 21–32

Service .NET 10 triển khai 12 API Event Management, dùng database `fanhub_event` riêng, JWT tương thích Identity và MassTransit/RabbitMQ. EF Core chỉ map schema; migration SQL trong `database/event` là nguồn schema duy nhất.

## Khởi động

Tại thư mục gốc repository, mở Docker Desktop Linux containers rồi chạy:

```powershell
.\docker\chinhduc\Start-EventService.ps1
```

Script khởi động hạ tầng, áp dụng migration (bao gồm `004_review_request`), build image, chờ API healthy. JWT key development được lấy từ cấu hình Identity hiện có nếu `.env` chưa có `JWT_SECRET`; key không được in ra hoặc commit. Nếu Identity đang dùng key từ environment/user-secrets, hãy đặt cùng key vào `JWT_SECRET` trong `.env`. Có thể đặt thêm `JWT_ISSUER`, `JWT_AUDIENCE`, `EVENT_HTTP_PORT` theo môi trường thật. Mặc định issuer `FanHub.IdentityService`, audience `FanHub.Services`.

Trên Windows, script build từ bản sao tạm của EventService và Shared.Contracts để tránh lỗi BuildKit `invalid file request Dockerfile` với reparse point của OneDrive. Bản sao không chứa `.env`, được dọn sau build; source gốc được giữ nguyên. Script này cần PowerShell 7. Với checkout ngoài OneDrive có thể dùng Compose build trực tiếp.

- Swagger: <http://127.0.0.1:5004/swagger>
- Liveness: <http://127.0.0.1:5004/health/live>
- Readiness: <http://127.0.0.1:5004/health/ready> (kiểm tra database/schema; không phải kiểm tra đầy đủ broker).

Swagger dùng nút Authorize với access token từ Identity. Không có endpoint tạo token giả hoặc tự nâng role. Muốn gọi API quản trị sự kiện cần JWT có role `EventOwner` hoặc `Admin`; token User thông thường chỉ được đọc thông tin công khai. Identity không được tự động khởi động bằng script này.

Request mẫu đầy đủ: [FanHub.EventService.http](FanHub.EventService.http). Với cấu hình local, không truy cập qua gateway vì gateway của repository chưa có routing nghiệp vụ.

## Hợp đồng HTTP

JSON dùng `snake_case`. Các trường không có trong DTO bị từ chối 400 để tránh tưởng rằng gửi `status`, `organizer_id` hoặc `is_featured` có tác dụng. Bảng Excel có body mẫu chung; các DTO ở đây cụ thể hóa nghiệp vụ và cần frontend dùng đúng định dạng.

| Task | Endpoint | Quyền / kết quả |
|---|---|---|
| 21 | GET `/api/v1/events` | JWT; chỉ Published |
| 22 | GET `/api/v1/events/{id}` | JWT; người ngoài chỉ thấy sự kiện đã từng công bố; owner/Admin thấy bản nháp |
| 23 | GET `/api/v1/events/organizer/me` | EventOwner/Admin; chỉ sự kiện do chính user tổ chức |
| 24 | POST `/api/v1/events` | EventOwner/Admin; 201, tạo Draft |
| 25 | PUT `/api/v1/events/{id}` | Owner/Admin; 200, chỉ Draft/Rejected; cần `version` lấy từ GET |
| 26 | POST `/api/v1/events/{id}/publish` | Owner/Admin; 202, PendingReview; cần ít nhất một danh mục active |
| 27 | DELETE `/api/v1/events/{id}` | Owner/Admin; 200, hủy nghiệp vụ; không xóa lịch sử |
| 28 | GET `/api/v1/events/{id}/attendees` | Owner/Admin; danh sách vé đã phát hành và người sở hữu hiện tại |
| 29 | POST `/api/v1/events/{id}/categories` | Owner/Admin; 200, thêm các ID chưa gắn; tối đa 20, chỉ Draft/Rejected |
| 30 | GET `/api/v1/categories` | JWT; danh mục active từ Community projection |
| 31 | POST `/api/v1/events/{id}/staffs` | Owner/Admin; 200, upsert staff CheckIn/Manager; user phải active trong projection |
| 32 | GET `/api/v1/events/featured` | JWT; Published, `is_featured=true`, chưa kết thúc |

Danh sách trả `{ "data": [...], "meta": { "total": 0, "page": 1, "limit": 20 } }`. `page` từ 1, `limit` 1–100. Danh sách sự kiện hỗ trợ `sort=newest|oldest|start_time`; danh mục sắp theo tên, người tham dự theo booking ID. Một người giữ nhiều vé có thể xuất hiện nhiều lần; không đếm mỗi hàng là một người duy nhất.

POST create/PUT update nhận `title`, `description`, `banner_url` tùy chọn, `start_time`, `end_time`, `capacity`, `location { name, address, latitude, longitude }`. PUT thêm `version` base64 8-byte rowversion. Thời gian nhận ISO 8601 có timezone, lưu UTC và trả `Z`. Thời điểm bắt đầu phải ở tương lai; end > start; tiêu đề tối đa 250, mô tả plain text tối đa 20.000 ký tự; banner chỉ http/https. Location được tạo mới cho mỗi lần thay đổi để không sửa địa điểm của sự kiện khác.

Lỗi: 400 dữ liệu sai (`ValidationProblemDetails`), 401 token sai/hết hạn hoặc user đã bị ban, 403 không đủ role/không phải owner, 404 không tồn tại hoặc bản nháp không được xem, 409 trạng thái/version/dữ liệu projection chưa sẵn sàng, 503 lỗi kết nối database. Lỗi nghiệp vụ có `code`, `message`, `trace_id`. Không trả stack trace cho client.

Thao tác publish/category/staff/cancel lặp lại cùng yêu cầu không tạo tác dụng lặp. POST tạo event chưa hỗ trợ idempotency key; client không tự retry khi không biết request đầu đã thành công hay chưa.

## Transaction và duyệt

Thao tác mutation khóa bản ghi Event, lưu thay đổi + snapshot outbox trong một SQL transaction. PUT so sánh rowversion để trả 409 khi hai editor cùng sửa. Chỉ đọc/ghi database của service.

Luồng: `Draft → PendingReview → Published/Rejected`; `Rejected → Draft` khi sửa. `Flagged` giữ PendingReview và đòi quyết định Admin. Giá trị `Approved` vẫn được schema cho phép vì thiết kế ban đầu, nhưng handler hiện chuyển trực tiếp sang Published khi nhận Approved hợp lệ. Review mang `review_request_id`, nên kết quả của lần gửi duyệt cũ hoặc event đã hủy không thể kích hoạt lại. Sự kiện đã bắt đầu không được duyệt mở bán. Sự kiện đã kết thúc không được hủy.

## RabbitMQ

Vhost mặc định `fanhub`. Consumer queues durable:

- `fanhub-event-projections`: `CategoryChangedEvent`, `BookingAttendeeChangedEvent`, `UserCreatedEvent`, `UserUpdatedEvent`, `UserBannedEvent`.
- `fanhub-event-reviews`: `EventReviewDecisionEvent`.

Các record nằm trong [Shared.Contracts](../shared/FanHub.Shared.Contracts/Events/EventIntegrationEvents.cs). User events giữ nguyên contract Identity. Message phải có MassTransit envelope/message type và `MessageId`; PHP/Python cần publish đúng envelope/topology, không gửi raw JSON tùy ý. Quyền phát moderation message chỉ dành cho producer tin cậy; trường `Source=Admin` trong body không tự xác thực người gửi. Môi trường production cần broker credentials/ACL riêng cho producer.

Outbox phát `EventChangedEvent`, `EventSubmittedForReviewEvent`, `EventStaffChangedEvent`, giữ `MessageId`, correlation và aggregate version khi retry. EventChanged có snapshot đầy đủ kể cả trạng thái Cancelled để Booking xử lý nhả vé/hoàn tiền khi được triển khai. Worker claim bằng lease 60 giây, publish timeout 30 giây, backoff tối đa 300 giây, dừng tự retry sau 20 lỗi; hàng lỗi giữ trong OUTBOX_MESSAGE để vận hành kiểm tra rồi reset attempt/next_attempt/published_at sau khi sửa nguyên nhân. Broker publisher confirm chỉ xác nhận broker nhận, không bảo đảm service đích đã xử lý.

Consumer ghi inbox và projection cùng transaction, ack sau commit; chặn MessageId trùng và source version cũ. Lỗi tạm retry 500/1000/3000 ms rồi vào queue `_error`; message sai cấu trúc không retry. User cache dùng timestamp vì Identity chưa có sequence/version; profile update không tự unban. Cần contract unban riêng từ Identity để khôi phục trạng thái này.

**Khởi tạo service mới:** tạo queue/binding subscriber trước khi phát dữ liệu. RabbitMQ không lưu message cho một queue chưa tồn tại. Khi Booking/AI/GPS/Notification được triển khai sau Event, cần replay các outbox đã lưu sau khi subscriber sẵn sàng hoặc bootstrap snapshot; inbox/version xử lý phần trùng. Chưa có job xóa outbox tự động.

## Kiểm thử

Sau khi hạ tầng chạy:

```powershell
.\docker\chinhduc\Test-EventService.ps1
```

Test dùng SQL Server/RabbitMQ thật, tạo database/login và vhost với tên ngẫu nhiên rồi xóa đúng tài nguyên do test tạo. HTTP host dùng tài khoản SQL chỉ có quyền DML. Không ghi dữ liệu mẫu vào `fanhub_event` hoặc vhost dùng phát triển. Nếu process test bị kill trước cleanup, tài nguyên có prefix `fanhub_event_test_`/`event-test-` có thể còn để kiểm tra.

Các ca gồm 12 route qua vòng đời duyệt/hủy, JWT và ownership, validation/phân trang, cập nhật đồng thời, inbox trùng/out-of-order, ban user, review cũ, publish thật qua RabbitMQ, Swagger và rollback khi outbox lỗi. Build không thay thế các integration tests này.

## Phần phụ thuộc nhóm

Community cần phát CategoryChangedEvent; Identity cần dùng cùng broker/vhost để gửi user events; Booking cần phát attendee snapshot; AI/Admin cần phát kết quả duyệt có review request ID. Các service của đồng đội chưa được triển khai ở đây. Việc chọn `is_featured` thuộc luồng quản trị ngoài 21–32 và chưa có endpoint trong EventService. Vì vậy database mới chưa có danh mục, staff hoặc featured event sẵn; không thêm seed giả vào môi trường phát triển.

Tham khảo: [EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency), [MassTransit messages](https://masstransit.io/architecture/encrypted-messages.html).
