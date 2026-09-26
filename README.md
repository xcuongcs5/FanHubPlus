# FanHubPlus - Hướng dẫn Cài đặt & Vận hành

FanHubPlus là hệ thống nền tảng tương tác giữa Fan và Thần tượng, được thiết kế theo kiến trúc **Microservices** hiện đại, kết hợp với Frontend là **Next.js**. 


EventService (task 21–32): [API, Docker và integration tests](services/dotnet/FanHub.EventService/README.md).
BookingService (task 33–42): [API, Docker, message contracts và integration tests](services/dotnet/FanHub.BookingService/README.md).

NotificationService (task 51–54): [API, Firebase, Docker và vận hành production](services/dotnet/FanHub.NotificationService/README.md). Payment/Wallet (43–50) tạm hoãn chờ sandbox.

Tài liệu này sẽ hướng dẫn các thành viên trong team cách khởi động dự án và chạy các bộ kiểm thử một cách chuẩn xác nhất, tránh tình trạng xung đột môi trường.

---

## 🛠 Yêu cầu hệ thống (Prerequisites)
Trước khi bắt đầu, đảm bảo máy bạn đã cài đặt các công cụ sau:
- **Docker Desktop** (Đang mở và chạy ở chế độ *Linux containers*)
- **.NET 10 SDK** (Dành cho việc biên dịch và chạy Unit Test cục bộ)
- **PowerShell 7 (`pwsh`)** hoặc **Windows PowerShell 5.1**
- **Node.js 18+** (Nếu bạn làm việc với Frontend Next.js)

---

## 🚀 Hướng dẫn khởi động Backend (Microservices)

Hệ thống Backend (Identity, Event, Booking, Database) đã được tự động hóa bằng các script PowerShell tiện dụng tại thư mục `docker/chinhduc/`.

### Bước 1: Khởi động Hạ tầng Database & Message Broker
Tất cả các service đều dùng chung một cụm SQL Server, RabbitMQ và Redis để tiết kiệm RAM. Bạn **KHÔNG** sử dụng file `docker-compose.yml` cũ ở ngoài thư mục gốc.

1. Mở Terminal (PowerShell) tại thư mục gốc `FanHubPlus`.
2. Chạy lệnh:
   ```powershell
   .\docker\chinhduc\Start-Databases.ps1 -Verify
   ```
   *(Script sẽ tự động tạo file `.env` chứa mật khẩu an toàn, tải các image Database, chạy các SQL Migration tạo bảng và kiểm tra kết nối).*

### Bước 2: Khởi động các Microservices
Mỗi service được đóng gói độc lập. Bạn muốn test hoặc làm việc ở service nào thì chạy lệnh của service đó:

- **Khởi động Identity Service** (Quản lý User, Xác thực JWT):
  ```powershell
  .\docker\chinhduc\Start-IdentityService.ps1
  ```
  *(Swagger: `http://localhost:5001/swagger`)*

- **Khởi động Event Service** (Quản lý Sự kiện):
  ```powershell
  .\docker\chinhduc\Start-EventService.ps1
  ```
  *(Swagger: `http://localhost:5004/swagger`)*

- **Khởi động Booking Service** (Quản lý Đặt vé):
  ```powershell
  .\docker\chinhduc\Start-BookingService.ps1
  ```
  *(Swagger: `http://localhost:5010/swagger`)*

---

## 🌐 Hướng dẫn kết nối Frontend (Next.js)

Nếu bạn chạy project `techwiz-frontend`, hãy đảm bảo Frontend trỏ đúng vào cổng của Identity Service hiện tại:

1. Trong thư mục gốc của frontend, tạo hoặc mở file `.env.local`
2. Thêm dòng cấu hình sau:
   ```env
   IDENTITY_SERVICE_URL=http://127.0.0.1:5001
   ```
3. Chạy server Frontend:
   ```bash
   npm run dev
   ```

---

## 🧪 Hướng dẫn chạy Kiểm thử (Testing)

Dự án hiện có 2 loại Test chính. **TUYỆT ĐỐI LƯU Ý** sự khác biệt cách chạy của từng loại:

### 1. Unit Tests (Kiểm thử Đơn vị) - Ví dụ: IdentityService
Các Unit Test sử dụng dữ liệu giả lập (Mocking) nên chạy độc lập không cần Database.
- **Cách chạy qua Visual Studio:** Mở file `FanHubPlus.slnx`, vào *Test > Test Explorer*, bấm nút **Run All**.
- **Cách chạy bằng Terminal:**
  ```powershell
  dotnet test services/dotnet/FanHub.IdentityService.UnitTests
  ```

### 2. Integration Tests (Kiểm thử Tích hợp) - Ví dụ: Event, Booking
Các bài Integration Test của `EventService` và `BookingService` **phải kết nối với SQL Server và RabbitMQ thật**. Nếu bạn ấn "Run All" trong Visual Studio cho 2 project này, chúng sẽ bị **LỖI (Failed)** ngay lập tức do thiếu môi trường.

**Cách chạy đúng:** Phải dùng script PowerShell thiết kế riêng để script tự động tạo Database nháp, nạp biến môi trường `.env`, chạy test và tự dọn dẹp sau khi chạy xong.
- Chạy test Event:
  ```powershell
  .\docker\chinhduc\Test-EventService.ps1
  ```
- Chạy test Booking:
  ```powershell
  .\docker\chinhduc\Test-BookingService.ps1
  ```

---

## 🧹 Dừng hệ thống khi kết thúc công việc
Khi không làm việc nữa, để giải phóng RAM mà **không làm mất dữ liệu** trong Database, chạy lệnh sau:
```powershell
docker compose --env-file docker/chinhduc/.env -f docker/chinhduc/compose.yml down
```

---
*Tài liệu chi tiết hơn về các quy ước Database và giao tiếp Messaging xem tại thư mục `docs/` và `database/README.md`.*

