# Luồng làm việc của từng role, kèm ví dụ cụ thể

> **Tài liệu:** Bổ sung cho `role-workflows.md`. Tài liệu kia mô tả luồng theo từng role; tài liệu này đi qua **một ví dụ xuyên suốt** để thấy từng role làm gì, ở bước nào, và chuyện gì xảy ra khi có lỗi.
> **Cập nhật:** 04/10/2026
>
> **Đối chiếu phiếu đăng ký V1.2 (06/10/2026):** ví dụ này mô tả hành vi đã triển khai. Các điểm khác phiếu: Scene không chứa Page; Artist Agent hiện chỉ soạn prompt ảnh (pipeline ảnh nhân vật là lõi nhưng chưa làm); Letterer rút gọn thoại là lõi; Public Reader là tính năng thêm ngoài phạm vi đăng ký (nhóm quyết định giữ). Chi tiết ở `docs/implementation/PanelForge_Scope_V1.2.md`.
> **Nguồn đặc tả:** `docs/requirements/PanelForge_Capstone_Content.md` (BR, US, UC), `requirements.md`, `role-workflows.md`, `storage-architecture.md`, `storage-strategy.md`, `ai-multi-agent-demo.md`. Các mã BR/US/UC trong ngoặc là mã trong đặc tả.

---

## 1. Bối cảnh của ví dụ

| Thành phần | Giá trị ví dụ |
|---|---|
| Studio Workspace | **Moonlight** |
| Series | **"Thành phố mưa"** (đọc phải sang trái, RTL) |
| Chapter | **Chương 3** |
| Scene | **Scene 1**: An gặp một người lạ dưới mưa; người lạ làm rơi một gói vải nhỏ |
| Quy tắc trong Series Bible | *"An chỉ nhận được chìa khóa bạc từ Chương 5; trước Chương 5, An chưa từng sở hữu hay nhìn thấy chiếc chìa khóa này."* |
| Thành viên | Producer **Lan**, Writer **Minh**, Artist **Hà**, Letterer **Khoa**, Editor **Thu**, Admin **Quang** |
| Người xem thử (có link) | Đối tác **Bình** |
| Độc giả công khai (app) | **Chi** |

Tên người chỉ để minh họa; dữ liệu seed thật dùng tài khoản dạng `producer@`, `writer@`, `artist@`, `letterer@`, `editor@`.

Cảnh ở Chương 3 được chọn để **không** vi phạm quy tắc Bible: An chưa được thấy chìa khóa, nên cảnh chỉ có một gói vải chưa mở. Đây là chỗ mà AI Writer sẽ viết sai ở lượt đầu và Continuity sẽ bắt.

Cấu trúc nội dung:

```
Studio Workspace "Moonlight"
  └── Series "Thành phố mưa"      (một Workspace có thể có nhiều Series)
       └── Chapter 3 ─┬─ Page → Panel → Element      (cây vật lý)
                  └─ Scene 1 → ScriptLine         (cấu trúc tự sự; Panel tham chiếu ScriptLine)
```

Pipeline 8 stage của Series (sao chép từ template `STANDARD_MANGA`):

```
Script → Thumbnail → Pencil → Ink → Color → Letter → Review → Approved
Writer   Artist      Artist   Artist Artist  Letterer Editor   Editor
```

### Ba loại "duyệt" khác nhau, đừng nhầm

| Loại | Ai duyệt | Kết quả |
|---|---|---|
| **Duyệt cổng stage** (BR-06, BR-20) | Editor mặc định; Studio có thể cấu hình người duyệt khác cho từng stage | Item (Page/Panel) rời stage hiện tại và sang stage kế tiếp. Stage trước bị đóng băng, muốn sửa phải trả về |
| **Đạt stage cuối `Approved`** | Sau cổng Review | Item hoàn tất công đoạn sản xuất; chưa có nghĩa là chương đã khóa |
| **Duyệt và khóa chương** (UC-20, BR-12) | **Producer** | Chương chuyển `Approved → Locked`, chỉ đọc; muốn sửa phải mở lại kèm lý do (UC-23). Sau đó mới tạo preview và phát hành (`Published`) |

Các Page/Panel khác nhau đi qua pipeline **độc lập và song song**, nhưng trong cùng một Page/Panel, stage sau chỉ bắt đầu khi stage trước đã được duyệt (BR-19).

---

## 2. Vòng đời một chương, nhìn từ trên xuống

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Quang (Admin)
    actor Prod as Lan (Producer)
    actor W as Minh (Writer)
    actor A as Hà (Artist)
    actor L as Khoa (Letterer)
    actor E as Thu (Editor)
    actor X as Bình (Người xem thử)
    actor R as Chi (Độc giả công khai)

    Admin->>Prod: Cấp quyền tạo Studio cho Lan; Lan tạo Moonlight và tự thành Producer; Admin bật AI cho Moonlight
    Prod->>Prod: Tạo Series, chỉnh pipeline, mời thành viên, lập Series Bible
    Prod->>W: Giao Chapter 3: stage Script (thông báo cho Minh)
    W->>W: Viết script, chạy AI, Accept Script
    E->>W: Duyệt cổng Script
    Prod->>A: Các Page/Panel sang Thumbnail, giao việc cho Hà
    A->>A: Layout, vẽ Pencil → Ink → Color ở ngoài, tải lên
    A->>E: Nộp panel tại cổng Artwork
    E-->>A: Change request (nếu còn lỗi)
    E->>L: Duyệt cổng Artwork → stage Letter
    L->>L: Balloon, gắn thoại, xử lý cảnh báo tràn khung
    L->>E: Nộp lettering
    E->>E: Duyệt cổng Lettering, rồi Review + Continuity với Bible
    E->>Prod: Các Page/Panel đạt Approved
    Prod->>Prod: Duyệt và khóa chương (Locked)
    Prod->>X: Tạo link xem thử cho chương đã khóa, gắn email Bình
    X->>Prod: Phản hồi theo trang
    Prod->>E: Triage phản hồi
    Prod->>Prod: Nếu cần sửa: mở lại chương kèm lý do, sửa, khóa lại
    Prod->>Prod: Phát hành (Published), xuất file
    R->>R: Thấy chương trong catalog công khai và đọc
```

---

## 3. Luồng của từng role

### 3.1 Admin (`SystemRole.Admin`): Quang

**Việc của Admin:** hạ tầng và người dùng. Quyền Admin **không tự cấp quyền đọc hay sửa nội dung** trong Workspace (BR-07).

**Luồng:**

1. Tạo tài khoản; khóa hoặc mở khóa tài khoản.
2. Cấp hoặc thu hồi quyền tạo Studio (`CanCreateStudio`, BR-22).
3. Sau khi một người có quyền tạo Studio, người đó tự tạo Workspace và tự thành Producer (BR-22). Admin không mời thành viên vào Workspace.
4. Cấu hình AI cho Workspace: provider, model được phép, khóa, quota. Theo phiếu V1.2, Admin còn đặt giới hạn storage (GB), quota ảnh và session policy cho từng Workspace.
5. Cấu hình lưu trữ, danh mục dùng chung (template pipeline, element type, export preset).
6. Xem audit log và mức sử dụng AI.

**Ví dụ cụ thể:**

- Quang cấp quyền tạo Studio cho Lan, Lan tạo Workspace "Moonlight" và tự động thành Producer (BR-22). Sau này khi Lan nghỉ, Quang thu hồi quyền tạo Studio của Lan; các Workspace Lan đã tạo vẫn giữ nguyên.
- Quang bật AI cho Moonlight: model `gemini-2.5-flash`, nhập khóa (lưu mã hóa), đặt quota tháng.
- Khoa nghỉ việc, Quang khóa tài khoản của Khoa. Các việc Khoa đang làm dở cần được Producer giao lại; hệ thống phải cho Lan thấy danh sách việc dở của Khoa để đổi người phụ trách.
- Cuối tháng Quang xem báo cáo usage để đối chiếu với quota.

**Trường hợp lỗi:**

- Chưa bật AI cho Moonlight mà Minh chạy workflow AI → `503 AI_UNAVAILABLE`; mọi chức năng sản xuất thủ công vẫn dùng bình thường.
- Quang không tạo được Series và không mở được nội dung Workspace nếu không có dòng `WorkspaceMember` ở đó (BR-07).

---

### 3.2 Producer (`WorkspaceRole.Producer`): Lan

**Việc của Producer:** dựng khung sản xuất, giao việc, theo dõi, khóa chương, phát hành. Là role duy nhất tạo được Series và mời thành viên.

**Luồng:**

1. Tạo Series trong Workspace (có thể nhiều Series); cấu hình pipeline.
2. Mời thành viên và gán role (role áp dụng cho cả Workspace, BR-07).
3. Lập Series Bible (nhân vật, địa điểm, thuật ngữ, plot fact; mỗi thay đổi gắn chương có hiệu lực, BR-15).
4. Giao việc theo chương hoặc panel, đặt hạn chót, dependency và người duyệt cổng; đổi người phụ trách khi cần. Mỗi lần giao hoặc giao lại phải có thông báo trước khi việc được coi là hoạt động (BR-14).
5. Theo dõi tiến độ, quá hạn, điểm nghẽn.
6. Duyệt và khóa chương; mở lại kèm lý do; khôi phục phiên bản; tạo link xem thử; phát hành; xuất file.

**Ví dụ cụ thể:**

- **Tạo Series.** Lan tạo "Thành phố mưa" trong Moonlight, chọn template "Standard Manga". Hệ thống tạo pipeline 8 stage và Series Bible rỗng.
- **Chỉnh pipeline.** Bộ truyện đen trắng nên Lan xóa stage "Color"; cô thêm stage "Proof" trước Review. Pipeline luôn còn ít nhất 2 stage. Xóa stage đang có việc dở dang bị từ chối (`409`).
- **Mời thành viên.** Lan mời `letterer@...` với vai trò Letterer. Vai trò gắn với Workspace; việc cụ thể mới giới hạn theo từng chương hoặc panel qua assignment.
- **Series Bible.** Lan ghi nhân vật An, địa điểm "Ga Mưa", và plot fact "An chỉ nhận chìa khóa bạc từ Chương 5" có hiệu lực từ Chương 1. Sau này nếu cốt truyện đổi, cô tạo phiên bản mới có hiệu lực từ chương khác, không ghi đè (BR-15).
- **Giao việc.** Lan giao Chapter 3 stage Script cho Minh, hạn 10/10; stage Pencil cho Hà, hạn 18/10, ưu tiên Cao. Hà nhận thông báo ngay khi được giao.
- **Dependency và người duyệt cổng.** Lan đặt "Ink của Page 2 chỉ bắt đầu khi Pencil của Page 2 được duyệt" (BR-19). Page 3 vẫn có thể vẽ song song. Cổng Review mặc định do Editor duyệt; Lan có thể đặt người duyệt khác cho một stage (BR-20).
- **Báo cáo tiến độ AI.** Lan chạy workflow `Producer`: hệ thống tính số liệu (% hoàn thành, việc quá hạn, điểm nghẽn theo stage), AI diễn giải thành rủi ro và đề xuất, mỗi nhận định trích khóa fact có thật; mọi con số `%` do code tính. Báo cáo không có Accept/Reject.
  - Ví dụ: "Chương 3: 2/6 việc quá hạn, nghẽn ở stage Pencil (Artist)".
- **Duyệt và khóa chương.** Khi mọi Page/Panel đạt `Approved`, Lan duyệt và khóa Chương 3 (UC-20, BR-12). Từ lúc này Pages và Panels chỉ đọc.
- **Link xem thử.** Chỉ **sau khi khóa**, Lan tạo link xem thử cho Bình, gắn đúng email của Bình, có hạn và có thể thu hồi (UC-11, BR-17).
- **Triage phản hồi.** Khi Bình góp ý, Lan hoặc Thu phân loại phản hồi (UC-24). Nếu cần sửa, Lan **mở lại chương kèm lý do** (UC-23), sửa, rồi khóa lại.
- **Phát hành.** Lan chuyển chương sang `Published` và xuất PDF/CBZ/Webtoon/JSON (UC-12).

**Ghi chú về quyền:** Producer luôn được override khi chuyển stage. Producer **không** mặc nhiên có quyền chạy workflow AI của Artist hay Letterer.

---

### 3.3 Writer (`WorkspaceRole.Writer`): Minh

**Việc của Writer:** quyết định "trang này kể chuyện gì": cốt truyện, scene, nội dung từng trang, thoại. Writer **đề xuất** cách chia scene/panel và nhịp kể; **bố cục thị giác cuối cùng thuộc về Artist** (xem 3.4).

**Luồng:**

1. Nhận việc stage Script, mở brief và Series Bible.
2. Tạo và sửa Chapter, Scene, script line (thoại, người nói, chỉ dẫn).
3. Liên kết nhân vật, địa điểm, đạo cụ với Series Bible.
4. Chạy AI nếu muốn; xem đề xuất; Accept, Edit hoặc Reject.
5. So sánh và khôi phục phiên bản script của mình.
6. Xử lý yêu cầu sửa; bàn giao qua cổng Script.

**Ví dụ cụ thể (luồng AI "Scene đầy đủ"):**

1. Minh vào `AI Multi-Agent`, chọn nhiệm vụ **FullScene**, nhập brief: "An gặp người lạ dưới mưa, người lạ làm rơi một gói vải nhỏ."
2. **WriterAgent** viết bản nháp. Bản đầu: "An nhặt gói vải, mở ra thấy chiếc chìa khóa bạc và nhớ lại ngày hôm qua…". Bản này sai quy tắc Bible.
3. **ContinuityAgent** đối chiếu với Bible, bắt lỗi kèm bằng chứng: "Theo Bible, An chưa từng sở hữu hay nhìn thấy chiếc chìa khóa này trước Chương 5."
4. Supervisor cho Writer sửa (tối đa 2 lượt). Bản sửa: "An nhặt gói vải, chưa mở ra, và chạy theo trả lại cho người lạ." Không còn chi tiết chìa khóa.
5. **StoryboardAgent** chia thành 1 Page, 5 Panel, kèm góc máy và thứ tự đọc. Đây là **gợi ý về nhịp và cấu trúc** để Artist tham khảo.
6. **EditorAgent** lập báo cáo review.
7. Minh xem hai đề xuất: **Script** và **Layout**. Anh Accept Script (tạo revision Script mới) và Edit nhẹ một câu thoại. Với Layout, Minh xác nhận không mất ý kể chuyện; việc chốt bố cục để Artist làm. Accept chỉ tạo revision, không chuyển stage, không khóa chương.
8. Editor duyệt cổng Script; các Page/Panel sang stage Thumbnail.

**Các biến thể của AI:**

- Nhiệm vụ **Script**: dừng ngay khi Continuity sạch, không chạy Storyboard hay Editor.
- Nhiệm vụ **Storyboard** từ script có sẵn: chỉ chạy khi Scene đã có script và **chưa có Page**.
- Còn lỗi chặn sau 2 lượt sửa → run dừng ở `WaitingForHuman`, chờ Minh tự xử lý.

**Sửa thoại sau khi Letterer đã làm:** khi Minh sửa một câu thoại đã được gắn balloon, Khoa (Letterer) phải nhận thông báo để cập nhật chữ trên canvas, và dòng thoại bị đánh dấu lỗi thời cho tới khi được cập nhật.

**Trường hợp lỗi:**

- Minh mở script lúc Hà vừa sửa → xung đột phiên bản, backend trả `409` thay vì ghi đè (BR-11).
- Chapter đã bị khóa → nhiệm vụ tạo đề xuất bị chặn `409` (BR-12).
- Minh thử chạy nhiệm vụ "Prompt ảnh" (của Artist) → `403`, nút Chạy bị khóa kèm lý do.
- Gemini hết quota (20 request/ngày/model ở free tier) → step lỗi `PROVIDER_UNAVAILABLE`, run `Failed`; Minh viết tay tiếp.

---

### 3.4 Artist (`WorkspaceRole.Artist`): Hà

**Việc của Artist:** quyết định cách câu chuyện được thể hiện bằng hình: **bố cục trang, số panel, kích thước và vị trí panel, góc máy, tư thế, khoảng trống cho chữ, thứ tự đọc**. Hà là người chốt layout; Writer xác nhận không mất ý, Editor kiểm tra nhịp đọc, Producer duyệt theo cổng đã cấu hình.

**Luồng:**

1. Xem hàng đợi việc của mình: brief, tham chiếu, hạn chót.
2. Dựng layout trong editor thumbnail và lưới panel; đánh dấu vùng quan trọng của tranh (Key Artwork Region) để Letterer tránh che.
3. Vẽ Thumbnail, Pencil, Ink, Color **ở phần mềm ngoài**, rồi tải từng phiên bản lên và gắn vào đúng Page/Panel.
4. So sánh các bản vẽ; nộp panel cho cổng Artwork.
5. Xử lý change request: sửa, tải bản mới, đánh dấu đã giải quyết, nộp lại.

**Ví dụ cụ thể:**

- **Nhận việc.** Hà thấy: "Chapter 3, Pencil, hạn 18/10, ưu tiên Cao, brief: panel 4 là cận cảnh gói vải rơi xuống vũng nước".
- **Tra Bible.** Hà mở hồ sơ An để xem model sheet và bảng màu.
- **Chốt layout.** Storyboard AI gợi ý 5 panel; Hà chọn giữ 5 panel nhưng phóng to panel 4 và chừa khoảng trống phía trên cho balloon. Cô đánh dấu khuôn mặt An ở panel 2 là vùng quan trọng. Layout cuối do Hà chốt.
- **Prompt ảnh bằng AI.** Hà chạy nhiệm vụ **Artist** (chỉ Artist chạy được). ArtistAgent soạn prompt cho từng panel của layout đã duyệt, kèm reference lấy từ Bible. Ví dụ panel 4: "Cận cảnh gói vải xám rơi xuống vũng nước, mưa rơi, tông xanh lạnh", reference: entry Bible "Ga Mưa". Mọi nhân vật trong panel phải được tham chiếu. Hà Accept để tạo revision `ImagePrompt`. Agent chỉ soạn prompt, không sinh ảnh. *(Theo phiếu V1.2, pipeline ảnh nhân vật gồm character sheet khoá, sinh ảnh từ sketch, tách nền và so tương đồng thuộc lõi nhưng chưa triển khai; đoạn này mô tả hành vi hiện tại.)*
- **Tải artwork lên.** Hà vẽ Pencil cả trang trong Clip Studio, tải lên, chọn stage "Pencil". Hệ thống tính SHA-256; nếu cùng file đã có trong Workspace thì dùng lại, không lưu trùng (BR-04, BR-13). Mỗi lần tải lại là một **phiên bản artwork mới**, bản cũ vẫn truy cập được.
- **Tách nền.** Hà tải ảnh người lạ, bấm "Tách nền" để ghép sang panel khác. Ảnh gốc giữ nguyên, kết quả là asset mới liên kết về ảnh gốc; Hà xem rồi mới chấp nhận (`requirements.md` 5.9).
- **AI tạo ảnh nháp.** Hà yêu cầu ảnh nháp cho panel 1; kết quả là đề xuất, chỉ vào panel khi Hà Accept (`requirements.md` 5.8).
- **Nộp.** Hà đặt Panel 4 "Ready for Review" để qua cổng Artwork.

**Xử lý change request (UC-17):**

- Thu khoanh một vùng trên Panel 4: "Đường viền gói vải để lộ hình chiếc chìa khóa, sửa lại để không lộ." Hà thấy vùng bị khoanh gắn đúng phiên bản ảnh đó, vẽ lại, tải bản n+1, đánh dấu đã giải quyết, nộp lại. Thu kiểm tra việc sửa nằm đúng vùng đã khoanh.

**Trường hợp lỗi:**

- Hà chạy nhiệm vụ Artist khi chưa có layout được Accept → `409` "Scene chưa có layout đã Accept".
- Panel bị xóa sau khi AI chạy → Accept bị chặn vì liên kết sai.
- Chương đã khóa → không tải thêm hay sửa panel được cho tới khi Producer mở lại (BR-12).
- Producer không chạy được nhiệm vụ Artist thay Hà.

---

### 3.5 Letterer (`WorkspaceRole.Letterer`): Khoa

**Việc của Letterer:** quyết định "chữ xuất hiện ra sao": balloon, font, kích thước, ngắt dòng, vị trí, hiệu ứng chữ. **Nội dung lời thoại do Writer quyết định**; Khoa quyết định cách trình bày.

**Luồng:**

1. Nhận trang đã qua cổng Artwork (stage Letter).
2. Nạp danh sách thoại từ script đã duyệt; kéo từng dòng vào panel.
3. Tạo balloon, khung dẫn, hiệu ứng chữ; áp typography preset của Series.
4. Xử lý cảnh báo; nộp qua cổng Lettering.

**Ví dụ cụ thể:**

- **Gắn thoại.** Khoa kéo câu thoại của An "Này anh ơi, anh vừa làm rơi thứ gì đó kia kìa, đợi tôi với!" vào Panel 3, tạo `DialogueBalloon` trỏ đuôi về An. Element lưu `ScriptLineId` và `SpeakerId`. **Mỗi dòng thoại chỉ gắn tối đa một balloon hoặc text element** (BR-05); khi cần tách một câu dài thành hai bóng thì tách dòng thoại trước.
- **Rút gọn thoại bằng AI.** Câu thoại dài quá bubble nhỏ. Khoa chạy nhiệm vụ **Letterer** (chỉ Letterer chạy được). LettererAgent đề xuất bản ngắn hơn, giữ ý và giọng nhân vật: "Anh ơi, anh làm rơi đồ!". Chip hiện "ước lượng ≤ 28 ký tự", do **code tính**, không phải do LLM nhận định.
  - Vì đề xuất này **đổi nội dung thoại**, người duyệt là **Writer hoặc Editor** (hoặc chính Khoa nếu chính sách Series cho phép Letterer áp dụng trực tiếp). Sau khi Accept, hệ thống tạo revision Script mới; thoại gốc vẫn nằm ở revision cũ.
  - Khoa chỉ tự quyết việc **trình bày**: font, cỡ chữ, kích thước bubble, vị trí.
- **Cảnh báo.** Hệ thống cảnh báo chữ tràn khung, đè lên vùng quan trọng mà Hà đã đánh dấu, sai thứ tự đọc (RTL). Cảnh báo tràn chữ có thể được xác nhận kèm lý do thay vì chặn nộp bài (BR-20).
- **Nộp.** Khoa đánh dấu hoàn tất lettering để qua cổng Lettering.

**Trường hợp lỗi:**

- Dòng thoại đã bị Minh sửa sau khi AI đọc → Accept bị chặn (liên kết sai với `originalText`).
- Một dòng thoại gắn vào hai bubble → bị chặn (BR-05).
- Artist thử Accept đề xuất Lettering → không có nút hoặc `403`.
- Thoại bị Minh sửa khi Khoa đang làm → Khoa nhận thông báo và dòng đó được đánh dấu lỗi thời.

---

### 3.6 Editor (`WorkspaceRole.Editor`): Thu

**Việc của Editor:** kiểm tra chất lượng, yêu cầu sửa, và **duyệt cổng stage** (mặc định). Editor không khóa hay phát hành chương; đó là việc của Producer.

**Luồng:**

1. Nhận thông báo cần review.
2. So sánh phiên bản; khoanh vùng, comment, vẽ chú thích gắn vào **đúng panel và đúng phiên bản**.
3. Chạy kiểm tra nhất quán với Series Bible; phân loại từng phát hiện (BR-21).
4. Tạo change request (có người phụ trách và trạng thái), hoặc duyệt cổng.
5. Review lại bản sửa.

**Ví dụ cụ thể:**

- **Kiểm tra nhất quán bằng AI.** Thu chạy nhiệm vụ **Editor** (Continuity → Editor). ContinuityAgent đối chiếu với Bible; EditorAgent lập báo cáo kèm checklist do code gom. Báo cáo: "Panel 2: thoại ngụ ý An đã từng có chiếc chìa khóa, mâu thuẫn plot fact 'chỉ nhận từ Chương 5'", kèm bằng chứng. Đây là **báo cáo tham khảo**, không phải đề xuất áp dụng.
- **Phân loại phát hiện.** Mỗi phát hiện kết thúc là một trong: Confirmed, Rejected, Ignored, Needs clarification. Ignored bắt buộc ghi lý do (BR-21). Thu xác nhận lỗi ở panel 2 (Confirmed) và bỏ qua một cảnh báo nhầm kèm lý do.
- **Sửa nguồn lỗi đúng chỗ.** Nếu Minh cho rằng cảnh cần giữ chi tiết chìa khóa, thì không thể coi bản đó là hết lỗi: phải sửa cảnh, hoặc Producer sửa Bible bằng một phiên bản mới có hiệu lực từ chương liên quan và được duyệt (BR-15). Không được bỏ qua lỗi continuity chỉ để cảnh trôi chảy.
- **So sánh và khoanh vùng.** Thu mở side-by-side bản Ink v1 và v2 của Panel 4, khoanh một vùng, ghi "Nét mực đứt ở viền gói vải".
- **Change request.** Thu tạo change request giao cho Hà kèm vùng khoanh. Khi trả một item lùi về stage trước, **bắt buộc có comment** (BR-06).
- **Duyệt cổng.** Khi bản Pencil của Page 2 đạt yêu cầu, Thu duyệt cổng Artwork: Page 2 sang stage Ink (và chỉ khi đó Ink mới bắt đầu, BR-19). Duyệt Pencil **không** có nghĩa Panel đã hoàn tất; chỉ khi qua cổng Review, item mới đạt stage `Approved`. Việc duyệt và khóa cả chương là của Lan.

**Quyền duyệt đề xuất AI theo loại:** Script (Producer, Writer, Editor); Layout (Producer, Writer, Artist, Editor); ImagePrompt (Producer, Artist, Editor); Lettering (Producer, Writer, Letterer, Editor). Backend tính `canDecide` cho người đang xem; Frontend chỉ hiện nút khi `canDecide`.

**Trường hợp lỗi:**

- Editor không có Accept/Reject với báo cáo Editor hay Producer, vì đó không phải đề xuất.
- Trả item về mà không nhập comment → bị từ chối.
- Chương đã khóa → Editor chỉ xem, không sửa; phản hồi sau khóa chuyển thành yêu cầu Producer mở lại chương.

---

### 3.7 Người xem thử có link (External Reviewer): Bình

**Việc của người xem thử:** xem bản thảo của **chương đã khóa** và phản hồi, **không** có tài khoản và không vào workspace sản xuất. Không phải `WorkspaceRole`, và khác với độc giả công khai ở 3.8 (BR-24).

**Luồng:**

1. Producer khóa chương, rồi tạo link xem thử gắn email của người nhận (BR-17).
2. Người nhận mở link; mọi lần truy cập, kể cả bị từ chối, đều được ghi nhật ký.
3. Đọc bản xem thử (manga lật ngang RTL hoặc webtoon cuộn dọc).
4. Gửi phản hồi theo chương hoặc theo trang.

**Ví dụ cụ thể:**

- Lan khóa Chương 3 rồi tạo link cho `binh@doitac.com`, hạn 7 ngày. Đây là đúng phiên bản sẽ được xuất bản.
- Bình mở link; ảnh có watermark hiển thị đúng email của Bình và thời gian. Nếu ảnh bị rò rỉ, truy ra được người nhận.
- Bình gửi phản hồi trang 3: "Cảnh người lạ biến mất hơi nhanh". Phản hồi được Lan hoặc Thu phân loại (UC-24).
- Lan nhận ra link đã gửi nhầm một người khác nên thu hồi trước hạn; từ đó link báo không còn hiệu lực.
- Hết hạn, link vô hiệu vĩnh viễn dù ai giữ URL (BR-09).

**Trường hợp lỗi:**

- Chương chưa khóa → không tạo được link xem thử (BR-17).
- Link của Bình bị người khác mở bằng email khác → bị từ chối và vẫn ghi nhật ký.

**Giới hạn:** Bình không thấy canvas, pipeline, Bible, hay nội dung ngoài phạm vi link. Ảnh lưu ở bucket private, truy cập qua URL ký ngắn hạn.

---

### 3.8 Độc giả công khai (Public Reader): Chi

> **Tính năng thêm, ngoài phạm vi đăng ký V1.2:** phiếu ghi xuất bản làm ngoài hệ thống và không có Public Reader; nhóm quyết định giữ mục này, không thuộc nghiệm thu.

**Việc của độc giả công khai:** đọc truyện **đã phát hành** qua catalog công khai và app, bằng tài khoản Reader riêng. Đây là luồng truy cập thứ ba, tách biệt khỏi truy cập nội bộ Workspace và khỏi link xem thử (BR-24).

**Luồng:**

1. Duyệt catalog các Series có chương `Published`, không cần đăng nhập.
2. Đăng ký tài khoản Reader (khác tài khoản Workspace).
3. Đọc các chương đã phát hành theo chiều đọc của Series.
4. Theo dõi Series; nếu chủ Series bật truy cập trả phí thì đăng ký để đọc mọi chương đã phát hành (US-30).

**Ví dụ cụ thể:**

- Lan phát hành Chương 3. Chi mở app, thấy "Thành phố mưa" trong catalog, đọc Chương 3 theo chiều phải sang trái.
- Chi đăng ký tài khoản Reader và theo dõi Series để biết khi có chương mới.
- Chương 4 đang sản xuất nên Chi không thấy được; chỉ chương `Published` xuất hiện.

**Giới hạn:** tài khoản Reader không dùng được cho đăng nhập vào Workspace, và ngược lại. Không thể dùng quyền của luồng này cho luồng khác.

---

### 3.9 Người dùng đã đăng nhập (`SystemRole.User`): mọi thành viên

**Luồng chung cho ai cũng có:**

- **Hồ sơ và bảo mật:** đổi mật khẩu, 2FA, liên kết Firebase/Google.
- **Hàng đợi việc cá nhân:** việc được giao, hạn chót, trạng thái.
- **Thông báo:** được giao việc hoặc giao lại (BR-14), bị `@mention`, có bài cần review, yêu cầu sửa, bị đưa trả lại, sắp đến hạn.
- **Tìm kiếm:** kịch bản, nhân vật Bible, nhãn panel, comment.
- **Lịch sử phiên bản và activity feed** trong phạm vi quyền của mình.

**Ví dụ:** Hà nhận thông báo "Bạn được giao Chapter 3 – Pencil, hạn 18/10", hai ngày trước hạn nhận nhắc nhở, và khi Thu tạo change request thì nhận thông báo kèm vùng khoanh.

---

## 4. Các luồng xuyên role

### 4.1 Luồng "làm lại" (rework)

```
Hà nộp Panel 4 (Ink) qua cổng Artwork
   ↓
Thu review → khoanh vùng "nét mực đứt" → tạo change request (có người phụ trách, trạng thái)
   ↓
Assignment của Hà: ReworkRequired + comment phản hồi
   ↓
Hà sửa, tải bản n+1, đánh dấu đã giải quyết, nộp lại → Submitted
   ↓
Thu review lại đúng vùng đã khoanh → duyệt cổng → item sang stage kế tiếp
```

Trạng thái Assignment: `Assigned → InProgress → Submitted → UnderReview → Approved`, nhánh phụ `ReworkRequired` và `Blocked`. `Approved` của một Assignment là việc đó đã được duyệt; **không đồng nghĩa** cả Panel hay cả chương đã duyệt.

### 4.2 Luồng đề xuất AI → quyết định của người

```
Người dùng gửi yêu cầu (workflowType + brief)
  → Backend kiểm quyền (thành viên, role, assignment, stage, chương khóa) và chụp snapshot context
  → Code (WorkflowRules) tính hành động hợp lệ
  → Supervisor chọn agent tiếp theo
  → Agent chạy; code kiểm output (AI service, rồi Backend kiểm lại)
  → Lưu step, output, prompt version+hash, usage, provenance
  → Người có quyền Accept / Edit / Reject
```

- AI chỉ **đề xuất**. Supervisor không viết nội dung, không duyệt, không chuyển stage.
- AI hỏng, chậm hoặc hết quota → quy trình thủ công không bị chặn.
- Mọi quyết định ghi người, thời điểm, lý do và `resultRevisionId`; lịch sử chỉ thêm, không ghi đè.

### 4.3 Luồng chương: duyệt, khóa, xem thử, mở lại, phát hành

```
Mọi Page/Panel đạt Approved (qua các cổng stage)
   ↓
Producer duyệt và khóa chương (Locked, chỉ đọc, BR-12)
   ↓
Producer tạo link xem thử gắn email người nhận (BR-17)
   ↓
Người xem thử đọc và phản hồi
   ↓
Producer/Editor triage phản hồi (UC-24)
   ├─ Không cần sửa → phát hành (Published) và xuất file
   └─ Cần sửa → Producer mở lại chương kèm lý do (UC-23) → InProduction
                → sửa theo luồng rework → duyệt → khóa lại → (link cũ cần tạo lại cho bản mới)
```

### 4.4 Xung đột phiên bản và khóa chương

- Hai người cùng sửa một thực thể: người lưu sau nhận `409`, không ghi đè bản mới hơn (BR-11).
- Chương đã khóa: không chạy được nhiệm vụ AI tạo đề xuất áp dụng được; Editor/Producer vẫn chạy được báo cáo chỉ đọc.

### 4.5 Một lần Accept không làm hơn việc của nó

Accept Script/Layout/ImagePrompt/Lettering chỉ **tạo content revision mới**. Nó không duyệt cổng stage, không chuyển stage, không khóa chương. Duyệt cổng và khóa chương luôn là hành động riêng của người có quyền.
