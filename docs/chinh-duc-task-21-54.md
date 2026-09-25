# Đối chiếu tài liệu và task 21–54 của Chính Đức

Nguồn: `PhanCong_API_V3_Admin.xlsx`, sheet **Phân Công 70 APIs**, các hàng Excel 22–55 (cột STT 21–54); `Database_Design_Report.docx`, mục 1–5 và sơ đồ ERD nhúng trong tài liệu. Đã đọc toàn bộ 83 dòng API và nội dung báo cáo để xác định ranh giới với phần việc của đồng đội. Tên sheet nói 70 nhưng thực tế có 83 task.

## Những điểm cần làm rõ từ nguồn và code

1. Bảng phân bổ trong báo cáo tách Event, Booking, Payment; mục 4.4 và ERD lại gộp chúng vào Event & Booking DB. Theo yêu cầu database riêng cho microservices, triển khai tách bốn database. Quan hệ EVENT → TICKET_BOOKING chuyển thành UUID + local event projection; không tạo khóa ngoại vật lý chéo database.
2. CATEGORY thuộc Community trong báo cáo, dù Event có task gắn và đọc danh mục. Event lưu bản sao đồng bộ qua RabbitMQ. Không chuyển quyền sở hữu danh mục sang Event.
3. Báo cáo chỉ liệt kê các bảng cốt lõi, chưa đủ cho transfer/upgrade/staff/wallet/device-token. Các bảng bổ sung được thiết kế theo chức năng của task, không khẳng định là danh sách cột đã được tài liệu nguồn quy định.
4. Request/response của nhiều dòng Excel là mẫu chung `title/description/status`, kể cả thanh toán và giữ vé. Đây chưa phải DTO hợp lệ cho nghiệp vụ. Giai đoạn API cần thống nhất body cụ thể, HTTP status, pagination, quyền và lỗi với frontend/tester; không dùng mẫu để thiết kế số dư hoặc xác nhận thanh toán.
5. Báo cáo ghi .NET 8; các project hiện tại dùng `net10.0`. Giai đoạn database không đổi target framework hoặc code của đồng đội.
6. Identity hiện đã dùng SQL Server/EF Core/MassTransit và Guid. Vì vậy schema mới dùng SQL Server `uniqueidentifier`, không lấy chuỗi mẫu `usr_xxx/evt_xxx` làm định dạng khóa vật lý.
7. Docker cũ có connection PostgreSQL cho Identity trong khi code dùng `AuthDb` SQL Server; infra cũ còn MySQL CMS. Docker mới nằm riêng tại `docker/chinhduc`, tránh áp đặt thay đổi lên phần việc ngoài 21–54.
8. Khi khảo sát ban đầu, bốn service của Chính Đức chưa có DbContext/entity/controller nghiệp vụ; Event và Payment còn weatherforecast mẫu. EventService đã triển khai 21–32, BookingService đã triển khai 33–42; NotificationService đã triển khai 51–54 với Firebase; Payment/Wallet 43–50 tạm hoãn chờ sandbox.

## Ánh xạ 34 task sang schema

Bảng dưới ánh xạ **database hỗ trợ** từng task. Task 21–32 hiện đã có implementation và integration tests, xem [EventService](../services/dotnet/FanHub.EventService/README.md); task 33–42 có [BookingService và integration tests](../services/dotnet/FanHub.BookingService/README.md); task 51–54 có [NotificationService/Firebase](../services/dotnet/FanHub.NotificationService/README.md); task 43–50 mới có schema và tạm hoãn theo yêu cầu nhóm. Tất cả endpoint yêu cầu Bearer JWT theo workbook, trừ webhook 44–45 công khai ở tầng HTTP nhưng cần xác thực chữ ký provider.

| STT | Method và endpoint | Chức năng theo phân công | Bảng / dữ liệu sử dụng |
|---|---|---|---|
| 21 | GET `/api/v1/events` | Danh sách sự kiện (phân trang) | EVENT, LOCATION |
| 22 | GET `/api/v1/events/{id}` | Chi tiết sự kiện | EVENT, LOCATION, EVENT_CATEGORY, CATEGORY_PROJECTION |
| 23 | GET `/api/v1/events/organizer/me` | [Dashboard] Sự kiện tôi tổ chức | EVENT (organizer_id) |
| 24 | POST `/api/v1/events` | [Dashboard] Tạo sự kiện mới | EVENT, LOCATION, OUTBOX_MESSAGE |
| 25 | PUT `/api/v1/events/{id}` | [Dashboard] Cập nhật sự kiện | EVENT, LOCATION, OUTBOX_MESSAGE |
| 26 | POST `/api/v1/events/{id}/publish` | [Dashboard] Yêu cầu duyệt xuất bản | EVENT, OUTBOX_MESSAGE; EVENT_REVIEW khi có kết quả duyệt |
| 27 | DELETE `/api/v1/events/{id}` | [Dashboard] Hủy sự kiện | EVENT, OUTBOX_MESSAGE (hủy, không xóa lịch sử) |
| 28 | GET `/api/v1/events/{id}/attendees` | [Dashboard] Người tham gia | ATTENDEE_PROJECTION, USER_PROJECTION |
| 29 | POST `/api/v1/events/{id}/categories` | Gắn danh mục cho sự kiện | EVENT_CATEGORY, CATEGORY_PROJECTION |
| 30 | GET `/api/v1/categories` | Danh sách danh mục | CATEGORY_PROJECTION |
| 31 | POST `/api/v1/events/{id}/staffs` | Thêm nhân viên sự kiện | EVENT_STAFF, USER_PROJECTION, OUTBOX_MESSAGE |
| 32 | GET `/api/v1/events/featured` | Sự kiện nổi bật | EVENT (is_featured, status) |
| 33 | POST `/api/v1/bookings/reserve` | Đặt vé (Async via Queue) | BOOKING_REQUEST, TICKET_TYPE, TICKET_BOOKING, OUTBOX_MESSAGE |
| 34 | GET `/api/v1/bookings/status/{requestId}` | Trạng thái đặt vé | BOOKING_REQUEST (request_id, user_id) |
| 35 | GET `/api/v1/bookings/my-tickets` | Danh sách vé của tôi | TICKET_BOOKING (user_id), EVENT_PROJECTION, TICKET_TYPE |
| 36 | GET `/api/v1/bookings/{id}` | Chi tiết vé | TICKET_BOOKING, TICKET_TYPE, EVENT_PROJECTION |
| 37 | POST `/api/v1/bookings/{id}/cancel` | Hủy vé | TICKET_BOOKING, TICKET_TYPE, OUTBOX_MESSAGE; PAYMENT_REFUND tại Payment |
| 38 | POST `/api/v1/bookings/transfer` | Chuyển nhượng vé | TICKET_TRANSFER, TICKET_BOOKING, OUTBOX_MESSAGE |
| 39 | POST `/api/v1/bookings/validate` | [Staff] QR Check-in | TICKET_CHECK_IN, TICKET_BOOKING, EVENT_STAFF_PROJECTION |
| 40 | GET `/api/v1/bookings/event/{eventId}` | Danh sách vé đã bán | TICKET_BOOKING, EVENT_PROJECTION (organizer_id) |
| 41 | GET `/api/v1/bookings/ticket-types/{eventId}` | Loại vé & số lượng còn | TICKET_TYPE (total - reserved - sold) |
| 42 | POST `/api/v1/bookings/{id}/upgrade` | Nâng cấp hạng vé | TICKET_UPGRADE, TICKET_TYPE, TICKET_BOOKING, OUTBOX_MESSAGE |
| 43 | POST `/api/v1/payments/create-intent` | Tạo phiên thanh toán | PAYMENT_TRANSACTION, BOOKING_PROJECTION, UPGRADE_PROJECTION |
| 44 | POST `/api/v1/payments/webhook/vnpay` | Webhook VNPay | PAYMENT_WEBHOOK, PAYMENT_TRANSACTION, OUTBOX_MESSAGE |
| 45 | POST `/api/v1/payments/webhook/momo` | Webhook MoMo | PAYMENT_WEBHOOK, PAYMENT_TRANSACTION, OUTBOX_MESSAGE |
| 46 | GET `/api/v1/payments/{bookingId}/status` | Trạng thái thanh toán | PAYMENT_TRANSACTION (booking_id, user_id) |
| 47 | POST `/api/v1/payments/refund` | Hoàn tiền | PAYMENT_REFUND, PAYMENT_TRANSACTION, WALLET_LEDGER, OUTBOX_MESSAGE |
| 48 | GET `/api/v1/wallets/me` | Xem số dư ví | WALLET (user_id, currency) |
| 49 | POST `/api/v1/wallets/deposit` | Nạp tiền vào ví | PAYMENT_TRANSACTION (Deposit), WALLET; WALLET_LEDGER sau xác nhận |
| 50 | GET `/api/v1/wallets/transactions` | Lịch sử giao dịch | WALLET_LEDGER, WALLET |
| 51 | GET `/api/v1/notifications` | Danh sách thông báo | NOTIFICATION (user_id) |
| 52 | PUT `/api/v1/notifications/{id}/read` | Đánh dấu đã đọc | NOTIFICATION (read_at) |
| 53 | PUT `/api/v1/notifications/read-all` | Đánh dấu tất cả đã đọc | NOTIFICATION (user_id, read_at) |
| 54 | POST `/api/v1/notifications/device-token` | Đăng ký FCM/APNs token | DEVICE_TOKEN |

## Điểm tích hợp còn phụ thuộc nhóm

- Identity: event user và quyền EventOwner/Admin, định dạng JWT subject nhất quán.
- Community/CMS: event danh mục; quyết định duyệt/hủy Event phải gửi về service sở hữu Event.
- AI: kết quả kiểm duyệt bất đồng bộ cho task 26.
- Blockchain: mint/chuyển NFT, xác minh QR ngắn hạn, trạng thái thất bại và retry.
- Organizer/Admin: luồng tạo hạng vé và phân bổ tồn (không có endpoint quản trị hạng vé trong 21–54).
- VNPay/MoMo và FCM/APNs: thông tin sandbox, secret/chứng chỉ và hợp đồng callback khi bắt đầu viết phần tích hợp.

Các phụ thuộc này không ngăn việc hoàn thiện schema. Chúng chưa được mô phỏng thành tích hợp chạy thật ở giai đoạn database.
