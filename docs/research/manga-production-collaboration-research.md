# Nghiên cứu: Quy trình sản xuất manga và cộng tác giữa các vai trò

> Phục vụ hoàn thiện thiết kế nghiệp vụ PanelForge. Không đánh giá code hiện tại, không có code/API/migration.
> Ngày kiểm chứng nguồn: 2026-10-04. Bible và continuity đã có báo cáo riêng ([manga-consistency-research.md](manga-consistency-research.md)); báo cáo này chỉ nhắc khi cần.

## 0. Cách đọc và độ tin cậy

| Nhãn | Ý nghĩa |
|---|---|
| **[Nguồn]** | Có tài liệu dẫn kèm link, tôi đã đọc nội dung trang (hoặc kết quả trích của trang). |
| **[Thông lệ]** | Kiến thức nghề phổ biến, không có một tài liệu chuẩn duy nhất. |
| **[Suy luận]** | Tôi suy ra từ nguồn/thông lệ; có thể sai. |
| **[Đề xuất]** | Đề xuất riêng cho PanelForge, không phải chuẩn ngành. |

**Thành thật về chất lượng nguồn** (quan trọng hơn mọi nhãn bên dưới):

- **Không đọc được:** hai nguồn tốt nhất cho quy trình Shueisha — bài học thuật [The last paradise for creative workers? (Shueisha và Weekly Shōnen Jump)](https://www.tandfonline.com/doi/full/10.1080/10286632.2023.2292719) và bài [FAMSF: How Manga Is Made](https://www.famsf.org/learn-engage/read-watch-listen/one-piece-only-how-manga-is-made) — đều trả về lỗi 403 khi tôi mở. Tôi **chưa đọc** hai bài này; không dẫn nội dung của chúng như đã kiểm chứng.
- **Đọc được nhưng tin cậy trung bình:** phỏng vấn biên tập viên ([MangaCraft: Yohei Tanahashi](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei)), và các bài của Anime News Network về Weekly Shonen Jump (qua kết quả tìm kiếm). Là lời kể trực tiếp của người làm nghề, nhưng chỉ là **một** biên tập viên/một tạp chí.
- **Nguồn thương mại/blog** (Mangaka Blog, Comicory, Multic, Writeseen): thông số hữu ích nhưng không phải tài liệu chính thức. Tôi chỉ dùng khi ghi rõ là blog.
- **Không có nguồn chính thức** của nhà xuất bản manga về thông số bản thảo mà tôi mở được. Thông số manga lấy từ tài liệu Clip Studio (công cụ), không phải từ nhà xuất bản.
- Không có nghiên cứu học thuật nào tôi tìm thấy về việc **quản lý cộng tác/phiên bản cho manga**. Phần phiên bản dựa trên thực hành tổng quát cho tài sản sáng tạo (design, VFX) và suy luận.

Phân biệt loại nguồn: **manga Nhật** (tạp chí, tantō), **comic phương Tây** (full script/Marvel method), **webtoon** (nền tảng), **thiết kế/VFX** (thực hành duyệt phiên bản). Mỗi nhận định nêu rõ thuộc loại nào, vì cơ cấu không chuyển thẳng giữa các loại. Tôi không dùng nguồn phim cho báo cáo này.

---

## 1. Trách nhiệm và bàn giao giữa các role

### 1.1 Cơ cấu thực tế rất khác nhau

Không có cơ cấu chung cho mọi studio. Các mô hình có nguồn:

| Mô hình | Ai làm gì | Nguồn |
|---|---|---|
| **Manga tạp chí Nhật (mangaka + tantō)** | Mangaka thường tự viết và vẽ (kiêm nhiều role). Tantō (biên tập viên phụ trách) nhận *name* (storyboard), góp ý, mangaka sửa và nộp lại; chu kỳ lặp khi còn thời gian. Mỗi biên tập viên của Jump phụ trách một–hai bộ đang đăng, họp với tác giả và thu bản thảo hằng tuần. Tantō mới bắt đầu ở vị trí sub-tantō. | [MangaCraft: phỏng vấn sub-tantō](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei) **[Nguồn]**; [ANN: Behind Weekly Shonen Jump](https://www.animenewsnetwork.com/convention/2025/all-the-news-and-reviews-from-anime-nyc/behind-weekly-shonen-jump-a-conversation-with-the-editor-in-chief/.228043) (chỉ đọc qua tóm tắt tìm kiếm) |
| **Comic Mỹ – full script** | Writer viết kịch bản đầy đủ: chia trang, chia panel, mô tả hình, thoại, SFX; artist dựng theo bản này. Writer có nhiều quyền kiểm soát hơn. | [Wikipedia: Script (comics)](https://en.wikipedia.org/wiki/Script_(comics)); [Comic Book Daily](https://www.comicbookdaily.com/columns/tales-from-the-comicdenn/marvel-method-full-script/) **[Nguồn]** |
| **Comic Mỹ – Marvel method** | Writer đưa cốt truyện ngắn; artist tự quyết nhịp, hành động, cách kể; sau khi vẽ, writer thêm thoại/caption/SFX. Quyền tác giả nghiêng về artist. | cùng nguồn trên **[Nguồn]** |
| **Webtoon nền tảng** | Quy trình script → thumbnail → sketch → line → color → lettering; thường một tác giả hoặc nhóm nhỏ. | [Creator's Resource Handbook của WEBTOON](https://webtoons-static.pstatic.net/creator101/en/pdf/Creators-Resource-Handbook-Updated.pdf?dt=2024011501) — **tôi không đọc được** (file vượt giới hạn tải); mô tả này chưa kiểm chứng trực tiếp **[Suy luận]** |

Hệ quả cho thiết kế: một cơ cấu 5 role tách rạch ròi (Producer, Writer, Artist, Letterer, Editor) **không phải mặc định của manga Nhật** (nơi mangaka thường kiêm nhiều việc), mà gần với studio/dự án nhóm hoặc quy trình comic phương Tây, nơi letterer là role riêng. **[Suy luận]** PanelForge nên coi 5 role là "mũ" (hat) gắn cho người, không phải 5 người.

### 1.2 Bảng ai quyết định cái gì

Cột "đề xuất" là phân chia **đề xuất cho PanelForge**, đối chiếu với các mô hình ở 1.1. Mọi quyết định vẫn qua Editor khi duyệt.

| Quyết định | Thông lệ (có nguồn) | Đề xuất cho PanelForge |
|---|---|---|
| **Cốt truyện, kết cấu chương** | Manga Nhật: mangaka đề xuất, tantō góp ý, họp hằng tuần/hằng tháng quyết đăng ([MangaCraft](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei)) **[Nguồn]** | **Writer** đề xuất; **Editor** duyệt |
| **Chia cảnh** | Phần của script (full script gộp trong script của writer) | **Writer** |
| **Phân trang, chia panel** | Full script: writer chia. Marvel: artist chia. Manga Nhật: mangaka dựng trong name | **Mặc định Artist** chia trang/panel trong thumbnail, Writer ghi ý đồ nhịp; **cấu hình được** theo dự án |
| **Layout, góc máy** | Marvel method: artist; full script: writer có thể chỉ định | **Artist** có quyền quyết; Writer chỉ ràng buộc khi nó là điều kiện cốt truyện |
| **Hình ảnh (nét, màu)** | Artist | **Artist** |
| **Lettering (đặt balloon, font, SFX)** | Comic Mỹ: letterer là nghề riêng ([Blambot](https://x.com/blambot/status/1825575481434923430), qua kết quả tìm kiếm). Manga Nhật: thường kiêm trong bản gốc | **Letterer** |
| **Nội dung thoại** | Full script: writer; Marvel: writer viết sau khi có hình | **Writer** viết; **Letterer** chỉ đề xuất rút gọn khi vượt chỗ chứa |
| **Duyệt cuối, phát hành** | Biên tập viên/tòa soạn quyết đăng ([MangaCraft](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei)) | **Editor** duyệt; **Producer** quyết lịch và phát hành |

### 1.3 Mỗi role nhận, tạo, bàn giao gì **[Đề xuất]**

| Role | Nhận | Tạo | Bàn giao cho |
|---|---|---|---|
| **Producer** | Kế hoạch dự án, trạng thái | Lịch, phân công, quyết định khi bất đồng | Mọi role |
| **Writer** | Brief, Bible | Outline, script (cảnh, thoại, ý đồ nhịp) | Artist (script đã chốt), Editor (duyệt script) |
| **Artist** | Script, Bible (model sheet, reference) | Thumbnail → Pencil → Ink → Color (tùy pipeline) | Letterer (trang đã đi nét, chừa vùng cho thoại), Editor |
| **Letterer** | Trang artwork, thoại, quy tắc chữ | Balloon, thoại, SFX | Editor |
| **Editor** | Bản nộp ở mỗi cổng | Nhận xét, quyết định duyệt/yêu cầu sửa | Producer (đã duyệt), người nộp (yêu cầu sửa) |

### 1.4 Người kiêm nhiều role và bất đồng sáng tạo

- **Kiêm role:** mangaka Nhật thường tự làm gần hết; hệ thống phải cho **một người giữ nhiều role**. **[Suy luận]** Rủi ro: người kiêm Writer + Editor tự duyệt bài mình. Đề xuất: không cho một người duyệt sản phẩm do chính họ nộp ở cổng duyệt cuối; nếu dự án chỉ có một người, cho phép bằng cờ "tự duyệt" ghi rõ trong nhật ký. **[Đề xuất]**
- **Bất đồng sáng tạo:** nguồn tôi có không nêu cách xử lý xung đột. Bài phỏng vấn sub-tantō chỉ cho biết tác giả chủ động hỏi ý kiến biên tập, không nói về xung đột ([MangaCraft](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei)). **[Nguồn, và là chỗ thiếu bằng chứng]** Quy trình leo thang đề xuất: (1) hai bên thảo luận ngay trên sản phẩm, (2) Editor phân xử về chất lượng/nhất quán, (3) Producer chốt về lịch/phạm vi, (4) ghi lý do. **[Đề xuất]** Cần phỏng vấn để xác nhận (mục 12).

---

## 2. Chuyển script thành trang manga

### 2.1 Định dạng script

| Kiểu | Nội dung | Nguồn |
|---|---|---|
| **Full script** | Từng trang, từng panel: mô tả hình, thoại, SFX | [Wikipedia](https://en.wikipedia.org/wiki/Script_(comics)) **[Nguồn]** |
| **Plot script (Marvel)** | Cốt truyện ngắn, artist tự dựng; thoại thêm sau | cùng nguồn **[Nguồn]** |
| **Manga: text plot rồi name** | Mangaka thường nộp cốt truyện dạng văn bản trước khi dựng name, để khỏi tốn công khi cốt truyện chưa ổn | [MangaCraft](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei) **[Nguồn]** |

### 2.2 Thumbnail/storyboard và nhịp kể

- Thumbnail/name là bước phác trang nhỏ, khối panel và hành động trước khi vẽ nét; nên nghĩ theo **trang đối diện (spread)** hơn là từng trang lẻ ([Mangaka Blog](https://www.mangaka.app/blogs/manga-storyboard-workflow-guide-2026-complete-guide); [Writeseen](https://writeseen.com/blog/manga-pages-layout), cả hai là blog). **[Nguồn, tin cậy trung bình]**
- Nhịp: nhiều panel nhỏ đẩy tốc độ; reveal quan trọng dùng splash hoặc spread để dừng nhịp; suspense đặt cuối trang phải, payoff ở panel đầu trang trái sau khi lật (theo tóm tắt kết quả tìm kiếm của các blog trên). **[Nguồn, tin cậy trung bình]**
- Chi tiết thứ tự đọc và lật trang phụ thuộc hướng đọc (manga RTL, trang "kế tiếp" nằm bên trái). **[Suy luận]**

### 2.3 Mô hình "mỗi Page thuộc một Scene" có phù hợp không? *(Kết luận theo phiếu V1.2: không phù hợp; chọn `Panel.SceneId`, Scene không chứa Page.)*

**Kết luận: không đủ chính xác cho thực tế; nên cho phép trang chuyển cảnh.** **[Suy luận]** — tôi không có nguồn nào bàn về mô hình dữ liệu này; lập luận dựa trên cách kể chuyện:

| Trường hợp | Vấn đề với "mỗi Page một Scene" |
|---|---|
| **Cảnh kéo dài nhiều trang** | Không vấn đề: nhiều Page cùng một Scene. |
| **Trang chuyển cảnh** | Một trang chứa cuối cảnh A và đầu cảnh B; Page thuộc hai Scene. |
| **Splash/spread nhiều trang** | Một hình trải qua hai Page nhưng thuộc một Scene. |
| **Montage** | Nhiều cảnh ngắn gói trong một trang. |

| Hướng | Mô tả | Đánh đổi |
|---|---|---|
| **A. Scene gắn ở mức Panel (hoặc nhóm Panel)** | Page chỉ là khung; thuộc cảnh nào do Panel quyết | Đúng với trang chuyển cảnh; phức tạp hơn |
| **B. Giữ "mỗi Page một Scene", trang chuyển cảnh thuộc cảnh đến, kèm ghi chú** | Đơn giản | Mất thông tin ở trang chuyển |

Khuyến nghị cho đồ án: **B**; mở rộng: **A**. **[Đề xuất]**

### 2.4 Manga theo trang và webtoon

| | Manga theo trang | Webtoon (cuộn dọc) |
|---|---|---|
| Đơn vị | Trang cố định, đọc theo trang đối diện | Dải dọc cuộn liên tục, cắt thành các ảnh |
| Nhịp | Lật trang tạo cú dừng | Khoảng trắng và độ dài cuộn tạo nhịp |
| Layout | Panel xếp 2D, RTL (manga) | Panel xếp một chiều dọc |
| Spread | Có | Không |

Thông số webtoon xem mục 7. Phần so sánh nhịp là **[Suy luận]**: nguồn tôi thấy là blog ([GoBookMart](https://gobookmart.com/why-readers-prefer-vertical-long-strip-formatting-over-traditional-manga-panels/)), chỉ đọc qua kết quả tìm kiếm.

---

## 3. Pipeline, review và approval

### 3.1 Review ở từng bước hay cuối chương?

- Manga Nhật: biên tập viên duyệt **name** trước khi vẽ sạch, lặp lại nhiều vòng ([MangaCraft](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei)). **[Nguồn]** Duyệt sớm ở bước phác, không chờ cuối.
- Blog quy trình nhấn mạnh duyệt storyboard trước khi vẽ line art để tránh sửa lại ([Mangaka Blog](https://www.mangaka.app/blogs/manga-storyboard-workflow-guide-2026-complete-guide)). **[Nguồn, tin cậy trung bình]**
- **[Suy luận]:** chi phí sửa tăng theo công đã đầu tư. Nên có **cổng duyệt sớm (script, thumbnail)** và **cổng cuối chương**; các bước giữa (pencil, ink, color) duyệt nhẹ.

### 3.2 Phân biệt năm khái niệm **[Đề xuất]**

Dựa trên thực hành duyệt phiên bản tài sản sáng tạo: bản đã duyệt "đóng băng", sửa thì tạo phiên bản mới ([Ziflow](https://www.ziflow.com/blog/design-version-control), [Air](https://air.inc/resources/version-control-for-creative-files), blog thương mại).

| Khái niệm | Câu hỏi | Ai làm | Kết quả |
|---|---|---|---|
| **Hoàn thành công việc (nộp)** | "Tôi làm xong phần của mình" | Người thực hiện | Sản phẩm *chờ duyệt* |
| **Duyệt sản phẩm** | "Sản phẩm này đạt" | Người duyệt (Editor) | Phiên bản cụ thể được đánh dấu *đã duyệt* |
| **Chuyển stage** | "Công việc sang bước kế tiếp" | Hệ thống/Producer khi đủ điều kiện | Sản phẩm bước kế được mở |
| **Khóa chương** | "Không ai sửa tiếp trừ khi mở khóa" | Editor/Producer | Chương chỉ đọc |
| **Phát hành** | "Độc giả công khai thấy" | Producer (theo quyết định Editor) | Bản phát hành bất biến |

Điểm then chốt: **hoàn thành ≠ duyệt ≠ chuyển stage.**

### 3.3 Điều kiện hoàn thành mỗi bước **[Đề xuất]**

Tôi không tìm thấy nguồn chuẩn; cần xác nhận qua phỏng vấn.

| Bước | Điều kiện nộp (gợi ý) |
|---|---|
| Script | Đủ cảnh, thoại, ý đồ nhịp; thuật ngữ khớp Bible |
| Thumbnail | Mỗi trang có panel và thứ tự đọc; nhịp lật trang đã xét |
| Pencil | Nhân vật/đạo cụ khớp model sheet; chừa vùng cho thoại |
| Ink | Nét sạch, không lệch panel |
| Color | Theo bảng màu nhân vật |
| Letter | Mọi thoại đã đặt, không tràn, không che mặt/nội dung quan trọng |
| Review | Editor đã đọc cả chương, lỗi nghiêm trọng đã xử lý |

### 3.4 Vòng nộp → yêu cầu sửa → sửa → nộp lại → duyệt

```
Nộp ──► Editor xem ──┬─► Duyệt ──► (chuyển stage)
                     └─► Yêu cầu sửa (nêu rõ chỗ + lý do)
                              │
                              ▼
                       Người nộp sửa ──► Nộp lại (phiên bản mới) ──► Editor xem ...
```

- Mỗi lần nộp lại tạo **phiên bản mới**; yêu cầu sửa gắn vào đúng phiên bản và vị trí (trang/panel). **[Đề xuất]**
- Manga Nhật có vòng tương tự cho name: nộp, góp ý, nộp lại ([MangaCraft](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei)). **[Nguồn]**
- **Song song:** sau khi thumbnail duyệt, các trang khác nhau có thể được vẽ cùng lúc; lettering một trang mở khi artwork trang đó chốt. Ràng buộc: bước sau của cùng một trang chỉ mở khi bước trước **được duyệt** (hoặc chỉ cần nộp, tùy cấu hình pipeline). **[Suy luận]**

---

## 4. Thay đổi và phiên bản

Không có nguồn nào mô tả cách studio manga xử lý bốn tình huống dưới đây. Phần này là **[Suy luận]** từ nguyên tắc "đã duyệt thì đóng băng, sửa là phiên bản mới" ([Ziflow](https://www.ziflow.com/blog/design-version-control)) và "chi phí sửa theo công đã đầu tư". Cần phỏng vấn kiểm chứng.

**Tiêu chí phân biệt [Đề xuất]:** thay đổi **làm mất hiệu lực bản duyệt** khi nó đổi thứ người duyệt đã đánh giá (ý nghĩa, nhân quả, bố cục, hình); **nhỏ** khi chỉ sửa lỗi bề mặt không đổi nội dung được duyệt (chính tả, dịch vài pixel).

| Tình huống | Sản phẩm bị ảnh hưởng | Ai quyết định | Ai cần thông báo | Duyệt lại |
|---|---|---|---|---|
| **Sửa script khi Artist đã vẽ**, đổi ý nghĩa (thêm panel, đổi hướng đi) | Thumbnail, pencil/ink/color các trang liên quan; thoại; Bible nếu đổi canon | Writer đề xuất, **Editor** xác nhận tác động, **Producer** xét lịch | Artist, Letterer, Producer | Có: trang liên quan quay về bước tương ứng |
| Sửa chữ nhỏ trong script (chính tả) | Thoại tương ứng | Writer | Letterer | Không, trừ khi tràn balloon |
| **Đổi layout khi artwork đã xong** | Thumbnail mới; ink/color/lettering của trang phải làm lại | **Artist** đề xuất, **Editor** duyệt, Producer chấp nhận chi phí | Writer, Letterer | Có |
| **Sửa thoại sau lettering** | Lớp chữ; có thể ảnh hưởng kích thước balloon | Writer đề xuất, **Editor** duyệt | Letterer | Nhẹ: duyệt lại phần chữ của trang |
| **Sửa nội dung đã duyệt** | Phiên bản đã duyệt | **Editor** mở lại bản duyệt (không âm thầm ghi đè) | Mọi người liên quan | Có, tạo phiên bản mới |
| **Sửa nội dung đã phát hành** | Bản phát hành (bất biến) | **Producer + Editor** | Mọi role; độc giả nếu hiển thị lịch sử | Có; tạo bản phát hành mới, giữ bản cũ |

**Nguyên tắc [Đề xuất]:**
1. Không ghi đè bản đã duyệt/đã phát hành; mọi sửa là phiên bản mới. Nguyên tắc tương tự: bản đã đóng băng thì sửa phải tạo phiên bản mới ([Ziflow](https://www.ziflow.com/blog/design-version-control)). **[Nguồn, blog thương mại]**
2. Người đề xuất thay đổi xem trước tác động (trang/sản phẩm nào quay về bước nào) trước khi người có quyền xác nhận.
3. Ghi lý do thay đổi.

Khi thay đổi script chạm sự kiện canon (ai chết, ai biết gì), nó đồng thời là thay đổi Bible; xem báo cáo continuity.

---

## 5. Lettering và khả năng đọc

Phân ba cấp: **quy tắc kỹ thuật** (sai là lỗi), **thông lệ** (đa số làm vậy), **lựa chọn nghệ thuật** (tùy tác giả).

| Chủ đề | Nội dung | Cấp | Nguồn |
|---|---|---|---|
| **Hướng đọc manga** | Phải→trái, trên→dưới; chữ Nhật dọc theo cột, phải→trái | Quy tắc theo thể loại/ngôn ngữ | [Comicory](https://www.comicory.com/blog/comic-book-lettering), [MangaConverter](https://www.mangaconverter.com/blog/why-does-manga-read-right-to-left) (blog) |
| **Thứ tự balloon** | Balloon của người nói trước đặt ở phía "đọc trước" (trái với LTR, phải với manga) | Thông lệ | [Comicory](https://www.comicory.com/blog/comic-book-lettering) |
| **Đuôi thoại** | Trỏ rõ về miệng/mặt người nói | Thông lệ | [Blambot](https://x.com/blambot/status/1095410149257285632) (người làm nghề; chỉ đọc qua kết quả tìm kiếm) |
| **Độ rộng đuôi** | Chỗ đuôi chạm balloon rộng xấp xỉ chiều cao chữ của font thoại | Thông lệ của một letterer | [Blambot](https://x.com/blambot/status/1095410149257285632) |
| **Căn giữa và khoảng đệm** | Căn giữa chữ; chừa ít nhất khoảng một chữ giữa chữ và mép balloon | Thông lệ | tóm tắt tìm kiếm về Blambot |
| **Độ dài balloon** | Dưới khoảng 24 từ để tránh khối chữ nặng | Lời khuyên của nguồn, không phải chuẩn | tóm tắt tìm kiếm |
| **Vị trí balloon** | Ưu tiên phần trên panel; tránh che mặt/miệng nhân vật | Thông lệ | [Comicory](https://www.comicory.com/blog/comic-book-lettering) |
| **SFX** | Trong manga là chữ vẽ hòa vào tranh; kích thước tỉ lệ với độ lớn âm thanh | Lựa chọn nghệ thuật + thông lệ | [Comicory](https://www.comicory.com/blog/comic-book-lettering) |
| **Vùng an toàn** | Chữ và nội dung quan trọng nằm trong khung trong để không bị cắt | Quy tắc kỹ thuật theo thông số bản thảo | [Clip Studio ASK](https://ask.clip-studio.com/en-us/detail?id=117518) |

**Giới hạn nguồn:** nguồn lettering mạnh nhất (Blambot) là bài đăng mạng xã hội của một người làm nghề; cuốn [The Essential Guide to Comic Book Lettering](https://www.goodreads.com/book/show/57954145-the-essential-guide-to-comic-book-lettering) tôi chỉ thấy trang giới thiệu, **chưa đọc**. Các nguồn còn lại là blog. Quy ước lettering manga Nhật (tategaki, furigana, font theo nhà xuất bản) tôi **không tìm được nguồn chính thức**. **[Thiếu bằng chứng]**

**Tràn chữ (overflow):** thoại dài hơn chỗ chứa phải được xử lý bằng đổi balloon, đổi cỡ chữ hoặc rút thoại; việc nào do ai quyết xem mục 4. **[Suy luận]**

---

## 6. Artwork và file bàn giao

### 6.1 Cả trang hay từng panel?

- Công cụ manga (Clip Studio) dựng **cả trang**: canvas có khung, vùng in, vùng bù cắt ([Clip Studio: Basics of creating a manga manuscript](https://tips.clip-studio.com/en-us/articles/733)). **[Nguồn]** Artist có thể tách panel trên layer/folder riêng nhưng bàn giao mặc định theo trang. **[Suy luận]**
- Webtoon: ảnh dọc cắt thành lát; đoạn dọc là đơn vị. **[Suy luận]**

### 6.2 File gốc, layer, render, model sheet, reference

| Loại | Vai trò | Ghi chú |
|---|---|---|
| **File gốc (layer)** | Nguồn để sửa | Định dạng phụ thuộc phần mềm. Clip Studio có layer raster và vector cho nét ([Clip Studio](https://tips.clip-studio.com/en-us/articles/7673)) **[Nguồn]** |
| **Ảnh render** | Ảnh phẳng để xem và duyệt | Sinh từ file gốc |
| **Model sheet, reference** | Chuẩn thiết kế nhân vật, bối cảnh | Thuộc Bible; Artist tra khi vẽ |

**Nguyên tắc [Đề xuất]:** file gốc là của Artist, hệ thống lưu như tệp đính kèm có phiên bản, không cố mở/biên tập; **ảnh render là thứ được duyệt**, nên Editor không cần phần mềm vẽ.

### 6.3 Phối hợp với phần mềm vẽ ngoài **[Đề xuất]**

Mục tiêu: Artist không đổi thói quen.

| Mức | Cách | Đánh đổi |
|---|---|---|
| **Tối thiểu** | Artist vẽ ở phần mềm quen thuộc, **tải ảnh render** (và tùy chọn tệp gốc) cho từng trang/bước | Đơn giản nhất; hệ thống không hiểu nội dung bên trong ảnh |
| **Mở rộng** | Cung cấp thông số canvas (kích thước, vùng an toàn) để tạo đúng mẫu trước khi vẽ | Cần cho người mới; thông số theo mục 7 |
| **Xa hơn** | Đồng bộ trực tiếp với phần mềm | Phụ thuộc phần mềm; **tôi chưa kiểm chứng khả năng tích hợp của phần mềm nào** |

Hệ thống chỉ nhận ảnh phẳng thì **không biết** ảnh đã đi nét hay tô màu chưa; người nộp phải khai báo bước. **[Suy luận]**

---

## 7. Preview, export và phát hành

### 7.1 Phân biệt bản **[Đề xuất]**

| Bản | Nghĩa | Ai thấy | Sửa được? |
|---|---|---|---|
| **Bản thảo (draft)** | Đang làm | Nhóm sản xuất | Có |
| **Bản đã duyệt** | Một phiên bản cụ thể được Editor chấp thuận | Nhóm | Không ghi đè; sửa = phiên bản mới |
| **Preview** | Bản xem thử qua liên kết cho External Reviewer | Người được cấp quyền | Chỉ xem |
| **Bản phát hành** | Ảnh chụp bất biến cho Public Reader | Công khai | Không; sửa = bản phát hành mới |

Preview nên gắn một **phiên bản cụ thể**, không phải "bản mới nhất", để nhận xét gắn đúng bản đã xem.

### 7.2 Thông số theo mục đích — **không có chuẩn chung**

| Mục đích | Thông số | Mức chắc chắn | Nguồn |
|---|---|---|---|
| **Bản thảo in tạp chí manga (B4, 600 dpi)** | Giấy 257×364 mm; kích thước hoàn thiện 220×310 mm; khung trong 180×270 mm; preset "thương mại, bù cắt 5 mm, đen trắng 600 dpi". Bù cắt có thể 3 hoặc 5 mm tùy nhà in | Là **mặc định của công cụ Clip Studio/hướng dẫn của họ**, **không phải xác nhận từ nhà xuất bản nào**; mỗi nhà xuất bản có thông số riêng | [Clip Studio ASK](https://ask.clip-studio.com/en-us/detail?id=117518), [Clip Studio tips](https://tips.clip-studio.com/en-us/articles/733), [General bleed guide](https://assets.clip-studio.com/en-us/detail?id=1792142) |
| **WEBTOON Canvas** | Ảnh rộng 800 px, cao tối đa 1280 px mỗi lát; tối đa 2 MB/ảnh, 100 ảnh/tập; JPG/PNG; hệ thống tự cắt/thu nhỏ nếu vượt | **Của riêng nền tảng này** | [Multic: Webtoon Format Guide](https://www.multic.com/guides/webtoon-format-guide/) (blog, khớp với kết quả tìm kiếm hướng dẫn Canvas) |
| **Tapas** | Rộng 800–940 px, tối đa 5 MB/ảnh | Theo blog tổng hợp; **chưa đối chiếu tài liệu Tapas** | [Multic](https://www.multic.com/guides/webtoon-format-guide/) |
| **GlobalComix** | Rộng 720–1080 px, tối đa 10 MB/ảnh | Theo blog tổng hợp; chưa đối chiếu | [Multic](https://www.multic.com/guides/webtoon-format-guide/) |
| **Kindle (fixed-layout)** | Khuyến nghị ≥ 300 ppi; ảnh JPEG | Theo hướng dẫn Amazon (đọc qua tóm tắt tìm kiếm) | [Amazon KDP](https://kdp.amazon.com/en_US/help/topic/G9GSTY4LTRT39D4Z), [Kindle Comic Creator guide](https://kc2.s3.amazonaws.com/KC2UserGuide_en.pdf) |
| **PDF / CBZ** | **Không tìm được nguồn chính thức về thông số chung**; CBZ thực chất là gói ảnh nén ZIP | Không xác nhận được chuẩn | — |

**Quy tắc thiết kế [Đề xuất]:** thông số là **hồ sơ xuất bản (profile) cấu hình được**, gắn nền tảng/nhà xuất bản. Không coi "800 px" hay "600 dpi" là chuẩn chung.

**Vùng an toàn:** khung trong là vùng nên đặt thoại và nội dung quan trọng; bù cắt là vùng bị cắt khi in ([Clip Studio ASK](https://ask.clip-studio.com/en-us/detail?id=117518)). **[Nguồn]** Tỉ lệ cụ thể phụ thuộc hồ sơ.

---

## 8. Giá trị của AI và nghiên cứu người dùng

### 8.1 Khó khăn nào đáng để AI hỗ trợ

Chưa có nghiên cứu người dùng nào xác nhận. Bảng sau là giả thuyết **[Suy luận]** cần phỏng vấn kiểm chứng:

| Khó khăn (giả thuyết) | Role | AI hỗ trợ kiểu | Rủi ro |
|---|---|---|---|
| Tìm mâu thuẫn xuyên chương | Editor, Writer | Gợi ý có dẫn chứng (xem báo cáo continuity) | Cảnh báo giả |
| Đề xuất bố cục/nhịp thumbnail từ script | Artist | Gợi ý bố cục | Chất lượng thẩm mỹ, "nhịp" khó đo |
| Gợi ý đặt balloon, kiểm tra tràn chữ | Letterer | Kiểm tra quy tắc + gợi ý | Che nội dung quan trọng; thứ tự đọc |
| Tổng hợp nhận xét review | Editor | Tóm tắt | Mất sắc thái |
| Kiểm tra thông số xuất bản | Producer | Kiểm tra theo hồ sơ | Ít rủi ro |

AI **đề xuất**, người có trách nhiệm quyết định.

### 8.2 Một tác vụ AI, workflow cố định, Multi-Agent

| Cách | Mô tả | Ưu | Nhược |
|---|---|---|---|
| **Một tác vụ AI** | Một lần gọi cho một việc (kiểm tra tràn chữ) | Đơn giản, rẻ, dễ đo | Hẹp |
| **Workflow cố định** | Chuỗi bước định sẵn (trích sự kiện → so Bible → soạn cảnh báo) | Dự đoán được, dễ gỡ lỗi | Cứng |
| **Multi-Agent** | Nhiều agent tự điều phối | Linh hoạt | Đắt, khó kiểm soát, khó đo |

**[Suy luận]** Tôi không có nguồn so sánh trực tiếp ba cách này cho manga. Khuyến nghị: bắt đầu với một tác vụ và workflow cố định; chỉ thêm Multi-Agent nếu đo được lợi ích vượt chi phí. **[Đề xuất]**

### 8.3 Cách đo **[Đề xuất]**

| Chỉ số | Cách đo |
|---|---|
| **Chất lượng** | Trên tập lỗi cài sẵn: recall, precision (xem báo cáo continuity); tỉ lệ đề xuất được chấp nhận |
| **Thời gian** | Thời gian hoàn thành có AI so với không AI (cùng người, cùng loại việc) |
| **Chi phí** | Chi phí gọi AI mỗi chương/tác vụ |
| **Công sức chỉnh sửa** | Tỉ lệ đề xuất AI: chấp nhận nguyên, sửa nhẹ, bác |

### 8.4 Câu hỏi phỏng vấn (chỉ là bộ câu hỏi, chưa có kết quả nào)

**Writer**
1. Mô tả cách bạn đi từ ý tưởng đến script gần nhất. Script chi tiết ở mức nào (cốt truyện, cảnh, panel)?
2. Lần gần nhất bạn phải sửa script sau khi Artist đã bắt đầu vẽ là khi nào? Ai bị ảnh hưởng?
3. Bạn nhớ chi tiết của các chương trước bằng cách nào? Lần gần nhất bạn mắc lỗi nhất quán là gì?
4. Bạn muốn quyết định đến đâu về layout, góc máy? Ở đâu bạn sẵn sàng nhường Artist?
5. Việc nào mất thời gian nhất mà bạn không thích?

**Artist**
1. Bạn nhận script ở dạng nào và cần thêm gì để vẽ mà khỏi hỏi lại?
2. Bạn làm theo trang hay panel? Phần mềm và định dạng file nào? Bản gốc có layer lưu ở đâu?
3. Khi script đổi sau khi bạn đã vẽ, bạn xử lý thế nào? Khi nào bạn chấp nhận vẽ lại, khi nào phản đối?
4. Bạn bàn giao cho người đặt chữ ra sao? Họ cần gì từ bạn?
5. Bạn có chấp nhận tải ảnh render lên một hệ thống ngoài không? Điều gì khiến bạn từ chối?

**Editor**
1. Bạn duyệt ở những bước nào? Mỗi bước bạn kiểm tra gì, dựa vào đâu để nói "đạt"?
2. Khi hai bên bất đồng sáng tạo, bạn xử lý ra sao? Ai chốt?
3. Khi nội dung đã duyệt phải sửa, bạn phân biệt "sửa nhỏ" và "phải duyệt lại" bằng cách nào?
4. Thông tin nào bạn thường mất nhiều thời gian nhất để tìm lại khi duyệt?
5. Bạn tin cảnh báo của AI ở mức nào? Điều gì khiến bạn bỏ qua hẳn?

**Letterer** (nếu có)
1. Bạn nhận gì từ Writer/Artist, và gặp vấn đề gì khi thoại dài hơn chỗ chứa?
2. Quy tắc bạn theo (đuôi thoại, thứ tự đọc, font) đến từ đâu? Có hướng dẫn nội bộ không?

---

## 9. Ví dụ xuyên suốt: từ brief đến phát hành, có một lần bị yêu cầu sửa

Truyện giả định "**Hồ Gương**", **Chương 3** (hư cấu minh họa). Hưng: Writer; Mai: Artist; Quân: Letterer; Trang: Editor; Tuấn: Producer.

| # | Bước | Điều xảy ra | Trạng thái / sản phẩm |
|---|---|---|---|
| 1 | **Brief** | Tuấn: "Ch.3, 16 trang, Lan tìm ra bí mật của gương, kết bằng cliffhanger." | Brief được duyệt |
| 2 | **Script** | Hưng viết cảnh chính: Lan soi gương, thấy cảnh quá khứ; ghi ý đồ nhịp: "reveal ở trang 16" | Script nộp |
| 3 | **Duyệt script** | Trang đọc và duyệt | Script **đã duyệt**; mở Thumbnail |
| 4 | **Thumbnail** | Mai dựng 16 trang nhỏ; reveal ở panel đầu trang 16 (trang bên trái sau khi lật) | Thumbnail nộp |
| 5 | **Yêu cầu sửa** | Trang nhận xét: "Trang 7–8: chuyển từ nhà sang hồ quá đột ngột, cần một panel chuyển." Gắn vào đúng phiên bản và trang 7–8 | **Yêu cầu sửa** (phiên bản 1) |
| 6 | **Sửa** | Mai thêm một panel chuyển; trang 7 chứa cuối cảnh nhà và đầu cảnh hồ (**một Page hai Scene**, xem 2.3) | Thumbnail phiên bản 2 |
| 7 | **Nộp lại → duyệt** | Trang duyệt phiên bản 2 | Thumbnail đã duyệt; mở Pencil song song theo trang |
| 8 | **Pencil/Ink/Color** | Mai vẽ ở phần mềm riêng, tải ảnh render từng trang và khai báo bước đã làm; trang 1–6 xong trước, lettering mở cho từng trang khi artwork trang đó duyệt | Từng trang qua cổng riêng |
| 9 | **Letter** | Quân đặt thoại; một dòng ở trang 4 tràn balloon → báo Hưng; Hưng rút ngắn câu; Trang duyệt thay đổi nhỏ (chỉ chữ) | Duyệt nhẹ lớp chữ |
| 10 | **Review cuối chương** | Trang đọc cả 16 trang và duyệt | Chương **đã duyệt** |
| 11 | **Khóa chương** | Tuấn khóa chương để chuẩn bị phát hành | Chương chỉ đọc |
| 12 | **Preview** | Tuấn gửi liên kết xem thử cho một Reviewer bên ngoài, gắn đúng phiên bản đã duyệt | Preview |
| 13 | **Phát hành** | Tuấn xuất theo hồ sơ nền tảng (ví dụ ảnh trang cho đọc online) | **Bản phát hành** bất biến |
| 14 | *(sau này)* | Phát hiện lỗi chính tả sau phát hành → sửa là **bản phát hành mới**, bản cũ giữ nguyên | Phát hành v2 |

Bước 5–7: vòng nộp → yêu cầu sửa → sửa → nộp lại → duyệt. Bước 9: thay đổi nhỏ. Bước 14: sửa sau phát hành. **[Đề xuất]**

---

## 10. Các lựa chọn thiết kế cho PanelForge

| # | Lựa chọn | Phương án | Lý do và đánh đổi |
|---|---|---|---|
| D1 | Role | **"Mũ" gắn cho người**, một người nhiều role | Phù hợp mangaka kiêm nhiều việc; cần chặn tự duyệt (1.4) |
| D2 | Ai chia trang/panel | Mặc định Artist, cấu hình theo dự án | Phản ánh khác biệt full script/Marvel; thêm cấu hình |
| D3 | Page ↔ Scene | Mặc định một Page một Scene; mở rộng cho trang chuyển cảnh | Đơn giản trước; mất thông tin chuyển cảnh (2.3) |
| D4 | Hoàn thành / duyệt / chuyển stage | Tách bạch | Ít nhầm; thêm thao tác |
| D5 | Thay đổi sau duyệt | Không ghi đè; phiên bản mới; xem tác động trước | An toàn lịch sử; tốn lưu trữ và thao tác |
| D6 | Mức duyệt | Cổng sớm (script, thumbnail) và cuối chương; bước giữa duyệt nhẹ | Cân bằng chi phí sửa với gánh nặng Editor |
| D7 | Phần mềm vẽ | Tải ảnh render, tệp gốc là đính kèm | Không đổi thói quen; hệ thống không hiểu nội dung ảnh |
| D8 | Xuất bản | Hồ sơ xuất bản cấu hình được | Đúng thực tế nền tảng; phải nhập hồ sơ |
| D9 | AI | Đề xuất, người quyết; bắt đầu từ tác vụ đơn và workflow cố định | Dễ đo, rẻ; kém "ấn tượng" hơn Multi-Agent |

---

## 11. Phạm vi cho đồ án và phần mở rộng

**Tối thiểu (đủ minh họa quy trình nghiệp vụ):**
- Role dạng "mũ", một người nhiều role, chặn tự duyệt ở cổng cuối.
- Pipeline cấu hình được (mẫu Script → … → Approved); phân biệt hoàn thành/duyệt/chuyển stage.
- Vòng nộp → yêu cầu sửa → sửa → nộp lại → duyệt, gắn phiên bản và vị trí.
- Bản đã duyệt/phát hành bất biến; sửa là phiên bản mới.
- Tải ảnh render theo trang/bước; preview theo phiên bản cụ thể.
- Một–hai hồ sơ xuất bản mẫu (ví dụ ảnh trang, webtoon 800 px).
- Một–hai kiểm tra AI đơn (tràn chữ hoặc thuật ngữ), chỉ gợi ý.

**Mở rộng:**
- Scene ở mức Panel (trang chuyển cảnh).
- Phân tích tác động thay đổi (trang nào quay về bước nào).
- Ghi chú trực tiếp trên ảnh.
- Nhiều hồ sơ xuất bản (PDF/CBZ/Kindle) sau khi xác nhận thông số từng nơi.
- Workflow AI nhiều bước; Multi-Agent nếu đo được lợi ích.
- Tích hợp phần mềm vẽ.

---

## 12. Câu hỏi chưa đủ bằng chứng hoặc cần phỏng vấn xác nhận

1. Nhóm sáng tác nhỏ ở Việt Nam thực tế chia role ra sao? Có Letterer riêng không? (Tôi không có nguồn về bối cảnh này.)
2. Khi Writer và Artist là hai người khác nhau, ai thực sự chia panel và quyết góc máy?
3. Biên tập viên manga xử lý bất đồng sáng tạo thế nào (nguồn tôi có không nói).
4. Điều kiện hoàn thành thực tế từng bước; Editor duyệt ở bước nào là đủ.
5. Ranh giới "thay đổi nhỏ" và "làm mất hiệu lực duyệt" trong thực tế.
6. Thông số bản thảo/lettering của nhà xuất bản cụ thể mà dự án nhắm tới; thông số Tapas, GlobalComix, PDF, CBZ cần đối chiếu tài liệu chính thức.
7. Quy ước lettering của manga Nhật (tategaki, furigana) và manga dịch sang tiếng Việt.
8. Artist có chấp nhận tải ảnh render lên hệ thống ngoài không; tệp gốc cần lưu ở đâu.
9. Hiệu quả thực tế của AI cho từng tác vụ (chưa có số liệu; xem 8.3).
10. Hai bài chưa đọc được: [bài học thuật về Shueisha](https://www.tandfonline.com/doi/full/10.1080/10286632.2023.2292719) và [FAMSF: How Manga Is Made](https://www.famsf.org/learn-engage/read-watch-listen/one-piece-only-how-manga-is-made); nên đọc trực tiếp để kiểm chứng mục 1.1 và 3.1.

---

## 13. Danh sách nguồn

**Manga (Nhật)**
- Phỏng vấn sub-tantō Yohei Tanahashi: https://mangacraft.substack.com/p/manga-editor-interview-with-yohei
- ANN: Behind Weekly Shonen Jump (qua tóm tắt tìm kiếm): https://www.animenewsnetwork.com/convention/2025/all-the-news-and-reviews-from-anime-nyc/behind-weekly-shonen-jump-a-conversation-with-the-editor-in-chief/.228043
- Bài học thuật Shueisha (**chưa đọc**, 403): https://www.tandfonline.com/doi/full/10.1080/10286632.2023.2292719
- FAMSF How Manga Is Made (**chưa đọc**, 403): https://www.famsf.org/learn-engage/read-watch-listen/one-piece-only-how-manga-is-made
- Clip Studio ASK (vùng in/cắt): https://ask.clip-studio.com/en-us/detail?id=117518
- Clip Studio tips (bản thảo manga): https://tips.clip-studio.com/en-us/articles/733
- Clip Studio tips (layer): https://tips.clip-studio.com/en-us/articles/7673
- Clip Studio assets (bleed guide): https://assets.clip-studio.com/en-us/detail?id=1792142

**Comic phương Tây**
- Script (comics): https://en.wikipedia.org/wiki/Script_(comics)
- Marvel Method vs Full Script: https://www.comicbookdaily.com/columns/tales-from-the-comicdenn/marvel-method-full-script/
- Blambot (lettering): https://x.com/blambot/status/1095410149257285632 và https://x.com/blambot/status/1825575481434923430
- The Essential Guide to Comic Book Lettering (**chưa đọc**): https://www.goodreads.com/book/show/57954145-the-essential-guide-to-comic-book-lettering

**Webtoon / nền tảng**
- WEBTOON Creator's Resource Handbook (**chưa đọc**, quá lớn): https://webtoons-static.pstatic.net/creator101/en/pdf/Creators-Resource-Handbook-Updated.pdf?dt=2024011501
- Multic Webtoon Format Guide (blog): https://www.multic.com/guides/webtoon-format-guide/
- Amazon KDP fixed-layout: https://kdp.amazon.com/en_US/help/topic/G9GSTY4LTRT39D4Z
- Kindle Comic Creator guide: https://kc2.s3.amazonaws.com/KC2UserGuide_en.pdf

**Blog quy trình (tin cậy trung bình)**
- Mangaka Blog: https://www.mangaka.app/blogs/manga-storyboard-workflow-guide-2026-complete-guide
- Writeseen (layout): https://writeseen.com/blog/manga-pages-layout
- Comicory (lettering): https://www.comicory.com/blog/comic-book-lettering
- MangaConverter (RTL): https://www.mangaconverter.com/blog/why-does-manga-read-right-to-left
- GoBookMart (webtoon vs manga): https://gobookmart.com/why-readers-prefer-vertical-long-strip-formatting-over-traditional-manga-panels/

**Phiên bản và duyệt tài sản sáng tạo (không riêng manga)**
- Ziflow: https://www.ziflow.com/blog/design-version-control
- Air: https://air.inc/resources/version-control-for-creative-files
