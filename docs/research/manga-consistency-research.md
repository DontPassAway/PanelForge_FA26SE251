# Nghiên cứu: Tính nhất quán trong sản xuất manga và hàm ý cho PanelForge

> Báo cáo nghiên cứu nghiệp vụ, không đề cập mã nguồn hay kiến trúc PanelForge hiện có.
> Ngày kiểm chứng nguồn: 2026-10-04.

## 0. Cách đọc báo cáo và mức độ tin cậy

Mỗi nhận định gắn một nhãn:

| Nhãn | Ý nghĩa |
|---|---|
| **[Nguồn]** | Có tài liệu dẫn kèm link; tôi đã đọc nội dung trang hoặc tóm tắt trang đó. |
| **[Thông lệ]** | Kiến thức nghề phổ biến, không có một tài liệu chuẩn duy nhất để dẫn. |
| **[Suy luận]** | Tôi suy ra từ nguồn hoặc thông lệ. Có thể sai. |
| **[Đề xuất]** | Đề xuất riêng cho PanelForge, không phải chuẩn ngành. |

**Giới hạn cần nói thẳng:**
- Quy trình manga ở Nhật (name, biên tập viên phụ trách) tôi chỉ tìm được blog và hướng dẫn thương mại, ví dụ [Mangaka Blog](https://www.mangaka.app/blogs/manga-storyboard-workflow-guide-2026-complete-guide), [Mangatorials](http://mangatorials.blogspot.com/p/blog-page_7.html). Đây là nguồn độ tin cậy trung bình, không phải tài liệu của nhà xuất bản. Các mô tả quy trình bên dưới vì vậy chủ yếu là **[Thông lệ]**.
- Chưa có tài liệu học thuật nào tôi tìm thấy về "kiểm tra nhất quán manga" như một bài toán riêng. Phần lớn nghiên cứu AI dùng cho tiểu thuyết/văn bản, tôi suy rộng sang manga (**[Suy luận]**).
- Các định nghĩa về retcon/canon lấy từ TV Tropes, là wiki cộng đồng, hữu ích để lấy thuật ngữ nhưng không phải nguồn học thuật.

---

## 1. Tính nhất quán gồm những gì?

"Nhất quán" không phải một thứ. Mỗi loại có cách vi phạm và cách kiểm tra khác nhau. **[Suy luận]**, tổng hợp từ thông lệ biên tập và từ phân loại lỗi của [ConStory-Bench](https://arxiv.org/html/2603.05890v1) (5 nhóm, 19 loại lỗi, trong đó lỗi thực tế và thời gian phổ biến nhất).

| Loại | Câu hỏi cốt lõi | Ví dụ vi phạm | Đặc thù manga |
|---|---|---|---|
| **Cốt truyện** | Sự kiện sau có phù hợp nhân quả với sự kiện trước? Mầm (setup) có thu hoạch (payoff)? | Bí mật được tiết lộ ở ch.10 nhưng ch.6 nhân vật đã hành động như biết rồi | Truyện đăng nhiều kỳ, phải sửa lại sau khi đã in |
| **Nhân vật** | Tính cách, động cơ, năng lực có đổi mà không có lý do? | Nhân vật nhút nhát bỗng hung hăng không có biến cố | Giọng nói còn là thiết kế (kiểu chữ, cách xưng hô) |
| **Timeline** | Thứ tự, khoảng cách thời gian, tuổi, mùa có khớp? | Ch.3 "ba ngày sau", ch.4 "tuần sau" nhưng vết thương đã lành | Trang không có chú thích ngày; thời gian lộ qua trang phục, thời tiết |
| **Kiến thức nhân vật** | Ai biết gì, từ lúc nào? | A chưa nghe tin B chết nhưng đã nhắc đến cái chết đó | Dễ vi phạm qua thoại và ánh mắt |
| **Hình ảnh** | Ngoại hình, trang phục, kiến trúc, tỉ lệ có khớp? | Sẹo trái ở ch.1, sẹo phải ở ch.5 mà không phải cảnh lật hình | Loại lỗi dễ thấy nhất với độc giả; xảy ra nhiều khi nhiều artist/assistant |
| **Đạo cụ** | Vật nào ở đâu, của ai, còn nguyên hay hỏng? | Kiếm gãy ở ch.7, nguyên vẹn ở ch.8 | Đạo cụ "nhảy" giữa các panel liên tiếp (continuity trong cảnh) |
| **Thoại** | Giọng, xưng hô, mức lịch sự; thoại khớp với người nói? | Nhân vật xưng "tôi" rồi "tao" với cùng người nghe không rõ lý do | Gán bóng thoại sai người nói; thứ tự đọc |
| **Thuật ngữ** | Một khái niệm có một tên, một nghĩa? | "Chakra" và "năng lượng" dùng lẫn lộn | Có thêm vấn đề bản dịch và furigana |
| **Quy luật thế giới** | Luật phép thuật/vật lý/xã hội có giữ? | Phép dịch chuyển "cần 1 ngày hồi" nhưng dùng liên tục | Hệ thống sức mạnh là điểm mạnh hay yếu của thể loại shounen |

Hai điểm đáng chú ý:

1. **Nhất quán trong cảnh** (panel này với panel kế) khác **nhất quán xuyên chương** (chương 3 với chương 30). Đội sản xuất xử lý hai việc này ở các thời điểm khác nhau. **[Thông lệ]**
2. Theo [ConStory-Bench](https://arxiv.org/html/2603.05890v1), lỗi nhất quán ở truyện dài do LLM tạo ra tập trung nhiều ở nhóm thực tế và thời gian, và hay xuất hiện ở giữa truyện. **[Nguồn]** Đây là kết quả với truyện văn bản do LLM viết, không phải manga; con người viết cũng dễ mắc lỗi tương tự khi truyện dài (**[Suy luận]**).

---

## 2. Bảng quy tắc nhất quán thường dùng

Mỗi quy tắc: ví dụ vi phạm, trường hợp hợp lệ, ngoại lệ, nguồn. Cột cuối cho biết đây là thông lệ nghề hay đề xuất.

| # | Quy tắc | Vi phạm | Hợp lệ | Ngoại lệ | Loại / nguồn |
|---|---|---|---|---|---|
| R1 | **Người chết không xuất hiện sau cái chết** trừ khi có lý do ghi nhận | Nhân vật chết ở ch.5, đối thoại trực tiếp ở ch.9 | Hiện ở hồi tưởng, giấc mơ, ảo giác, hồi sinh có giải thích | Hồi sinh/giả chết (phải ghi trong Bible là sự kiện canon) | Story Skills mã hóa quy tắc này: trường `death` và `mentions`, [tài liệu](https://github.com/danjdewhurst/story-skills). **[Nguồn]** |
| R2 | **Payoff không đến trước setup** | Lời giải ở ch.8, câu đố mới được đặt ở ch.9 | Thu hoạch ngay trong cùng chương | Cố ý đảo thứ tự (kể ngược) | Story Skills: `promises` với `planted`/`payoff`. **[Nguồn]** Nguyên lý "mầm trước, quả sau" là **[Thông lệ]** |
| R3 | **Câu hỏi mở phải được giải hoặc chủ ý để mở** | Bí ẩn đặt ra rồi quên | Chủ ý để mở cho mùa sau | Truyện kết mở nghệ thuật | Story Skills: `questions`, cảnh báo khi quá lâu chưa giải. **[Nguồn]** |
| R4 | **Trạng thái nhân vật/đạo cụ chỉ đổi qua sự kiện** | Kiếm gãy rồi nguyên vẹn không giải thích | Sửa kiếm được mô tả | Phép hoặc quy luật thế giới cho phép | Story Skills: trường `state` cho nhân vật, vật, kiến thức. **[Nguồn]** |
| R5 | **Kiến thức nhân vật không vượt quá thông tin họ đã nhận** | Nhân vật nói về bí mật chưa ai kể | Suy luận hợp lý; thông tin nghe lén có ghi nhận | Nhân vật có năng lực đọc tâm/tiên tri | **[Thông lệ]**; **[Suy luận]**: tách "độc giả biết" với "nhân vật biết" là cách kể dramatic irony |
| R6 | **Một thực thể, một tên** (thuật ngữ) | "Hắc Long" và "Rồng Đen" cho cùng thứ | Biệt danh/bí danh đã đăng ký | Nhân vật gọi sai có chủ ý | [Novelcrafter Codex](https://www.novelcrafter.com/features/codex) nhận diện tên, bí danh, biệt danh để liên kết. **[Nguồn, từ tài liệu quảng bá]** |
| R7 | **Ngoại hình cố định theo "model sheet"**; thay đổi chỉ khi có lý do | Màu tóc đổi, không có biến cố | Đổi kiểu tóc, thay đồ, bị thương | Hồi tưởng thời nhỏ (khác thiết kế), có ghi chú | **[Thông lệ]** (model sheet là công cụ nghề của anime/manga; không có nguồn chuẩn để dẫn) |
| R8 | **Đạo cụ giữ vị trí giữa các panel liên tiếp** | Cốc ở tay trái panel 2, tay phải panel 3 | Nhân vật đổi tay, có hành động | Cắt cảnh, nhảy thời gian | **[Thông lệ]** |
| R9 | **Timeline đơn điệu** trừ khi đánh dấu rõ hồi tưởng/đảo thời gian | Ngày ch.6 sớm hơn ch.5 | Hồi tưởng có đánh dấu | Truyện phi tuyến tính | Story Skills có `date`/`time` cho timeline. **[Nguồn]** |
| R10 | **Thoại gán đúng người nói** | Bóng thoại chỉ về nhân vật A, lời của B | Lời ngoài khung (narration), thoại từ ngoài panel có đuôi bóng thoại | Thoại tập thể | Bài toán gán thoại cho người nói được nghiên cứu ở [Manga Whisperer / Magi](https://openaccess.thecvf.com/content/CVPR2024/papers/Sachdeva_The_Manga_Whisperer_Automatically_Generating_Transcriptions_for_Comics_CVPR_2024_paper.pdf). **[Nguồn]**; quy tắc nghiệp vụ: **[Thông lệ]** |
| R11 | **Quy luật thế giới áp dụng đồng đều** | Luật "phép cần mặt trăng" bị bỏ qua giữa chừng | Có ngoại lệ đã giải thích | Điều kiện đặc biệt đã ghi trước | **[Thông lệ]** |
| R12 | **Mâu thuẫn đã duyệt có chủ ý phải được ghi "miễn trừ"** | Báo lỗi lặp lại cho cùng một chỗ chủ ý | Lỗi đã được biên tập viên chấp nhận | — | Story Skills: `continuity/exemptions.md` cho phát hiện bị bác. **[Nguồn]** Ý tưởng áp dụng vào manga: **[Đề xuất]** |
| R13 | **Mỗi retcon phải có ghi nhận** | Đổi quá khứ nhân vật mà không để lại dấu vết | Retcon có giải thích, ghi vào Bible | — | Phân biệt "continuity error" với "revision" ở [TV Tropes: Retcon](https://tvtropes.org/pmwiki/pmwiki.php/Main/Retcon). **[Nguồn, wiki]**; quy tắc "phải ghi" là **[Đề xuất]** |

**Nhận xét quan trọng**: Story Skills, theo tài liệu, coi mâu thuẫn "như trình biên dịch coi lỗi kiểu" và dùng **kiểm tra xác định (deterministic)** thay vì nhờ LLM đoán ([README](https://github.com/danjdewhurst/story-skills)). **[Nguồn]** Hướng này hợp với các quy tắc có thể mã hóa thành dữ liệu (R1–R4, R9). Các quy tắc hình ảnh (R7, R8) không mã hóa kiểu này được dễ dàng. **[Suy luận]**

---

## 3. Series Bible tổ chức thế nào?

### 3.1 Ba trục phân loại

**Trục A: mức độ chắc chắn (trạng thái xác nhận)** **[Đề xuất]**, dựa trên thông lệ phân biệt canon và không canon (xem [Loose Canon](https://tvtropes.org/pmwiki/pmwiki.php/Main/LooseCanon), [Unreliable Canon](https://tvtropes.org/pmwiki/pmwiki.php/Main/UnreliableCanon)):

| Trạng thái | Nghĩa | Ai được thay đổi | Có ràng buộc các role khác? |
|---|---|---|---|
| **Canon** | Đã duyệt, là sự thật của truyện | Editor/Producer | Có |
| **Ý tưởng (idea)** | Có thể dùng, chưa cam kết | Writer | Không |
| **Giả thuyết (hypothesis)** | Ghi nhận để kiểm tra, ví dụ "nhân vật có thể là em của X" | Writer/Editor | Không, nhưng nên cảnh báo nếu nội dung mâu thuẫn |
| **Bị loại (deprecated)** | Từng canon, đã thay (retcon) | Editor | Chỉ để tra cứu lịch sử |

**Trục B: bất biến và thay đổi theo thời gian** **[Suy luận]**, tương ứng "progressions" của Novelcrafter ([Codex](https://www.novelcrafter.com/features/codex): theo dõi thay đổi của nhân vật, địa điểm theo thời gian) **[Nguồn]**:

- **Cố định (identity)**: tên, ngày sinh, thiết kế gốc, nhóm máu.
- **Trạng thái (state)**: vị trí, thương tích, đồ đang cầm, mối quan hệ, ai biết gì. Mỗi giá trị có **mốc hiệu lực** (từ chương/trang nào, đến khi nào).

**Trục C: ai biết** **[Suy luận]**:

| Loại | Ví dụ |
|---|---|
| **Sự thật thế giới** | Hung thủ thật là B |
| **Kiến thức nhân vật** | A tin hung thủ là C; D chưa từng thấy B |
| **Kiến thức độc giả** | Độc giả đã được cho biết B là hung thủ ở ch.12 |

Chia 3 loại này giúp phân biệt "mâu thuẫn" với "kịch tính": độc giả biết mà nhân vật không biết là kỹ thuật kể chuyện hợp lệ, không phải lỗi. Story Skills theo dõi trạng thái "kiến thức" trong trường `state` ([nguồn](https://github.com/danjdewhurst/story-skills)). **[Nguồn]** Việc tách riêng "kiến thức độc giả" là **[Đề xuất]**.

### 3.2 Cấu trúc Bible mẫu cho PanelForge

Sáu nhóm theo yêu cầu: **Characters, Locations, Props, Terminology, Style Rules, Plot Facts**. Mẫu dưới đây là **[Đề xuất]**.

```
SERIES BIBLE
├── Characters
│   ├── Cố định: tên, bí danh, tuổi gốc, thiết kế gốc (ảnh tham chiếu), giọng nói (xưng hô, từ khẩu ngữ)
│   ├── Trạng thái theo mốc: ngoại hình hiện tại, thương tích, đồ đang mang, quan hệ
│   └── Kiến thức: danh sách điều nhân vật biết, kèm "biết từ chương nào"
├── Locations
│   ├── Cố định: bố cục, kiến trúc, ảnh tham chiếu
│   └── Trạng thái: hư hại, mùa, ai đang ở đó
├── Props
│   ├── Cố định: thiết kế, kích thước, công năng
│   └── Trạng thái: chủ sở hữu, vị trí, hư hỏng
├── Terminology
│   ├── Thuật ngữ chuẩn + bí danh được phép + cách dùng + bản dịch
│   └── Từ cấm / viết tắt không dùng
├── Style Rules
│   ├── Quy tắc hình ảnh: tỉ lệ, đường nét, bảng màu, cách vẽ cảnh hồi tưởng
│   └── Quy tắc chữ: font thoại, bóng thoại cho từng nhân vật, hiệu ứng âm thanh
└── Plot Facts
    ├── Sự kiện: nội dung, chương, thứ tự, thời điểm
    ├── Setup/Payoff đang mở, câu hỏi chưa giải
    └── Quy luật thế giới (có điều kiện + ngoại lệ đã biết)
```

Mỗi mục có các trường chung: `trạng thái xác nhận` (canon/ý tưởng/giả thuyết/bị loại), `hiệu lực từ-đến`, `nguồn` (chương/trang gốc), `người đề xuất`, `người duyệt`.

**Cấu trúc của các sản phẩm tham chiếu** (để PanelForge không sao chép nguyên):

| Công cụ | Cấu trúc được mô tả | Bằng chứng |
|---|---|---|
| Novelcrafter Codex | Characters, Locations, Lore, Objects/Items, Subplots + danh mục tùy chỉnh; nhận diện tên/bí danh; Progressions | Trang tính năng [Codex](https://www.novelcrafter.com/features/codex) **[Nguồn, tài liệu quảng bá]** |
| xnovelist | Characters, locations, voice/style ("continuity" được nhắc đến) | [README](https://github.com/giapnguyen74/xnovelist) **[Nguồn]**; xem mục 7 để biết phần nào là lộ trình |
| Story Skills | Hồ sơ nhân vật, thế giới, cốt truyện dạng markdown + frontmatter có kiểu | [README](https://github.com/danjdewhurst/story-skills) **[Nguồn]** |

---

## 4. Xử lý ngoại lệ

### 4.1 Nhận định cần kiểm chứng: "Hồi tưởng phải retcon Bible"

**Kết luận: không mặc định đúng.** **[Suy luận]**, dựa trên định nghĩa:

- Theo [TV Tropes: Retcon](https://tvtropes.org/pmwiki/pmwiki.php/Main/Retcon), retcon là thay đổi thông tin đã được thiết lập, khác với việc mở rộng nó. Đồng thời trang này phân biệt **continuity error** (sai sót) với **revision** (thay đổi vẫn nằm trong tinh thần ban đầu, có thể kèm lý do như người kể không đáng tin). **[Nguồn, wiki]**
- Hồi tưởng chỉ là **cách trình bày** một sự kiện quá khứ. Việc nó có đổi canon hay không phụ thuộc vào nội dung hồi tưởng so với những gì đã có.

Phân biệt ba loại hồi tưởng:

| Loại | Đặc điểm | Hành động với Bible |
|---|---|---|
| **Hồi tưởng phù hợp canon** | Thêm chi tiết nhưng không mâu thuẫn dữ kiện đã có (kể rõ hơn một cảnh đã nhắc qua) | **Bổ sung** Plot Facts mới (canon mới). Không phải retcon. |
| **Hồi tưởng làm đổi canon** | Mâu thuẫn dữ kiện đã duyệt (ch.2 nói A đến muộn, hồi tưởng ở ch.20 cho thấy A có mặt) | **Retcon**: phải quyết định sửa chương cũ, hoặc giải thích (người kể sai), hoặc giữ mâu thuẫn như lỗi. |
| **Hồi tưởng không phải sự thật** | Theo góc nhìn nhân vật nói dối hoặc nhớ sai; truyện đánh dấu rõ | **Không đổi canon**. Ghi nó là "lời kể của X" (nguồn nhận thức), không phải sự thật thế giới. |

Quy tắc hữu ích: **hồi tưởng có hai câu hỏi độc lập**: (1) nó có mâu thuẫn canon hiện có không? (2) ai là người kể, và có đáng tin không? Chỉ khi "có mâu thuẫn" và "người kể đáng tin / truyện khẳng định là thật" thì mới thành retcon thực sự. **[Đề xuất]**

### 4.2 Bảng ngoại lệ

| Ngoại lệ | Vì sao hợp lệ | Cách ghi vào Bible | Dấu hiệu cần cảnh báo |
|---|---|---|---|
| **Hồi tưởng** | Cảnh quá khứ; nhân vật chết hoặc ngoại hình cũ được phép | Gắn thẻ `flashback`, mốc thời gian quá khứ; tách khỏi trạng thái hiện tại | Chi tiết trái với Plot Facts đã canon (xem 4.1) |
| **Giấc mơ** | Không phải sự kiện thật | Thẻ `dream`; sự kiện **không** vào Plot Facts trừ khi truyện nói giấc mơ báo trước/thật | Giấc mơ có hậu quả vật lý (vết thương) mà không giải thích |
| **Lời nói dối** | Nhân vật nói sai; độc giả có thể biết hoặc không | Ghi như "lời X nói" kèm sự thật thế giới đối lập | Lời nói dối bị coi là sự thật ở cảnh khác |
| **Người kể không đáng tin** | Góc nhìn thiên lệch có chủ ý | Đánh dấu nguồn kể là không đáng tin; chỉ coi là "kiến thức nhân vật" | Truyện không bao giờ cho thấy sự thật, dễ thành lỗi thực sự (xem [Unreliable Canon](https://tvtropes.org/pmwiki/pmwiki.php/Main/UnreliableCanon)) |
| **Hồi sinh** | Biến cố cốt truyện được thừa nhận | Sự kiện canon: `chết ch.5` → `hồi sinh ch.30` + lý do; ngừng áp R1 từ mốc này | Hồi sinh không có cơ chế, hoặc lặp lại quá nhiều |
| **Retcon** | Biên tập thay đổi canon | Mục cũ chuyển `deprecated` kèm lý do và danh sách chương bị ảnh hưởng; mục mới canon | Retcon im lặng, không ghi (xem R13) |

**Về kiểm tra tự động:** Story Skills cho phép liệt kê ngoại lệ có chủ ý (`mentions`, `exemptions.md`) ([nguồn](https://github.com/danjdewhurst/story-skills)). **[Nguồn]** Ý tưởng chung là hệ thống không nên cố "đoán" ý đồ; người duyệt tuyên bố ý đồ, hệ thống nhớ để không báo lỗi lặp. **[Suy luận]**

---

## 5. Từng role kiểm tra gì, lúc nào?

Năm role của PanelForge: **Producer, Writer, Artist, Letterer, Editor**.

### 5.1 Trách nhiệm theo role

Đây là **[Đề xuất]** gắn vào các thông lệ chung (script → name/storyboard → artwork → lettering → review). Tôi không có tài liệu chuẩn mô tả đúng cách chia trách nhiệm này.

| Role | Kiểm tra chính | Dữ liệu Bible dùng | Cổng (lúc nào) |
|---|---|---|---|
| **Producer** | Tiến độ, phân công; người chốt khi bất đồng; không sửa "sự thật" một mình | Trạng thái canon/retcon, danh sách mục chờ duyệt | Mọi cổng (điều phối) |
| **Writer** | Cốt truyện, timeline, kiến thức nhân vật, thoại, thuật ngữ, quy luật thế giới. Đề xuất Plot Facts mới | Plot Facts, Characters (kiến thức), Terminology | **Script**: trước khi giao sang storyboard |
| **Artist** | Hình ảnh: ngoại hình, địa điểm, đạo cụ, phục trang, nhất quán trong cảnh | Characters (cố định + trạng thái), Locations, Props, Style Rules | **Storyboard/artwork**: đối chiếu model sheet |
| **Letterer** | Thoại: gán đúng người nói, xưng hô, thuật ngữ, font bóng thoại, thứ tự đọc | Terminology, giọng nhân vật, Style Rules (chữ) | **Lettering**: sau khi artwork chốt |
| **Editor** | Duyệt xuyên chương: mọi loại; phân xử canon/retcon; chấp nhận miễn trừ | Toàn bộ Bible | **Review**: cổng cuối, cộng cổng sau script |

### 5.2 Luồng kiểm tra theo giai đoạn

```
Script ──► Storyboard ──► Artwork ──► Lettering ──► Review ──► Duyệt
 Writer      Artist         Artist      Letterer     Editor
 (cốt truyện,(bố cục, nhất  (ngoại hình, (thoại,      (xuyên chương,
  timeline,   quán trong     đạo cụ,     thuật ngữ,   phân xử)
  kiến thức)  cảnh)          địa điểm)   người nói)
```

Nguyên tắc: **lỗi phát hiện càng sớm, sửa càng rẻ.** Sửa một dòng trong script rẻ hơn vẽ lại trang đã đi nét. **[Thông lệ]**; các blog như [Mangaka Blog](https://www.mangaka.app/blogs/manga-storyboard-workflow-guide-2026-complete-guide) cũng nhấn mạnh duyệt storyboard trước khi vẽ line art để tránh sửa lại (blog thương mại, tin cậy trung bình). **[Nguồn]**

### 5.3 Giải quyết bất đồng

Ví dụ: Artist cho rằng nhân vật phải đeo kiếm (theo Bible), Writer nói đã bỏ kiếm ở chương trước. **[Đề xuất]**:

1. **Xác định loại bất đồng**: lỗi (một bên sai so với Bible), hay thay đổi canon (Bible lỗi thời)?
2. **Nếu Bible rõ ràng**: Bible thắng; bên sai sửa.
3. **Nếu Bible mơ hồ hoặc lỗi thời**: Writer đề xuất cập nhật (trạng thái "giả thuyết" hoặc "ý tưởng"), Editor duyệt thành canon.
4. **Nếu hai bên đều có lý**: Producer chốt, ghi lý do trong mục.
5. **Nếu chủ ý chấp nhận mâu thuẫn**: ghi miễn trừ (R12).

Điểm mấu chốt: **mọi quyết định để lại dấu vết**. Đa số tranh cãi lặp lại vì không ai nhớ lần trước chốt gì. **[Suy luận]**

---

## 6. AI hỗ trợ bằng phương pháp nào?

Giải thích bằng ngôn ngữ đơn giản. Mức độ phù hợp với PanelForge là **[Suy luận]**.

| Phương pháp | Nói đơn giản | Mạnh ở đâu | Yếu ở đâu |
|---|---|---|---|
| **Dữ liệu có cấu trúc** | Ghi sự thật thành bảng/trường: "A chết ở ch.5" | Chính xác, tra cứu được, nền tảng cho mọi cách khác | Phải có người nhập; không "hiểu" văn bản tự do |
| **Rule-based** | Máy đối chiếu bảng với quy tắc cứng: "người chết xuất hiện sau ch.5 → báo lỗi" | Xác định, giải thích được, không bịa ([Story Skills](https://github.com/danjdewhurst/story-skills) làm theo hướng này **[Nguồn]**) | Chỉ bắt được lỗi đã viết thành luật và nằm trong dữ liệu có cấu trúc |
| **RAG** | Máy tìm đoạn Bible/chương liên quan rồi đưa cho LLM đọc trước khi trả lời. Nền tảng: [Lewis et al. 2020](https://arxiv.org/abs/2005.11401) | Mở rộng đến văn bản dài, có dẫn chứng | Tìm sai đoạn thì kết luận sai; bỏ sót mâu thuẫn nằm ở nhiều nơi |
| **LLM** | Mô hình ngôn ngữ đọc và nhận xét | Hiểu ngữ cảnh, bắt lỗi mơ hồ (giọng, mâu thuẫn ngầm) | Có thể bịa lỗi hoặc bỏ sót. Story Skills tự nói mô hình ngôn ngữ kém giữ nhất quán dài hạn ([README](https://github.com/danjdewhurst/story-skills)) **[Nguồn]**; [ConStory-Bench](https://arxiv.org/html/2603.05890v1) đo được lỗi nhất quán ở truyện do LLM viết **[Nguồn]** |
| **Kiểm tra hình ảnh** | Mô hình thị giác phát hiện/so khớp nhân vật, chữ, bóng thoại, panel | [Magi](https://openaccess.thecvf.com/content/CVPR2024/papers/Sachdeva_The_Manga_Whisperer_Automatically_Generating_Transcriptions_for_Comics_CVPR_2024_paper.pdf) phát hiện nhân vật/panel/chữ, gom nhân vật theo danh tính, gán thoại cho người nói; mở rộng ở [Tails Tell Tales](https://arxiv.org/pdf/2408.00298) (tên nhân vật xuyên chương) **[Nguồn]** | So khớp *chi tiết nhỏ* (đúng sẹo bên nào, đúng hoa văn áo) chưa thấy bằng chứng đã giải quyết tốt; độ chính xác trên bản vẽ riêng của dự án chưa biết. **[Suy luận]** |

**Gợi ý ghép**: dữ liệu có cấu trúc làm nền, rule-based bắt lỗi chắc chắn, RAG + LLM xử lý lỗi mơ hồ **ở dạng gợi ý để người duyệt**, hình ảnh ở mức hỗ trợ. **[Đề xuất]**

### Cách đo chất lượng **[Đề xuất]**

Theo tinh thần đánh giá của [ConStory-Bench](https://arxiv.org/html/2603.05890v1) (đo mật độ lỗi, có bằng chứng dẫn kèm):

| Chỉ số | Ý nghĩa | Vì sao quan trọng |
|---|---|---|
| **Recall** | Trong N lỗi cài sẵn, bắt được bao nhiêu | Bỏ sót lỗi làm mất niềm tin |
| **Precision** | Trong cảnh báo, bao nhiêu là lỗi thật | Báo nhiều quá thì đội bỏ qua |
| **Tỉ lệ bác của người duyệt** | Editor bác bao nhiêu cảnh báo | Chỉ báo thực tế |
| **Có dẫn chứng** | Cảnh báo nêu rõ đoạn nào mâu thuẫn đoạn nào | Người duyệt kiểm được trong vài giây |
| **Phân tách theo loại** | Đo riêng cốt truyện, hình ảnh, thoại... | Mỗi loại khác nhau về độ khó |

Cách thử: lấy một bộ truyện ngắn đã biết, **cài lỗi có chủ ý** (như ví dụ ở mục 8.3), rồi đếm. Story Skills dùng cách tương tự, có dự án cố ý hỏng để thử ([README](https://github.com/danjdewhurst/story-skills)). **[Nguồn]**

---

## 7. Kiểm chứng ba nguồn: tài liệu mô tả, khả năng có bằng chứng, chưa thử nghiệm

Ghi chú phương pháp: tôi đọc README và trang tính năng. Tôi **không cài đặt hay chạy** công cụ nào, nên không có kết luận về **hiệu quả thực tế**. Việc thiếu bằng chứng dưới đây **không** có nghĩa công cụ "không làm được".

### 7.1 Story Skills ([danjdewhurst/story-skills](https://github.com/danjdewhurst/story-skills))

| Loại | Nội dung |
|---|---|
| **Tài liệu mô tả** | Bộ 24 skill viết truyện bằng markdown cho Claude Code/Codex và các agent khác. "Continuity engine" coi mâu thuẫn như lỗi biên dịch. Theo dõi: `death`, `promises` (`planted`/`payoff`), `questions`, `cast` của cảnh, `state` (nhân vật, vật, kiến thức), `date`/`time`. Mức độ: error (mâu thuẫn), warning (chưa giải), dismissed (ghi trong `continuity/exemptions.md`). |
| **Khả năng có bằng chứng** | Repo có thư mục `evals/` (kiểm thử hồi quy theo fixture), schema JSON, năm dự án ví dụ và một dự án cố ý hỏng (`the-unraveled-thread`), bộ test CLI. Đây là bằng chứng mạnh nhất trong ba công cụ rằng cơ chế được kiểm thử có hệ thống. |
| **Chưa thử nghiệm / chưa biết** | Tôi chưa chạy `evals/` nên chưa biết kết quả. Chưa thấy đánh giá độc lập. Dùng cho **tiểu thuyết**, không có bằng chứng cho manga/hình ảnh. Khả năng bắt lỗi hình ảnh: tài liệu không đề cập (**không** suy ra là không làm được). |

### 7.2 xnovelist ([giapnguyen74/xnovelist](https://github.com/giapnguyen74/xnovelist))

| Loại | Nội dung |
|---|---|
| **Tài liệu mô tả** | Trình soạn thảo chạy hoàn toàn trên trình duyệt, dữ liệu lưu cục bộ, không backend, dùng khóa API riêng hoặc mô hình cục bộ (Ollama, LM Studio). Có Story Bible (characters, locations, voice/style). README nhắc "check continuity" như khả năng AI cấp 1 và "style/continuity-aware context". |
| **Khả năng có bằng chứng** | Theo README, bản đã phát hành chỉ có workspace bible nhân vật/địa điểm (v0.1). |
| **Chưa thử nghiệm / chưa phát hành** | Các cơ chế bible/continuity còn lại là **lộ trình** (v0.4–1.0). Cơ chế kiểm tra và cách chọn ngữ cảnh đưa vào AI: phần tôi đọc không mô tả. Tôi chưa xem tài liệu kiến trúc nên **chưa kết luận** về cách hoạt động. |

### 7.3 Novelcrafter ([Codex](https://www.novelcrafter.com/features/codex))

| Loại | Nội dung |
|---|---|
| **Tài liệu mô tả** | Codex gồm Characters, Locations, Lore, Objects/Items, Subplots, danh mục tùy chỉnh. Nhận diện tên, bí danh, biệt danh khi gõ. Progressions theo dõi thay đổi theo timeline. Codex là "nguồn sự thật" cho AI; trang quảng bá nói AI "tuân thủ các quy tắc trong Codex". Có [Codex Recipes](https://www.novelcrafter.com/courses/codex-cookbook/tracking-character-arcs) hướng dẫn theo dõi nhân vật qua nhiều tập. |
| **Khả năng có bằng chứng** | Có tài liệu hướng dẫn và nhiều bài đánh giá của người dùng/blog (ví dụ [Kindlepreneur](https://kindlepreneur.com/novelcrafter-review/)). Chúng cho thấy tính năng tồn tại và dùng được, nhưng là đánh giá cảm nhận, không phải đo đạc. |
| **Chưa thử nghiệm / chưa biết** | "Smart Highlighting" phát hiện mâu thuẫn không có mô tả cơ chế chi tiết. Không có số liệu độ chính xác. Chưa kiểm tra AI có thực sự giữ đúng quy tắc Codex hay không. Các đánh giá bên thứ ba tôi chỉ thấy qua kết quả tìm kiếm, **chưa đọc đầy đủ**. |

**So sánh** **[Suy luận]**: ba công cụ chia sẻ ý tưởng "sự thật có cấu trúc làm nền, AI dùng nó làm ngữ cảnh". Chỉ Story Skills công bố cơ chế kiểm tra và bộ thử nghiệm mà tôi thấy được; hai công cụ còn lại thiếu mô tả cơ chế hoặc chưa phát hành phần liên quan. Cả ba đều không phục vụ manga (hình ảnh, bóng thoại, phân công role).

---

## 8. PanelForge nên áp dụng những gì?

Đề xuất ở mức nghiệp vụ. Toàn bộ mục này là **[Đề xuất]**.

### 8.1 Phần tối thiểu (làm trước)

| Chức năng | Mô tả nghiệp vụ | Lý do |
|---|---|---|
| **Bible có trạng thái xác nhận** | Mỗi mục là canon / ý tưởng / giả thuyết / bị loại | Tránh coi ý tưởng của Writer là luật |
| **Trạng thái theo mốc** | Trạng thái nhân vật/đạo cụ có "từ chương nào" | Nền tảng cho kiểm tra timeline |
| **Kiểm tra theo luật cứng** | R1, R2, R3, R9 (người chết, payoff trước setup, câu hỏi mở, thứ tự thời gian) | Xác định, giải thích được, ít cảnh báo giả |
| **Kiểm tra thuật ngữ** | Phát hiện tên lạ so với danh sách chuẩn và bí danh | Letterer/Writer dùng được ngay |
| **Thẻ ngoại lệ** | Gắn hồi tưởng/giấc mơ/lời nói dối vào đoạn, và miễn trừ có lý do | Giảm cảnh báo giả |
| **Nhật ký quyết định** | Mỗi lần duyệt/bác/retcon lưu lý do | Tránh tranh cãi lặp |
| **Danh sách kiểm tra theo role** | Mỗi cổng hiển thị đúng mục Bible liên quan role đó | Giảm đọc dư |

### 8.2 Phần nâng cao (sau, cần đo đạc)

| Chức năng | Mô tả | Cần cẩn trọng |
|---|---|---|
| **Gợi ý bằng RAG + LLM** | Tìm mâu thuẫn mơ hồ giữa script mới và Bible/chương cũ, kèm trích dẫn | Chỉ **gợi ý**, người duyệt quyết định; đo precision/recall trước |
| **Kiểm tra hình ảnh** | Đối chiếu nhân vật với model sheet; kiểm tra gán thoại (hướng [Magi](https://arxiv.org/pdf/2408.00298)) | Chưa biết độ chính xác trên dữ liệu riêng; thử trên mẫu nhỏ trước |
| **Phân tích tác động retcon** | Khi đổi một canon, liệt kê chương/trang bị ảnh hưởng | Cần liên kết sự kiện với chương ở dạng dữ liệu |
| **Đề xuất cập nhật Bible từ script** | LLM đọc script, đề xuất Plot Facts mới ở trạng thái "giả thuyết" | Không bao giờ tự thành canon |
| **Kiến thức độc giả** | Theo dõi độc giả đã biết gì, để Writer cân bằng tiết lộ | Giá trị nghiệp vụ cao nhưng cần quy ước nhập liệu |

### 8.3 Ví dụ xuyên suốt nhiều chương

Truyện giả định "**Hồ Gương**" (hư cấu để minh họa). Nhân vật: **Lan** (nữ chính), **Minh** (người bạn), **Hùng** (kẻ phản diện). Đạo cụ: **gương cổ**. Quy luật: *người nhìn vào gương cổ vào ban đêm sẽ thấy quá khứ*.

| Chương | Sự kiện | Cập nhật / cảnh báo | Xử lý |
|---|---|---|---|
| **Ch.1** | Lan nhặt gương cổ nứt góc phải | Props: gương cổ, `nứt góc phải`, chủ: Lan. Plot Fact canon: "gương cho thấy quá khứ khi nhìn ban đêm" | Artist lưu ảnh model sheet gương |
| **Ch.2** | Minh bị nhốt trong tầng hầm; Lan chưa biết | Kiến thức: Minh biết Hùng là chủ nhà; Lan **chưa biết** | — |
| **Ch.3** | Minh chết | Minh: `chết ch.3` | R1 bắt đầu áp dụng |
| **Ch.5** | Cảnh Minh nói chuyện với Lan | **Cảnh báo R1** (Minh đã chết) | Writer xác nhận **hồi tưởng phù hợp canon**: gắn thẻ `flashback`, không vào Plot Facts mới. Không phải retcon. |
| **Ch.8** | Artist vẽ vết nứt gương bên **trái** | **Cảnh báo hình ảnh** (R7/R8) so với model sheet | Editor xác nhận lỗi thật; Artist sửa |
| **Ch.12** | Lan nói "Hùng là chủ nhà" khi chưa ai kể | **Cảnh báo R5** (kiến thức nhân vật) | Writer thêm cảnh Lan nghe lén ở ch.11; cảnh báo đóng |
| **Ch.15** | Hồi tưởng cho thấy Minh còn sống sau ch.3 | **Cảnh báo R1 + mâu thuẫn Plot Fact** | Editor chọn **retcon** ("Minh giả chết"). Mục cũ "Minh chết ch.3" chuyển `deprecated`, tạo canon mới "Minh giả chết ch.3, lộ diện ch.15". Hệ thống liệt kê ch.3–14 chịu ảnh hưởng để rà lại |
| **Ch.18** | Hùng nói dối: "Tôi không biết cái gương" | Ghi như lời của Hùng; sự thật thế giới: Hùng biết gương | Không báo lỗi; ghi "lời nói dối" |
| **Ch.20** | Lan dùng gương **ban ngày** | **Cảnh báo quy luật thế giới** | Writer xác nhận ngoại lệ (nứt gương mở rộng khả năng); thêm ngoại lệ có điều kiện vào luật |

Từ ví dụ này:
- Cùng một "mâu thuẫn bề mặt" (người chết xuất hiện) có thể là hồi tưởng hợp lệ (ch.5) hoặc retcon (ch.15). Hệ thống chỉ **hỏi**; người duyệt quyết định (xem 4.1).
- Role phát hiện theo giai đoạn: Writer (ch.12, script), Artist/Editor (ch.8, artwork), Editor (ch.15, review).
- Mọi quyết định để lại dấu vết (ngoại lệ, retcon, lời nói dối).

---

## 9. Tóm tắt nhận định chính

1. Nhất quán gồm ít nhất chín loại, mỗi loại cần cách kiểm tra khác nhau. Lỗi thực tế và thời gian phổ biến ở truyện dài do LLM viết ([ConStory-Bench](https://arxiv.org/html/2603.05890v1)).
2. Các quy tắc mã hóa được (người chết, setup/payoff, trạng thái, timeline) hợp với kiểm tra xác định, như [Story Skills](https://github.com/danjdewhurst/story-skills) mô tả.
3. Bible nên phân trạng thái xác nhận, tách bất biến với thay đổi theo mốc, và tách sự thật thế giới với kiến thức nhân vật/độc giả.
4. "Hồi tưởng phải retcon Bible" **không** mặc định đúng: chỉ hồi tưởng làm đổi canon đã duyệt mới là retcon.
5. AI hợp ở vai **gợi ý có dẫn chứng**; luật cứng làm nền; kiểm tra hình ảnh còn ở mức nghiên cứu ([Magi](https://openaccess.thecvf.com/content/CVPR2024/papers/Sachdeva_The_Manga_Whisperer_Automatically_Generating_Transcriptions_for_Comics_CVPR_2024_paper.pdf)).
6. Ba công cụ tham chiếu chưa được tôi chạy thử; phần "chưa biết" ở mục 7 cần đánh giá thực nghiệm nếu muốn kết luận chắc hơn.

## 10. Danh sách nguồn

- Story Skills: https://github.com/danjdewhurst/story-skills
- xnovelist: https://github.com/giapnguyen74/xnovelist
- Novelcrafter Codex: https://www.novelcrafter.com/features/codex
- Novelcrafter Codex Recipes: https://www.novelcrafter.com/courses/codex-cookbook/tracking-character-arcs
- ConStory-Bench ("Lost in Stories"): https://arxiv.org/html/2603.05890v1
- Manga Whisperer (Magi), CVPR 2024: https://openaccess.thecvf.com/content/CVPR2024/papers/Sachdeva_The_Manga_Whisperer_Automatically_Generating_Transcriptions_for_Comics_CVPR_2024_paper.pdf
- Tails Tell Tales: https://arxiv.org/pdf/2408.00298
- RAG (Lewis et al. 2020): https://arxiv.org/abs/2005.11401 (từ trí nhớ về bài báo gốc, chưa mở lại trang trong phiên này)
- TV Tropes (Retcon, Loose Canon, Unreliable Canon): https://tvtropes.org/pmwiki/pmwiki.php/Main/Retcon
- Quy trình storyboard (blog thương mại, tin cậy trung bình): https://www.mangaka.app/blogs/manga-storyboard-workflow-guide-2026-complete-guide
