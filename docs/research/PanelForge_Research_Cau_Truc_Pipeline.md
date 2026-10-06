# PanelForge – Research mô hình cấu trúc nội dung & pipeline sản xuất

> **Nhãn**: **[Nguồn]** có dẫn chứng · **[Suy luận]** suy ra từ nguồn · **[Đề xuất]** thiết kế riêng cho PanelForge · **[Cần xác nhận]** chưa đủ bằng chứng.
>
> **Giới hạn bằng chứng (đọc trước)**
> - **Không tìm được** hướng dẫn quy trình chính thức của Shueisha/Kodansha/Shogakukan. Bằng chứng về manga Nhật đến từ: bài của Web Japan (chỉ thấy qua kết quả tìm kiếm, trang trả 403 khi tải), bài hướng dẫn tiếng Nhật, phỏng vấn biên tập viên và newsletter VIZ.
> - Nguồn comic phương Tây là blog nghề, Wikipedia, memo của Jim Shooter; nguồn webtoon là hướng dẫn của Clip Studio (hãng phần mềm) và handbook của WEBTOON (chỉ thấy qua tìm kiếm).
> - **Workflow của Clip Studio là workflow của một công cụ, không phải chuẩn ngành.** Tôi chỉ dùng nó làm chứng cứ về *cách một công cụ phổ biến mô hình hóa trang/panel/layer*.
> - Tôi chưa đọc toàn văn mọi trang; một số chi tiết (ví dụ "4–5 vòng sửa name") chỉ có ở một nguồn và được đánh dấu.

---

## 1. Cấu trúc nội dung

### 1.1 Thuật ngữ nghề vs khái niệm quản lý

| Thành phần | Thuật ngữ nghề hay khái niệm của PanelForge? | Bằng chứng |
|---|---|---|
| **Workspace** | **Khái niệm quản lý** (không có trong nghề; gần nhất là "nhà xuất bản/studio/imprint") | Không tìm thấy nguồn nghề dùng khái niệm này **[Suy luận]** |
| **Series** | **Thuật ngữ nghề** (連載/series). Biên tập viên theo dõi tiến độ, hướng truyện, deadline theo series ([Wikipedia](https://en.wikipedia.org/wiki/Manga_artist)) | **[Nguồn]** |
| **Chapter/Episode** | **Thuật ngữ nghề**, là đơn vị phát hành/nộp bài. Webtoon dùng "episode" ([Clip Studio – Webtoon](https://tips.clip-studio.com/en-us/articles/7417)) | **[Nguồn]** |
| **Scene** | **Khái niệm tự sự** mượn từ kịch bản/phim. Trong script comic, đơn vị tổ chức chuẩn là **page và panel**, không phải scene ([Wikipedia – Script](https://en.wikipedia.org/wiki/Script_(comics))). McCloud có "scene-to-scene" nhưng đó là **loại chuyển tiếp giữa panel**, không phải đơn vị chứa ([tóm tắt](https://www.comicscube.com/2010/12/comic-book-glossary-transitions-part-1.html)) | **[Nguồn + Suy luận]**: Scene là khái niệm quản lý hữu ích, nhưng không phải đơn vị sản xuất vật lý |
| **Page** | **Thuật ngữ nghề** và là đơn vị bản thảo/bàn giao vật lý. Webtoon **không có** page in; có canvas cuộn dọc cắt lát khi xuất (xem 1.3) | **[Nguồn]** |
| **Panel (koma)** | **Thuật ngữ nghề**, đơn vị bố cục; việc sắp xếp gọi là *komawari* ([GlobalComix](https://globalcomix.com/news/details/254)) | **[Nguồn]** |
| **Element** | **Khái niệm quản lý** (gộp balloon, caption, SFX, hình...). Nghề gọi riêng: balloon, caption, SFX, background... | **[Suy luận]** |

### 1.2 Làm rõ từng thành phần

| Thành phần | Đại diện | Ai tạo/sửa/dùng | Từ giai đoạn nào | Quan hệ | Bắt buộc? |
|---|---|---|---|---|---|
| Workspace | Ranh giới tổ chức/quyền/thành viên | Producer tạo; mọi role dùng | Trước sản xuất | Chứa Series | **Không** (có thể ngầm định với người làm một mình) [Đề xuất] |
| Series | Tác phẩm dài kỳ; mang style, bible, cấu hình pipeline | Producer/Editor tạo; Writer chỉnh bible | Trước sản xuất | Chứa Chapter; sở hữu Bible | Có |
| Chapter | Đơn vị phát hành/duyệt cuối | Producer tạo; Editor duyệt | Từ plot | Chứa nội dung của chương, theo **thứ tự** | Có (one-shot = series 1 chapter) |
| Scene | Một khối câu chuyện liền mạch (thời gian/địa điểm/mục tiêu) | Writer tạo; Artist/Editor đọc | **Từ script**, trước khi có trang | **Tham chiếu và thứ tự** trên panel; không phải vật chứa của trang | **Không** bắt buộc về mặt sản xuất |
| Page | Một trang bản thảo | Artist tạo (phân trang ở thumbnail/name) | Từ thumbnail | Chứa panel; có **thứ tự**; có thể ghép đôi (spread) | Có (trừ webtoon) |
| Panel | Một khung hình | Artist tạo ở thumbnail | Từ thumbnail | Thuộc page; tham chiếu script | Có (splash = 1 panel) |
| Element | Vật thể chồng trên/trong panel | Artist (hình), Letterer (chữ) | Từ thumbnail (ghi chú) đến letter | Thuộc panel hoặc page; có **thứ tự đọc** riêng | Không |

### 1.3 Kiểm chứng Scene → Page → Panel

| Tình huống | Điều nguồn nói | Hệ quả cho mô hình |
|---|---|---|
| Scene dài nhiều page | Thông thường; scripts đi theo page/panel, scene không phải khung ([Wikipedia – Script](https://en.wikipedia.org/wiki/Script_(comics))) | Cây chứa nghiêm ngặt Scene→Page vẫn biểu diễn được |
| Một page chứa nhiều scene | McCloud nêu *scene-to-scene* là một loại chuyển tiếp **giữa các panel**, xảy ra được trong cùng một trang ([tóm tắt](https://www.comicscube.com/2010/12/comic-book-glossary-transitions-part-1.html)); gutter ngang lớn hơn gutter dọc có thể báo hiệu chuyển thời gian ([GlobalComix](https://globalcomix.com/news/details/254)) | **Page không thuộc đúng một Scene** → cây chứa Scene→Page **vỡ** |
| Panel hồi tưởng/montage/nhiều thời điểm | Chuyển tiếp aspect-to-aspect, non-sequitur tồn tại ([tóm tắt](https://www.comicscube.com/2010/12/comic-book-glossary-transitions-part-1.html)) | Scene không luôn liền mạch về thời gian; cần "cờ" hoặc tag, không ép thành scene riêng **[Suy luận]** |
| Double-page spread | Hai trang đối diện thiết kế như một đơn vị đọc ([GlobalComix](https://globalcomix.com/news/details/249/creator-tips-and-tricks-8-gutters-and-panel-layouts)) | Cần quan hệ **Page ↔ Page**, không nằm trong cây |
| Splash, panel không viền, hình tràn khung | Splash là một panel lớn không viền; panel tràn lề rất phổ biến trong manga ([GlobalComix](https://globalcomix.com/news/details/249/creator-tips-and-tricks-8-gutters-and-panel-layouts)) | Viền là **thuộc tính hiển thị**, không phải cấu trúc; splash = page có 1 panel |
| Webtoon cuộn dọc | Canvas dài, tối đa 800×1280 px mỗi lát, 100 file/episode; khoảng trống giữa panel quan trọng (≥200 px, 600–1000 px cho chuyển cảnh) ([Clip Studio](https://tips.clip-studio.com/en-us/articles/7417), [WEBTOON Canvas](https://webtoons-static.pstatic.net/creator101/en/pdf/Creators-Resource-Handbook-Updated.pdf)) | Không có "page" theo nghĩa in; lát cắt là **sản phẩm xuất**, không phải đơn vị sáng tác |

### 1.4 So sánh ba lựa chọn cấu trúc

```
(1) Cây nghiêm ngặt            (2) Hai cấu trúc tách rời, liên kết       (3) Kết hợp có giới hạn
Chapter                         Chapter                                   Chapter
 └ Scene                         ├ Tự sự: Scene → Script block            ├ Page → Panel  (cây vật lý, chính)
    └ Page                       └ Trang: Page → Panel                    └ Scene (khối tự sự, chứa script block)
       └ Panel                      (liên kết N–N tự do)                       Panel ──tham chiếu──▶ script block
```

| Tiêu chí | (1) Cây chặt | (2) Tách + liên kết tự do | (3) Kết hợp có giới hạn |
|---|---|---|---|
| Biểu diễn Scene dài nhiều page | Được | Được | Được |
| Page có 2 scene (chuyển cảnh trong trang) | **Không** (phải tách trang giả hoặc nhân đôi) | Được | Được |
| Hồi tưởng/montage | Không (ép thành scene giả) | Được (liên kết tự do) | Được nếu có **cờ/tag**; scene không chồng lấn |
| Spread, splash, tràn khung | Spread: không; splash: được | Được | Spread = liên kết Page↔Page; splash = page 1 panel |
| Script trước layout | Được (scene chứa script) | Được | Được |
| Phân trang lại | Gãy: đổi trang là đổi cha | Tốt | Tốt: script block giữ liên kết, panel mới được gán lại |
| Độ phức tạp thao tác | Thấp nhất | **Cao**: người dùng giữ hai cây, dễ lệch | Trung bình: một cây chính + ràng buộc |
| Rủi ro | Mô hình sai thực tế, người dùng "lách" | Liên kết bị bỏ quên, khó chứng minh tính nhất quán | Cần quy tắc rõ, nhiều trường hợp biên phải nói rõ là chưa hỗ trợ |

**Khuyến nghị cho MVP [Đề xuất]: chọn (3)**:
- **Cây vật lý chính**: Chapter → Page → Panel. Đây là thứ pipeline, artwork, review và bàn giao thực sự vận hành trên đó (xem mục 2.4).
- **Scene là khối tự sự**, chứa script block, **không** chứa Page. Panel *tham chiếu* script block; scene của một panel được **suy ra** từ block nó mang.
- **Scene liền mạch, không chồng lấn, tùy chọn** (không có scene vẫn làm được).
- **Hỗ trợ**: page nhiều scene, scene nhiều page, splash, spread (cờ + cặp page), panel không viền (thuộc tính), chiều đọc (cờ Series).
- **Chưa hỗ trợ (ghi rõ trong đồ án)**: scene xen kẽ phức tạp (cross-cutting), webtoon cuộn dọc, panel hình dạng tự do với thứ tự đọc không suy ra được.

**Đánh giá mô hình dự kiến**: giữ Series/Chapter/Page/Panel; **thay** "Scene chứa Page" bằng "Scene tham chiếu panel qua script"; **tách** Element (xem 1.5); Workspace chỉ là ranh giới tổ chức, không phải tầng nội dung.

### 1.5 Script, artwork và Element

**Script tổ chức theo đơn vị nào?**
- Full script: theo **page và panel**, panel có nhãn, balloon đánh số để letterer đỡ nhầm ([Wikipedia – Script](https://en.wikipedia.org/wiki/Script_(comics))). **[Nguồn]**
- Marvel/plot script: artist làm layout từ **bản tóm tắt** rồi writer thêm thoại sau ([Wikipedia – Script](https://en.wikipedia.org/wiki/Script_(comics)), [Comics Beat](https://comicsbeat.com/marvel-style-marvel-method-comics)). **[Nguồn]**
- Manga Nhật: biên tập viên có thể nhận **plot dạng chữ trước name** để thống nhất hướng trước khi mangaka bỏ công vẽ ([phỏng vấn Mangacraft](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei)). **[Nguồn]**
- Hệ quả: **script không luôn gắn với panel**; ở plot-first nó chưa có page/panel nào để gắn. **[Suy luận]**

**Khi chưa có layout, thoại và mô tả hành động ở đâu?** Ở cấp **Scene/Chapter** như các script block (hành động, thoại, caption, SFX), có thứ tự, chưa gán panel. **[Đề xuất]**

**Khi phân trang lại?** Script block giữ nguyên, chỉ **đổi panel được gán**. Block chưa gán hiện thành "script chưa có chỗ trong layout" để Artist/Editor thấy. **[Đề xuất]**

**Artwork: cả trang vs từng panel vs file nhiều layer**
- Clip Studio EX quản lý tác phẩm nhiều trang bằng thư mục chứa **một file `.clip` cho mỗi trang** cùng file quản lý ([Clip Studio](https://tips.clip-studio.com/en-us/articles/9949)); mỗi panel có thể là một **folder riêng kèm mask** ngăn nét vẽ tràn ra ngoài ([Clip Studio](https://tips.clip-studio.com/en-us/articles/5600)). **[Nguồn, chỉ phản ánh một công cụ]**
- Webtoon: canvas dài được **gộp phẳng rồi cắt lát** khi xuất ([Clip Studio](https://tips.clip-studio.com/en-us/articles/4001)). **[Nguồn]**
- Hệ quả: **đơn vị file thường là trang** (hoặc episode), còn panel là *vùng logic trong file*. Quản lý "ảnh từng panel" riêng chỉ đúng nếu nhóm thực sự làm vậy. **[Suy luận]**

**Balloon, narration, SFX, artwork, ảnh tham chiếu: cùng là Element?**

| Loại | Bản chất | Gợi ý |
|---|---|---|
| Balloon, caption, SFX | Đối tượng chữ chỉnh sửa được (text, vị trí, kiểu, thứ tự đọc); thuộc Letterer | Cùng nhóm **"lettering element"** |
| Artwork | File nguồn nhiều layer hoặc hình | **Asset/artifact**, không phải element chỉnh sửa trong PanelForge |
| Ảnh tham chiếu | Tài liệu hỗ trợ, không thuộc trang | **Tham chiếu/attachment**, không phải nội dung trang |

Phân biệt ba lớp **[Đề xuất]**: **(a) đối tượng chỉnh sửa** (balloon có text); **(b) asset nguồn** (PSD/CLIP, sketch, ref); **(c) hình đã render** (bản flatten để review, lát webtoon, bản phát hành). Gộp cả ba vào "Element" làm mờ ranh giới chịu trách nhiệm và lịch sử phiên bản.

**Thứ tự đọc vs quan hệ chứa**: thứ tự đọc (panel→panel, balloon→balloon; manga phải→trái, [GlobalComix](https://globalcomix.com/news/details/254)) là **thuộc tính thứ tự**, độc lập với "balloon nằm trong panel nào" hay thứ tự lớp (z-order). Balloon có thể gối qua nhiều panel và vẫn có thứ tự đọc riêng; script đánh số balloon cho mục đích đó ([Wikipedia – Script](https://en.wikipedia.org/wiki/Script_(comics))). **[Nguồn + Suy luận]**

---

## 2. Pipeline sản xuất

### 2.1 Phân biệt script / storyboard-name / thumbnail / pencil

- **Name (ネーム)**: bản phác có **chia khung, bố cục và thoại**, đi trước bản vẽ chính; bên đặt hàng/biên tập viên kiểm tra name rồi mới sang sản xuất ([CrowdWorks](https://crowdworks.jp/times/know-how/11031/); [Web Japan](https://web-japan.org/trends/09_culture/pop110210.html) qua kết quả tìm kiếm; phỏng vấn biên tập viên dùng *nemu* ([Mangacraft](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei))). **[Nguồn]** Một nguồn tìm kiếm khác nói name có thể sửa nhiều lần dưới sự xem xét của biên tập viên; con số "4–5 lần" chỉ có ở một nguồn nên **[Cần xác nhận]**. Từ nguyên của chữ "name" tôi **chưa xác minh**.
- **Thumbnail** (Tây/webtoon): phác nhỏ, xác lập **dòng chảy panel**; **storyboard** tiếp đó mới cấu hình panel, blocking nhân vật, đặt bubble ([Clip Studio](https://tips.clip-studio.com/en-us/articles/7417)). **[Nguồn]**
- **Pencil/sketch/下書き**: bản vẽ chi tiết nhân vật và nền, sau khi layout được chấp thuận. **[Nguồn]**
- **Quan hệ [Suy luận]**: *name* của manga ≈ *thumbnail + storyboard + thoại thô*; *script* ở comic Tây là văn bản do writer viết, còn ở manga thường **gộp vào vai mangaka**; trường hợp có writer riêng **[Cần xác nhận]**.

### 2.2 Bảng stage

> Role/Người duyệt là **đề xuất mặc định**; thực tế khác theo xưởng.

| Stage | Mục đích | Đầu vào | Công việc | Đầu ra | Role | Người duyệt | Điều kiện bàn giao |
|---|---|---|---|---|---|---|---|
| **Script** (plot + script) | Chốt nội dung, thoại | Brief, Bible | Plot, chia scene, viết khối script | Script (chữ) | Writer | Editor | Editor duyệt; thoại đủ; khớp Bible |
| **Thumbnail/Name** | Chốt phân trang, layout, dòng chảy, thoại thô | Script | Phân trang, panel, góc máy, đặt thoại thô | Thumbnail/name từng page | Artist | Editor | Mọi script block đã gán panel hoặc ghi chú lý do; có chỗ cho balloon |
| **Pencil** | Vẽ chi tiết | Thumbnail đã duyệt | Pencil nhân vật, nền | Trang pencil | Artist (+trợ lý) | Editor/Artist chính | Khớp thumbnail; nhất quán Bible |
| **Ink** | Nét sạch để in | Pencil duyệt | Ink, mảng đen (manga: cả tone/finish) | Trang ink | Artist/Inker | Editor | Nét sạch, panel rõ |
| **Color** (nếu có) | Màu | Ink | Flat, render, effects | Trang màu | Colorist | Editor/Artist | Đúng bảng màu Bible |
| **Letter** | Chữ cuối | Trang + script cuối | Balloon, caption, SFX | Trang đã letter | Letterer | Editor | Khớp script cuối; không che hình quan trọng; thứ tự đọc đúng |
| **Review** | *Không phải stage vẽ*: cổng kiểm tra | Sản phẩm vừa nộp | Xem, phản hồi | Quyết định + ghi chú | Editor | (chính nó) | Theo checklist |
| **Approved** | *Không phải stage*: **kết quả** của review | – | – | Trạng thái | – | – | – |

**Diễn giải [Suy luận]**: Review và Approved không cùng bản chất với Pencil/Ink: chúng xảy ra **sau mỗi stage** và có kết quả ("Approved/Changes requested"). Trong chuỗi comic Tây, editor duyệt trang sau penciling rồi mới chuyển inker ([Book Riot](https://bookriot.com/in-conversation-talking-about-drawinginkingcoloringlettering-comics/), [ComicBlocc](https://www.comicbloc.com/read/2011/11/can-you-spare-10-minutes/)), tức Review **lặp lại**, không chỉ ở cuối.

### 2.3 Biến thể pipeline

| Biến thể | Khác biệt | Có thể bỏ/gộp/lặp/song song | Nguồn |
|---|---|---|---|
| **Manga đen trắng** | Không có Color; có tone/finish/solid black; trợ lý làm ink, lettering, shading, nền ([Wikipedia](https://en.wikipedia.org/wiki/Manga_artist)); name được biên tập viên duyệt trước bản chính | Bỏ Color; Thumbnail/Name *gộp* với script thô khi mangaka tự viết; nền/ink có thể **song song** nhờ trợ lý | **[Nguồn]** |
| **Comic màu (Tây)** | Chuỗi writer→penciller→inker→colorist→letterer, editor điều phối ([Book Riot](https://bookriot.com/in-conversation-talking-about-drawinginkingcoloringlettering-comics/)). Lettering thường **sau pencil, trước color**; kiểu DC đôi khi **song song với color** ([Shooter memo](https://jimshooter.com/2011/05/lettering-and-balloon-placement-memo.html/)) | Letter có thể **đứng trước** Color hoặc **song song** | **[Nguồn]** |
| **Webtoon** | Pre-production → thumbnail → storyboard → sketch → ink → color → effects → lettering → publish ([Clip Studio](https://tips.clip-studio.com/en-us/articles/7417)); studio chia bộ phận ([Clip Studio](https://tips.clip-studio.com/en-us/articles/11512)); có thêm Effects và Publish/slicing | Thêm Effects; đơn vị là episode/canvas | **[Nguồn, qua một bài hướng dẫn]** |
| **Sáng tác độc lập** | Một người làm nhiều bước; thường không có duyệt chính thức | Bỏ phần lớn cổng duyệt (tự duyệt có ghi nhận) | **[Suy luận]** |
| **Nhóm Writer + Artist riêng** | Có bàn giao script rõ; full script hoặc Marvel-method (artist làm layout, thoại thêm sau) ([Wikipedia – Script](https://en.wikipedia.org/wiki/Script_(comics))) | Thứ tự Script/Thumbnail **đảo** ở Marvel method | **[Nguồn]** |

**Lettering và review có nhất thiết ở cuối?** **Không.** (a) Lettering có thể bắt đầu trên pencil và chạy **song song** với color; (b) có artist letter thử *trước* khi pencil để chừa chỗ cho thoại ([Patreon](https://www.patreon.com/SuitedDevil/posts/process-2-basic-34105832)); (c) vị trí balloon do writer/penciler/editor quyết, letterer thường không tự quyết ([Shooter memo](https://jimshooter.com/2011/05/lettering-and-balloon-placement-memo.html/)); (d) review xảy ra ở nhiều cổng. Pipeline mẫu của đề tài chỉ là **một cấu hình hợp lý**, không phải chuẩn chung. **[Nguồn + Suy luận]**

### 2.4 Đơn vị chạy pipeline

| Đơn vị | Ưu | Nhược | Hợp với |
|---|---|---|---|
| **Chapter** | Đơn giản; khớp phát hành | Một trang chậm kéo cả chương; không biểu diễn "trang này ink, trang kia pencil" | Script, review cuối, phát hành |
| **Page** | Khớp đơn vị file/bản thảo (Clip Studio: 1 file/trang) và chuỗi "editor duyệt từng trang rồi sang người kế" ([Book Riot](https://bookriot.com/in-conversation-talking-about-drawinginkingcoloringlettering-comics/)) | Spread cần xử lý chung; sửa 1 panel kéo cả trang nếu không tinh | Thumbnail đến Letter |
| **Panel** | Chi tiết nhất; phù hợp review/sửa | Quá nhỏ cho bàn giao; artwork thường là file trang | **Chú thích/yêu cầu sửa**, không phải stage |
| **Sản phẩm bàn giao (deliverable)** | Đúng thứ được nộp (file pencil, file ink) | Cần định nghĩa rõ | Mô hình tổng quát |

**Khuyến nghị [Đề xuất]**: stage chạy trên **Page** (cho Thumbnail→Letter), **Chapter** cho Script và cổng cuối, **Panel** làm đơn vị *chú thích review và yêu cầu rework*. Webtoon (episode/canvas) để ngoài MVP.

**Năm tình huống**

| Tình huống | Xử lý với "stage theo page" [Đề xuất] |
|---|---|
| Script cả chương duyệt, layout từng trang chưa xong | Script chạy ở cấp Chapter và **Approved**; page ở Thumbnail với trạng thái riêng. Không cần chờ cả chương |
| Một trang Ink, trang khác Pencil | Mỗi page có stage riêng; Chapter chỉ hiển thị tổng hợp |
| Artwork là ảnh cả trang nhưng chỉ một panel cần sửa | Yêu cầu sửa gắn **panel**, rework chạy ở **page** (vì file là trang) nhưng *phạm vi duyệt lại ghi là panel đó*; không đẩy cả trang lùi nhiều stage |
| Lettering bắt đầu khi một số trang xong | Cho phép Letter bắt đầu theo **điều kiện vào** của từng page (ví dụ Ink đã duyệt), không chờ cả chương |
| Một người kiêm nhiều role | Hệ thống gán nhiều role; cổng duyệt giữ nguyên nhưng cho "tự duyệt" có đánh dấu |

### 2.5 Phân biệt các loại trạng thái

Không gộp thành **một** trạng thái. **[Đề xuất]**, lý do: nguồn cho thấy người *làm*, người *duyệt* và việc *phát hành* là các vai khác nhau (editor điều phối và duyệt: [ComicBlocc](https://www.comicbloc.com/read/2011/11/can-you-spare-10-minutes/)).

| Trục | Ví dụ giá trị | Câu hỏi trả lời |
|---|---|---|
| **Production stage** | Script, Thumbnail, Pencil, Ink, Color, Letter | Công việc thuộc loại nào? |
| **Task state** | Chưa bắt đầu, Đang làm, Bị chặn, Xong | Người làm đang ở đâu? |
| **Submission state** | Nháp, Đã nộp, Nộp lại | Sản phẩm đã đưa cho người duyệt chưa? |
| **Approval** | Chờ, Approved, Yêu cầu sửa, Hết hiệu lực (superseded) | Người duyệt quyết định gì? |
| **Release state** | Chưa phát hành, Hẹn giờ, Đã phát hành, Thu hồi | Độc giả thấy chưa? |

### 2.6 Cấu hình và kiểm soát pipeline

- **Ai cấu hình?** Producer (cấp Series) với góp ý của Editor; người dùng độc lập tự cấu hình. Không có nguồn nói rõ; **[Đề xuất]**.
- **Tuyến tính có đủ không?** **Chưa đủ**: nguồn cho thấy Letter có thể song song Color, trợ lý làm nền song song, Marvel method đảo thứ tự Script/Thumbnail. Đề xuất mô hình "stage + **điều kiện vào**" (ví dụ Letter vào được khi Pencil/Ink đã duyệt) thay vì dãy cứng, nhưng **không** làm đồ thị tự do (quá phức tạp cho MVP).
- **Đổi pipeline khi đang chạy [Đề xuất]**: pipeline có **phiên bản**; item đang chạy giữ phiên bản cũ trừ khi được di chuyển có chủ đích; thêm stage chỉ áp cho item chưa bắt đầu; xóa/bỏ qua stage phải ghi nhận và không âm thầm làm mất hiệu lực bản duyệt.
- **Điều kiện duyệt**: checklist theo stage (ví dụ Thumbnail: dòng chảy đọc được, chừa chỗ balloon; Letter: khớp script cuối, không che hình, thứ tự đọc đúng). Checklist là **đề xuất**; riêng việc chừa chỗ cho balloon có nguồn ([Patreon](https://www.patreon.com/SuitedDevil/posts/process-2-basic-34105832)).
- **Approved là stage hay kết quả?** **Kết quả** (xem 2.2). Cột "Approved" trên bảng Kanban chỉ là cách hiển thị.
- **Review từng stage vs review cuối chương**: review stage là cổng chất lượng *cục bộ* để chuyển cho role kế tiếp; review cuối kiểm các thứ chỉ thấy khi nhìn cả chương (nhịp, cặp spread, nhất quán chữ, continuity) và là cổng **cho phép phát hành**. Spread buộc phải review như một cặp ([GlobalComix](https://globalcomix.com/news/details/249/creator-tips-and-tricks-8-gutters-and-panel-layouts)). **[Suy luận]**

### 2.7 Rework và thay đổi

| Thay đổi | Phần bị ảnh hưởng | Cần thông báo | Phạm vi làm lại | Duyệt lại |
|---|---|---|---|---|
| **Sửa script sau storyboard** | Panel đang gán block đó; artwork của panel nếu đã vẽ | Artist, Letterer, Editor | Chỉ panel/page liên kết; thay đổi *chỉ ở chữ* không làm layout hết hiệu lực | Phần bị đổi |
| **Sửa layout sau artwork** | Pencil/ink/màu/letter của **page** đó | Writer (nếu đổi nhịp), Letterer, Colorist | Cả page (file là trang) | Thumbnail mới trước, rồi các stage sau của page đó |
| **Sửa thoại sau lettering** | **Chỉ** balloon/caption liên quan | Letterer, Editor | Re-letter đúng vài element; **không đụng artwork** | Đối chiếu script mới |
| **Yêu cầu sửa 1 panel** | Panel; trang chứa nó nếu file là trang | Role giữ stage hiện tại | Panel, theo phụ thuộc | Duyệt đúng panel |

**Nguyên tắc [Đề xuất]**: phân biệt thay đổi *ngữ nghĩa/bố cục* (làm mất hiệu lực stage sau) với thay đổi *chữ/định dạng* (chỉ đánh dấu element lệch script). Khi script block bị sửa mà balloon đã letter, hiển thị cảnh báo "lệch script" thay vì tự động đưa trang về đầu pipeline. Mỗi thay đổi ghi người yêu cầu, người duyệt, bản cũ→mới.

---

## 3. Ví dụ xuyên suốt

**Chương mẫu 8 trang**:
- **Scene A**: trang 1–2 (một scene kéo dài hai trang).
- **Trang 3**: panel 1–2 là Scene A; panel 3–5 là Scene B (chuyển cảnh trong trang).
- Trạng thái: P1 đã Letter; P2 đang Ink; P3 đang Pencil; P4 Thumbnail đã duyệt; P5–8 mới có script.
- **Sự kiện 1**: Editor yêu cầu sửa **panel 3 của P2**.
- **Sự kiện 2**: Writer đổi **một câu thoại** của P1 sau khi đã letter.

### Trong từng mô hình cấu trúc

| Yếu tố | (1) Cây chặt | (2) Tách + liên kết | (3) Kết hợp (khuyến nghị) |
|---|---|---|---|
| Scene A kéo dài 2 trang | Scene A ⊃ P1, P2 | Scene A ↔ panel của P1–P2 | Scene A chứa block; panel P1–P2 tham chiếu block |
| **Trang 3 có 2 scene** | **Không đặt được**: P3 thuộc A hay B? Phải tách P3 giả hoặc gán sai | Panel P3.1–2 ↔ A, P3.3–5 ↔ B | Panel P3.1–2 mang block của A, P3.3–5 mang block của B; **page không thuộc scene** |
| P5–8 (chỉ script) | Scene B chứa P5–8 *chưa tồn tại* (page rỗng) | Block thuộc Scene, chưa liên kết | Block thuộc Scene B, **chưa gán panel**; hiện "chưa có layout" |
| Sửa panel 3 của P2 | Gắn vào panel | Gắn vào panel | Gắn vào panel; rework chạy ở P2 |
| Đổi 1 câu thoại ở P1 | Sửa balloon; không rõ liên hệ script | Block ↔ balloon qua liên kết | Block đổi → balloon P1.2 hiện "lệch script" → Letterer sửa |

### Trong từng mô hình pipeline

| Yếu tố | Chapter-level tuyến tính | Page-level + điều kiện vào (khuyến nghị) |
|---|---|---|
| Các trang ở stage khác nhau | **Không biểu diễn** (cả chương một stage) | Mỗi page một stage: P1 Letter, P2 Ink, P3 Pencil, P4 Thumbnail, P5–8 Script |
| Sửa panel 3 P2 | Phải đưa cả chương lùi | P2 quay Ink (hoặc Pencil nếu sửa hình); P1, P3–8 không đổi |
| Đổi thoại P1 sau Letter | Dễ bị hiểu là làm lại chương | Chỉ P1 quay lại Letter; **không** quay về Pencil; P1 duyệt lại ở Letter |
| Letter bắt đầu khi P2–P8 chưa xong | Bị chặn | Được, theo điều kiện vào từng page |

```
Chapter 8 trang (cấu trúc 3)
 Page 1  [Letter ✓]   Page 2 [Ink]   Page 3 [Pencil]   Page 4 [Thumb ✓]   Page 5-8 [Script only]
   │ P1.2 ◀─ script block (đổi thoại)                │ P3.1-2 → Scene A   P3.3-5 → Scene B
   └ "lệch script" → Letterer sửa → Review Letter    │ P2.3 ◀─ yêu cầu sửa (Editor)
```

---

## 4. Khuyến nghị: giữ gì, thay gì, vì sao

| Hạng mục | Quyết định | Lý do |
|---|---|---|
| Series, Chapter, Page, Panel | **Giữ** | Là thuật ngữ nghề và đơn vị làm việc thật |
| Workspace | **Giữ như ranh giới tổ chức/quyền**, không tính là tầng nội dung; có thể ngầm với cá nhân | Không có trong nghề; cần cho cộng tác |
| **Scene chứa Page** | **Thay**: Scene chứa *script block*, panel tham chiếu block | Page có thể có 2 scene; script có trước layout |
| Element | **Tách**: lettering element / asset / hình render | Ba loại khác trách nhiệm, vòng đời, phiên bản |
| Thứ tự đọc | **Mô hình riêng**, không suy từ cây chứa | Spread, RTL, balloon xuyên panel |
| Stage Script→Letter | **Giữ làm template mặc định**, cấu hình theo Series; thêm "tone/finish" cho manga đen trắng, bỏ qua được Color | Biến thể thực tế khác nhau |
| **Review, Approved** | **Bỏ khỏi danh sách stage**, mô hình hóa là cổng và kết quả duyệt | Review lặp nhiều lần; Approved là kết quả |
| Tuyến tính cứng | **Thay** bằng stage + điều kiện vào, vẫn đơn giản | Letter song song Color; nền/ink song song |
| Đơn vị pipeline | **Page** (Chapter cho Script/cuối; Panel cho chú thích) | Khớp đơn vị file và chuỗi duyệt trang |
| 5 loại trạng thái | **Tách** | Tránh nhập nhằng "xong/đã duyệt/đã phát hành" |
| Webtoon | **Ngoài MVP** | Không có page in; sản phẩm là canvas cắt lát |

---

## 5. Phạm vi Đồ Án, giới hạn, câu hỏi cần xác nhận

**Phạm vi đề xuất**: manga/comic dạng trang (đen trắng hoặc màu), có cờ chiều đọc; nhóm 2–6 người; stage theo page; script block và liên kết panel; spread/splash/panel không viền ở mức thuộc tính; cổng duyệt + kết quả duyệt; pipeline có phiên bản, cấu hình theo Series.

**Giới hạn cần nói rõ khi bảo vệ**: không hỗ trợ webtoon cuộn dọc, scene xen kẽ phức tạp, panel tự do không suy ra thứ tự đọc; chưa xác thực với studio chuyên nghiệp; nguồn nhà xuất bản chính thức chưa tìm được.

**Câu hỏi nên hỏi người làm nghề**
1. Với manga, ai chịu trách nhiệm script/plot khi có writer riêng? Name chứa thoại cuối hay chỉ thoại thô?
2. Name thường sửa bao nhiêu vòng và ai chốt? (Thay cho con số một-nguồn "4–5 vòng".)
3. Lettering xảy ra trước hay sau khi ink/color xong? Ai quyết vị trí balloon?
4. Bạn bàn giao theo file trang hay ảnh từng panel? Có làm việc theo layer/panel riêng không?
5. Khi chỉ một panel cần sửa, bạn làm lại cả trang hay chỉ panel đó? Có phải duyệt lại cả trang không?
6. Một trang chứa hai cảnh có thường gặp không? Bạn có gán "scene" khi làm việc không, hay chỉ "trang/panel"?
7. Spread được duyệt như một cặp thế nào? Có lần nào lệch giữa hai trang không?
8. Bạn cần những trạng thái nào mà bảng "To do/Doing/Done" không thể hiện được?
9. (Studio) Ai quyết định thay đổi quy trình khi dự án đang chạy?

---

## Nguồn
- Manga: [Wikipedia – Manga artist](https://en.wikipedia.org/wiki/Manga_artist) · [Web Japan](https://web-japan.org/trends/09_culture/pop110210.html) (chỉ qua tìm kiếm) · [CrowdWorks](https://crowdworks.jp/times/know-how/11031/) · [Mangacraft – phỏng vấn biên tập viên](https://mangacraft.substack.com/p/manga-editor-interview-with-yohei) · [VIZ Originals newsletter 5](https://www.viz.com/blog/posts/viz-originals-creator-newsletter-issue-5) · [GlobalComix – komawari](https://globalcomix.com/news/details/254), [layout](https://globalcomix.com/news/details/249/creator-tips-and-tricks-8-gutters-and-panel-layouts)
- Comic Tây: [Wikipedia – Script](https://en.wikipedia.org/wiki/Script_(comics)) · [Comics Beat – Marvel method](https://comicsbeat.com/marvel-style-marvel-method-comics) · [Jim Shooter memo](https://jimshooter.com/2011/05/lettering-and-balloon-placement-memo.html/) · [Book Riot](https://bookriot.com/in-conversation-talking-about-drawinginkingcoloringlettering-comics/) · [ComicBlocc](https://www.comicbloc.com/read/2011/11/can-you-spare-10-minutes/) · [Patreon – SuitedDevil](https://www.patreon.com/SuitedDevil/posts/process-2-basic-34105832) · [Comics Cube – McCloud transitions](https://www.comicscube.com/2010/12/comic-book-glossary-transitions-part-1.html)
- Webtoon & công cụ: [Clip Studio – Webtoon guide](https://tips.clip-studio.com/en-us/articles/7417) · [Clip Studio – Webtoon studio](https://tips.clip-studio.com/en-us/articles/11512) · [Clip Studio – webtoon features](https://tips.clip-studio.com/en-us/articles/4001) · [Clip Studio – multi-page EX](https://tips.clip-studio.com/en-us/articles/9949) · [Clip Studio – panels](https://tips.clip-studio.com/en-us/articles/5600) · [WEBTOON Creators Handbook](https://webtoons-static.pstatic.net/creator101/en/pdf/Creators-Resource-Handbook-Updated.pdf) (chỉ qua tìm kiếm)
