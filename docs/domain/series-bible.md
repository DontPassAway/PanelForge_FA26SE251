# Series Bible

> **Tài liệu:** Giải thích Series Bible trong PanelForge: nó là gì, chứa gì, được phiên bản hóa ra sao, ai dùng và dùng để làm gì.
> **Cập nhật:** 04/10/2026 (thêm mục 10 và 11: tham khảo thiết kế và quy tắc kiểm tra bằng code)
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
| **Hết hiệu lực ở chương** (`EffectiveToChapterNumber`) | Chương cuối còn hiệu lực (gồm cả chương đó); trống = tới hết truyện |
| **Tên giai đoạn** (`StageName`) | Tên do người dùng đặt, vd. "Lúc nhỏ", "Trưởng thành". Không phải số phiên bản |
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

### Giai đoạn của nhân vật (vd. Goku lúc nhỏ và trưởng thành)

Không đặt tên kiểu "Goku V1 / V1.x / V2", vì dễ hiểu nhầm thành số lần chỉnh sửa. Giữ **một mục** `CHR-GOKU`, mỗi giai đoạn là một bản ghi có **tên** và **phạm vi chương**:

| Giai đoạn | Hiệu lực từ | Hết hiệu lực ở |
|---|---|---|
| Lúc nhỏ | Chương 1 | Chương 19 |
| Trưởng thành | Chương 20 | (trống = tới hết truyện) |

Quy tắc chọn bản ở Chương N: lấy bản có phạm vi chứa N; nếu nhiều bản cùng chứa thì lấy bản bắt đầu muộn nhất (giai đoạn mới hơn), rồi tới bản sửa mới nhất. Nếu không bản nào chứa N (vd. nhân vật rời truyện sau chương 50), mục đó không xuất hiện ở chương N. Sửa chi tiết trong cùng giai đoạn (thêm mặt nghiêng, chỉnh màu áo) là một bản mới **cùng tên giai đoạn và cùng phạm vi**; nếu bản cũ đã khoá thì Producer dùng "Thay thế bản khoá", bản thay thế giữ nguyên giai đoạn và phạm vi.

Hạn chế hiện tại: cảnh hồi tưởng (Chương 25 có Goku lúc nhỏ) chưa chọn được giai đoạn riêng cho từng Panel.

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

## 5. Ai làm gì với Bible (Kiến trúc V2.5 - Hợp tác 3 bên)

| Role | Quyền với Bible |
|---|---|
| **Writer** | Khởi tạo mục và bản nháp (`Draft / Specifying`: mô tả ngoại hình, tính cách, vai trò, mốc chương bắt đầu `EffectiveFromChapterNumber`); liên kết script với mục Bible; duyệt sơ bộ bản vẽ của Artist (`WriterEndorsed`) hoặc yêu cầu vẽ lại (`ReworkRequired`) |
| **Artist** | Nhận yêu cầu vẽ từ **Bible Design Queue** (`Pending Concept Art / Rework`); vẽ Character Sheet / Model Sheet / Concept bối cảnh và tải ảnh Asset lên qua CAS SHA-256 (`ConceptSubmitted`, ghi nhận `ArtistUserId`); xem bản `CanonLocked` khi vẽ layout/artwork chương |
| **Editor** | Góp ý, đối chiếu nguyên tác cốt truyện; xác nhận thiết kế (`WriterEndorsed`) hoặc gửi Change Request yêu cầu vẽ lại (`ReworkRequired`); chạy kiểm tra nhất quán |
| **Producer** | Phê duyệt chốt và khóa canon cuối cùng (`🔒 CanonLocked`); khi đã khóa thì không mở khóa (`/unlock` bị cấm hoàn toàn); thay thế bản đã khoá bằng `/supersede` (kèm lý do bắt buộc và tạo revision mới cùng chương); kiểm tra **Chapter-Scoped Bible Gate** trước khi duyệt kịch bản đưa vào sản xuất |
| **Letterer** | Xem: thuật ngữ, cách xưng hô |
| **Admin** | Không có quyền nội dung trong Workspace |
| **Người xem thử, độc giả công khai** | Không thấy Bible |

---

## 6. Bible được dùng ở đâu

### 6.1 Liên kết script với Bible (Writer)
Writer gắn tên nhân vật, địa điểm, đạo cụ trong script với mục Bible tương ứng (`bibleEntryIds` và `speakerCharacterId`). Nhờ đó hệ thống biết "An" trong dòng thoại này chính là nhân vật An trong Bible, chứ không chỉ là một chuỗi chữ.

### 6.2 Tham chiếu khi vẽ (Artist)
Artist mở hồ sơ nhân vật để xem ngoại hình khi vẽ. ArtistAgent chỉ lấy reference từ Bible của lần chạy đó, và mọi nhân vật xuất hiện trong panel đều phải được tham chiếu.

### 6.3 Kiểm tra nhất quán và Rào chắn theo Chương (Chapter-Scoped Bible Gate)
Trước khi đưa Chương N vào sản xuất hình ảnh, hệ thống quét mọi nhân vật và bối cảnh xuất hiện trong kịch bản Chương N. Chỉ khi tất cả thực thể này có Revision hiệu lực ở trạng thái `CanonLocked` tại Chương N thì kịch bản mới được Producer duyệt khóa (`Freeze Script`) để chuyển giao sang Artist.

---

## 7. Ví dụ xuyên suốt

Bộ truyện "Thành phố mưa", Workspace Moonlight.

1. **Lập Bible.** Lan (Producer) và Minh (Writer) khởi tạo thông tin nhân vật An, địa điểm Ga Mưa, đạo cụ chìa khóa bạc, và plot fact "An chỉ nhận chìa khóa bạc từ Chương 5", có hiệu lực từ Chương 1 (`Draft`).
2. **Artist thiết kế.** Hùng (Artist) thấy An trong **Bible Design Queue**, vẽ Model Sheet và tải ảnh lên (`ConceptSubmitted`, lưu SHA-256 vào kho Asset và ghi nhận `ArtistUserId = Hùng`).
3. **Writer/Editor review.** Minh (Writer) thấy đúng trang phục thời học sinh của An, bấm xác nhận (`WriterEndorsed`).
4. **Producer khóa Canon.** Lan (Producer) kiểm tra tổng thể và bấm `Approve & Lock Canon` (`🔒 CanonLocked`).
5. **Sản xuất Chương 1.** Minh nộp kịch bản Chương 1. Vì An đã có bản `CanonLocked` hiệu lực tại Ch.1, **Chapter-Scoped Bible Gate** thông qua. Lan bấm "Duyệt kịch bản & Đưa vào sản xuất" (`Freeze Script`), mở khóa bàn làm việc Thumbnail/Layout cho Hùng.
6. **Thay đổi ở Chương 15.** Khi An trưởng thành ở Chương 15, Minh tạo giai đoạn mới "Trưởng thành" (`EffectiveFromChapterNumber = 15`). Hùng vẽ Model Sheet mới. Các chương 1–14 vẫn đối chiếu theo bản Lúc nhỏ, không bao giờ bị ảnh hưởng.

---

## 8. API cho Bible (Kiến trúc V2.5)

| Việc | Endpoint | Quyền |
|---|---|---|
| Xem toàn bộ Bible kèm phiên bản mới nhất của từng mục | `GET /api/series/{seriesId}/bible` | Thành viên Workspace |
| Xem Bible **có hiệu lực tại một chương** | `GET /api/series/{seriesId}/bible/at-chapter/{n}` | Thành viên Workspace |
| Lấy hàng đợi thiết kế cho Artist | `GET /api/series/{seriesId}/bible/design-queue` | Thành viên Workspace |
| Tải ảnh reference / model sheet (CAS SHA-256) | `POST /api/series/{seriesId}/bible/assets` | Producer, Writer, Artist |
| Tạo mục mới (tự tạo phiên bản 1 `Draft`) | `POST /api/series/{seriesId}/bible/entries` | Producer, Writer, Artist |
| Thêm phiên bản mới / Nộp Concept Art | `POST /api/series/{seriesId}/bible/entries/{id}/revisions` | Producer, Writer, Artist |
| Writer/Editor xác nhận duyệt thiết kế | `POST .../revisions/{id}/endorse` | Producer, Writer, Editor |
| Yêu cầu Artist vẽ lại (kèm nhận xét) | `POST .../revisions/{id}/request-rework` | Producer, Writer, Editor |
| Producer duyệt và chốt khóa canon | `POST .../revisions/{id}/lock` | Producer |
| Thay thế bản đã khóa (tạo revision cùng chương, khoá luôn) | `POST .../revisions/{id}/supersede` | Producer |

---

## 9. Lưu trữ ảnh tham chiếu: Content-Addressed Storage (CAS SHA-256)

Toàn bộ ảnh Model Sheet, sketch biểu cảm, concept art trong Series Bible được tải lên qua `POST /api/series/{seriesId}/bible/assets`, tính mã băm `SHA-256`, lưu trữ trong kho lưu trữ Asset của Workspace và truy xuất bằng signed URL ngắn hạn hoặc endpoint Asset bảo mật `/api/series/{seriesId}/bible/assets/{sha}`. Tuyệt đối không nhập link ảnh tùy tiện từ bên ngoài.

---

## 10. Tham khảo thiết kế từ các nguồn ngoài

Đã mở trực tiếp ba nguồn ngày 04/10/2026. Phần dưới chỉ ghi điều trang nguồn nói; mọi áp dụng cho PanelForge là **đề xuất thiết kế**, chưa phải tính năng đã có.

| Nguồn | Giấy phép | Dùng cho việc gì | Dùng trực tiếp được không |
|---|---|---|---|
| [danjdewhurst/story-skills](https://github.com/danjdewhurst/story-skills) | **MIT** | Ý tưởng **quy tắc kiểm tra bằng code** trên metadata có cấu trúc | Không: là CLI Node.js cho tiểu thuyết (Markdown kèm YAML), không có khái niệm trang, panel, ảnh. Chỉ học ý tưởng rồi tự cài trong .NET |
| [xnovelist – STORY_BIBLE.md](https://github.com/giapnguyen74/xnovelist/blob/main/docs/STORY_BIBLE.md) | **Không thấy giấy phép trên trang** | Ý tưởng **schema trường dữ liệu** và cách chọn Bible đưa cho AI | Không sao chép nguyên văn hay mã; chỉ tham khảo ý tưởng |
| [Novelcrafter – Codex](https://www.novelcrafter.com/features/codex) | **Sản phẩm thương mại** (gói 4 đến 20 USD/tháng, dùng thử 21 ngày) | **Trải nghiệm** quản lý mục Bible và thay đổi theo mốc thời gian | Không: chỉ để tham khảo UX; có xuất Codex ra thư mục |

### 10.1 Những điều mỗi nguồn thực sự có

**story-skills** (263 sao, 423 commit tại thời điểm đọc, có bộ test và harness đánh giá)
- Lệnh `story continuity` kiểm **tất định bằng code**: nhân vật xuất hiện sau khi chết, hứa hẹn và giải quyết đúng thứ tự (setup trước payoff), câu hỏi được đặt trước khi trả lời, nhân vật POV có trong danh sách cảnh, trạng thái bền vững tham chiếu đúng chương có thật, thời gian di chuyển theo tuyến địa điểm.
- Mô hình trạng thái: nhân vật (sống/chết, có mặt, kiến thức), đồ vật (trạng thái hoạt động hay bị phá hủy, chủ sở hữu, vị trí), **kiến thức của độc giả khác kiến thức của nhân vật**, dòng thời gian cảnh.
- Cách xử lý ngoại lệ: hồi tưởng cố ý được cho phép; nhân vật đã chết được nhắc đến qua trường `mentions`; finding bị bác bỏ ghi vào `continuity/exemptions.md`.
- **Điều chưa xác nhận:** nhận xét gốc nói công cụ bắt "đồ vật đã bị phá hủy nhưng vẫn hoạt động". Trang chỉ nói có theo dõi trạng thái đồ vật, **không liệt kê đó là một kiểm tra cụ thể**, nên đừng coi đó là chức năng đã xác minh.
- Giới hạn của chính nguồn: kiểm tra dựa trên **metadata được khai báo**, không đọc hiểu mọi mâu thuẫn trong văn bản tự do.

**xnovelist**
- Character: `id` ổn định, `name`, `aliases`, `role`, `age`, `appearance`, `traits`, `desires`, `fears`, `speechPatterns`, `relationships` (id, loại, ghi chú), `notes`, `evidence` (chương kèm trích dẫn).
- Location: `id`, `name`, `aliases`, `scale` (phòng đến thế giới), `descriptors`, `significance`, `inhabitants`, `notes`, `evidence`.
- Style: nhịp câu, từ vựng (từ nên dùng hay tránh), quy ước thoại, ngôi kể và thì, bảng cảm giác, **cặp đại từ** (cho ngôn ngữ nhiều cách xưng hô).
- **Continuity theo chương không phải schema cứng**: là Markdown tự do theo mẫu (tóm tắt, trạng thái nhân vật, độc giả đã biết gì, nút thắt chưa giải quyết, ràng buộc tiếp theo).
- Khi dựng context cho chương N: chỉ lấy nhân vật và địa điểm **được nhắc trong văn bản**, và lấy continuity của chương **N−1**.

**Novelcrafter**
- Loại mục: nhân vật, địa điểm, lore, đồ vật, tuyến phụ. Có tên gọi khác (aliases) nhận diện khi gõ, tham chiếu giữa các mục, trường tùy chỉnh, **Progressions** (ghi thay đổi theo mốc thời gian), và chia sẻ một Codex giữa nhiều quyển trong cùng series.

### 10.2 Cái PanelForge đã có và cái nên học

| Ý tưởng | PanelForge đã có chưa | Nên làm gì |
|---|---|---|
| Thay đổi theo timeline (Progressions) | **Có**: `BibleEntryRevision.EffectiveFromChapterNumber` (BR-15), truy vấn Bible tại chương N | Không cần học thêm về dữ liệu. Chỉ học **cách hiển thị**: một mục có dòng thời gian "An trước Chương 5 / từ Chương 5" |
| Chia sẻ giữa nhiều quyển | Mỗi Series có một Bible riêng | Chưa cần; một Workspace nhiều Series chia sẻ thuật ngữ chung là ý tưởng P2 |
| **Aliases** (tên gọi khác) | Chưa có trường riêng | Thêm `aliases` vào `DetailsJson`: biệt danh, cách gọi theo quan hệ, không dấu. Rất quan trọng với tiếng Việt vì xưng hô thay đổi theo người nói |
| **Evidence** (chương kèm trích dẫn nguồn của từng sự thật) | Finding AI đã bắt buộc có bằng chứng; chưa có ở mục Bible | Thêm `evidence` vào revision: chương hoặc trang nơi sự thật được xác lập |
| Trường chi tiết theo loại | `DetailsJson` tự do, không có khuôn | Định nghĩa khuôn trường theo loại (10.3) |
| Cặp đại từ và quy ước xưng hô | Loại `Terminology` đã có | Dùng `Terminology` với cấu trúc người nói → người nghe → cách gọi |
| Chọn Bible đưa cho AI theo cảnh | AI đã lấy Bible **có hiệu lực tại chương** | Học thêm: chỉ đưa các mục **được nhắc trong cảnh**, giảm token và giảm bề mặt tấn công prompt injection |
| Continuity chương N−1 | Chưa có | Có thể **sinh tự động** "trạng thái cuối chương trước" từ các revision hiệu lực, không cần viết tay |

### 10.3 Mở rộng riêng cho manga (đề xuất của riêng PanelForge)

Ba nguồn là công cụ cho **văn bản**; không nguồn nào xử lý hình ảnh. Phần dưới là mở rộng do PanelForge tự thiết kế, nằm trong `DetailsJson` nên **không cần đổi bảng**.

| Loại mục | Trường gợi ý thêm |
|---|---|
| Character | `aliases`, `modelSheet` (AssetId của ảnh model sheet), `outfits` (tên, hiệu lực từ chương), `colorPalette` (màu tóc, mắt, da), `distinguishingMarks` (sẹo, phụ kiện), `speechPatterns`, `relationships` |
| Prop | `appearance`, `owner`, `status` (đang dùng, hỏng, mất), `availableFromChapter` |
| Location | `scale`, `descriptors`, `inhabitants`, ảnh tham chiếu |
| StyleRule | quy tắc hình ảnh: độ dày nét, kiểu screentone, bảng màu, cách vẽ cảnh mưa |
| Terminology | cặp người nói → người nghe → cách xưng hô |
| PlotFact | `establishedAtChapter`, `knownBy`, `revealedToReaderAtChapter` |

Ví dụ phần `DetailsJson` của mục nhân vật An (minh họa, giá trị do mình đặt):

```json
{
  "aliases": ["An", "cô bé ga Mưa"],
  "appearance": { "hair": "đen, ngắn", "eyes": "nâu" },
  "outfits": [{ "name": "áo mưa vàng", "fromChapter": 1 }],
  "speechPatterns": "nói ít, câu ngắn",
  "relationships": [{ "with": "NGUOI_LA", "kind": "chưa gặp", "fromChapter": 1 }]
}
```

Kiểm tra bằng thị giác (ví dụ sẹo, màu mắt trong tranh) theo phiếu V1.2 là lớp thứ ba (hình ảnh) của kiểm tra nhất quán nhân vật và thuộc **lõi** (xem `requirements.md` 5.11); code hiện chưa triển khai.

---

## 11. Quy tắc kiểm tra continuity bằng code

Nguyên tắc học từ story-skills: **tách kiểm tra tất định bằng code khỏi đánh giá bằng AI**. Cái gì có đáp án đúng sai rõ ràng thì để code làm; AI chỉ dùng cho phần mơ hồ (giọng văn, mâu thuẫn theo ngữ nghĩa). Điều này cũng khớp với hướng "code trước, AI sau" trong `panelforge-design-research.md`.

### 11.1 Các loại quy tắc đề xuất

| Mã quy tắc | Kiểm tra gì | Dữ liệu cần | Phụ thuộc |
|---|---|---|---|
| `PROP_NOT_BEFORE` | Đạo cụ hoặc sự kiện **không được xuất hiện trước chương X** (ví dụ chìa khóa bạc chỉ từ Chương 5) | Mục Bible có hiệu lực từ chương X; dòng script liên kết tới mục đó | Cần liên kết script ↔ mục Bible (FR-Writer-04) |
| `CHARACTER_AFTER_DEATH` | Nhân vật đã chết vẫn nói hay xuất hiện ở chương sau | Trạng thái "đã chết từ chương X"; speaker của dòng thoại | **Làm sớm được**: `ScriptLine.SpeakerCharacterId` đã trỏ tới mục Bible |
| `REF_NOT_FOUND` | Tham chiếu tới mục Bible hay chương không tồn tại hoặc đã vô hiệu | Liên kết và danh sách mục | Cần liên kết script ↔ mục Bible |
| `SETUP_BEFORE_PAYOFF` | Hệ quả xuất hiện trước nguyên nhân | Quan hệ giữa các PlotFact | Cần thêm quan hệ giữa các sự kiện |
| `OBJECT_STATE_CONFLICT` | Đạo cụ đã hỏng hay mất nhưng vẫn được dùng | Trạng thái đạo cụ theo chương; liên kết prop ↔ dòng hoặc panel | Cần liên kết prop ↔ panel; nguồn chưa xác nhận kiểm tra này |
| `STRICT_SPELLING` | Tên nhân vật hay địa điểm viết khác tên chuẩn hoặc alias | Tên và `aliases` | Dùng được ngay với văn bản thoại |
| `FORBIDDEN_TERM` | Dùng cách xưng hô hay thuật ngữ sai theo cặp người nói → người nghe | Mục `Terminology` có cấu trúc | Cần speaker và người nghe |

**Ví dụ với bộ truyện mẫu:** quy tắc "An chỉ nhận chìa khóa từ Chương 5" là một `PROP_NOT_BEFORE` với `entry = CHIA_KHOA_BAC`, `notBeforeChapter = 5`. Khi dòng thoại của Chương 3 liên kết tới mục đó, code báo lỗi ngay mà **không cần gọi AI**; AI chỉ cần cho các trường hợp không có liên kết.

### 11.2 Khớp với `ConsistencyRule` hiện có

Thực thể `ConsistencyRule` của dự án đã có `RuleType`, `Pattern`, `Severity`, `IsEnabled` và `ConfigurationJson`. Các giá trị `RuleType` hiện có là `CharacterContinuity`, `PropContinuity`, `LoreContinuity`, `ToneConsistency`, `StrictSpelling`, `DialogueOverflow`, `Custom`.
- Các quy tắc ở 11.1 có thể dùng `RuleType` có sẵn (ví dụ `PropContinuity`) cùng tham số trong `ConfigurationJson` (`entryCode`, `notBeforeChapter`), nên **không cần bảng mới** cho phần quy tắc.
- Mỗi lần kiểm tra tạo `ConsistencyFinding` (E-26) có: mã quy tắc, mức độ, dòng script, **phiên bản mục Bible đã dùng**, và đoạn trích làm bằng chứng.

### 11.3 Ngoại lệ hợp lệ

Học từ story-skills: hồi tưởng và nhắc đến người đã khuất là hợp lệ.
- Đánh dấu **hồi tưởng** ở mức Scene hoặc dòng script để quy tắc bỏ qua.
- Bác bỏ một finding là một bản ghi có lý do, đúng với BR-21 (Ignored bắt buộc ghi lý do).

### 11.4 Kết luận: có đưa vào dự án được không?

| Câu hỏi | Trả lời |
|---|---|
| Thêm vào `series-bible.md` để làm tài liệu thiết kế? | **Có**, đã thêm ở mục 10 và 11 |
| Dùng nguyên repo hay tài liệu của họ? | **Không**. story-skills là Node.js cho tiểu thuyết; xnovelist không có giấy phép; Novelcrafter là sản phẩm thương mại |
| Có cần sửa mô hình dữ liệu? | **Ít**: `DetailsJson`, `ConsistencyRule` và `BibleEntryRevision` đã đủ chỗ chứa. Phần thiếu thật sự là **liên kết script ↔ mục Bible** (hiện chỉ có `SpeakerCharacterId` cho nhân vật) |
| Quy tắc nào làm sớm nhất? | `CHARACTER_AFTER_DEATH` và `STRICT_SPELLING`, vì chỉ cần dữ liệu đã có |
| Giới hạn? | Kiểm tra chỉ đúng khi metadata được khai báo đủ; không thay được AI cho mâu thuẫn trong văn bản tự do |

Ưu tiên đề xuất: đưa vào nhóm **P1** của backlog trong `panelforge-design-research.md` (kiểm tra bằng code và bằng chứng continuity), sau khi có CRUD ScriptLine và liên kết script ↔ Bible ở P0.

---

## 12. Tóm tắt

- Bible là **một kho có phiên bản** gồm 6 loại thông tin, mỗi Series một cái.
- Nó vừa là **tài liệu tham chiếu cho người**, vừa là **nền kiểm tra cho AI**.
- Mỗi sự thật có **phiên bản theo chương**: đúng ở chương này, có thể thay đổi hợp lệ ở chương sau.
- Không bao giờ sửa đè; mọi thay đổi là một phiên bản mới có người làm, thời điểm và mã băm.
- AI chỉ **đề xuất**, người quyết định từng phát hiện.
