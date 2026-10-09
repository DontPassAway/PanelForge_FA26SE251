# PanelForge – Research nghiệp vụ & phương pháp đánh giá

> **Quy ước nhãn**: **[Nguồn]** = có dẫn chứng; **[Suy luận]** = suy ra từ nguồn/logic; **[Đề xuất]** = thiết kế riêng cho PanelForge; **[Giả thuyết]** = chưa kiểm chứng, cần phỏng vấn/thử nghiệm.
> **Giới hạn trung thực**: nguồn tra được chủ yếu là tài liệu thứ cấp (Wikipedia, blog nghề, bài tổng hợp), tôi mới đọc phần tóm tắt kết quả tìm kiếm, chưa đọc toàn văn từng trang. Chưa có khảo sát/phỏng vấn thật, nên **phần "khó khăn" phần lớn là giả thuyết**. Không có số liệu thị trường nào được bịa. Cần bổ sung nguồn nhà xuất bản và phỏng vấn thật trước khi bảo vệ.

---

## 0. Phân biệt manga / comic / webtoon

| | Manga (Nhật) | Comic phương Tây | Webtoon |
|---|---|---|---|
| Quy trình | Họa sĩ cùng biên tập viên làm *name* (storyboard thô) → duyệt → bản vẽ, có trợ lý ([Wikipedia](https://en.wikipedia.org/wiki/Manga_artist), [Web Japan](https://web-japan.org/trends/09_culture/pop110210.html)) | Chuỗi chuyền tay: writer → penciller → inker → colorist → letterer, editor điều phối ([Book Riot](https://bookriot.com/in-conversation-talking-about-drawinginkingcoloringlettering-comics/), [ComicBlocc](https://www.comicbloc.com/read/2011/11/can-you-spare-10-minutes/)) | Studio chia bộ phận: script, storyboard, nhân vật, nền, render ([Clip Studio](https://tips.clip-studio.com/en-us/articles/11512)) |
| Đơn vị | Trang/spread | Trang | Cuộn dọc theo episode |
| Hệ quả | Page→Panel khớp nhất | Pipeline tuần tự khớp nhất | Layout dọc: **ngoài phạm vi MVP** [Đề xuất] |

Pipeline "Script → Thumbnail → Pencil → Ink → Color → Letter → Review → Approved" là **hỗn hợp**: Script/Thumbnail gần manga, Pencil→Ink→Color→Letter gần comic Mỹ. Hợp lý nhưng cần nói rõ khi bảo vệ. [Suy luận]

---

## 1. Ai dùng PanelForge và khó khăn chính là gì?

### 1.1 Cách làm việc hiện nay
- **Manga**: biên tập viên giám sát hướng truyện, nhịp, layout, deadline; họa sĩ làm name/thumbnail rồi vẽ; trợ lý có phạm vi rất khác nhau tùy tác giả ([Wikipedia](https://en.wikipedia.org/wiki/Manga_artist)). **[Nguồn]**
- **Comic phương Tây**: editor chuyển công việc từ người này sang người kế tiếp và làm cầu nối khi có mâu thuẫn; trang được editor duyệt rồi mới sang inker ([ComicBlocc](https://www.comicbloc.com/read/2011/11/can-you-spare-10-minutes/), [Book Riot](https://bookriot.com/in-conversation-talking-about-drawinginkingcoloringlettering-comics/)). **[Nguồn]**
- **Webtoon/webcomic**: vẫn storyboard → sketch → ink → color → letter nhưng thường tinh gọn, ít vòng review phòng ban ([tổng hợp](https://thephantom.fan/comics/the-titles-and-roles-in-comic-book-production/), [Clip Studio](https://tips.clip-studio.com/en-us/articles/11512)). **[Nguồn]**
- **Công cụ thực tế** (Clip Studio, Photoshop, Drive/Dropbox, Discord/Slack, Notion/Trello): **[Giả thuyết]**, chưa có nguồn định lượng; cần hỏi khi phỏng vấn.

### 1.2 Bảng người dùng – khó khăn – giá trị

| Nhóm | Bối cảnh | Khó khăn (mức bằng chứng) | Giá trị PanelForge |
|---|---|---|---|
| **Sáng tác độc lập** (một người kiêm nhiều role) | Tự viết, vẽ, letter | Giữ continuity trong đầu/file rời; mất nhịp tiến độ **[Giả thuyết]** | Series Bible, AI nhắc continuity; ít cần phân quyền |
| **Nhóm nhỏ 2–6 người** | Cộng tác từ xa, tự tổ chức | Bàn giao qua chat/Drive → nhầm bản, không biết ai chờ ai, review rải rác **[Giả thuyết, hợp lý vì quy trình chuyền tay có trong nguồn trên]** | Pipeline trạng thái, giao việc, review gắn panel, lịch sử phiên bản |
| **Studio (>6, nhiều series)** | Có producer/editor chuyên trách | Phân công nhiều series, báo cáo tiến độ, bàn giao giữa bộ phận **[Suy luận từ mô hình studio Webtoon]** | Dashboard tiến độ, quyền theo role, audit trail |

### 1.3 Có bằng chứng vs giả thuyết
- **Có nguồn**: vai trò, thứ tự bàn giao, editor là điều phối viên.
- **Giả thuyết cần xác nhận**: (a) tần suất dùng nhầm phiên bản; (b) thời gian mất cho tìm file/review; (c) mức sẵn sàng dùng AI; (d) nhóm nhỏ có chịu đổi công cụ không; (e) lo ngại về AI và bản quyền.

### 1.4 Nhóm mục tiêu MVP: **nhóm nhỏ 2–6 người** [Đề xuất]
Lý do: (1) đau nhất ở **bàn giao và review**, cần hệ thống nhưng chưa cần hạ tầng studio; (2) dễ tuyển 5–8 người phỏng vấn/thử nghiệm trong một học kỳ; (3) đủ role để chứng minh pipeline; (4) sáng tác độc lập được phục vụ như trường hợp "một người nhiều role"; (5) studio để giai đoạn sau.

### 1.5 Câu hỏi phỏng vấn
Theo nguyên tắc *The Mom Test*: hỏi chuyện quá khứ cụ thể, không hỏi ý kiến về tương lai ([tóm tắt](https://mtlynch.io/book-reports/the-mom-test/)). Không bịa người tham gia hay kết quả; mục tiêu 5–8 người mỗi nhóm.

1. Kể về chương gần nhất bạn làm: ai tham gia, bắt đầu và kết thúc thế nào?
2. File chương đó nằm ở đâu? Lần gần nhất không tìm được hoặc dùng nhầm bản là khi nào? Mất bao lâu, hậu quả gì?
3. Lần gần nhất phải sửa sau khi đã "xong" một công đoạn: cái gì đổi, ai quyết, làm lại bao nhiêu?
4. Phản hồi review đến từ đâu (chat, comment, PDF)? Có lần nào sót phản hồi không?
5. Bạn biết ai đang chờ ai bằng cách nào? Mỗi tuần mất bao nhiêu cuộc họp/tin nhắn chỉ để hỏi tiến độ?
6. Lần gần nhất thiết lập nhân vật/địa điểm bị mâu thuẫn giữa các chương: phát hiện lúc nào, sửa thế nào?
7. Bạn đã thử công cụ nào (Trello, Notion, Git…)? Vì sao bỏ?
8. Bạn đã dùng AI cho việc gì trong truyện? Phần nào tuyệt đối không muốn AI chạm vào? Vì sao?
9. Bạn đã từng trả tiền hoặc bỏ thời gian để giải quyết vấn đề nào ở trên chưa?
10. Ai khác nên tôi nói chuyện cùng?

---

## 2. Mỗi role nhận gì, làm gì, bàn giao gì?

Bảng là **[Đề xuất]**, dựa trên thông lệ comic/manga. Thực tế khác nhau theo xưởng; ở manga, mangaka thường tự làm phần lớn việc từ name đến vẽ.

| Role | Đầu vào | Công việc | Bàn giao | Người nhận | Điều kiện bàn giao | Quyền quyết định |
|---|---|---|---|---|---|---|
| **Producer** | Ý tưởng, ngân sách, deadline | Tạo series/chapter, giao việc, theo dõi tiến độ, quản lý bible | Brief chương, lịch, phân công | Writer, cả đội | Brief đủ: mục tiêu, số trang, hạn | Lịch, nhân sự, ưu tiên; **không** quyết nghệ thuật |
| **Writer** | Brief, Series Bible | Cốt truyện, chia scene, script + thoại | Script đã chốt (scene/page/panel, thoại) | Artist, Letterer (thoại) | Editor duyệt script | **Cốt truyện, chia scene, thoại** |
| **Artist** | Script, Bible | Phân trang, layout, góc máy (thumbnail), rồi pencil/ink | Thumbnail → Pencil → Ink | Editor (duyệt thumbnail) → Colorist/Letterer | Thumbnail được duyệt trước khi vẽ sạch | **Phân trang, layout, góc máy, artwork** (trong khuôn script) |
| **Colorist** (tùy chọn) | Ink + bảng màu Bible | Tô màu | Trang màu | Letterer | Editor/Artist OK màu | Màu sắc theo style Bible |
| **Letterer** | Trang cuối + script thoại | Đặt bóng thoại, SFX, chữ | Trang đã letter | Editor | Thoại khớp script đã chốt | Vị trí/kiểu chữ; **không** sửa nội dung thoại |
| **Editor** | Mọi sản phẩm | Duyệt từng công đoạn, phản hồi, chuyển giai đoạn | Approve/Return + ghi chú | Role trước đó / Producer | Có tiêu chí review | **Duyệt cuối** từng công đoạn và chương |
| **External Reviewer** | Bản thảo được chia sẻ | Phản hồi | Comment | Editor | – | Chỉ góp ý, **không** quyết |
| **Public Reader** | Bản đã phát hành | Đọc | – | – | – | Không |

**Thông lệ vs đề xuất**: thông lệ là writer đưa script, editor duyệt, sản phẩm chuyền tuần tự ([ComicBlocc](https://www.comicbloc.com/read/2011/11/can-you-spare-10-minutes/)). Đề xuất riêng của PanelForge: gộp storyboard vào Artist (giống storyboarder trong mô hình Webtoon) và cổng duyệt thumbnail bắt buộc.

**Một người kiêm nhiều role** [Đề xuất]: cho phép gán nhiều role cho một tài khoản. Cổng duyệt vẫn được ghi nhận, tự duyệt được phép nhưng đánh dấu "self-approved", để giữ truy vết mà không chặn người làm một mình.

### Ví dụ xuyên suốt: "Chương 3 – Cuộc rượt đuổi", 12 trang
1. **Producer** tạo Chapter, brief 12 trang, hạn 3 tuần, giao Writer/Artist/Letterer.
2. **Writer** viết script 4 scene; **Editor** duyệt → script *Approved*.
3. **Artist** làm thumbnail; Editor yêu cầu sửa trang 5 (nhịp chậm) → trả lại **chỉ trang 5** → duyệt. External Reviewer xem bản thumbnail và để lại comment.
4. **Artist** pencil→ink theo từng trang đã duyệt, không chờ cả chương.
5. **Letterer** đặt thoại trên trang đã ink; Editor duyệt → *Approved*; Producer phát hành → Public Reader đọc. *(Phiếu V1.2: xuất bản làm ngoài hệ thống, Producer không có quan hệ với nhà xuất bản; Public Reader là tính năng thêm ngoài phạm vi đăng ký, nhóm quyết định giữ.)*

---

## 3. Khi nội dung thay đổi hoặc bị trả về

### 3.1 Nguyên tắc [Đề xuất]
- **Thay đổi nhỏ (không mất hiệu lực bản duyệt)**: không đổi ý nghĩa hay cấu trúc (lỗi chính tả, nudge bóng thoại, chỉnh nét). Ghi version mới, giữ nguyên trạng thái duyệt, chỉ thông báo.
- **Thay đổi lớn (làm mất hiệu lực)**: đổi nội dung/bố cục ảnh hưởng công đoạn sau. Thu hồi duyệt **chỉ của đối tượng bị ảnh hưởng** (panel/page) và các phần phụ thuộc trực tiếp.
- **Phạm vi làm lại tối thiểu**: tính theo phụ thuộc Panel → Page → Scene; không mặc định làm lại cả chương.
- **Bằng chứng**: mỗi thay đổi ghi ai yêu cầu, ai duyệt, lý do, version cũ→mới, ai đã được thông báo.

### 3.2 Năm tình huống

| Tình huống | Người yêu cầu | Người quyết định | Sản phẩm bị ảnh hưởng | Cần thông báo | Phạm vi làm lại | Điều kiện duyệt lại |
|---|---|---|---|---|---|---|
| **Script đổi sau khi storyboard duyệt** | Writer/Editor | Editor chấp thuận thay đổi; Writer quyết nội dung | Thumbnail của page/panel liên quan, artwork nếu đã bắt đầu | Artist, Letterer, Producer | Chỉ page/panel có đổi; phần không đổi giữ trạng thái duyệt | Artist cập nhật thumbnail → Editor duyệt lại phần đó |
| **Layout đổi sau khi Artist đã vẽ** | Artist/Editor | Editor (cùng Artist về kỹ thuật) | Pencil/ink/màu/letter của page đó | Writer (nếu ảnh hưởng nhịp), Letterer, Colorist | Cả page đổi layout; page khác không | Thumbnail mới được duyệt trước khi vẽ lại |
| **Thoại đổi sau lettering** | Writer/Editor | Writer (nội dung), Editor | Chỉ lớp letter của panel liên quan | Letterer | Re-letter panel đó, **không đụng artwork** | Editor đối chiếu với script mới |
| **Editor yêu cầu sửa một panel hoặc cả trang** | Editor | Editor ra yêu cầu; role giữ công đoạn xử lý | Panel/page và công đoạn sau nó | Role đang giữ công đoạn, Producer | Mặc định theo panel; "cả trang" chỉ khi Editor ghi rõ | Editor Approve đúng đối tượng |
| **Nội dung đã duyệt/khóa/phát hành cần sửa** | Bất kỳ role, Reviewer, hoặc Reader báo lỗi | Editor (và Producer nếu đã phát hành) | Bản đã khóa/phát hành | Người liên quan; Reader nếu cần | **Không sửa trực tiếp**: mở revision mới từ bản cũ, bản cũ giữ nguyên | Editor duyệt revision → Producer phát hành lại (bản cũ lưu trữ, có changelog) |

### 3.3 Quy trình chung [Đề xuất]
1. **Tạo Change Request** (đối tượng, lý do, ảnh hưởng dự kiến).
2. **Phân loại** nhỏ/lớn (Editor xác nhận).
3. **Tính phạm vi** theo phụ thuộc và liệt kê người nhận thông báo.
4. **Thực hiện** trên phiên bản mới; bản cũ giữ nguyên.
5. **Duyệt lại chỉ phần bị ảnh hưởng** và ghi bằng chứng (ai, khi nào, version).

### Tiếp ví dụ Chương 3
Writer đổi một câu thoại ở panel 2 trang 7 sau lettering → thay đổi **nhỏ** (chỉ lớp thoại) → thông báo Letterer → re-letter 1 panel → Editor đối chiếu → xong. Các trang còn lại **giữ nguyên trạng thái duyệt**.
Nếu thay vào đó Writer đổi *thứ tự sự kiện* ở trang 7–8 → thay đổi **lớn** → thu hồi duyệt thumbnail trang 7–8 → Artist vẽ lại hai trang → duyệt lại; 10 trang còn lại không bị ảnh hưởng.

---

## 4. Tiêu chí chứng minh hệ thống và AI có ích

Đo **trước/sau** trên cùng nhóm hoặc so sánh hai nhóm. Mọi con số mục tiêu dưới đây là **[Đề xuất, chưa phải kết quả thực nghiệm]**. Tỷ lệ Accept hoặc cảm nhận "hay hơn" không được dùng làm bằng chứng duy nhất.

### 4.1 Hệ thống

| Chỉ số | Cách đo | Baseline | Giới hạn |
|---|---|---|---|
| Thời gian tìm đúng bản | Giao tác vụ "tìm bản duyệt mới nhất của trang X", bấm giờ | Drive/Dropbox + chat | Mẫu nhỏ; kỹ năng cá nhân khác nhau |
| Thời gian giao việc → nhận việc | Timestamp hệ thống vs nhật ký thủ công | Giao việc qua chat | Chat khó trích dữ liệu |
| Thời gian review (gửi → có quyết định) | Timestamp | Review qua PDF/chat | Phụ thuộc người review |
| Thời gian xử lý rework (yêu cầu → duyệt lại) | Timestamp theo Change Request | Quy trình cũ | Độ khó thay đổi không đồng đều |
| **Lỗi dùng nhầm phiên bản** | Số lần sai trong kịch bản "bẫy" (2–3 bản gần giống nhau) | Thư mục thủ công | Cần thiết kế bẫy công bằng |
| **Truy vết** | Hỏi "ai đổi gì, khi nào, vì sao": tỷ lệ trả lời đúng + thời gian | Hỏi lại qua chat | Phụ thuộc kỷ luật ghi chép |
| **Dễ sử dụng** | SUS + thời gian hoàn thành + số lỗi thao tác | Công cụ đang dùng | Dưới ~10 người chỉ mang tính định hướng |

Mục tiêu đề xuất (ví dụ): giảm thời gian tìm bản đúng ≥ 30%; gần như không nhầm phiên bản trong kịch bản bẫy; SUS ≥ 68.

### 4.2 AI

| Chỉ số | Cách đo | Baseline | Giới hạn |
|---|---|---|---|
| **Chất lượng đề xuất** | Rubric 1–5 (liên quan, nhất quán Bible, dùng được), ≥ 2 người chấm **mù** | Bản không AI / người viết | Chủ quan; báo độ đồng thuận (Cohen's κ) |
| **Continuity precision/recall** | Bộ truyện có **lỗi gài sẵn** đã gán nhãn. Precision = cảnh báo đúng / tổng cảnh báo; recall = lỗi bắt được / tổng lỗi | Rà soát thủ công; kiểm tra luật đơn giản | Bộ dữ liệu nhỏ, tự tạo → nguy cơ lệch |
| **Thời gian tiết kiệm** | Cùng tác vụ có/không AI, bấm giờ | Không AI | Hiệu ứng học, thứ tự thử |
| **Công sức chỉnh sửa** | Tỷ lệ nội dung bị sửa sau khi nhận đề xuất, số thao tác | – | Accept ≠ chất lượng |
| **Độ trễ** | p50/p95 thời gian phản hồi | – | Phụ thuộc mạng/nhà cung cấp |
| **Chi phí** | Token × đơn giá trên mỗi tác vụ | LLM call đơn | Giá thay đổi theo thời gian |

Accept rate chỉ là chỉ số phụ, luôn đi kèm công sức chỉnh sửa và chấm mù.

### 4.3 Multi-Agent so với baseline
Nghiên cứu gần đây cho thấy multi-agent **không mặc định tốt hơn**: khi chuẩn hóa điều kiện, nhiều hệ multi-agent không vượt single-agent tương đương và tốn kém hơn ([arXiv 2606.05670](https://arxiv.org/pdf/2606.05670)); ở bài toán khuyến nghị điều trị, single-agent ngang với đội đa chuyên môn ([arXiv 2507.10911](https://arxiv.org/abs/2507.10911v1)). **[Nguồn; tôi mới thấy phần tóm tắt, cần đọc toàn văn trước khi trích dẫn]**

Thiết kế so sánh [Đề xuất]:
- **Ba điều kiện**: (A) một LLM call với prompt tốt; (B) workflow cố định nhiều bước, không agent tự quyết; (C) Multi-Agent.
- **Giữ nguyên**: cùng mô hình nền, cùng dữ liệu, cùng ngân sách token/độ trễ tối đa, cùng thang chấm.
- **Tiêu chí kết luận**: chỉ giữ (C) nếu hơn (A) và (B) đáng kể về chất lượng/recall so với chi phí và độ trễ tăng thêm. Nếu không, kết luận dùng A hoặc B vẫn là kết quả hợp lệ khi bảo vệ.
- **Bộ tình huống**: 20–30 tác vụ gán nhãn gồm (1) continuity với lỗi gài, (2) rút gọn thoại, (3) đề xuất storyboard, (4) báo cáo tiến độ từ dữ liệu giả lập; chạy lặp nhiều lần để thấy phương sai.
- **Người đánh giá**: ≥ 2 người ngoài nhóm phát triển (ưu tiên người có kinh nghiệm làm truyện tranh), chấm mù, ghi độ đồng thuận.

### 4.4 Kịch bản thử nghiệm hệ thống [Đề xuất]
Một chương thử 8–12 trang với 4–6 người dùng. Tác vụ: (a) tìm bản đã duyệt, (b) giao việc, (c) review và trả về một panel, (d) xử lý Script đổi sau duyệt, (e) trả lời "ai đổi gì". Chạy trước/sau (hoặc hai nhóm) với cùng tác vụ.

---

## 5. Quyết định đề xuất cho MVP & giả thuyết cần kiểm chứng

### Quyết định MVP [Đề xuất]
1. **Đối tượng**: nhóm nhỏ 2–6 người; hỗ trợ một người nhiều role.
2. **Phạm vi**: manga/comic dạng trang (Page/Panel); bỏ cuộn dọc Webtoon.
3. **Cốt lõi**: pipeline trạng thái theo page/panel, bàn giao có điều kiện, review gắn đối tượng, lịch sử phiên bản, Change Request phân loại nhỏ/lớn.
4. **AI**: người chịu trách nhiệm quyết định cuối; bắt đầu với 2 tác vụ đo được (continuity check, rút gọn thoại); Multi-Agent chỉ giữ nếu thắng baseline.
5. **Phát hành**: bản đã khóa/phát hành chỉ sửa qua revision mới.

### Giả thuyết cần kiểm chứng

| # | Giả thuyết | Cách kiểm chứng |
|---|---|---|
| H1 | Nhóm nhỏ mất đáng kể thời gian tìm bản/hỏi tiến độ | Phỏng vấn (câu 2, 5) + đo thời gian |
| H2 | Dùng nhầm phiên bản đủ thường xuyên để đáng giải quyết | Phỏng vấn (câu 2) + kịch bản bẫy |
| H3 | Phân loại thay đổi nhỏ/lớn giảm khối lượng làm lại thực tế | So sánh khối lượng làm lại trước/sau |
| H4 | Người dùng chấp nhận AI nếu họ giữ quyết định cuối | Phỏng vấn (câu 8) |
| H5 | AI continuity check đạt precision/recall đủ dùng | Bộ dữ liệu lỗi gài |
| H6 | Multi-Agent thắng baseline đủ để bù chi phí | Thí nghiệm 4.3 |
| H7 | Cổng duyệt thumbnail bắt buộc hợp với nhóm nhỏ, không gây nghẽn | Thử nghiệm + quan sát |

---

## Nguồn đã dùng
- Manga: [Wikipedia – Manga artist](https://en.wikipedia.org/wiki/Manga_artist), [Web Japan – The Creation of Manga](https://web-japan.org/trends/09_culture/pop110210.html)
- Comic: [Book Riot](https://bookriot.com/in-conversation-talking-about-drawinginkingcoloringlettering-comics/), [ComicBlocc](https://www.comicbloc.com/read/2011/11/can-you-spare-10-minutes/), [Roles in comic production](https://thephantom.fan/comics/the-titles-and-roles-in-comic-book-production/)
- Webtoon: [Clip Studio Tips](https://tips.clip-studio.com/en-us/articles/11512)
- Phỏng vấn: [The Mom Test – tóm tắt](https://mtlynch.io/book-reports/the-mom-test/)
- Multi-agent: [arXiv 2606.05670](https://arxiv.org/pdf/2606.05670), [arXiv 2507.10911](https://arxiv.org/abs/2507.10911v1)

**Nên làm tiếp để vững khi bảo vệ**: thay nguồn thứ cấp bằng nguồn nhà xuất bản hoặc chia sẻ trực tiếp của tác giả (ví dụ tài liệu Webtoon Canvas, bài của mangaka), đọc toàn văn hai bài arXiv, và chạy 5–8 buổi phỏng vấn thật.
