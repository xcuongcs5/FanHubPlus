# Notification Service — Chính Đức, task 51–54

.NET 10, SQL Server riêng `fanhub_notification`, EF Core, RabbitMQ/MassTransit và Firebase Admin SDK 3.7.0 (FCM HTTP v1). Không đọc database service khác. SQL migrations là nguồn schema; app login chỉ DML. Task 43–50 tạm hoãn theo yêu cầu nhóm.

## Chạy local

```powershell
# Chỉ API/inbox: push bị tắt, không đánh dấu gửi thành công giả
pwsh -File docker/chinhduc/Start-NotificationService.ps1
# Có Firebase project ID trong docker/chinhduc/.env và file service account local
pwsh -File docker/chinhduc/Start-NotificationService.ps1 -WithFirebase
pwsh -File docker/chinhduc/Test-NotificationService.ps1
```

Swagger http://127.0.0.1:5012/swagger, port đổi qua `NOTIFICATION_HTTP_PORT`. JWT cùng issuer/audience/secret Identity. Secret account đặt tại `docker/secrets/firebase-service-account.json`, mount read-only vào `/run/secrets`; không copy vào image. `FIREBASE_PROJECT_ID` trong `.env`; tùy chọn `FIREBASE_CREDENTIALS_FILE` cho vị trí khác. Không dùng biến FIREBASE_PRIVATE_KEY dạng multiline.

`/health/live` kiểm tra process, `/health/ready` kiểm tra database/schema và bus; trả `push_enabled` để phân biệt chế độ local. Readiness không chứng minh IAM hoặc thiết bị nhận được FCM. Production không cho tắt Firebase/messaging; Swagger chỉ Development.

## API và frontend

Tất cả yêu cầu JWT Identity, user ID lấy từ subject, không nhận user_id từ client. JSON snake_case, UTC. 120 request/phút/user/instance, trả 429 khi quá giới hạn; cần rate limit tổng ở gateway khi scale. Request body tối đa 16 KiB.

| Task | Endpoint | Hành vi |
|---|---|---|
| 51 | GET `/api/v1/notifications?page=1&page_size=20&unread_only=false` | Chỉ user hiện tại; total, unread_count, items; page_size 1–100 |
| 52 | PUT `/api/v1/notifications/{id}/read` | 204, idempotent; ID của user khác trả 404 |
| 53 | PUT `/api/v1/notifications/read-all` | Chỉ thông báo của user tới thời điểm bắt đầu request; trả updated_count |
| 54 | POST `/api/v1/notifications/device-token` | Đăng ký/refresh/rotate hoặc logout; không trả token trong response |

```json
{"provider":"FCM","device_id":"<installation UUID>","token":"<FCM registration token>","is_active":true}
```

`device_id` là UUID ngẫu nhiên sinh một lần cho installation/browser profile, lưu bền vững và giữ qua logout/login. Dùng token từ Firebase client SDK, không dùng Firebase ID token đăng nhập, API key hoặc service account. Đồng bộ khi đăng nhập/token refresh và định kỳ; logout gọi cùng endpoint với `is_active:false` trước khi xóa JWT. User khác không được tắt registration hiện tại. Tối đa 20 thiết bị active/user; FCM token không được gắn hai device_id. Body chứa user_id hay field lạ bị từ chối.

Android, Web và iOS đều dùng **FCM registration token**. iOS cần APNs key/certificate cấu hình trong Firebase; raw APNs token/provider APNs chưa được route này hỗ trợ. Task 54 giữ tên device-token và hỗ trợ token legacy còn được Firebase hỗ trợ; SDK 3.7 đánh dấu Token deprecated để hướng tới FID. Chưa nhận FID, không nhầm FID với registration token. Khi frontend chuyển FID, cần mở rộng contract/storage có phân loại target và test tương ứng.

Push chỉ chứa nội dung chung và `data.notification_id`. Frontend nhận push rồi gọi API danh sách bằng JWT để lấy nội dung thật; không render message/data_json thành HTML chưa escape. Nếu tài khoản đã đổi thì không tìm thấy ID cũ. Client nên chống trùng theo notification_id, xử lý foreground/background và xin quyền notification. Web cần HTTPS/service worker và VAPID từ cùng Firebase project.

## Nhận sự kiện và delivery

- `NotificationRequestedEvent(UserId, Type, Title, Message, Data)` từ publisher nội bộ tin cậy, MessageId ổn định khi retry. Không có public API gửi thông báo tới user tùy ý.
- `BookingChangedEvent`: lưu version cuối, chỉ tạo thông báo khi trạng thái/owner thay đổi sang Active, Cancelled, Expired, RefundPending, Refunded, CheckedIn; bỏ qua snapshot cũ/lặp.
- Identity UserCreated/Updated/Banned đồng bộ local projection; profile update không tự bỏ ban.

Queue mặc định `fanhub-notification-projections`; topology typed MassTransit, không tự đổi thành exchange chuỗi `fanhub.events` cũ trong scaffold. Publisher phải dùng cùng broker/vhost/contracts. Inbox + notification + delivery commit cùng transaction. Delivery nhắm binding_id hiện tại của device. Rotate/logout/đổi user làm mất hiệu lực delivery cũ; không chuyển thông báo cũ sang tài khoản mới. Token quá 30 ngày không refresh bị bỏ qua. Không phát lại toàn bộ lịch sử khi đăng ký thiết bị mới.

Worker claim bằng UPDATE có điều kiện và lease 2 phút, kiểm tra lại binding/user/ban trước gửi; khóa device trong tối đa thời gian gửi để phối hợp registration. Mỗi instance xử lý tuần tự, nhiều instance phối hợp qua SQL; không tuyên bố đã load test quy mô lớn. Firebase request timeout 30 giây. Retry exponential backoff từ 60 giây + jitter, tối đa 12 attempt và tuổi delivery 24h; SDK cũng xử lý retry của provider. Process chết sau claim được reclaim sau lease.

`Unregistered` tắt token; `InvalidArgument`/`SenderIdMismatch` fail delivery nhưng không tự xóa token (có thể do cấu hình/payload). Lỗi tạm thời retry. Không log token/credential/provider diagnostic nguyên văn. `Sent` nghĩa là FCM đã chấp nhận, không chứng minh thiết bị đã hiển thị.

Không thể transaction nguyên tử giữa SQL và FCM: nếu FCM nhận rồi process chết trước commit, có thể gửi lặp. notification_id ổn định + Android/Web tag/APNs collapse-id hỗ trợ giảm trùng; client vẫn cần dedupe. Push chỉ chứa lời nhắc chung để hạn chế lộ nội dung nếu FCM giao sau logout.

## Triển khai production

`docker/chinhduc/notification-production.yml` là manifest độc lập, không khởi động SQL Developer/RabbitMQ local. Cần điền biến bắt buộc trong secret manager/CI environment và dùng image đã kiểm thử với immutable tag/digest. Container chạy non-root, filesystem read-only, drop capabilities, giới hạn tài nguyên, graceful stop 45 giây. Chưa deploy lên hạ tầng production của nhóm.

1. Chạy migration có checksum trên database production bằng tài khoản migration riêng; cấp app login DML trong database này, không DDL/cross-database. App không tự migrate.
2. SQL bắt buộc Encrypt=True, TrustServerCertificate=False và chứng chỉ hợp lệ; RabbitMQ AMQPS/5671 với hostname certificate đúng. App từ chối cấu hình SQL/Rabbit không an toàn trong Production.
3. TLS ingress/gateway phía trước cổng loopback 5012; không public port database/broker. Không bật forwarded headers tin mọi proxy. JWT issuer/audience/secret trùng Identity, quản lý/rotate qua secret manager; bảo vệ database backup/token at rest bằng mã hóa của hạ tầng.
4. Ưu tiên Workload Identity/ADC. Manifest có secret mount ADC configuration/service account; không dùng owner/editor role. Bật FCM HTTP v1 API, cấp quyền gửi FCM tối thiểu ở đúng project. Trên nền tảng có ADC tự động có thể bỏ file mount/GOOGLE_APPLICATION_CREDENTIALS. Không đưa credential vào image/Git.
5. Dùng RabbitMQ account/vhost/permissions riêng cho Notification và publisher tin cậy; Compose local có credential chung chỉ để development. Cấu hình quorum queue/HA, backup SQL, retention/error-queue access theo chính sách vận hành.
6. Kiểm chứng bằng thiết bị test được chủ thiết bị cho phép: login, đăng ký token, phát command nội bộ MessageId mới, kiểm tra một thông báo API và FCM; logout/đổi tài khoản rồi thử lại. Chưa có FCM token thiết bị nên chưa kiểm chứng bước này, quyền IAM gửi thật hoặc APNs/Web frontend.

Không tự xóa lịch sử để áp đặt retention. Nhóm cần thống nhất thời hạn lưu notification/inbox/delivery; khi purge phải xóa delivery trước notification theo batch, giữ inbox đủ lâu để chống replay. Cảnh báo backlog/Failed, Rabbit `_error`, tỷ lệ 429/5xx và sai cấu hình Firebase; không lấy readiness thay cho monitoring gửi.

## Vận hành và test

`database/notification/operations.sql.example` là truy vấn chẩn đoán chỉ đọc. Metric meter `FanHub.NotificationService`, counter `notification.push.outcomes` với outcome; cần nối OpenTelemetry/exporter của hạ tầng, chưa có dashboard mặc định. Theo dõi pending già nhất, Failed tăng và lease kẹt. Sửa nguyên nhân rồi replay Rabbit error message giữ MessageId; trước khi retry Failed delivery phải kiểm tra binding/TTL và khả năng FCM đã nhận để tránh lặp. Không sửa Sent thành Pending hàng loạt.

Integration tests dùng database/login/vhost ngẫu nhiên trên SQL/Rabbit thật, dọn riêng sau test. Sender worker giả lập không gửi tới người dùng. Test Firebase riêng chạy SDK thật với HTTP transport giả lập, kiểm tra payload và parsing lỗi. Bao phủ 4 route, ownership, read idempotency, pagination, inbox/replay/rollback, version ordering, token rotation/account switch/logout, đăng ký đồng thời/giới hạn thiết bị, claim/reclaim nhiều worker, retry/invalid token/expiry/ban và từ chối cấu hình production không có TLS.

Tài liệu chính thức: [Firebase Admin gửi message](https://firebase.google.com/docs/cloud-messaging/send/admin-sdk), [quản lý registration](https://firebase.google.com/docs/cloud-messaging/manage-tokens), [xử lý lỗi Admin SDK](https://firebase.google.com/docs/reference/admin/error-handling).
