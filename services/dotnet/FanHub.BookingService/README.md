# Booking Service — Chính Đức, task 33–42

.NET 10, SQL Server/EF Core, RabbitMQ/MassTransit. Chỉ đọc/ghi `fanhub_booking` bằng login riêng; dữ liệu Event, Staff, User đồng bộ bằng message. Không gọi HTTP hoặc truy vấn database service khác.

## Chạy local

Từ thư mục gốc, dùng PowerShell 7 và Docker Desktop:

```powershell
pwsh -File docker/chinhduc/Start-BookingService.ps1
pwsh -File docker/chinhduc/Test-BookingService.ps1
```

Swagger: http://127.0.0.1:5010/swagger. Readiness: `/health/ready`. Script chạy migration, build bằng bản sao tạm tránh lỗi OneDrive reparse point, rồi chờ container healthy. Port đổi bằng `BOOKING_HTTP_PORT` trong `.env`. Không xóa dữ liệu Event.

JWT: issuer `FanHub.IdentityService`, audience `FanHub.Services`, HMAC-SHA256, subject UUID. `JWT_SECRET` phải giống Identity; script lấy development key hiện có nếu chưa cấu hình. Không commit `.env`.

## 10 endpoint

Tất cả yêu cầu Bearer JWT. JSON snake_case, ngày UTC, UUID, giá decimal. Pagination: `page=1&page_size=20`, page_size tối đa 100. Không nhận giá/trạng thái thanh toán từ client.

| Task | Endpoint | Quyền/kết quả |
|---|---|---|
| 33 | POST `/api/v1/bookings/reserve` | 202 + request_id; consumer tạo vé sau |
| 34 | GET `/api/v1/bookings/status/{requestId}` | Chủ request; status, failure_code, booking_id |
| 35 | GET `/api/v1/bookings/my-tickets` | Vé của chủ hiện tại, phân trang |
| 36 | GET `/api/v1/bookings/{id}` | Chủ hiện tại hoặc organizer/Admin |
| 37 | POST `/api/v1/bookings/{id}/cancel` | Chủ vé, trước giờ bắt đầu; nhả tồn/đề nghị hoàn |
| 38 | POST `/api/v1/bookings/transfer` | Chủ vé Active; người nhận có User projection Active |
| 39 | POST `/api/v1/bookings/validate` | Staff CheckIn/Manager đúng event hoặc organizer/Admin |
| 40 | GET `/api/v1/bookings/event/{eventId}` | Organizer/Admin, vé đã bán, phân trang |
| 41 | GET `/api/v1/bookings/ticket-types/{eventId}` | Hạng active và tồn total − reserved − sold |
| 42 | POST `/api/v1/bookings/{id}/upgrade` | Chủ vé Active; cùng event, tier cao hơn |

Body reserve:

```json
{"event_id":"<UUID>","ticket_type_id":"<UUID>","quantity":1,"idempotency_key":"order-001"}
```

Transfer: `booking_id`, `to_user_id`, `idempotency_key`. Upgrade: `ticket_type_id` (hạng đích), `idempotency_key`. Key dài 1–100, chỉ chữ ASCII/số/`.`/`_`/`:`/`-`. Retry cùng key/nội dung trả kết quả cũ; đổi nội dung trả 409. Cancel không cần body. Đây là DTO nghiệp vụ thay cho mẫu title/description/status chung trong workbook.

Mỗi booking_id là một vé, thanh toán từng vé. Giữ chỗ 15 phút. Request Completed nghĩa là đã tạo vé, không có nghĩa đã thanh toán. Free ticket chuyển MintPending không tạo payment amount 0; không mint token giả.

## Transaction và lifecycle

Reserve/request/outbox commit cùng transaction. Consumer khóa event bằng `sp_getapplock`, kiểm tra tồn/sức chứa rồi tạo đủ vé; CHECK constraint và rowversion bảo vệ bổ sung. Capacity giảm từ Event chặn đặt thêm khi đầy, không xóa vé đã bán.

Reserved → MintPending khi Payment xác nhận → Active khi Blockchain xác nhận. TransferPending chỉ đổi chủ sau xác nhận Blockchain. UpgradePending giữ hạng đích; thanh toán chênh lệch thành công mới đổi hạng/nhả hạng cũ. Hết hạn/thất bại trả lại tồn giữ. Hủy vé đã trả tiền phát refund, chỉ sang Refunded khi nhận đủ xác nhận hoàn. Refund theo giao dịch gốc; chuyển NFT không chuyển người nhận tiền của giao dịch gốc.

Worker nhả giữ chỗ hết hạn, hủy vé khi Event Cancelled. TransferPending phải nhận kết quả Blockchain trước khi hủy; CheckedIn không tự hoàn. Thanh toán đến muộn, sai giá hoặc dư giao dịch được yêu cầu hoàn, không phục hồi vé hết hạn/oversell. Mint lỗi giữ MintPending kèm blockchain_error để vận hành retry.

## Message và tích hợp nhóm

Contract: `shared/FanHub.Shared.Contracts/Events/BookingIntegrationEvents.cs`. Cùng broker/vhost với nhóm; durable queue `fanhub-booking-projections`. Compose dùng credential chung cho development; triển khai cần quyền broker riêng cho publisher tin cậy.

- Nhận EventChanged, EventStaffChanged, ba Identity event; TicketTypeConfigured; BookingPaymentResult, BookingRefundResult; TicketMintResult, TicketTransferResult (tên type đều hậu tố `Event`). ReservationRequestedEvent là command nội bộ từ outbox.
- Phát BookingCreatedEvent, BookingChangedEvent (snapshot/version), BookingAttendeeChangedEvent cho Event; UpgradeRequestedEvent, BookingRefundRequestedEvent, TicketMintRequestedEvent, TicketTransferRequestedEvent.
- TicketTypeConfiguredEvent từ luồng quản trị tin cậy: event/type UUID, tên, giá, currency, tier, total, sale window, active, SourceVersion tăng dần. Workbook không phân công public API tạo hạng vé trong 33–42; không thêm route/seed giả. Event projection phải có trước. Tổng phân bổ không vượt capacity; không đổi giá/currency/tier khi có vé giữ/bán.

Inbox và mutation commit cùng transaction. MessageId bắt buộc, snapshot bỏ qua version cũ; payment chống trùng bằng TransactionId kể cả MessageId khác. Outbox at-least-once giữ MessageId khi retry; bên nhận cũng phải idempotent. Tối đa 20 lần publish với backoff. Theo dõi OUTBOX_MESSAGE chưa publish và queue `_error`; sửa nguyên nhân rồi replay message lỗi hoặc đặt lại attempt_count/next_attempt_at theo quy trình vận hành. Giữ nguyên MessageId khi replay.

Publisher thật từ quản trị hạng vé, Payment, Blockchain cần nối đúng contract. Nếu message đến trước dữ liệu phụ thuộc và hết retry, replay từ error queue sau khi đồng bộ nguồn. Không có API giả xác nhận thanh toán/NFT.

## QR task 39

Body: `booking_id`, `nft_token_id`, `user_id`, `timestamp` (Unix giây), `blockchain_signature` (`0x` + 65 byte r/s/v).

Xác minh **raw Keccak-256** UTF-8 của `nft_token_id|timestamp|user_id`, UUID lowercase D; ECDSA secp256k1, v=27/28. Không Ethereum personal-message prefix. Recovered address phải khớp public address `BLOCKCHAIN_SIGNER_ADDRESS` trong `.env`. TTL 30 giây, cho phép phía ký đi trước 5 giây. Vé Active đúng NFT/chủ hiện tại; check-in từ start−2h đến end. INSERT check-in và đổi trạng thái cùng transaction, chỉ vào một lần.

Đội Blockchain cần xác nhận định dạng và cung cấp signer address. **Chưa cấu hình signer thì trả 503**, không bỏ qua chữ ký. Không đặt private key ở Booking. Test dùng key ngẫu nhiên trong process test. Xác minh dựa trên signer tin cậy và owner projection, không truy vấn on-chain từng lần quét.

## Kiểm thử

9 nhóm integration test dùng SQL Server/RabbitMQ thật, database/login/vhost ngẫu nhiên và dọn riêng sau chạy. Bao phủ 10 route; 8 request tranh 3 vé; idempotency; payment/mint/cancel/refund; hết hạn/late payment; transfer; upgrade/late upgrade; QR thật/giả/hết hạn/check-in lặp; JWT/banned; snapshot cũ; Event Cancelled; capacity giảm; rollback vé/tồn khi outbox INSERT lỗi.

Chưa phải end-to-end với VNPay/MoMo hoặc blockchain node thật. Task 43–54 thuộc các nhánh service tiếp theo.
