# Payment & Wallet — task 43–50

.NET 10, SQL Server riêng `fanhub_payment`, EF Core, MassTransit/RabbitMQ, JWT, VNPAY 2.1.0 HMAC-SHA512. Docker chạy non-root. SQL scripts trong `database/payment` và `database/common` là schema authority; không dùng EnsureCreated/EF migrations. Không truy vấn database hoặc gọi HTTP đồng bộ sang Booking.

## Chạy local

1. Copy `docker/chinhduc/payment-providers.example.json` sang **`docker/secrets/payment-providers.json`**, điền sandbox. File này bị Git và Docker build context bỏ qua, mount read-only. Không đưa secret vào appsettings, log hoặc frontend.
2. Chạy `pwsh -File docker/chinhduc/Start-PaymentService.ps1`: giữ `.env`, migrate database, build/chờ healthy. Docker Desktop dùng Linux containers.
3. Swagger `http://localhost:5011/swagger`; `/health/ready`, `/health/live`. JWT cùng issuer/audience/key với Identity. `PAYMENT_HTTP_PORT` đổi port local.
4. `pwsh -File docker/chinhduc/Test-PaymentService.ps1`: SQL/RabbitMQ thật, database/login/vhost ngẫu nhiên, dọn đúng tài nguyên test.

## API

Body/response snake_case, từ chối trường JSON ngoài DTO. Subject UUID từ JWT; không nhận user_id hoặc giá vé từ client. Tài khoản bị khóa bị từ chối. Rate limit 120 request/phút/user/replica; ingress cần limit toàn cụm. Task 44–45 công khai ở HTTP, xác thực provider riêng.

| Task | API | Input / kết quả |
|---|---|---|
| 43 | POST `/api/v1/payments/create-intent` | `booking_id`, `upgrade_id` tùy chọn, `provider` VNPay/Wallet, `idempotency_key`. Trả transaction_id, amount, currency, status, checkout_url, expires_at. |
| 44 | GET **và** POST `/api/v1/payments/webhook/vnpay` | GET query theo VNPAY; POST query hoặc form-urlencoded theo workbook. Trả đúng `RspCode`/`Message`. |
| 45 | POST `/api/v1/payments/webhook/momo` | JSON camelCase MoMo, HMAC-SHA256, partner/order/request/amount. 204 đã xử lý, 401 chữ ký sai, 503 chưa cấu hình. |
| 46 | GET `/api/v1/payments/{bookingId}/status` | payments/refunds của chính người trả tiền, gồm nâng hạng. |
| 47 | POST `/api/v1/payments/refund` | `transaction_id`, `idempotency_key`, `reason`; hủy vé trước. 202 chứa refund/status, không có nghĩa đã hoàn tiền. |
| 48 | GET `/api/v1/wallets/me` | wallet_id, balance, currency, status; chưa có ví trả 0/NotCreated, GET không tạo row. |
| 49 | POST `/api/v1/wallets/deposit` | `amount` VND nguyên 10.000–100.000.000, `provider=VNPay`, `idempotency_key`; trả checkout, chưa cộng tiền. |
| 50 | GET `/api/v1/wallets/transactions?page=1&page_size=20` | Sổ cái của chính user, thứ tự ổn định; page_size 1–100. |

`GET /api/v1/payments/return/vnpay` chỉ hiển thị kết quả xác minh chữ ký, **không settle/cộng tiền/phát message**. Frontend kiểm tra booking qua task 46. Deposit: retry task 49 cùng key/body trả transaction/status cũ; ledger chỉ chứa khoản đã ghi nhận.

Task 45 có handler/test chữ ký và idempotency, nhưng **chưa có sandbox MoMo**; MoMo tắt mặc định. Create checkout/refund chủ động hiện chỉ VNPay và ví nội bộ; tạo MoMo trả `provider_unavailable`. Muốn chạy trọn luồng MoMo cần adapter create/query/refund và SIT, không chỉ bật cờ. Không coi callback giả lập là live MoMo đã hoạt động.

## Quy tắc nghiệp vụ

- Mỗi vé thanh toán riêng; free ticket do Booking xử lý. VND nguyên dương, amount×100 tối đa 12 chữ số VNPAY. Không lưu PAN/CVV/OTP.
- Booking/Upgrade projection quyết định payer, giá, currency, expiry. Nâng hạng theo chủ vé hiện tại; giao dịch gốc và hoàn tiền gốc vẫn của người trả tiền ban đầu.
- Transaction-scoped `sp_getapplock`, rowversion và unique index bảo vệ idempotency/payment/wallet/provider transaction. Balance, ledger, webhook, outbox commit trong cùng SQL transaction.
- Một checkout duy nhất cho mỗi booking/upgrade, kể cả attempt thất bại/chưa rõ kết quả. Cùng key/body lấy intent cũ; đổi key/body không tạo URL thứ hai có thể thu tiền trùng. Sau thất bại muốn trả lại cần reservation mới; đây là chính sách thận trọng của bản này.
- IPN xác minh HMAC constant-time, merchant/reference/amount/currency/mã kết quả/provider transaction; từ chối query/form trùng key. VND mặc định khi provider không gửi currency. Mã 00 thành công, 02 trùng, 01 không có order, 04 sai tiền, 97 chữ ký sai, 99 lỗi xử lý để retry.
- Chỉ ResponseCode=00 **và** TransactionStatus=00 xác nhận paid. Callback thất bại không ghi đè success; late success sau failure vẫn ghi nhận.
- Late payment sau hết hạn vẫn là tiền thực nhận: phát BookingPaymentResult để Booking áp dụng hoặc yêu cầu hoàn, không tự phục hồi vé đã nhả tồn.
- Deposit chỉ cộng ví sau callback/querydr hợp lệ. Hai lần tiêu đồng thời không làm âm số dư. Ví Frozen/Closed chặn tiêu/nạp mới; khoản nạp đã thu vẫn được ghi có để không mất nghĩa vụ với chủ ví.
- Ledger chỉ INSERT/SELECT bằng app login, SQL DENY UPDATE/DELETE. Refund ghi bút toán bù.

## Hoàn tiền / đối soát

**Hoàn toàn bộ transaction**, phù hợp BookingRefundResult chỉ có bool. Chưa hỗ trợ refund một phần, rút ví, tự hoàn deposit. Public refund chỉ sau hủy vé, không hoàn vé đang sử dụng. Consumer BookingRefundRequested kiểm tra transaction/booking/amount/currency, chống trùng theo transaction kể cả MessageId mới.

Refund ví atomic ledger/outbox. VNPAY: Pending → worker claim Processing, commit trước gọi mạng. RequestId ổn định theo refund_id, type=02, amount×100, TransactionDate=CreateDate gốc GMT+7. Kiểm tra chữ ký phản hồi và các trường khớp; API 00 nhưng transaction processing chưa được phát RefundSucceeded.

Timeout/crash/phản hồi không xác minh giữ Processing/last_error, **không tự POST refund lại**. querydr mỗi 30 phút; chỉ full-refund đúng tiền đã thành công mới chốt. Thành công của thanh toán gốc không chứng minh hoàn tiền thành công. Nếu provider không trả trạng thái hoàn qua querydr, vận hành cần đối chiếu Merchant Admin/VNPAY và xử lý có kiểm soát; không reset Pending tùy tiện.

Payment pending/failed querydr mỗi 5 phút, tối đa 100 lần; hết lượt giữ dữ liệu để vận hành, không giả định chưa thu tiền. IPN vẫn có thể chốt sau đó. `database/payment/operations.sql.example` theo dõi refund treo, transaction cần đối soát, outbox lỗi, lệch balance/ledger; cần kết nối các điều kiện này với cảnh báo thực tế.

## Messaging

Nhận BookingCreatedEvent, BookingChangedEvent, UpgradeRequestedEvent, BookingRefundRequestedEvent và ba Identity events. Queue `fanhub-payment-projections`. Snapshot bỏ version cũ, inbox/mutation cùng transaction. Upgrade quote bất biến. Phụ thuộc chưa đến sau bounded retry vào `_error`; replay đúng MessageId sau khi khôi phục nguồn.

Phát BookingPaymentResultEvent và BookingRefundResultEvent qua outbox lease/publisher-confirm, at-least-once, tối đa 20 lần backoff. Booking đã có consumers đúng contract. Production cần broker credential/ACL riêng cho publisher tin cậy; compose dùng shared credential development.

## Sandbox / production

Cập nhật theo nhóm ngày 25/09/2026: **tạm hoãn task 45 MoMo** do chưa đăng ký được sandbox; tạm hoãn nghiệm thu trên Merchant/SIT VNPAY do cổng đăng nhập chưa sử dụng được. Không thay xác thực chữ ký bằng mock trong runtime và không đánh dấu SIT đã đạt. Các chức năng VNPAY/ví, kiểm thử tự động và worker đối soát được giữ nguyên.

Đã chuẩn bị tunnel sandbox tùy chọn: `pwsh -File docker/chinhduc/Start-VnPayTunnel.ps1`. Proxy chỉ mở IPN và Return, không mở Swagger/API ví. Script lưu URL trong `docker/secrets/payment-tunnel.json`, cập nhật ReturnUrl và restart Payment. Quick Tunnel có thể đổi hostname khi restart, chỉ dùng thử nghiệm; production cần domain HTTPS ổn định. Tunnel hiện đã dừng theo quyết định hoãn test. Khi tiếp tục, chạy script và đăng ký URL IPN mới với VNPAY; không dùng lại hostname cũ khi chưa kiểm tra.

VNPAY phải gọi được **HTTPS công khai** `/api/v1/payments/webhook/vnpay`; đăng ký URL với VNPAY/SIT. localhost chỉ chạy local/ReturnUrl dev, không phải IPN từ Internet. Hiện chưa có public domain. Đặt ReturnUrl/ServerIp đúng môi trường. Sau proxy, cấu hình `ReverseProxy:KnownProxies` đúng IP; không tin X-Forwarded-For tùy ý. Ingress giữ nguyên query, không log bearer/checksum.

`payment-production.yml` bổ sung SQL/Rabbit TLS, hostname, endpoint production. Secret JSON load sau env nên phải thay bằng **bộ merchant production đầy đủ**; không dùng sandbox trong Production. Startup từ chối SQL không xác minh chứng chỉ, Rabbit không TLS, tắt worker, sandbox Production. Trước vận hành cần HTTPS ingress, secret manager, broker ACL, backup/PITR, cảnh báo, clock sync và SIT thực tế. Docker Development healthy không chứng minh đã đủ các điều kiện đó.

## Kiểm thử 25/09/2026

**45 cases pass**: 22 model/data có sẵn, 23 case mới cho API/concurrency/SQL+RabbitMQ, worker refund, giao thức HMAC và cấu hình production. Provider network giả lập trong suite; không dùng secret thật.

Bao phủ giá tin cậy/idempotency; JWT/ownership/banned; chữ ký/merchant/amount/trùng/sai thứ tự; Return không settle; ledger bất biến/tiêu ví đồng thời; cancel/refund ví; late payment; upgrade owner/snapshot cũ; rollback outbox lỗi; MoMo callback; timeout refund không POST lại; chỉ chốt refund sau đối soát đúng loại; RFC4231 HMAC và phản hồi giả mạo.

Docker build/chạy healthy. Credential sandbox thật đã đưa đến trang **Chọn phương thức thanh toán (Test)**. Smoke tạo deposit chưa thanh toán, không nhập thẻ/giả IPN/cộng tiền. `Test-VnPaySandboxCheckout.ps1 -BearerToken <JWT>` dùng token dev của bạn. **Chưa hoàn tất thanh toán thẻ + public IPN + refund thật trên sandbox**, cần URL IPN và nghiệm thu SIT.

Nguồn: [VNPAY thanh toán/IPN](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html), [VNPAY query/refund](https://sandbox.vnpayment.vn/apis/docs/truy-van-hoan-tien/querydr%26refund.html), [MoMo notification](https://developers.momo.vn/v3/vi/docs/payment/api/collection-link/).
