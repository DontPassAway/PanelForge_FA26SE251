# Series Bible

> **Tài liệu:** Giải thích Series Bible trong PanelForge: nó là gì, chứa gì, được phiên bản hóa ra sao, ai dùng và dùng để làm gì.
> **Cập nhật:** 04/10/2026
> **Nguồn:** `requirements.md` (3.2, 4.5, 5.2), `PanelForge_Capstone_Content.md` (BR-01, BR-08, BR-15, BR-18, BR-21, US-01), `project-plan.md` (2.2), và code `Domain/Entities/Bible`, `BibleController`.

---

## 1. Series Bible là gì

**Series Bible là kho lưu trữ có phiên bản chứa mọi "sự thật cố định" của một bộ truyện.** Nó gồm:

- Nhân vật (Characters)
- Địa điểm (Locations)
- Đạo cụ (Props)
- Thuật ngữ (Terminology)
- Quy tắc phong cách (Style rules)
- Các sự kiện cốt truyện đã xảy ra (Established plot facts)

Nó đóng hai vai trò cùng lúc:

1. **Tài liệu tham chiếu cho con người.** Artist xem ngoại hình nhân vật, Writer tra cách xưng hô, Editor đối chiếu khi review.
2. **Nguồn "nền" (grounding) cho AI.** Khi AI kiểm tra tính nhất quán, nó đối chiếu nội dung với Bible, không dựa vào trí nhớ hay suy đoán.

### Vì sao cần

Một bộ truyện dài có hàng trăm chương, nhiều người làm. Nếu không có nơi ghi lại sự thật chung, các lỗi dễ xảy ra là: tóc nhân vật đổi màu, gọi nhầm tên một địa điểm, một nhân vật biết điều mà đáng lẽ chưa thể biết. Theo `requirements.md` 1.3, việc theo dõi thủ công ngày càng khó khi bộ truyện lớn lên. Bible giải quyết bằng cách đặt mọi sự thật vào một chỗ, có lịch sử, truy vết được.

Mỗi Series có đúng **một** Bible, được tạo rỗng khi tạo Series.

---

## 2. Cấu trúc

```
Series
  └── Series Bible (1 cái)
        └── Mục Bible (BibleEntry)   ← nhiều mục, thuộc 1 trong 6 loại
              └── Phiên bản (BibleEntryRevision)   ← nhiều phiên bản, chỉ thêm, không sửa
```

### 2.1 Sáu loại mục

| Loại | Dùng để ghi | Ví dụ |
|---|---|---|
| **Character** | Ngoại hình, tính cách, quan hệ, ảnh model sheet | An: tóc đen ngắn, mắt nâu, hay nói ít |
| **Location** | Địa điểm và đặc điểm | Ga Mưa: mái vòm kính, luôn có mưa |
| **Prop** | Đạo cụ quan trọng | Chiếc chìa khóa bạc |
| **Terminology** | Thuật ngữ, tên riêng, cách xưng hô | An gọi người lạ là "anh", không gọi "ngài" |
| **StyleRule** | Quy tắc phong cách | Cảnh mưa dùng tông xanh lạnh |
| **PlotFact** | Sự kiện cốt truyện đã được xác lập | "An chỉ nhận chìa khóa bạc từ Chương 5" |

### 2.2 Thông tin của một mục

| Trường | Ý nghĩa |
|---|---|
| Mã (`Code`) và Tên (`Name`) | Định danh và tên hiển thị |
| Phụ đề (`Subtitle`) | Mô tả ngắn, nếu cần |
| Mô tả (`Description`) | Nội dung chính |
| Chi tiết (`DetailsJson`) | Các thuộc tính có cấu trúc, tùy loại (ví dụ màu tóc, màu mắt của nhân vật) |
| Ảnh tham chiếu | Ảnh minh họa, ví dụ model sheet của nhân vật |
| Mức ưu tiên kiểm tra (`Priority`) | `Low`, `Standard`, `StrictCheck`: mức độ AI phải kiểm tra kỹ mục này |
| `StrictCheck` | Cờ bật kiểm tra nghiêm ngặt |
| Đang hoạt động (`IsActive`) | Mục có đang được dùng không |

### 2.3 Phiên bản của một mục

Mỗi lần sửa một mục là tạo thêm **một phiên bản mới**; phiên bản cũ không bị đổi hay xóa. Mỗi phiên bản ghi:

| Trường | Ý nghĩa |
|---|---|
| Số phiên bản | Tăng dần: 1, 2, 3… |
| Tóm tắt (`Summary`) | Thay đổi này là gì |
| Bản chụp nội dung (`SnapshotJson`) | Toàn bộ nội dung mục tại thời điểm đó |
| Mã băm (`ContentHash`) | Dùng để kiểm tra toàn vẹn |
| Tác giả (`AuthorId`) | Ai tạo phiên bản này |
| **Có hiệu lực từ chương** (`EffectiveFromChapterNumber`) | Phiên bản này bắt đầu đúng từ chương nào |
| Chương liên quan (`AssociatedChapterId`) | Chương gắn với thay đổi, nếu có |
| Phiên bản đầu tiên? (`IsInitialVersion`) | Đánh dấu bản khởi tạo |

---

## 3. Quy tắc quan trọng nhất: Bible phiên bản hóa theo chương (BR-15)

> Mỗi thay đổi của một mục Bible gắn với một **chương bắt đầu có hiệu lực**. Kiểm tra nhất quán ở Chương N dùng đúng các phiên bản có hiệu lực tại Chương N. Sửa ngược về quá khứ thì lưu thành **phiên bản mới**, không ghi đè.

### Ví dụ

Mục PlotFact "Chiếc chìa khóa bạc":

| Phiên bản | Nội dung | Hiệu lực từ |
|---|---|---|
| v1 | "An chưa từng sở hữu hay nhìn thấy chiếc chìa khóa này." | Chương 1 |
| v2 | "An đã nhận chiếc chìa khóa bạc từ người lạ." | Chương 5 |

- Khi kiểm tra **Chương 3**, hệ thống dùng v1. Nếu cảnh nào ở Chương 3 cho An cầm hay nhìn thấy chìa khóa, đó là **lỗi nhất quán**.
- Khi kiểm tra **Chương 6**, hệ thống dùng v2. Cùng cảnh đó **hợp lệ**.

Nhờ vậy, khi một sự thật thay đổi hợp lệ ở chương sau, hệ thống **không báo lỗi giả** cho các chương trước, và trạng thái cũ vẫn luôn dựng lại được.

### Sửa Bible khi phát hiện sai

Nếu Producer nhận ra Bible ghi sai từ đầu, không sửa đè v1. Cô tạo v2 có hiệu lực từ chương liên quan, ghi rõ lý do trong tóm tắt. Cả hai phiên bản vẫn còn trong lịch sử.

---

## 4. Các quy tắc đi kèm

| Quy tắc | Nội dung | Ảnh hưởng tới Bible |
|---|---|---|
| **BR-01** Bất biến | Dữ liệu chỉ thêm, không xóa hay ghi đè | Mọi chỉnh sửa tạo phiên bản mới |
| **BR-08** Khôi phục không phá hủy | Khôi phục một bản cũ tạo ra bản mới ở đầu lịch sử | Các bản ở giữa không bị xóa |
| **BR-18** Kiểm toán đầy đủ | Mọi thay đổi ghi người làm, thời điểm, nội dung đổi | Mỗi phiên bản có tác giả và mã băm |
| **BR-02** AI chỉ đề xuất | Con người quyết định cuối cùng | AI đối chiếu với Bible nhưng không tự sửa Bible hay tự duyệt |
| **BR-10** Provenance AI | Mỗi kết quả AI phải lưu model, tham số, prompt, ngữ cảnh | Kết quả đối chiếu truy vết được tới phiên bản Bible đã dùng |
| **BR-21** Phân loại phát hiện | Mỗi phát hiện kết thúc là Confirmed, Rejected, Ignored hoặc Needs clarification; Ignored bắt buộc ghi lý do | Editor xử lý từng lỗi nhất quán do AI tìm ra |
| **BR-07** Quyền theo Workspace | Chỉ thành viên Workspace mới truy cập | Bible là dữ liệu chưa phát hành, không công khai |

---

## 5. Ai làm gì với Bible

| Role | Quyền với Bible |
|---|---|
| **Producer** | Tạo mục, cập nhật nhân vật/địa điểm/đạo cụ/thuật ngữ/quy tắc phong cách, ghi plot fact, tạo phiên bản sửa đổi |
| **Writer** | Đề xuất bổ sung hoặc sửa; liên kết script với mục Bible |
| **Artist** | Xem: model sheet, bảng màu, đạo cụ. Dùng làm tham chiếu khi vẽ |
| **Letterer** | Xem: thuật ngữ, cách xưng hô |
| **Editor** | Góp ý; chạy kiểm tra nhất quán và xử lý các phát hiện |
| **Admin** | Không có quyền nội dung trong Workspace |
| **Người xem thử, độc giả công khai** | Không thấy Bible |

---

## 6. Bible được dùng ở đâu

### 6.1 Liên kết script với Bible (Writer)
Writer gắn tên nhân vật, địa điểm, đạo cụ trong script với mục Bible tương ứng. Nhờ đó hệ thống biết "An" trong dòng thoại này chính là nhân vật An trong Bible, chứ không chỉ là một chuỗi chữ.

### 6.2 Tham chiếu khi vẽ (Artist)
Artist mở hồ sơ nhân vật để xem ngoại hình khi vẽ. ArtistAgent chỉ lấy reference từ Bible của lần chạy đó, và mọi nhân vật xuất hiện trong panel đều phải được tham chiếu.

### 6.3 Kiểm tra nhất quán bằng AI (Editor)
ContinuityAgent đối chiếu script với **các mục Bible có hiệu lực tại chương đang kiểm tra**:

- Mỗi phát hiện kèm **bằng chứng** trích từ Bible. Ví dụ: "Theo Bible, An chưa từng nhìn thấy chiếc chìa khóa này trước Chương 5."
- Mô hình AI chỉ trả về **khóa ngắn** của mục Bible (dạng `B1`, `B2`), còn mã định danh thật do code cấp, để AI không bịa ra mục không tồn tại.
- Dữ liệu Bible đưa vào prompt được coi là **dữ liệu**, không phải mệnh lệnh, nhằm chống prompt injection.
- Kết quả là **đề xuất/báo cáo**; Editor phân loại từng phát hiện (BR-21).

Dữ liệu của lần chạy (bản chụp Bible, phiên bản đã dùng, prompt, kết quả) được lưu để truy vết.

---

## 7. Ví dụ xuyên suốt

Bộ truyện "Thành phố mưa", Workspace Moonlight.

1. **Lập Bible.** Lan (Producer) ghi nhân vật An, địa điểm Ga Mưa, đạo cụ chìa khóa bạc, và plot fact "An chỉ nhận chìa khóa bạc từ Chương 5", có hiệu lực từ Chương 1.
2. **Viết script.** Minh (Writer) viết cảnh ở Chương 3; AI Writer viết bản đầu "An mở gói vải thấy chiếc chìa khóa bạc…".
3. **AI bắt lỗi.** ContinuityAgent đối chiếu với plot fact đang hiệu lực ở Chương 3 và báo mâu thuẫn, kèm bằng chứng.
4. **Sửa.** Minh sửa cảnh để An chưa mở gói vải. Hoặc, nếu nhóm quyết định đổi cốt truyện, Lan tạo phiên bản mới của plot fact có hiệu lực từ chương liên quan và được duyệt. Không được bỏ qua lỗi continuity chỉ để cảnh trôi chảy.
5. **Editor xử lý.** Thu (Editor) phân loại phát hiện: Confirmed. Một cảnh báo nhầm khác bị Ignored kèm lý do.
6. **Chương 5.** Lan tạo phiên bản v2 của plot fact: An đã nhận chìa khóa, hiệu lực từ Chương 5. Từ đó cảnh An cầm chìa khóa ở Chương 5 trở đi hợp lệ, còn Chương 3 vẫn được kiểm bằng v1.

---

## 8. API hiện có cho Bible

| Việc | Cách làm |
|---|---|
| Xem toàn bộ Bible kèm phiên bản mới nhất của từng mục | Lấy Series Bible theo Series |
| Xem Bible **có hiệu lực tại một chương** | Lấy mục Bible theo số chương (đúng BR-15) |
| Tạo mục mới (tự tạo phiên bản 1) | Tạo `BibleEntry` |
| Thêm phiên bản mới có hiệu lực từ một chương | Thêm `BibleEntryRevision` (chỉ thêm, BR-01) |
| Xem lịch sử các phiên bản của một mục | Lấy lịch sử mục |

Các thao tác nằm ở `BibleController`.

---

## 9. Lưu ý về ảnh tham chiếu

Ảnh tham chiếu của một mục (ví dụ model sheet nhân vật) hiện được lưu dưới dạng chuỗi URL. Theo `storage-strategy.md`, nên chuyển sang tham chiếu bằng `AssetId` của hệ thống lưu trữ ảnh, để quyền truy cập nhất quán, ảnh không bị lấy từ nguồn ngoài, và ảnh thuộc Workspace.

---

## 10. Tóm tắt

- Bible là **một kho có phiên bản** gồm 6 loại thông tin, mỗi Series một cái.
- Nó vừa là **tài liệu tham chiếu cho người**, vừa là **nền kiểm tra cho AI**.
- Mỗi sự thật có **phiên bản theo chương**: đúng ở chương này, có thể thay đổi hợp lệ ở chương sau.
- Không bao giờ sửa đè; mọi thay đổi là một phiên bản mới có người làm, thời điểm và mã băm.
- AI chỉ **đề xuất**, người quyết định từng phát hiện.
