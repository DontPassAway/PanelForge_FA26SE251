# Test thanh toán SePay

Hướng dẫn test luồng VietQR → SePay webhook → tự duyệt đơn, từ máy local tới SePay thật. Cấu hình production xem `SEPAY_SETUP.md`.

Kết quả đúng ở mọi mức test: đơn chuyển `Pending → Approved`, ví độc giả tăng đúng số tiền (hoặc mở chương / gia hạn VIP), màn Thanh toán ghi người duyệt "SePay (tự động)", và app mobile tự báo thành công (nó polling `GET /api/wallet/transactions/{orderCode}/status` mỗi 3 giây).

## 0. Chuẩn bị

| Việc | Cách làm |
|---|---|
| Postgres + DB `panelforge_dev` | Như khi chạy BE bình thường |
| Tài khoản dev | `DevUserSeeder` (chỉ môi trường Development), mật khẩu chung là `DevUserSeeder.DefaultPassword`. Dùng `reader@panelforge.com` để tạo đơn, `admin@panelforge.com` để cấu hình ngân hàng |
| API key webhook | Chạy `powershell -ExecutionPolicy Bypass -File scripts/sepay-set-key.ps1`. Script sinh key, lưu vào user-secrets (`SePay:WebhookApiKey`) và copy vào clipboard, không in ra màn hình. Chạy lại = key mới, phải dán lại trên SePay |
| Chạy BE | `dotnet run --project src/PanelForge.API --launch-profile http` (cổng 5044). Khởi động lại BE sau khi đổi key |

> Chạy script và BE trong **terminal thật** của máy. Nếu dùng agent có sandbox, file user-secrets do agent ghi có thể chỉ nằm trong sandbox, BE chạy ngoài sandbox sẽ không thấy key (webhook trả 503).

### Đặt tài khoản nhận tiền cho Studio
Webhook chỉ duyệt khi tiền vào đúng số tài khoản ghi trên QR của đơn. Studio chưa khai ngân hàng thì QR dùng tài khoản giả `0123456789`, webhook sẽ trả `IgnoredAccountMismatch`.

Khai trên web (trang Thanh toán của Studio → Cài đặt ngân hàng, cần vai trò **Publisher** hoặc **Admin**), hoặc bằng API:

```bash
A=http://localhost:5044/api; J=$(mktemp)
curl -s -c $J -X POST $A/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@panelforge.com","password":"<mật khẩu dev>"}'
curl -s -b $J $A/workspaces            # lấy id các Studio
curl -s -b $J -X PUT $A/workspaces/<workspaceId>/payments/bank-settings \
  -H "X-Requested-With: XMLHttpRequest" -H "Content-Type: application/json" \
  -d '{"bankBin":"970422","bankName":"MB Bank","bankAccountNo":"<STK đã liên kết SePay>","bankAccountName":"<TÊN CHỦ TK>"}'
```

Lưu ý khi gọi API bằng curl:
- Request đổi dữ liệu (POST/PUT/DELETE) bằng cookie phải có header `X-Requested-With` (chống CSRF), thiếu thì 401.
- `WorkspacePaymentsController` chỉ cho `Publisher,Admin`; tài khoản Producer nhận 403.
- Mã BIN ngân hàng: MB Bank `970422`, Vietcombank `970436`, VietinBank `970415`, Techcombank `970407`, BIDV `970418`, ACB `970416`.

## 1. Unit test (không cần gì ngoài .NET)

```bash
dotnet test test/PanelForge.UnitTests --filter "FullyQualifiedName~Payments"
```
`SePayWebhookTests` (6 test) phủ: duyệt nạp ví, mở chương, gửi trùng, thiếu tiền, sai tài khoản, không có mã / chuyển ra / mã không tồn tại.

## 2. Test local, giả lập SePay bằng curl (không tốn tiền)

```bash
A=http://localhost:5044/api; J=$(mktemp); H="X-Requested-With: XMLHttpRequest"
K='<key, dán từ clipboard>'

# 1) Đăng nhập độc giả và tạo đơn nạp 2.000đ vào Studio đã khai ngân hàng
curl -s -c $J -X POST $A/auth/login -H "Content-Type: application/json" \
  -d '{"email":"reader@panelforge.com","password":"<mật khẩu dev>"}'
curl -s -b $J $A/wallet/me                                   # ghi lại balanceVnd
curl -s -b $J -X POST $A/wallet/topup -H "$H" -H "Content-Type: application/json" \
  -d '{"amountVnd":2000,"workspaceId":"<workspaceId>"}'      # ghi lại orderId (PFxxxxxx) và accountNumber

# 2) Giả làm SePay báo tiền vào
curl -s -X POST $A/payments/sepay/webhook -H "Content-Type: application/json" \
  -H "Authorization: Apikey $K" \
  -d '{"id":9001,"gateway":"MBBank","accountNumber":"<accountNumber>","transferType":"in","transferAmount":2000,"content":"MBVCB.1 <orderId> FT123","referenceCode":"TEST"}'
# → {"success":true,"outcome":"Approved"}

# 3) Kiểm tra
curl -s $A/wallet/transactions/<orderId>/status              # status: Approved, isCompleted: true
curl -s -b $J $A/wallet/me                                   # balanceVnd tăng 2000
```

Các ca nên thử thêm (đổi payload ở bước 2):

| Thay đổi | `outcome` mong đợi | Đơn |
|---|---|---|
| Gửi lại y hệt lần 2 | `IgnoredAlreadyProcessed` | Ví không tăng thêm |
| `transferAmount` = 1999 | `IgnoredAmountTooLow` | Giữ Pending |
| `accountNumber` khác | `IgnoredAccountMismatch` | Giữ Pending |
| `transferType` = `out` | `IgnoredNotIncoming` | Giữ Pending |
| `content` không có mã PF | `IgnoredNoOrderCode` | Giữ Pending |
| Sai / thiếu header `Authorization` | HTTP 401 | |
| Chưa đặt key ở BE | HTTP 503 | |

Đơn mở chương: tạo bằng `{"purpose":"ChapterUnlock","chapterId":"<id chương trả phí>","language":"<mã ngôn ngữ>"}`; sau webhook, `GET /api/wallet/me` có chương trong `unlockedChapterIds`.

## 3. Test với SePay thật

### 3.1. Mở BE ra internet
SePay không gọi được `localhost`. Mở tunnel trong một terminal riêng, giữ cửa sổ mở suốt lúc test:

```powershell
ssh -R 80:localhost:5044 nokey@localhost.run
```
hoặc (cần cài `winget install --id Cloudflare.cloudflared`):
```powershell
cloudflared tunnel --url http://localhost:5044
```
Lệnh in ra địa chỉ `https://xxxx.lhr.life` / `https://xxxx.trycloudflare.com`. Địa chỉ **đổi mỗi lần chạy lại** → phải sửa URL webhook trên SePay.

Kiểm tra tunnel: `curl -X POST https://xxxx.../api/payments/sepay/webhook -H "Content-Type: application/json" -d '{}'` phải trả **401** (BE có key, tunnel thông).

### 3.2. Tạo webhook trên my.sepay.vn (Tích hợp WebHooks → Thêm)
1. **Cơ bản:** tên `PanelForge`; URL `https://xxxx.../api/payments/sepay/webhook`; loại giao dịch **Tiền vào**; định dạng **JSON**; bật **Tự động gửi lại khi server trả lỗi**.
2. **Tài khoản:** chọn tài khoản ngân hàng đã liên kết (phải trùng STK đã khai cho Studio). Không bật "bỏ qua nếu không có code thanh toán".
3. **Bảo mật:** kiểu **API Key**, Ctrl+V dán key từ `scripts/sepay-set-key.ps1`.
4. **Cảnh báo:** tùy chọn.

### 3.3. Giả lập giao dịch (không tốn tiền)
1. Tạo đơn trên app mobile hoặc bằng curl ở mục 2, ghi lại `PFxxxxxx`.
2. Bật **Test mode** ở thanh bên trái my.sepay.vn, tạo giao dịch tiền vào giả: đúng tài khoản, đúng số tiền, nội dung chứa `PFxxxxxx`.
3. Xem **Nhật ký webhook** trên SePay: HTTP 200, body `{"success":true,"outcome":"Approved"}`. Log BE có dòng `SePay webhook <id> (<ref>) order PFxxxxxx: Approved`.

### 3.4. Chuyển khoản thật
Tạo đơn nạp 2.000đ trên app, quét QR bằng app ngân hàng, **giữ nguyên nội dung chuyển khoản**. Sau vài giây tới khoảng 1 phút, màn QR trên app chuyển thành công.

## 4. Gỡ lỗi nhanh

| Triệu chứng | Nguyên nhân thường gặp |
|---|---|
| Webhook trả 503 | BE chưa đọc được `SePay:WebhookApiKey` (chưa chạy script, chưa restart BE, hoặc key nằm trong sandbox của agent) |
| 401 | Key trên SePay khác key ở BE, hoặc SePay chưa chọn chứng thực API Key |
| SePay báo lỗi kết nối / timeout | Tunnel đã tắt hoặc đổi địa chỉ; BE không chạy |
| `IgnoredAccountMismatch` | Studio chưa khai đúng STK đã liên kết SePay |
| `IgnoredNoOrderCode` | Ngân hàng/người chuyển sửa mất mã `PFxxxxxx` trong nội dung |
| `IgnoredOrderNotFound` | Đơn tạo ở DB khác (ví dụ server khác) với BE đang nhận webhook |
| Mobile không đổi trạng thái | App trỏ sai BE; kiểm tra `GET /api/wallet/transactions/{orderCode}/status` trực tiếp |
