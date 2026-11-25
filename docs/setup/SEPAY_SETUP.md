# SePay: tự duyệt đơn VietQR

Tài liệu dành cho người (hoặc AI agent) cấu hình nốt SePay. Code BE đã xong trên nhánh `Danh`; chỉ còn cấu hình và kiểm thử với tài khoản SePay thật.

## 1. Đã có trong code

| Thành phần | File | Ghi chú |
|---|---|---|
| Endpoint webhook | `src/PanelForge.API/Controllers/SePayWebhookController.cs` | `POST /api/payments/sepay/webhook`, `[AllowAnonymous]`, xác thực bằng header `Authorization: Apikey <key>` (so sánh constant-time) |
| Xử lý nghiệp vụ | `src/PanelForge.Application/Features/Payments/Commands/ProcessSePayWebhook/ProcessSePayWebhookCommand.cs` | Tìm mã `PFxxxxxx` trong `code` → `content` → `description`, đối chiếu tài khoản và số tiền, rồi duyệt |
| Kích hoạt quyền lợi dùng chung | `src/PanelForge.Application/Features/Payments/Common/PaymentFulfillment.cs` | Nạp ví / mở chương / gia hạn VIP. Duyệt tay (`ApprovePaymentCommand`) và webhook cùng gọi |
| Domain | `PaymentTransaction.Approve(Guid?)`, `PaymentTransaction.Reopen()` | `ConfirmedByUserId = null` nghĩa là SePay tự duyệt. Không có migration |
| Hiển thị | `GetWorkspacePaymentsQuery` | Cột người duyệt ghi `SePay (tự động)` |
| Test | `test/PanelForge.UnitTests/Application/Payments/SePayWebhookTests.cs`, `test/PanelForge.UnitTests/API/SePayWebhookControllerTests.cs` | Duyệt nạp ví, mở chương, gửi trùng, thiếu tiền, sai tài khoản, không có mã, đơn Expired, tài khoản ảo (`subAccount`), hai request đồng thời, xác thực API key (`503`/`401`/`200`) |
| Chống duyệt hai lần | `PaymentTransactionConfiguration`, `ReaderWalletConfiguration` | `updated_at` là concurrency token: hai request cùng duyệt một đơn (SePay gửi lại, hoặc Producer duyệt tay đúng lúc) thì lần lưu sau ra `409`, SePay gửi lại và gặp `IgnoredAlreadyProcessed`. Số dư ví cũng không bị ghi đè khi nạp/trừ cùng lúc |

Mobile đã polling `GET /api/wallet/transactions/{orderCode}/status` mỗi 3 giây nên khi webhook duyệt, app tự thấy đơn hoàn tất. FE và mobile không cần sửa.

### Quy tắc duyệt

| Điều kiện | Kết quả (`outcome` trong response) |
|---|---|
| `transferType` khác `in` | `IgnoredNotIncoming` |
| Không tìm thấy `PF` + 6 số | `IgnoredNoOrderCode` |
| Mã không tồn tại trong DB | `IgnoredOrderNotFound` |
| Đơn đã Approved/Rejected (SePay gửi lại) | `IgnoredAlreadyProcessed` |
| `accountNumber` và `subAccount` đều khác `BankAccountNo` của đơn | `IgnoredAccountMismatch` |
| `transferAmount` nhỏ hơn số tiền đơn | `IgnoredAmountTooLow`, đơn giữ Pending để Producer duyệt tay |
| Khớp hết (đơn Pending hoặc Expired) | `Approved` |

Mọi trường hợp trên đều trả `200 {"success": true, ...}` để SePay không gửi lại. Chưa cấu hình key thì trả `503`, sai key thì `401`, đụng một lần lưu khác cùng lúc thì `409` (SePay tự gửi lại).

## 2. Các bước còn lại

### Bước 1. Cấu hình trên SePay (người có tài khoản SePay làm)
1. Đăng nhập https://my.sepay.vn, liên kết tài khoản ngân hàng nhận tiền.
2. Vào **Tích hợp WebHooks → Thêm webhooks**:
   - Sự kiện: **Có tiền vào**.
   - Chọn đúng tài khoản ngân hàng vừa liên kết.
   - URL: `https://<domain>/api/payments/sepay/webhook` (khi chạy local cần tunnel, xem bước 4).
   - **Bỏ chọn** "Bỏ qua nếu nội dung giao dịch không có Code thanh toán": server tự tìm mã `PFxxxxxx`. Nếu muốn bật, phải cấu hình tiền tố code thanh toán trên SePay là `PF` và độ dài 8 ký tự.
   - Kiểu chứng thực: **API Key**. Sinh key ngẫu nhiên, ví dụ `openssl rand -hex 32`.
   - Gọi lại khi lỗi (retry): bật.

### Bước 2. Đặt key vào server (không commit key)
- Production (VM, Docker Compose): tên biến trong `.env` phụ thuộc file compose đang dùng, ghi sai tên thì key không vào container và webhook trả `503`.
  - Dùng `docker-compose.yml` của repo (dòng `SePay__WebhookApiKey=${SEPAY_WEBHOOK_API_KEY:-}`): `.env` cạnh file compose ghi
    ```
    SEPAY_WEBHOOK_API_KEY=<key>
    ```
  - Dùng compose prod có `env_file: /opt/panelforge/.env` (nạp thẳng vào container): ghi
    ```
    SePay__WebhookApiKey=<key>
    ```
  Sau đó `docker compose up -d api`. Kiểm tra: gọi webhook không có header phải ra `401` (ra `503` nghĩa là key chưa vào).
- Local: `dotnet user-secrets set "SePay:WebhookApiKey" "<key>" --project src/PanelForge.API` hoặc biến môi trường `SePay__WebhookApiKey`. Không ghi key vào `appsettings*.json`.

### Bước 3. Khai tài khoản nhận tiền cho từng Studio
QR của đơn dùng tài khoản trong **Cài đặt ngân hàng** của Studio (`PUT /api/workspaces/{id}/payments/bank-settings`). Số tài khoản này **phải trùng** tài khoản đã liên kết SePay, nếu không webhook trả `IgnoredAccountMismatch`.

Studio chưa khai thì QR dùng tài khoản mặc định trong cấu hình `SePay:DefaultBank` (đơn không có ngữ cảnh Studio rơi vào Studio tên chứa "komaforge"). Màn Cài đặt ngân hàng của Producer cũng hiện tài khoản này khi Studio chưa khai. Giá trị mặc định trong code là tài khoản **giả** chỉ cho dev (`970422` / `MB Bank` / `0123456789` / `KOMAFORGE STUDIO`); môi trường không phải Development mà còn dùng tài khoản giả thì API ghi cảnh báo lúc khởi động.

Production: đặt tài khoản thật đã liên kết SePay (không cần khai cho từng Studio):

| Khoá cấu hình | Biến trong `.env` cạnh `docker-compose.yml` của repo | Biến khi compose dùng `env_file` |
|---|---|---|
| `SePay:DefaultBank:Bin` (mã BIN, vd `970436` Vietcombank) | `SEPAY_DEFAULT_BANK_BIN` | `SePay__DefaultBank__Bin` |
| `SePay:DefaultBank:Name` | `SEPAY_DEFAULT_BANK_NAME` | `SePay__DefaultBank__Name` |
| `SePay:DefaultBank:AccountNo` | `SEPAY_DEFAULT_BANK_ACCOUNT_NO` | `SePay__DefaultBank__AccountNo` |
| `SePay:DefaultBank:AccountName` (in hoa, không dấu) | `SEPAY_DEFAULT_BANK_ACCOUNT_NAME` | `SePay__DefaultBank__AccountName` |

Đổi tài khoản mặc định chỉ ảnh hưởng đơn tạo sau đó; đơn Pending cũ vẫn mang tài khoản cũ (webhook đối chiếu theo tài khoản ghi trên đơn).

### Bước 4. Kiểm thử

Hướng dẫn đầy đủ (local, giả lập, SePay Test mode, gỡ lỗi): `SEPAY_TESTING.md`. Tóm tắt:
1. Gọi thử không cần chuyển tiền thật:
   ```bash
   curl -X POST https://<domain>/api/payments/sepay/webhook \
     -H "Content-Type: application/json" -H "Authorization: Apikey <key>" \
     -d '{"id":1,"gateway":"MBBank","accountNumber":"<STK>","transferType":"in","transferAmount":2000,"content":"PF123456","code":null,"subAccount":null}'
   ```
   Mong đợi: `200 {"success":true,"outcome":"IgnoredOrderNotFound"}`.
2. Tạo đơn nạp 2.000đ từ app mobile, dùng mã đơn thật thay `PF123456` trong lệnh trên (đúng STK, đúng số tiền) → `outcome: Approved`, ví người đọc tăng 2.000đ, màn Thanh toán của Producer ghi "SePay (tự động)".
3. Test thật: chuyển khoản 2.000đ đúng nội dung. Xem log SePay (my.sepay.vn → Nhật ký webhook) và log API, có dòng `SePay webhook <id> (<ref>) order PFxxxxxx: Approved`.
4. Local: dùng tunnel (`cloudflared tunnel --url http://localhost:5044` hoặc ngrok) rồi trỏ URL webhook SePay tạm tới tunnel.

## 3. Giới hạn đã biết

- Người đọc chuyển 2 lần cho cùng mã đơn: lần sau bị bỏ qua (`IgnoredAlreadyProcessed`), Producer phải tự hoàn tiền. Chưa lưu `id` giao dịch SePay.
- Chuyển thừa tiền vẫn duyệt theo giá đơn, phần thừa không cộng vào ví.
- Đơn Expired (độc giả mở lại popup QR tạo đơn mới) mà tiền vẫn vào đúng thì vẫn được duyệt, vì tiền đã thực nhận.
- Endpoint không có rate limit riêng; đã chặn bằng API key.
