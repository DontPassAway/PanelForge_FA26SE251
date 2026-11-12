# PanelForge – Toàn bộ scope theo phiếu đăng ký V1.2

> **Đã thay thế (2026-11-12):** Phiếu đăng ký V1.2 không còn dùng làm căn cứ phạm vi. Phạm vi và nghiệp vụ hiện hành theo `panelforge-fe/docs/PANELFORGE_MASTER_SPECIFICATION_V2_6.md`; việc đang làm ghi ở `panelforge-fe/docs/TEAM_WORKBOARD.md`. Tài liệu này giữ làm lịch sử.

Nguồn: `docs/reports/GFA26SE140_Phieu_FA26SE251_JapanComicWorkflow_V1.2.docx` (bản hiện tại, Record of Changes: dòng 1.0 và 1.1 giữ nguyên, dòng 1.2 là các chỉnh sửa sau Review 1, cập nhật đến 06/10/2026).
Nhóm 5 người, thời gian 09/2026 – 03/2027, giảng viên hướng dẫn: Nguyễn Tấn Phúc.

---

## 1. Tổng quan

**PanelForge** là nền tảng web quản lý sản xuất manga/webtoon. Tác phẩm không được coi là một đống file mà là **dữ liệu có cấu trúc, có phiên bản**, và cả nhóm cùng làm việc trên đó.

Ba trụ cột:
1. **Biểu diễn có cấu trúc** của tác phẩm (đến từng balloon).
2. **Pipeline sản xuất** chạy trên biểu diễn đó.
3. **Lớp AI multi-agent**: mỗi agent hỗ trợ một vai trò, mọi đóng góp đều được ghi provenance.

Các vấn đề cần giải quyết: dữ liệu rời rạc, cộng tác phân mảnh, lỗi nhất quán (continuity), mất lịch sử sửa, tiến độ không nhìn thấy, không có provenance cho AI, AI dùng rời rạc, feedback bên ngoài không gắn với phiên bản.

---

## 2. Mô hình nội dung

```
Workspace → Series → Chapter ─┬─ Page → Panel → Element      (cây vật lý, chính)
                              └─ Scene → Script block        (cấu trúc tự sự, song song)

Panel ──tham chiếu──▶ Script block      (scene của panel được suy ra từ block nó mang)
```

- **Scene không còn chứa Page.** Một Page có thể có Panel của nhiều Scene, và một Scene có thể trải qua nhiều Page.
- **Script block** là một dòng hành động, thoại, caption hoặc SFX, có thứ tự, thuộc Scene. Script block có thể tồn tại trước khi có Page hay Panel. Khi phân trang lại, block giữ nguyên, chỉ đổi panel được gán.
- Quy tắc: Scene liền mạch, không chồng lấn. Scene **tuỳ chọn khi làm tay**; các luồng agent (ví dụ Writer Agent viết script) chạy trên một Scene. Một Panel thuộc đúng một Page và tối đa một Scene.
- **Spread** là liên kết giữa hai Page, **splash** là Page có một Panel, panel không viền là thuộc tính hiển thị. **Thứ tự đọc** là thuộc tính riêng, hướng đọc đặt theo cờ của Series.
- **Element** là đơn vị có kiểu: dialogue balloon, narration box, sound effect, character instance, artwork layer.
- Mỗi thực thể có metadata, vị trí và quan hệ riêng nên có thể truy vấn và kiểm tra.
- **Frame template** chỉ là cấu hình khởi tạo các Panel của Page mới. Sau khi Page tạo ra, các Panel độc lập, đổi template sau không ảnh hưởng page cũ.
- **Series Bible**: kho có cấu trúc, versioned theo chương, gồm nhân vật, địa điểm, đạo cụ, thuật ngữ, quy tắc phong cách, sự kiện đã chốt. Vừa là tham chiếu cho người, vừa là **bộ nhớ chung cho AI agent** (luôn đọc phiên bản có hiệu lực tại chương đang làm).
- **Character sheet** (trong Bible): sketch hành động, cảm xúc, chuyển động do artist cung cấp, cùng tính cách, cách nói, phong cách vẽ, chế độ màu (lõi: trắng đen; màu là mở rộng). Được phê duyệt và **khoá** làm tham chiếu duy nhất, versioned theo chương. Phiên bản mới có hiệu lực từ chương được chọn. Sau khi khoá chỉ có thể thay thế bằng phiên bản mới, không sửa được.

---

## 3. Actor và Role

### 3.1 Actor là người

| Actor / Role | Chức năng chính |
|---|---|
| **Administrator** | Quản lý user, role, permission. Cấp hoặc thu hồi quyền tạo Studio Workspace. Cấu hình AI (provider, model, bật/tắt, quota token tháng). Đặt luật cho từng Workspace: storage (GB), session policy (thời gian sống, idle timeout, số phiên đồng thời), quota sinh ảnh. Retention và backup. Xem audit log, báo cáo AI usage, AI provenance, usage từng Workspace. Quản lý master data: stage template, element type, frame template, export preset. **Không tham gia sản xuất.** |
| **Producer / Series Owner** | Người được Admin cho phép tạo Workspace thì trở thành Producer của Workspace đó. Tạo và cấu hình Series (stage, role, lịch phát hành), ghi synopsis và story arc. Bật/tắt AI trong giới hạn Admin cho phép. Mời thành viên, giao việc và deadline theo chapter/panel. Quản lý Series Bible. Phê duyệt và khoá character sheet. Xem dashboard (tiến độ theo stage, workload, quá hạn, burndown) và màn hình usage. Duyệt hoặc từ chối chapter, khoá chapter, tạo export package. Tạo preview link có hạn và watermark mà người bên ngoài mở được; **Producer không có quan hệ với nhà xuất bản**, mọi liên hệ với NXB nằm ngoài PanelForge. Mở phiên bản chapter mới để làm lại. Khôi phục phiên bản cũ. Xem provenance report |
| **Writer** | Cùng Producer ghi synopsis và story arc của Series, lập outline cho từng chapter. Viết script trong editor có cấu trúc (scene, dialogue, direction). Chạy **Writer Agent** (nháp scene) và **Continuity Agent** (kiểm tra với Bible), rồi duyệt từng gợi ý. Chạy **Storyboard Agent** để tách script đã duyệt thành Page/Panel. Mô tả panel, shot note, thoại, gán người nói. Liên kết với Bible. So sánh và khôi phục phiên bản script |
| **Artist** | Hàng đợi việc cá nhân (brief, tham chiếu, deadline). Lập layout trong thumbnail/panel-grid editor, bắt đầu từ frame template hoặc xin gợi ý từ Storyboard Agent. Upload artwork theo stage. Truy cập reference và asset library. Upload sketch nhân vật vào Bible. Trả lời change request. Upload bản nháp nhân vật và yêu cầu sinh ảnh (pose, cảm xúc, shot scale, phong cách, màu: lõi là trắng đen, màu là mở rộng), rồi accept/edit/reject. Dùng nhân vật sinh ra như một artwork-layer element đã tách nền. Review gợi ý AI |
| **Letterer** | Đặt, đổi kích thước, style balloon/narration/SFX trên canvas. Gắn mỗi text element với dòng script và người nói. Nhận cảnh báo tràn balloon, che artwork, sai thứ tự đọc (**tính bằng code**). Typographic preset theo Series. Nhờ Letterer Agent rút gọn thoại cho vừa balloon (**lõi**) và dịch thoại (**scope mở rộng**) |
| **Editor / Reviewer** | Review Panel/Page/Chapter, so sánh song song với bản cũ. Comment và vẽ chú thích **gắn vào vùng của đúng phiên bản**. Chạy consistency check và phân loại phát hiện (nhân vật, thuật ngữ, sự kiện, thứ tự đọc). Xem báo cáo nhất quán hình ảnh với character sheet. Chạy **Editor Agent** để nháp report và comment từ chối. Ra change request, theo dõi xử lý. **Quyết định duyệt/từ chối luôn do người** |
| **Publisher / Reader** (external reviewer, không phải thành viên workspace) | Chỉ mở **preview link có hạn, có watermark** ở chế độ đọc. Feedback ở mức chapter/page/panel, gắn với phiên bản đang xem. Không có quyền sản xuất, không vào workspace |
| **Mọi user đã đăng nhập** | Đăng nhập, hồ sơ, tuỳ chọn thông báo. Nhận thông báo in-app và email. Tìm kiếm script, panel, Bible, comment. Xem activity feed, lịch sử phiên bản và AI provenance trail của nội dung được phép |

### 3.2 Actor không phải người

| Actor | Vai trò |
|---|---|
| **AI agents** (Writer, Continuity, Storyboard, Editor, Artist; Letterer, Producer là mở rộng) | Chỉ đưa ra **gợi ý**. Không đổi stage, không duyệt gate |
| **LLM provider / image-model provider** (bên ngoài) | Cung cấp model qua gateway. Có thể chậm, bị giới hạn hoặc ngừng |
| **Hệ thống** (code) | Kiểm tra xác định: hình học panel, quyền, deadline, tràn balloon, luật nhất quán, quota |

---

## 4. Flow

### F1. Quản trị Workspace
```
Admin cấp quyền tạo Workspace cho user
  → User tạo Studio Workspace → trở thành Producer
  → Admin đặt giới hạn: storage GB, token AI/tháng, quota ảnh, session policy
  → Mọi thao tác bị chặn nếu vượt giới hạn (kiểm tra TRƯỚC khi làm)
  → Producer và Admin xem màn hình usage
```
Giới hạn áp dụng cho: upload, gọi AI, sinh ảnh, đăng nhập, gia hạn token. Vượt giới hạn thì trả lỗi rõ ràng, không lỗi âm thầm.

### F2. Thiết lập Series
```
Producer tạo Series → cấu hình stage, role, lịch phát hành
  → mời thành viên, gán role
  → tạo Series Bible
  → artist upload sketch → Producer phê duyệt và khoá character sheet (hiệu lực từ chương X)
```

### F3. Pipeline sản xuất chính
```
Script → Thumbnail → Pencil → Ink → Color → Letter → Review → Approved
```
- Stage có thể cấu hình theo studio (state machine có guard).
- Giao việc theo Page/Panel, có deadline và phụ thuộc, theo dõi tiến độ ở mức panel.
- Chapter được Producer duyệt thì bị khoá.

### F4. Quy trình AI multi-agent (đường chính)
```
Brief + Bible revision hiện hành + script cũ
  → [Writer Agent] nháp script scene
  → [Continuity Agent] kiểm tra với Bible, mỗi phát hiện kèm bằng chứng
        ├─ có lỗi → quay lại Writer (tối đa 2 vòng sửa)
        └─ ổn
  → Người duyệt script (accept / edit / reject)
  → [Storyboard Agent] tách thành Page + Panel (thứ tự đọc, shot scale, link dòng script)
  → Người duyệt
  → [Editor Agent] soạn review report và comment từ chối từ findings + checklist do code gom
  → Editor (người) quyết định duyệt / từ chối
```
Mỗi bước là gợi ý. Mọi bước đều được lưu.

### F5. Pipeline ảnh nhân vật (7 bước)
```
1. Artist chọn character sheet đã khoá
2. Upload sketch nhân vật
3. Yêu cầu ảnh theo pose, cảm xúc, shot scale, phong cách, màu (lõi trắng đen, màu là mở rộng)
   → image model sinh ảnh (sheet + sketch là tham chiếu bắt buộc)
4. Segmentation: tách nhân vật khỏi nền
5. So sánh với reference sheet, chấm điểm tương đồng
6. Artist accept / edit / reject
7. Kết quả được lưu thành artwork-layer Element độc lập + content revision mới
```
Nếu tách nền lỗi hoặc ảnh vẽ ngoài hệ thống thì artist upload layer thủ công, và vẫn áp dụng kiểm tra tương đồng.

**Giới hạn bóc tách ảnh:** artist có thể upload từng layer độc lập và khai báo loại Element. Với ảnh đã gộp, lõi chỉ tách nhân vật khỏi nền đơn giản, có người kiểm tra kết quả. Hệ thống **không cam kết** tự động tách cả trang thành các Element có ngữ nghĩa (nhân vật, nền, balloon, SFX).

### F6. Review và làm lại
```
Artist đánh dấu "ready for review"
  → Editor so sánh với bản cũ, comment/vẽ vùng gắn vào đúng version
  → Editor chạy consistency check, xem báo cáo nhất quán hình ảnh
  → duyệt  → Producer duyệt chapter → khoá
  → từ chối → change request → rework tạo VERSION MỚI (bản cũ giữ nguyên)
```

### F7. Feedback từ Publisher
```
Producer tạo preview link (có hạn, watermark)
  → Publisher đọc, để feedback gắn với đúng phiên bản chapter/page/panel
  → Producer mở phiên bản chapter mới để làm lại (nếu cần)
```
Việc xuất bản nằm **ngoài** hệ thống. PanelForge chỉ chuẩn bị phiên bản và export package.

### F8. Phiên bản và provenance
```
Mọi thay đổi → event ghi nối đuôi (append-only) → lịch sử đầy đủ
  → so sánh song song, rollback về bất kỳ phiên bản nào
Với AI: Workflow Run → Agent Step → Suggestion → Human Decision → Content Revision
```

### F9. Export
```
Dữ liệu có cấu trúc → ảnh trang in, PDF, CBZ, webtoon cuộn dọc + JSON mở
```
Có thể nhập lại JSON bằng schema công khai.

---

## 5. Phần AI

### 5.1 Nguyên tắc
- Agent chỉ **gợi ý**. Con người luôn accept, edit hoặc reject. Agent không đổi stage, không duyệt gate.
- **Kiểm tra xác định do code làm**, không để model: hình học panel, quyền, deadline, tràn balloon, luật nhất quán.
- Dùng quy trình có các bước rõ ràng, không để agent trò chuyện tự do. Vòng sửa có giới hạn.
- Tất cả agent dùng **một model qua một gateway** để thực thi quota và ghi usage.
- **Bộ nhớ Bible (versioned memory):** các mục Bible ở revision có hiệu lực tại chương đang làm được đưa vào ngữ cảnh agent bằng truy vấn có cấu trúc, chụp thành snapshot bất biến. Vector retrieval chỉ thêm khi ngữ cảnh vượt ngân sách của model (ngưỡng thống nhất với giảng viên). Code hiện đưa toàn bộ mục Bible hiệu lực của Series, chưa lọc theo scene.
- AI chạy trong **service tách riêng**, không bao giờ gọi trong một request nghiệp vụ lõi.
- Đóng góp của dự án là tích hợp agent vào cộng tác theo vai trò, bộ nhớ Bible có phiên bản, review gắn panel và provenance. **Không huấn luyện model.**

### 5.2 Các agent

| Agent | Phạm vi | Input | Output |
|---|---|---|---|
| **Writer** | Core | Brief, chapter outline, Bible revision hiện hành, script trước | Dòng script của một scene |
| **Continuity** | Core | Script, Bible | Danh sách phát hiện, mỗi cái kèm bằng chứng (Bible revision + dòng script) |
| **Storyboard** | Core | Script đã duyệt | Page/Panel kèm thứ tự đọc, shot scale (wide/medium/close-up), link dòng script |
| **Editor** | Core (giai đoạn đầu chỉ gom findings và checklist do code chuẩn bị) | Findings + checklist do code gom | Review report, comment từ chối (nháp) |
| **Artist** | Core (luồng ảnh riêng) | Character sheet khoá, sketch, pose/cảm xúc/shot scale | Chuẩn bị yêu cầu cho pipeline ảnh |
| **Letterer** | Core (rút gọn thoại cho vừa balloon); Mở rộng (dịch đa ngôn ngữ) | Thoại, giới hạn độ dài balloon | Thoại rút gọn, và bản dịch vừa balloon (mở rộng) |
| **Producer** | Mở rộng | Tiến độ | Giải thích tiến độ và điểm nghẽn |

### 5.3 Nhất quán nhân vật (mục tiêu trung tâm), 3 lớp
1. **Văn bản**: Continuity Agent kiểm tra script với sheet, dẫn bằng chứng.
2. **Luật**: các luật nhất quán chạy bằng code.
3. **Hình ảnh**: mọi hình nộp lên hoặc sinh ra được so với reference sheet, lệch thì gắn cờ.

Đo lường: dùng tập lỗi gài sẵn (seeded-defect), báo cáo precision/recall (tối thiểu 70%, **giá trị đề xuất chờ xác nhận với giảng viên**), điểm tương đồng dưới ngưỡng 0.75 trên thang 0–1 (cũng là đề xuất) thì cần người xem. **Hệ thống không hứa nhất quán tuyệt đối**, chỉ làm lộ ra các lệch.

### 5.4 Sinh ảnh
- Gọi API của model ảnh hosted (có thể không phải Gemini; Gemini API không có free tier nên nhóm cần xác nhận nhà cung cấp và chi phí) hoặc model chạy local (cần GPU 8–12 GB).
- Nằm sau **adapter thay được**. Nền tảng vẫn dùng được khi không có: khoá sheet, kiểm tra nhất quán và upload thủ công không phụ thuộc sinh ảnh.
- Ảnh sinh ra được gắn nhãn, qua bước accept/edit/reject với provenance đầy đủ.
- Quyền sử dụng ảnh sinh ra phải xác minh với nhà cung cấp trước khi dùng ngoài demo.
- **Ranh giới:** AI sinh ảnh nhân vật. Background, bố cục trang và balloon được lắp trong editor từ các Element có cấu trúc. Sinh cả cảnh gồm nhân vật và background nằm ngoài lõi.

### 5.5 Provenance AI
Chuỗi: **Workflow Run → Agent Step** (agent, model, tham số, phiên bản prompt, input revision) **→ Suggestion → Human Decision** (accept/edit/reject, ai làm, sửa bao nhiêu) **→ Content Revision**.
- Suggestion bị từ chối cũng được lưu.
- Output gốc của AI không bao giờ bị sửa. Chỉnh sửa tạo revision dẫn xuất.
- Trả lời được: agent nào, từ script và Bible revision nào, tạo ra panel này, và ai đã duyệt.

### 5.6 Chịu lỗi
Timeout (30 giây, đề xuất chờ xác nhận), circuit breaker, provider dự phòng. Run bị ngắt giữ các bước đã xong và tiếp tục từ bước lỗi. Đường làm tay luôn dùng được khi AI hỏng.

### 5.7 Đánh giá
Đánh giá agent trên tập lỗi gài sẵn, so với baseline một prompt. Đo precision/recall của phát hiện continuity, độ hợp lệ của storyboard, tỉ lệ chấp nhận và công sửa khi người review.

### 5.8 Loại trừ
Không sinh cả trang. Không có công cụ vẽ tự do (artist vẽ ở phần mềm ngoài rồi upload). Không đưa manga thương mại có bản quyền vào hệ thống. Chỉ gửi nội dung được phép cho provider khi demo.

---

## 6. Phạm vi lõi và mở rộng

**Lõi (bắt buộc):** mô hình nội dung, Series Bible, pipeline sản xuất, review gắn panel, provenance, workspace governance, workflow Writer → Continuity → Storyboard → Editor có người duyệt, Artist Agent, pipeline ảnh nhân vật (khoá sheet, kiểm tra 3 lớp, sinh ảnh sketch-to-image demo trên ít nhất 1 nhân vật), cảnh báo tràn balloon tính bằng code, AI rút gọn thoại cho vừa balloon (Letterer Agent). Demo lõi là **manga trắng đen**; chế độ màu (stage Color và kiểm tra nhất quán màu) là cấu hình mở rộng, không nằm trong nghiệm thu.

**Mở rộng:** dịch thoại sang ngôn ngữ khác (trong giới hạn độ dài balloon), Producer Agent, sinh cả trang hoặc cả cảnh. Co-editing thời gian thực có thể giảm xuống optimistic locking kèm phát hiện xung đột.

---

## 7. Yêu cầu phi chức năng (tóm tắt)

Toàn vẹn lịch sử (không ghi đè, không xoá) · Provenance đầy đủ và mang theo khi export · Nhất quán khi sửa đồng thời · Đáp ứng nhanh (tác vụ nặng chạy bất đồng bộ) · AI suy giảm nhẹ nhàng · **Nhất quán nhân vật đo được** · Bảo vệ tác phẩm chưa phát hành (link ngắn hạn, watermark, ghi log truy cập) · Quy mô nội dung (lưu theo nội dung băm, loại trùng) · Cấu hình không cần deploy lại · **Quản trị tài nguyên Workspace** · Dễ dùng cho creative (kéo thả, dark theme, chịu mất mạng ngắn, song ngữ Việt/Anh; đo bằng usability test với Task Success Rate ≥ 80%, đề xuất) · Di động mô hình (schema mở, định dạng chuẩn) · Dễ bảo trì và kiểm thử (test tự động, CI).

---

## 8. Phân công Work Package (sau khi chia lại)

| WP | Vai trò | Nội dung | Done khi |
|---|---|---|---|
| **WP1** Project Management, Domain Analysis, Content Schema | PM / BA / QA Lead | Yêu cầu với đối tác creator, phân tích quy trình, đặc tả content model (Workspace, Series, Chapter, cây Page/Panel/Element và cấu trúc Scene/Script block) và Bible, kiến trúc, event store, test plan + test hệ thống + usability test, tài liệu, dataset demo, gói bảo vệ, luật governance, đặc tả agent | Schema, Bible, governance, agent và protocol đánh giá (kể cả mức đo UX như Task Success Rate ≥ 80%, đề xuất) được giảng viên duyệt |
| **WP2** Content Domain, Provenance, Version Control | Backend / Domain Engineer | Aggregate, Bible domain, event store, projection, content-addressed storage, version/diff/rollback, provenance (kể cả chuỗi AI), schema versioning, **pipeline render/export** | Dựng lại được trạng thái quá khứ; truy vết run → revision bằng test tự động; export PDF/CBZ/JSON |
| **WP3** Production Pipeline, Access Control, Deployment | Backend / DevOps | Workflow engine, giao việc/deadline, review/approval, thông báo, RBAC + ABAC, link ngoài có hạn, enforcement giới hạn Workspace, **container, CI/CD, deploy cloud, monitoring, backup** | Giới hạn được thực thi; chapter đi từ script đến approval có kiểm quyền; stakeholder để lại feedback; hệ thống deploy qua CI/CD |
| **WP4** Structured Editors, Production Workspace | Frontend | Script editor, panel canvas, thumbnail/layout planner, lettering canvas, annotation, so sánh phiên bản, dashboard, task queue, reading/preview, màn hình character sheet, frame template và **màn hình usage** (storage, token AI, ảnh, session so với giới hạn) | Producer, writer, artist, editor hoàn thành luồng chính trong usability test, đạt Task Success Rate ≥ 80% (đề xuất) |
| **WP5** AI Assistance Services, AI Evaluation | Full-stack / AI Engineer | Dịch vụ multi-agent (Writer, Continuity, Storyboard, Editor, kèm **Artist Agent** và **Letterer Agent rút gọn thoại**), gateway, Bible memory, pipeline ảnh nhân vật, adapter API ảnh, **màn hình AI review và provenance view**, đánh giá agent | Workflow 4 agent, Artist Agent, Letterer rút gọn thoại và pipeline nhân vật chạy end-to-end; màn hình AI review và provenance view hỗ trợ accept/edit/reject; có báo cáo đánh giá; demo cho thấy AI bị ngắt mà nền tảng vẫn chạy |

Chủ sở hữu của cả 5 WP vẫn ghi `[member to be assigned]`.

---

## 9. Sản phẩm bàn giao

Nền tảng web đã deploy (đa vai trò) · Backend + API có tài liệu · Dịch vụ AI multi-agent tách riêng + pipeline ảnh · Đặc tả JSON schema mở kèm tool import/export (kiểm chứng chỉ với **cấu trúc**, không dùng nội dung, từ tác phẩm tự sáng tác hoặc có giấy phép, vì không đưa manga thương mại có bản quyền vào hệ thống) · Bộ tài liệu (SRS, SDD, DB/event-store/AI provenance, test plan và báo cáo, báo cáo đánh giá AI, hướng dẫn deploy, hướng dẫn người dùng song ngữ) · Dataset demo ≥ 3 chương có lịch sử sản xuất đầy đủ, sheet khoá, ảnh sinh từ sketch, bước AI có provenance, ít nhất một phiên bản chapter tạo từ feedback · Video demo, slide, mã nguồn kèm hướng dẫn build/deploy.

---

## 10. Ngoài scope

- Xuất bản/phát hành (diễn ra ngoài hệ thống; Producer không liên kết với publisher).
- Công cụ vẽ tự do.
- Sinh cả trang bằng AI.
- Dùng manga thương mại có bản quyền.
- Huấn luyện model.
- Scene xen kẽ phức tạp (cross-cutting giữa hai scene trong một chuỗi).
- Canvas chỉnh sửa webtoon cuộn dọc vô hạn (các trang đã dựng vẫn xuất được thành ảnh cuộn dọc).
- Panel hình dạng tự do mà thứ tự đọc không suy ra được.
- Public Reader, danh mục công khai và đăng ký trả phí: **tính năng thêm đã có trong code, ngoài phạm vi đăng ký, không thuộc nghiệm thu** (nhóm quyết định giữ).

---

## 11. Điểm còn bỏ ngỏ trong phiếu

- Tên chủ sở hữu từng WP.
- Các giá trị nghiệm thu timeout 30 giây, precision/recall tối thiểu 70%, ngưỡng tương đồng 0.75, Task Success Rate ≥ 80% là **đề xuất**, chưa được giảng viên xác nhận.
- "Hoạt ảnh" trong nhận xét của giảng viên chưa rõ có phải animation không; phiếu chưa đề cập.
- Nhà cung cấp API sinh ảnh chưa chọn.
- Ngày ký trên phiếu (15/09/2026) sớm hơn các ngày sửa đổi.
- Mô hình Scene/Page/Panel đã được sửa theo research (xem mục 2) nhưng **chưa được người làm manga kiểm chứng**. Nhóm dự định hỏi một creator hoặc câu lạc bộ thử nghiệm.
