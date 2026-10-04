# PanelForge – Research về Role, Pipeline, AI Agent và công cụ hỗ trợ

> Cập nhật: 2026-10-04. Đã đối chiếu với `Backend/PanelForge_FA26SE251/docs/requirements/requirements.md` (bản đã sửa cùng ngày) và code backend (domain, seed, controller). Khả năng của công cụ lấy từ README/trang chính thức, **chưa chạy thử**. Phần phân quyền và kiến trúc agent là **đề xuất thiết kế**.

**Nguyên tắc cốt lõi:** Writer quyết định "trang này kể chuyện gì"; Artist quyết định "câu chuyện được thể hiện bằng hình ảnh và layout thế nào"; Letterer quyết định "chữ xuất hiện ra sao"; Editor kiểm tra chất lượng; Producer duyệt và quản lý sản xuất.

---

## 0. Cấu trúc dự án và số role

```
Studio Workspace            ← ranh giới tenant: thành viên, role, cấu hình AI
  └── Series (nhiều series)  ← mỗi series có pipeline, Series Bible, chương riêng
       └── Chapter → Scene → Page → Panel → Element
```

Xác nhận từ code: `StudioWorkspace` chứa `ICollection<Series>`, `Series` có `WorkspaceId`.

| Nhóm | Role | Nguồn |
|---|---|---|
| Hệ thống (`SystemRole`) | `User`, `Admin` | `Domain/Enums/SystemRole.cs` |
| Trong Studio (`WorkspaceRole`) | `Producer`, `Writer`, `Artist`, `Letterer`, `Editor` | `Domain/Enums/WorkspaceRole.cs` |
| Người ngoài | Reader / External Stakeholder: không có tài khoản sản xuất, vào bằng link xem trước có hạn (`ExternalPreviewLink`, có watermark, log truy cập) | requirements 3.7, 7.3; entity `Publishing/ExternalPreviewLink.cs` |

Tổng cộng **7 role trong code** (2 + 5), cộng thêm vai trò người ngoài truy cập qua link. Trong requirements, "Administrator" ứng với `Admin`, "Authenticated Users" ứng với `User`.

---

## 1. Phân quyền quyết định theo từng phần của trang truyện

| Nội dung | Chịu trách nhiệm chính | Phối hợp / kiểm tra |
|---|---|---|
| Định hướng bộ truyện, phạm vi, số trang | Producer (thống nhất với Writer) | Editor góp ý |
| Cốt truyện, diễn biến chương, nhân vật | **Writer** | Editor kiểm tra logic; Producer duyệt |
| Scene: bối cảnh, nhân vật, sự kiện | **Writer** | Artist kiểm tra khả năng thể hiện |
| Nội dung từng trang, nhịp kể, điểm lật trang | **Writer đề xuất** | Artist điều chỉnh; Editor review |
| Số panel, kích thước, vị trí panel (page layout) | **Artist** | Writer xác nhận không mất ý; Editor kiểm tra nhịp đọc |
| Góc máy, tư thế, biểu cảm, bố cục trong panel (panel composition) | **Artist** | Writer làm rõ ý đồ |
| Lời thoại, lời dẫn, người nói | **Writer** | Editor biên tập |
| Balloon, font, ngắt dòng, vị trí chữ, hiệu ứng chữ | **Letterer** | Artist chừa khoảng trống; Editor review |
| Yêu cầu sửa nội dung / hình ảnh | **Editor** | Role được giao thực hiện sửa |
| Phê duyệt cuối, khóa chương, phát hành | **Producer** | Sau các bước review của pipeline |

**Hai nghĩa của "layout":** *page layout* (bao nhiêu panel, ở đâu, lớn nhỏ ra sao) và *panel composition* (nhân vật, nền, góc máy, chỗ trống cho chữ trong một panel). Artist phụ trách cả hai; Writer cung cấp ý đồ kể chuyện.

---

## 2. Dây chuyền sản xuất một trang manga

Pipeline mặc định (seed `STANDARD_MANGA`, mỗi Series sao chép một bản khi tạo):

| Stage | Việc | Role | Ước tính |
|---|---|---|---|
| Script | Viết kịch bản có cấu trúc | Writer | 3 ngày |
| Thumbnail | Phác bố cục: chia khung, khung to nhỏ, vị trí nhân vật | Artist | 2 ngày |
| Pencil | Vẽ chì chi tiết (hình dáng, biểu cảm, bối cảnh) | Artist | 4 ngày |
| Ink | Đi nét mực đè lên nét chì | Artist | 3 ngày |
| Color | Tô màu (có thể bỏ với manga đen trắng) | Artist | 3 ngày |
| Letter | Balloon, chữ, hiệu ứng chữ | Letterer | 2 ngày |
| Review | Kiểm duyệt (cổng duyệt) | Editor | 1 ngày |
| Approved | Hoàn tất | Editor | 0 |

Chia nhỏ để sửa sớm cho rẻ: chê bố cục ở Thumbnail mất vài phút, phát hiện ở Ink phải vẽ lại cả trang.

**Pipeline chỉ theo dõi trạng thái công việc**: trang đang ở bước nào, ai làm, hạn bao giờ, đã duyệt chưa. Nó không yêu cầu hệ thống có công cụ vẽ chì hay đi mực. Page và Panel đều có `CurrentStageId`, nên theo dõi được tới từng khung tranh.

### Biết "ảnh này đã đi mực hay chưa"
Hệ thống biết nhờ **metadata**, không nhờ phân tích pixel: ảnh được nộp ở stage Ink của Panel 3 thì được ghi là bản Ink của Panel 3, và Editor xác nhận khi duyệt. Có thể thêm cảnh báo bằng code (ví dụ ảnh stage Color gần như toàn xám), chỉ là cảnh báo.

### Biết "element trong ảnh"
- Element dựng **trong canvas** (balloon, chữ, nhân vật đặt vào, prop) có cấu trúc đầy đủ: loại, vị trí, nội dung, `scriptLineId`.
- Ảnh **tải lên** là một lớp artwork (`ARTWORK_LAYER`); hệ thống không tự hiểu bên trong có gì. Muốn biết thì người khai báo tag, đối chiếu script/Bible, hoặc AI thị giác đề xuất (luôn có người xác nhận).

### Tình trạng triển khai (theo code đã đọc)
| Tính năng | Trạng thái |
|---|---|
| 8 stage mặc định, sao chép khi tạo Series | Đã có |
| Producer cấu hình stage (thêm/sửa/xóa/đổi thứ tự): 5 endpoint trong `SeriesPipelineController` | Đã có |
| Approval gate, stage transition có điều kiện (role, bắt buộc comment, hướng lùi) | Mức domain, **chưa có API gọi** |
| Assignment (role, brief, hạn chót, ưu tiên, trạng thái, rework) | Mức domain, **chưa có API** |
| Panel-level progress (`CurrentStageId` trên Page/Panel) | Mức domain, chưa có query tổng hợp |
| Change request độc lập | Chỉ có `RequestRework` trong Assignment |
| Dependencies | **Chưa có** (chỉ có trạng thái `Blocked`) |
| Lưu và quản lý file artwork (Asset) | **Chưa thấy** trong Domain |

---

## 3. Từng role: người làm gì, AI hỗ trợ gì

### 3.1 Writer / WriterAgent
- **Người:** viết cốt truyện, scene, mô tả panel, shot note, thoại, gán người nói, liên kết Series Bible.
- **AI:** chia script thành Scene → Page → Panel (requirements 5.1); kiểm tra nhất quán với Bible (5.2); gợi ý hội thoại.
- **Đầu ra:** script nháp có cấu trúc, kèm lý do, chờ Writer duyệt.

### 3.2 Artist / ArtistAgent
- **Người:** tạo thumbnail và layout, **tải artwork lên theo phiên bản**, **tách nền ảnh đã tải**, nộp panel để review, trả lời change request.
- **AI:** đề xuất layout (5.3); **tạo ảnh nháp** (5.8); **tách nền** (5.9); so sánh ngoại hình nhân vật (5.4).
- **Đầu ra:** phương án layout, ảnh nháp và ảnh tách nền, đều là Suggestion/asset dẫn xuất gắn với Panel.

### 3.3 Letterer / LettererAgent
- **Người:** balloon, khung dẫn, hiệu ứng chữ, gắn chữ với dòng script và người nói, typography preset.
- **AI:** phân tích chữ tràn khung, đè lên tranh, sai thứ tự đọc (5.5); dịch; đọc chữ từ ảnh nhập vào.
- **Đầu ra:** text element gắn `scriptLineId`, speaker, tọa độ.

### 3.4 Editor / EditorAgent
- **Người:** review panel/page/chương, so sánh phiên bản, comment neo theo vùng ảnh, vẽ chú thích, kiểm tra nhất quán, tạo change request, duyệt hoặc trả về.
- **AI:** đối chiếu Bible, phát hiện mâu thuẫn thoại ↔ hình, thiếu nội dung, tổng hợp findings.
- **Đầu ra:** finding có vị trí, bằng chứng, mức độ, đề xuất sửa.

### 3.5 Producer / ProducerAgent
- **Người:** tạo Series trong Workspace, mời thành viên, giao việc theo chương/panel, hạn chót, dependency, theo dõi tiến độ, duyệt/khóa chương, xuất file, khôi phục phiên bản.
- **AI:** tóm tắt tiến độ, phát hiện điểm nghẽn, đề xuất phân công, cảnh báo trễ hạn.
- **Đầu ra:** báo cáo và đề xuất kèm dữ liệu căn cứ.
- **Lưu ý:** các agent Editor/Producer cần dữ liệu Assignment và stage thật; hiện mới có sườn domain.

### 3.6 Administrator (`Admin`) và Reader
| Role | Trách nhiệm | AI |
|---|---|---|
| **Administrator** | Tài khoản, cấu hình AI provider/model/quota, lưu trữ, template pipeline, audit | Tổng hợp usage và lỗi; không quyết định nội dung |
| **Reader / External** | Xem link xem trước có hạn, gửi phản hồi theo chương/trang | Không cần agent sản xuất riêng |

**Khuyến nghị bản đầu:** Supervisor + 5 agent sản xuất.

---

## 4. Các công cụ hỗ trợ

### 4.1 Tóm tắt

| Công cụ | Role | Loại | License | Tự host | Mức sẵn sàng |
|---|---|---|---|---|---|
| manga-image-translator | Letterer | OCR / dịch / xóa chữ / dàn chữ | GPL-3.0 | Có (API, Docker) | Cao cho luồng nhập ảnh |
| Manga-Panel-Extractor | Artist, Editor | Tách panel từ ảnh | MIT | Có (CLI) | Cao, cần kiểm tra kết quả |
| Manga-Panel-LayoutGAN | Artist | Sinh layout | MIT | Có | Thử nghiệm |
| ComfyUI | Artist | Engine workflow ảnh | GPL-3.0 | Có (API local) | Cao |
| StoryDiffusion | Artist | Chuỗi ảnh nhất quán | Apache 2.0 | Có, GPU lớn | Thử nghiệm |
| rembg | Artist | Tách nền | MIT (model có license riêng) | Có (CLI, thư viện, HTTP server) | Cao |
| LlamaGen Comic API | Writer + Artist | SaaS tạo comic | Thương mại | Không | Cần kiểm chứng định dạng output |

### 4.2 Chi tiết

**manga-image-translator** ([repo](https://github.com/zyddnys/manga-image-translator)): pipeline phát hiện chữ → OCR → dịch → inpaint xóa chữ cũ (LaMa hoặc Stable Diffusion) → render chữ mới. 20+ ngôn ngữ, dịch online (DeepL, OpenAI, Sakura) hoặc offline (NLLB, Sugoi, M2M100, Qwen2). Có CLI, Web UI, REST API, Docker; GPU tùy chọn. GPL-3.0, README tự nhận còn giai đoạn đầu. Dùng cho luồng nhập trang ảnh có sẵn hoặc dịch; trang do PanelForge tạo đã có script nên không cần OCR lại. GPL: chạy như service tách biệt.

**Manga-Panel-Extractor** ([repo](https://github.com/adenzu/Manga-Panel-Extractor)): tách panel bằng xử lý ảnh (OpenCV), xuất mỗi panel một file. Thiết kế cho manga, có thể lỗi với manhwa; tách joint panel chậm ~10 lần; khi thất bại có thể trả ảnh giống ảnh gốc, nên cần bước kiểm tra kết quả. MIT.

**Manga-Panel-LayoutGAN** ([repo](https://github.com/koesan/Manga-Panel-LayoutGAN)): LayoutGAN++ (GAN Transformer), input là số panel (`--num_elements`), output là tọa độ lưu dạng pickle; huấn luyện trên MangaZero. MIT. Hợp với yêu cầu 5.3 (gợi ý layout, người Accept/Edit/Reject). Không tự hiểu kịch bản; cần logic chọn số panel và bước kiểm tra layout (chồng lấn, panel quá nhỏ).

**ComfyUI** ([repo](https://github.com/comfyanonymous/ComfyUI)): engine node-graph, workflow lưu dạng JSON, API local, hàng đợi bất đồng bộ, chạy lại phần thay đổi, README nêu chạy được từ khoảng 4GB VRAM + 8GB RAM. Hỗ trợ Flux, SDXL, SD3.5…; mở rộng bằng custom nodes. GPL-3.0; license từng model là riêng. Là lớp thực thi hợp nhất cho **tạo ảnh nháp** (5.8), có thể gồm cả bước tách nền qua custom node. Không tự bảo đảm nhân vật nhất quán hay đúng script.

**StoryDiffusion** ([repo](https://github.com/HVision-NKU/StoryDiffusion)): Consistent Self-Attention (SD1.5/SDXL) giữ nhân vật nhất quán qua chuỗi ảnh; cần tối thiểu 3–5 prompt; thử trên GPU 24GB, bản low-VRAM dự kiến >20GB. Apache 2.0. Mức thử nghiệm; cân nhắc kỹ thuật tương đương trong ComfyUI (reference, IP-Adapter) trước.

**rembg** ([repo](https://github.com/danielgatis/rembg)): tách nền bằng mô hình segmentation, 15+ model (u2net, isnet-general-use, birefnet-general, birefnet-portrait, **isnet-anime** cho nhân vật hoạt hình, SAM chọn theo điểm, ViTMatte làm mịn viền). Chạy bằng CLI, thư viện Python hoặc **HTTP server**; CPU hoặc GPU; mỗi model ~110–380MB (bria-rmbg khoảng 1GB). Rembg là MIT nhưng **model mặc định `bria-rmbg` có license BRIA riêng, thương mại phải trả phí** → chọn model khác (ví dụ isnet-anime, u2net) và kiểm tra license của model đó.

**LlamaGen Comic API** ([trang](https://llamagen.ai/comic-api)): `POST /v1/comics/generations`, có MCP remote; input prompt hoặc script (`promptUrl`), 500+ preset, 1–20 panel; output JSON preview và metadata; 150 credit/comic. Free 600 credit; Starter $90/năm đến Scale $4.000/năm. Animation/Character/Scene/Storyboard API còn waitlist. Phụ thuộc dịch vụ ngoài; chưa rõ output có tách panel/bubble/tọa độ để nhập vào schema PanelForge; nội dung truyện gửi bên thứ ba cần Admin cấu hình. Nên thử gói Free trước.

---

## 5. Tính khả thi của hai tính năng mới

### 5.1 Họa sĩ tải ảnh lên và tách nền: **khả thi, độ khó thấp–trung bình**

- Tách nền là bài toán đã giải sẵn. rembg chạy được như một service HTTP riêng, backend chỉ gọi API.
- Cần thêm ở backend (chưa thấy trong Domain): entity **Asset** lưu file (theo 6.5 nên content-addressed để chống trùng); **phiên bản artwork** gắn Panel và stage; **asset dẫn xuất** có `SourceAssetId`, thao tác, tên tool/model; job bất đồng bộ cho ảnh lớn (6.4).
- Luồng: Artist tải ảnh → hệ thống lưu asset gốc không đổi → Artist bấm "Tách nền" → job gọi rembg → kết quả là asset mới gắn với asset gốc → Artist xem và chấp nhận → dùng làm `ARTWORK_LAYER` hoặc element nhân vật.
- Lưu ý chất lượng (suy luận, cần thử): các model tách nền nhắm vào vật thể và người; **bản mực đen trắng (nét trên nền trắng)** dễ cho kết quả kém hoặc mất nét mảnh, trong khi **ảnh màu và nhân vật** hợp hơn. Nên thử `isnet-anime` trên vài trang mẫu trước, và luôn cho Artist xem/chỉnh trước khi dùng.
- Rủi ro license: tránh model mặc định `bria-rmbg` nếu có kế hoạch thương mại.

### 5.2 AI tạo ảnh: **khả thi, độ khó trung bình–cao, cần giữ đúng ràng buộc**

- Đã bổ sung vào requirements mục 5.8. Điểm then chốt: ảnh sinh ra là **Suggestion** (5.6), không tự thành artwork; lưu thành phiên bản mới, không ghi đè; ghi provenance (model, tham số, prompt, reference, output, người chấp nhận, mức người sửa); Admin kiểm soát provider/model/quota.
- Dự án đã có nền: `AiSuggestion`, `AiSuggestionDecision`, `AiUsageRecord`, `AiWorkflowRun`, cấu hình AI theo workspace. Ảnh có thể đi theo luồng Suggestion này thay vì làm đường mới.
- Hai phương án thực thi:
  - **Tự host ComfyUI** (GPL-3.0, service tách biệt): kiểm soát tốt, workflow lưu JSON để truy vết; cần GPU và chọn model.
  - **Provider ngoài do Admin cấu hình** (ví dụ API tạo ảnh): không cần GPU, nhưng gửi nội dung ra ngoài và tốn quota.
- Rủi ro: nhất quán nhân vật không được bảo đảm (cần reference từ Series Bible và người duyệt); license của model sinh ảnh; chi phí GPU; ảnh AI trong sản phẩm xuất bản có thể gặp vấn đề bản quyền/chính sách nền tảng, nên ghi nhãn rõ ảnh AI qua provenance.
- Vị trí hợp lý trong pipeline: bản nháp ở **Thumbnail**, nền, hoặc ảnh tham khảo; Artist vẫn chịu trách nhiệm cho Pencil/Ink/Color.

---

## 6. Kiến trúc "một bộ não" đề xuất

```
Người dùng (role) ──► Backend (kiểm quyền Workspace, version, provenance)
                          │  danh sách hành động được phép
                          ▼
                     Supervisor ── chọn agent theo NHIỆM VỤ
          ┌──────────┬──────────┼──────────┬───────────┐
       Writer     Artist     Letterer    Editor     Producer
        Agent      Agent      Agent       Agent       Agent
          │          │           │           │           │
       LLM+Bible  ComfyUI /   manga-image-  Checker    Dữ liệu
                  rembg /     translator    bằng code  pipeline
                  LayoutGAN
```

- Supervisor chọn agent theo nhiệm vụ, không chỉ theo role người đăng nhập. Ví dụ Producer hỏi "đánh giá trang này" → EditorAgent kiểm chất lượng + ProducerAgent đánh giá tiến độ, không tự cấp quyền sửa script.
- Kiểm tra chính xác dùng **code**, LLM chỉ giải thích: chữ vượt khung (cần font và kích thước thật), panel chồng lấn, thiếu binding, sai stage, quá hạn.
- Mọi output AI đi qua Suggestion → người duyệt, kèm provenance.

---

## 7. Ví dụ một trang đi qua pipeline

Cảnh: *An gặp người lạ dưới mưa và nhận ra chiếc vòng quen thuộc.*

| Bước | Ai | Việc |
|---|---|---|
| 1 | Writer + WriterAgent | Viết script, nhịp: nhìn thấy → tiến lại → nhận ra chiếc vòng |
| 2 | Artist + ArtistAgent | Nhận 2 phương án layout (4 hoặc 5 panel); chọn 5 panel, panel chiếc vòng lớn hơn |
| 3 | ArtistAgent → ComfyUI | (Tùy chọn) ảnh nháp từng panel làm tham khảo, ở dạng Suggestion |
| 4 | Artist | Vẽ Pencil → Ink → Color bên ngoài, tải từng phiên bản lên, nộp review |
| 5 | Artist + rembg | Tách nền nhân vật để ghép vào panel khác (tùy chọn), duyệt kết quả |
| 6 | Letterer + LettererAgent | Đặt balloon, gắn chữ với câu thoại và speaker, xử lý cảnh báo tràn khung |
| 7 | Editor + EditorAgent | Kiểm tra chiếc vòng đúng Bible, hình khớp hành động; duyệt hoặc tạo change request |
| 8 | Producer | Duyệt, khóa chương, xuất |

---

## 8. Lộ trình đề xuất

1. **Lõi sản xuất:** Assignment API → nộp bài → duyệt/trả về → chuyển stage → tiến độ Panel → dependency tối thiểu. Kèm **Asset + phiên bản artwork** (cần cho mọi bước sau).
2. **Canvas và lettering:** layout chỉnh được, balloon gắn script, cảnh báo tràn khung.
3. **AI văn bản:** WriterAgent, EditorAgent, LettererAgent, ProducerAgent + checker bằng code.
4. **Tách nền:** service rembg, asset dẫn xuất, thử `isnet-anime` trên trang mẫu.
5. **AI tạo ảnh:** ComfyUI (hoặc provider ngoài) qua luồng Suggestion, lưu provenance.
6. **Thử nghiệm:** LayoutGAN, manga-image-translator, Panel Extractor, StoryDiffusion, LlamaGen (gói Free).

---

## 9. Rủi ro và điểm cần kiểm chứng

- **License:** manga-image-translator và ComfyUI là GPL-3.0 → chạy service tách biệt, xem xét trước khi thương mại hóa; license model ảnh và model tách nền là vấn đề riêng (đặc biệt `bria-rmbg`).
- **Phần cứng:** StoryDiffusion cần >20GB VRAM; ComfyUI và rembg linh hoạt hơn.
- **Chất lượng không bảo đảm:** nhất quán nhân vật, tách nền bản mực đen trắng, layout theo kịch bản, tách panel manhwa đều cần người duyệt.
- **Dữ liệu ra ngoài:** provider ngoài (LlamaGen hoặc API tạo ảnh) chỉ dùng khi Admin cấu hình; rủi ro quyền riêng tư nội dung chưa phát hành (7.4).
- **Hạ tầng thiếu:** chưa thấy Asset/lưu file trong Domain; đây là nền cho cả upload, tách nền và AI tạo ảnh.
- **Chưa kiểm chứng:** mới đọc README và code, chưa chạy thử công cụ nào; nên thử từng công cụ với 2–3 trang mẫu.

## Nguồn
- https://github.com/zyddnys/manga-image-translator
- https://github.com/adenzu/Manga-Panel-Extractor
- https://github.com/koesan/Manga-Panel-LayoutGAN
- https://github.com/comfyanonymous/ComfyUI
- https://github.com/HVision-NKU/StoryDiffusion
- https://github.com/danielgatis/rembg
- https://llamagen.ai/comic-api
