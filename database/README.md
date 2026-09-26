# Database cho Chính Đức — task 21–54

Thư mục này quản lý schema, migration SQL Server, Docker và kiểm thử database. EventService (task 21–32) đã có API, EF Core mapping và RabbitMQ inbox/outbox; xem [hướng dẫn EventService](../services/dotnet/FanHub.EventService/README.md). Booking, Payment và Notification vẫn ở giai đoạn nền database, chưa có API nghiệp vụ. Chưa tích hợp cổng thanh toán hoặc push notification.

## Phân chia database

| Task | Project hiện có | Database | Tài khoản ứng dụng |
|---|---|---|---|
| 21–32 | `FanHub.EventService` | `fanhub_event` | `fanhub_event_app` |
| 33–42 | `FanHub.BookingService` | `fanhub_booking` | `fanhub_booking_app` |
| 43–50 | `FanHub.PaymentWalletService` | `fanhub_payment` | `fanhub_payment_app` |
| 51–54 | `FanHub.NotificationService` | `fanhub_notification` | `fanhub_notification_app` |

Mỗi service có database, tài khoản và lịch sử migration riêng. Local development dùng chung **một SQL Server instance** để tiết kiệm RAM. Đây là tách database theo service, chưa phải bốn SQL Server container độc lập. Khi triển khai riêng, có thể đặt từng database trên instance riêng mà không phải bỏ khóa ngoại liên database vì thiết kế này không có khóa ngoại đó.

Database Event sở hữu `LOCATION`, `EVENT`, `EVENT_CATEGORY`, `EVENT_STAFF`, `EVENT_REVIEW`. `CATEGORY_PROJECTION` là bản sao từ Community/CMS; Event không sở hữu danh mục gốc. `ATTENDEE_PROJECTION` nhận trạng thái vé từ Booking để phục vụ task 28.

Database Booking sở hữu `TICKET_TYPE`, `BOOKING_REQUEST`, `TICKET_BOOKING`, `TICKET_TRANSFER`, `TICKET_UPGRADE`, `TICKET_CHECK_IN`. Nó giữ `EVENT_PROJECTION`, `EVENT_STAFF_PROJECTION` để đọc thông tin sự kiện và kiểm tra quyền staff tại chỗ.

Database Payment sở hữu `PAYMENT_TRANSACTION`, `PAYMENT_WEBHOOK`, `PAYMENT_REFUND`, `WALLET`, `WALLET_LEDGER`. `BOOKING_PROJECTION`, `UPGRADE_PROJECTION` cung cấp số tiền và thời hạn tin cậy từ Booking.

Database Notification sở hữu `NOTIFICATION`, `DEVICE_TOKEN`, `NOTIFICATION_DELIVERY`. Mỗi database đều có bản riêng của `USER_PROJECTION`, `INBOX_MESSAGE`, `OUTBOX_MESSAGE`, `__schema_migrations`.

Xem [ánh xạ từng API](../docs/chinh-duc-task-21-54.md) và [hợp đồng dữ liệu cùng quy tắc transaction](../docs/chinh-duc-data-contracts.md).

## Chạy trên Windows

Mở Docker Desktop ở chế độ Linux containers. Từ thư mục gốc repository:

```powershell
.\docker\chinhduc\Start-Databases.ps1 -Verify
```

Script tạo `docker/chinhduc/.env` với mật khẩu ngẫu nhiên nếu chưa tồn tại, đợi SQL Server/RabbitMQ/Redis healthy, chạy migration rồi kiểm thử. Nó giữ nguyên `.env` đã có. File này đã nằm trong quy tắc `.gitignore` của repository. Không dùng tài khoản `sa` trong service.

Stack có project name `fanhub-chinhduc`, volume và network riêng. Không cần chạy hoặc ghép với hai file Compose cũ trong `docker/`. Các file cũ còn cấu hình PostgreSQL/MySQL và đường dẫn build chưa hoàn chỉnh nên không phải entrypoint của giai đoạn này.

Chạy thủ công, cũng từ thư mục gốc:

```powershell
.\docker\chinhduc\Initialize-Environment.ps1
docker compose --env-file docker/chinhduc/.env -f docker/chinhduc/compose.yml up -d --wait sqlserver rabbitmq redis
docker compose --env-file docker/chinhduc/.env -f docker/chinhduc/compose.yml run --rm db-migrate
docker compose --env-file docker/chinhduc/.env -f docker/chinhduc/compose.yml run --rm --entrypoint /bin/bash db-migrate /scripts/verify.sh
```

Không chỉ dựa vào `up -d`: container migration chạy một lần, cần kiểm tra exit code. `Start-Databases.ps1` thực hiện việc này.

## Kết nối SSMS và service

| Thành phần | Từ máy Windows | Trong network Compose |
|---|---|---|
| SQL Server | `localhost,14334` | `sqlserver,1433` |
| RabbitMQ AMQP | `localhost:5673` | `rabbitmq:5672` |
| RabbitMQ Management | `http://localhost:15673` | `rabbitmq:15672` |
| Redis | `localhost:6381` | `redis:6379` |

Các port có thể đổi trong `.env`. SQL/RabbitMQ/Redis chỉ bind loopback. RabbitMQ user `fanhub`, vhost `fanhub`, mật khẩu lấy từ `RABBITMQ_PASSWORD`; Redis dùng `REDIS_PASSWORD`. Bộ broker này độc lập với broker của đồng đội: khi tích hợp liên service, tất cả publisher/consumer liên quan phải dùng cùng broker/vhost đã thống nhất.

Trong SSMS: chọn **SQL Server Authentication**, database tương ứng và tài khoản ứng dụng trong bảng đầu. Lấy mật khẩu từ biến `EVENT_DB_PASSWORD`, `BOOKING_DB_PASSWORD`, `PAYMENT_DB_PASSWORD`, `NOTIFICATION_DB_PASSWORD` trong `.env`; bật **Trust server certificate** cho môi trường Docker local.

Connection string theo service (EventDb, BookingDb và NotificationDb đã được sử dụng; PaymentDb dành cho giai đoạn tiếp theo):

```text
ConnectionStrings__EventDb=Server=localhost,14334;Database=fanhub_event;User Id=fanhub_event_app;Password=<EVENT_DB_PASSWORD>;Encrypt=True;TrustServerCertificate=True
ConnectionStrings__BookingDb=Server=localhost,14334;Database=fanhub_booking;User Id=fanhub_booking_app;Password=<BOOKING_DB_PASSWORD>;Encrypt=True;TrustServerCertificate=True
ConnectionStrings__PaymentDb=Server=localhost,14334;Database=fanhub_payment;User Id=fanhub_payment_app;Password=<PAYMENT_DB_PASSWORD>;Encrypt=True;TrustServerCertificate=True
ConnectionStrings__NotificationDb=Server=localhost,14334;Database=fanhub_notification;User Id=fanhub_notification_app;Password=<NOTIFICATION_DB_PASSWORD>;Encrypt=True;TrustServerCertificate=True
```

Khi service chạy cùng network Docker, đổi host thành `sqlserver,1433`. Với EF Core sau này, map rõ tên bảng/cột, `uniqueidentifier` → `Guid`, `datetime2(3)` theo UTC, `decimal(19,4)` → `decimal`, `rowversion` → concurrency token. Không chạy `EnsureCreated()` hoặc một bộ initial migration EF khác lên schema đã tạo. Migration SQL trong thư mục này là nguồn quản lý schema duy nhất; nếu chuyển sang EF migrations, cần baseline riêng trước.

## Migration và bảo toàn dữ liệu

- `001_initial.sql`: schema nghiệp vụ riêng của từng service.
- `002_messaging.sql`: cùng định nghĩa kỹ thuật nhưng tạo độc lập trong từng database.
- `003_...sql`: các ràng buộc bổ sung của Booking và Payment.
- `004_review_request.sql`: Event lưu ID lần gửi duyệt tại EVENT/EVENT_REVIEW để chặn kết quả cũ.
- `booking/004_booking_lifecycle.sql`: version cấu hình hạng vé, lỗi Blockchain và BOOKING_PAYMENT chống trùng/hoàn bù.
- Thêm file mới với số tăng dần, duy nhất trong tập service + common, ví dụ `004_add_index.sql`. Script SQL không chứa `GO`, `USE`, `COMMIT`, `ROLLBACK` hoặc các chỉ thị sqlcmd; runner bọc transaction và thực thi một batch.
- Migration được khóa bằng `sp_getapplock`, lưu SHA-256 và commit cùng DDL. Chạy lại bỏ qua phiên bản đã áp dụng. Nếu nội dung file đã áp dụng bị sửa, runner dừng. `.gitattributes` giữ LF để checksum không đổi khi checkout Windows/Linux.
- Tài khoản ứng dụng không có quyền DDL, không ghi `__schema_migrations`, không truy cập database service khác. `WALLET_LEDGER` chỉ cho ứng dụng SELECT/INSERT; điều chỉnh tiền bằng entry nghiệp vụ bổ sung, không sửa lịch sử.
- Không tự đổi mật khẩu login đã tồn tại. Sửa `.env` đơn thuần không đổi password bên SQL Server/RabbitMQ đã lưu trên volume. Khi xoay mật khẩu, đổi ở server trước rồi cập nhật `.env`; runner sẽ báo lỗi nếu mật khẩu app không khớp.
- Không seed user/event/payment giả vào database. Dữ liệu dùng trong kiểm thử nằm trong transaction và được rollback.

Dừng mà giữ dữ liệu:

```powershell
docker compose --env-file docker/chinhduc/.env -f docker/chinhduc/compose.yml down
```

Không thêm `-v` nếu muốn giữ database, queue và cache bền vững. Đây là cấu hình development (SQL Server Developer, chứng chỉ tự ký, secret qua `.env`), chưa phải cấu hình production.

## Kiểm thử

`database/tests` kiểm tra ngày/địa điểm/sức chứa, khóa ngoại, stock guard, chống đặt trùng và check-in trùng, liên kết vé cùng sự kiện, số dư âm, thanh toán/webhook/ledger trùng, JSON thông báo, read-all, token thiết bị, inbox và quyền truy cập từng service. Các test chạy trực tiếp trên SQL Server, không thay bằng SQLite/InMemory.

`Start-Databases.ps1 -Verify` còn kiểm tra checksum chống sửa migration cũ và rollback DDL/history khi một migration cố ý thất bại. Test chỉ sửa bản sao trong thư mục tạm của container; không sửa file migration nguồn và không giữ bảng thử nghiệm.

Test database kiểm tra CHECK constraint và quyền truy cập. Bộ integration test [BookingService](../services/dotnet/FanHub.BookingService/README.md) kiểm tra đặt vé đồng thời, inbox/outbox, lifecycle và chữ ký QR trên SQL/Rabbit thật. Chưa kiểm thử gateway thanh toán, blockchain node hoặc push provider thật.

Tham khảo vận hành chính thức: [SQL Server container và sqlcmd](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker), [Compose healthcheck và thứ tự khởi động](https://docs.docker.com/compose/how-tos/startup-order/).

Notification migration `003_delivery_safety.sql` bổ sung binding/lease/TTL và booking version projection. [Notification integration tests](../services/dotnet/FanHub.NotificationService/README.md) kiểm tra API, transaction và delivery trên SQL/Rabbit thật; Firebase transport được giả lập trong test, không gửi tới thiết bị người dùng.
