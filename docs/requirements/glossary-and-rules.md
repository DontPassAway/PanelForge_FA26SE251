# Từ khóa và quy tắc của PanelForge

> **Tài liệu:** Giải thích mọi từ khóa dùng trong dự án và toàn bộ quy tắc vận hành.
> **Cập nhật:** 06/10/2026 (đối chiếu lại với phiếu đăng ký V1.2)
> **Nguồn:** `PanelForge_Capstone_Content.md` (BR-01 đến BR-24, US, UC, FR, NFR, E-xx), `requirements.md`, `project-plan.md`, `ai-multi-agent-demo.md`, `storage-architecture.md`, `storage-strategy.md`, `CLAUDE.md` của Backend, và code domain.
> **Cấu trúc:** Phần 1 là từ khóa (ý nghĩa và tác dụng). Phần 2 là quy tắc của dự án.
> Các ví dụ (tên người, số liệu) chỉ để minh họa.

---

# PHẦN 1. TẤT CẢ TỪ KHÓA TRONG DỰ ÁN

## 1.1 Cấu trúc nội dung

| Từ khóa | Ý nghĩa | Tác dụng |
|---|---|---|
| **Studio Workspace** | Một "xưởng" làm truyện, gồm thành viên, role, cấu hình AI và nhiều Series | Là ranh giới tenant: dữ liệu của Studio này không lẫn sang Studio khác |
| **Series** | Một bộ truyện | Có pipeline, Series Bible, chương, preset typography và luật nhất quán riêng |
| **Chapter** | Một chương của Series | Đơn vị duyệt, khóa, xem thử và phát hành |
| **Scene** | Khối tự sự liền mạch trong chương, không chồng lấn và tuỳ chọn khi làm tay | Gom các Script block. **Không chứa Page** (theo phiếu V1.2): một Page có thể có Panel của nhiều Scene, và một Scene có thể trải qua nhiều Page. Các luồng agent chạy trên một Scene |
| **Script block** | Một dòng hành động, thoại, caption hoặc SFX của script, có thứ tự, thuộc một Scene | Có thể tồn tại trước khi có Page hay Panel; Panel tham chiếu các block nó mang, nên Scene của Panel được suy ra từ block. Phân trang lại chỉ đổi panel được gán |
| **Chapter outline** | Dàn ý của một chapter, lập từ synopsis và story arc của Series | Writer dùng làm đầu vào cho Writer Agent |
| **Synopsis / Story arc** | Tóm tắt và các mạch truyện lớn của Series | Producer hoặc Writer ghi; Writer lập chapter outline từ đó |
| **Frame template** | Mẫu bố cục trang dùng lại, do Admin quản lý | Chỉ là cấu hình khởi đầu: khởi tạo các Panel của Page mới, sau đó Panel là entity độc lập và đổi template không làm đổi Page đã có |
| **Double-page spread / Splash** | Trang đôi / trang một khung | Trang đôi là liên kết giữa hai Page; splash là Page chỉ có một Panel; panel không viền là thuộc tính hiển thị |
| **Character sheet** | Bản tham chiếu của nhân vật: sketch hành động, cảm xúc, chuyển động, tính cách, cách nói, phong cách vẽ, chế độ màu | Được duyệt và khoá, versioned theo chương; chỉ thay thế bằng bản mới, không sửa. Lõi là trắng đen, màu là mở rộng |
| **Page** | Một trang truyện | Có kích thước, DPI, chiều đọc; chứa các Panel |
| **Panel** | Một khung tranh trong trang | Đơn vị nhỏ nhất theo dõi tiến độ (panel-level progress) |
| **Element** | Một phần tử trên khung: balloon, khung dẫn, SFX, layer ảnh, nhân vật, đạo cụ | Là thứ Artist và Letterer kéo thả trên canvas |
| **ScriptLine** | Một dòng kịch bản: thoại, chỉ dẫn, người nói | Cho phép biết "câu thoại nào, ai nói, thuộc cảnh nào" |
| **Series Bible** | Kho sự thật cố định của bộ truyện (xem `series-bible.md`) | Tham chiếu cho người, nền kiểm tra cho AI |
| **Canvas** | Vùng làm việc kéo thả của một trang | Nơi dựng layout và đặt chữ |
| **Reading order / Reading direction** | Thứ tự đọc panel và chữ, là thuộc tính riêng không suy từ quan hệ chứa; hướng đọc là cờ của Series (manga phải sang trái RTL, trái sang phải LTR). Webtoon chỉ có ở dạng xuất ảnh cuộn dọc, không có canvas cuộn vô hạn | Quyết định cách sắp xếp và kiểm tra chữ |
| **Key Artwork Region** | Vùng quan trọng của tranh do Artist đánh dấu (ví dụ khuôn mặt) | Để Letterer không đặt balloon đè lên |
| **DPI** | Độ phân giải của trang | Cần cho in ấn và xuất bản |

## 1.2 Người dùng, role và quyền

| Từ khóa | Ý nghĩa | Tác dụng |
|---|---|---|
| **SystemRole** | Role cấp hệ thống, chỉ gồm `Admin` và `User` | Phân biệt người quản trị nền tảng với tài khoản thường. **Không tự cấp quyền nội dung** |
| **Admin** | Quản trị viên hệ thống | Quản lý tài khoản, quyền tạo Studio, cấu hình AI, đặt giới hạn cho từng Workspace (storage, quota token, quota ảnh, session policy), retention, backup, master data, audit và usage. Không mời thành viên, không đọc nội dung Workspace, không tham gia sản xuất |
| **User** | Tài khoản đã đăng nhập | Dùng được các chức năng chung; vào Workspace khi có membership |
| **WorkspaceRole** | Role trong một Studio: `Producer`, `Writer`, `Artist`, `Letterer`, `Editor` | Quyết định hành động được làm trong Studio đó |
| **WorkspaceMember** | Dòng nối giữa User và Workspace, mang theo WorkspaceRole | Không có dòng này thì không có quyền nào (không có truy cập ngầm) |
| **Producer** | Nhà sản xuất / chủ Series | Tạo Series (ghi synopsis, story arc), mời thành viên, giao việc, duyệt và khoá character sheet, theo dõi usage so với giới hạn, khóa chương, tạo preview link và gói xuất. Việc xuất bản làm ngoài hệ thống (xem BR-24); Producer không có quan hệ với nhà xuất bản |
| **Writer** | Biên kịch | Viết script, đề xuất cấu trúc và nhịp kể |
| **Artist** | Họa sĩ | Chốt layout, vẽ, tải artwork |
| **Letterer** | Người đặt chữ | Balloon, font, vị trí chữ |
| **Editor** | Biên tập viên | Review, yêu cầu sửa, duyệt cổng stage |
| **External Reviewer** | Người xem thử qua link; nhà xuất bản (publisher) là một External Reviewer | Xem chương đã khóa và phản hồi ở cấp chương, trang hoặc panel, gắn đúng phiên bản đang xem; không có tài khoản, không có quyền sản xuất |
| **Public Reader** | Độc giả công khai (tài khoản Reader trên app) | Đọc chương đã phát hành. **Tính năng thêm ngoài phạm vi đăng ký V1.2**, không thuộc nghiệm thu (xem BR-24) |
| **CanCreateStudio** | Quyền do Admin cấp cho một User | Cho phép User tạo Studio; người tạo tự thành Producer |
| **Session policy** | Luật phiên đăng nhập của Workspace do Admin đặt: thời gian sống, idle timeout, số phiên đồng thời | Được kiểm khi đăng nhập hoặc làm mới token; idle timeout còn được kiểm ở mỗi request (đo từ lần hoạt động cuối của phiên, sai số ≤ 30 giây); user thuộc nhiều Workspace chịu giá trị chặt nhất |
| **RBAC / ABAC** | Phân quyền theo role / theo thuộc tính (assignment, trạng thái tài nguyên) | Kiểm quyền chi tiết ở server |

## 1.3 Sản xuất và quy trình

| Từ khóa | Ý nghĩa | Tác dụng |
|---|---|---|
| **Pipeline** | Dây chuyền các stage của một Series | Chuẩn: Script → Thumbnail → Pencil → Ink → Color → Letter → Review → Approved |
| **PipelineTemplate** | Mẫu pipeline do Admin quản lý | Khi tạo Series, hệ thống sao chép mẫu để Producer tùy chỉnh |
| **Stage** | Một công đoạn của pipeline (có role phụ trách, thời lượng ước tính, cờ cổng duyệt) | Cho biết item đang ở đâu |
| **Thumbnail** | Bản phác bố cục rất thô | Chốt chia khung và vị trí nhân vật; sửa ở đây rẻ nhất |
| **Pencil** | Vẽ chì chi tiết | Hình dáng, biểu cảm, bối cảnh |
| **Ink** | Đi nét mực đè lên nét chì | Ra bản đen trắng sạch |
| **Color** | Tô màu | Lõi demo là manga trắng đen nên bỏ stage này; chỉ bật khi dùng chế độ màu (mở rộng, không thuộc nghiệm thu) |
| **Letter** | Đặt balloon và chữ | Hoàn thiện lời thoại trên tranh |
| **Review** | Stage duyệt (cổng duyệt) | Editor kiểm tra chất lượng và nhất quán |
| **Approved** | Stage cuối của pipeline | Item hoàn tất sản xuất (khác với chương được duyệt và khóa) |
| **Transition** | Bước chuyển giữa hai stage, có điều kiện | Ràng buộc ai được chuyển, có bắt buộc comment không |
| **Guarded transition** | Transition phải qua cổng duyệt | Đóng băng stage trước (BR-06) |
| **Approval gate / Stage gate** | Điều kiện hoàn thành của một stage (Script, Artwork, Lettering, Consistency) | Mặc định Editor duyệt, Studio có thể đổi người duyệt (BR-20) |
| **Backward transition / Reject** | Chuyển lùi về stage trước | Bắt buộc có comment giải thích |
| **Assignment** | Một việc được giao: người nhận, role, brief, hạn, ưu tiên, trạng thái | Là đơn vị giao việc và kiểm quyền theo việc |
| **Trạng thái Assignment** | `Assigned`, `InProgress`, `Submitted`, `UnderReview`, `Approved`, `ReworkRequired`, `Blocked` | Theo dõi vòng đời một việc |
| **Deadline / Priority** | Hạn chót / độ ưu tiên | Phát hiện việc quá hạn, sắp xếp việc |
| **Dependency** | Việc sau chỉ bắt đầu khi việc trước được duyệt | Giữ đúng thứ tự trong cùng Page/Panel |
| **Panel-level progress** | Tiến độ theo từng Panel | Producer thấy tiến độ thật, không chỉ cả trang |
| **Change request** | Yêu cầu sửa từ Editor, gắn với vùng và phiên bản cụ thể | Người được giao sửa và đánh dấu đã giải quyết |
| **Rework** | Làm lại sau change request | Assignment sang `ReworkRequired` |
| **Trạng thái Chapter** | `InProduction`, `Approved`, `Locked`, `Published` | Điều khiển cho phép sửa, xem thử, phát hành |
| **Lock / Reopen** | Khóa chương (chỉ đọc) / mở lại kèm lý do | Bảo vệ chương đã duyệt khỏi sửa nhầm |
| **Preview link** | Link xem thử có hạn cho người ngoài, gắn một email người nhận, có watermark hiện người nhận | Chỉ cấp cho chương đã khóa; thu hồi được; feedback gắn đúng phiên bản đang xem |
| **Export** | Xuất chương ra PDF, CBZ, webtoon, JSON | Giao bản hoàn chỉnh |

## 1.4 Phiên bản, truy vết và đồng thời

| Từ khóa | Ý nghĩa | Tác dụng |
|---|---|---|
| **Append-only** | Chỉ thêm, không sửa hay xóa dữ liệu đã lưu | Giữ nguyên lịch sử |
| **Revision** | Một phiên bản đã lưu của nội dung | Xem lại, so sánh, khôi phục |
| **ContentRevision** | Revision của script, layout hoặc image prompt trong một Scene (`Script`, `Layout`, `ImagePrompt`) | Lịch sử bất biến của nội dung có cấu trúc |
| **BibleEntryRevision** | Revision của một mục Bible, có hiệu lực từ một chương | Lịch sử sự thật của bộ truyện |
| **Event sourcing** | Lưu từng thao tác thành "sự kiện" thay vì chỉ lưu trạng thái cuối | Tua lại, xem ai làm gì |
| **Event store / Marten** | Kho sự kiện, chạy trên PostgreSQL | Lưu thao tác kéo thả trên canvas (Page aggregate) |
| **Domain event** | Một sự kiện nghiệp vụ (`ElementAdded`, `ElementMoved`, `PageCreated`…) | Đơn vị lịch sử của canvas |
| **Projection** | Bản đọc được dựng từ chuỗi sự kiện | Truy vấn nhanh trạng thái hiện tại |
| **Rollback / Restore** | Khôi phục bản cũ | Tạo phiên bản mới ở đầu lịch sử, không xóa bản ở giữa |
| **Optimistic concurrency** | Từ chối cập nhật dựa trên phiên bản cũ | Chống ghi đè mất dữ liệu khi nhiều người cùng sửa |
| **Conflict (409)** | Lỗi xung đột phiên bản | Người lưu sau được báo, không bị ghi đè âm thầm |
| **Provenance** | Nguồn gốc của một nội dung: ai, khi nào, có AI không, model nào | Truy vết, nghiệm thu, giải trình |
| **Audit log** | Nhật ký thao tác: người làm, thời điểm, thay đổi, IP | Điều tra và kiểm toán |
| **Soft delete** | Đánh dấu đã xóa thay vì xóa cứng | Giữ lịch sử |
| **ContentHash / SHA-256** | Mã băm nội dung | Kiểm tra toàn vẹn, chống trùng |

## 1.5 Lưu trữ ảnh

| Từ khóa | Ý nghĩa | Tác dụng |
|---|---|---|
| **Asset** | Một file ảnh hoặc file đã tải lên, kèm metadata | Đối tượng trung tâm của lưu trữ ảnh |
| **Content-addressable storage (CAS)** | Tên file chính là mã băm SHA-256 của nội dung | Bất biến, chống trùng, kiểm tra toàn vẹn |
| **Deduplication** | Cùng hash thì dùng lại, không lưu hai lần | Tiết kiệm dung lượng (trong phạm vi một Workspace) |
| **Object storage** | Kho lưu file nhị phân (kiểu S3) | Chứa ảnh gốc, ảnh hiển thị, thumbnail, file xuất |
| **Pre-signed URL** | URL ký có hạn ngắn để tải lên hoặc xem file | Không có link công khai cố định |
| **Variant** | Bản phái sinh của ảnh: ảnh hiển thị WebP, thumbnail | Canvas và danh sách chạy nhẹ |
| **Derived asset** | Asset sinh từ asset khác (tách nền, ảnh AI) | Giữ ảnh gốc, truy được nguồn |
| **Background removal / Cut-out** | Tách nền ảnh; trong pipeline ảnh nhân vật là bước tách nhân vật khỏi nền đơn giản | Ghép nhân vật vào panel khác. Với ảnh đã gộp phẳng chỉ tách nhân vật và có người kiểm tra; không tự phân rã cả trang thành Element |
| **Watermark** | Dấu chìm hiển thị người xem | Truy ra người làm rò rỉ |
| **Storage quota / Retention policy** | Hạn mức dung lượng (GB) theo Workspace / chính sách giữ file | Admin kiểm soát chi phí và dọn dẹp; vượt hạn mức thì upload bị từ chối kèm lỗi rõ ràng |

## 1.6 AI

| Từ khóa | Ý nghĩa | Tác dụng |
|---|---|---|
| **AI Suggestion** | Kết quả AI dưới dạng đề xuất | Người Accept, Edit hoặc Reject; chưa thành nội dung chính thức |
| **Human-in-the-loop** | Người luôn quyết định cuối | AI không tự duyệt |
| **Grounding** | Neo kết quả AI vào dữ liệu thật (Series Bible) | Giảm bịa, có bằng chứng |
| **Supervisor** | Thành phần điều phối chọn agent kế tiếp | Không viết nội dung, không duyệt, không chuyển stage; không phải role |
| **Agent** | Một tác nhân AI chuyên môn. Lõi: Writer, Continuity, Storyboard, Editor, Artist, và Letterer (rút gọn thoại). Mở rộng: Producer, và dịch thoại của Letterer | Mỗi agent một việc, có prompt và bộ kiểm riêng; không đổi stage, không duyệt cổng |
| **Workflow type** | Loại nhiệm vụ AI: `FullScene`, `Script`, `Storyboard`, `Artist`, `Letterer`, `Editor`, `Producer` | Quy định chuỗi agent hợp lệ và ai được chạy |
| **Guardrail (WorkflowRules)** | Luật bằng code giới hạn hành động của AI | Supervisor chọn sai thì bị ghi đè bằng hành động an toàn |
| **StopForHuman / WaitingForHuman** | AI dừng chờ người xử lý | Khi thiếu dữ liệu hoặc còn lỗi chặn |
| **Continuity check** | Kiểm tra nhất quán với Bible | Bắt lỗi cốt truyện, kèm bằng chứng |
| **Finding / Triage** | Một phát hiện của AI / việc phân loại nó | Mỗi finding kết thúc là Confirmed, Rejected, Ignored hoặc Needs clarification |
| **Script breakdown** | Storyboard Agent chia script đã duyệt thành Page và Panel, kèm thứ tự đọc, shot scale và liên kết tới dòng script nguồn | Tăng tốc dựng cấu trúc |
| **Layout suggestion** | AI gợi ý bố cục trang | Điểm khởi đầu cho Artist |
| **Balloon-fit / Overflow** | Phát hiện chữ tràn khung, đè tranh, sai thứ tự đọc; do **code** tính, không phải mô hình | Cảnh báo cho Letterer |
| **Dialogue shortening** | Letterer Agent rút gọn thoại cho vừa balloon trong giới hạn độ dài (lõi); dịch thoại sang ngôn ngữ khác là mở rộng | Thoại đổi nghĩa phải được Writer hoặc Editor duyệt |
| **Character image pipeline** | Pipeline 7 bước: chọn character sheet khoá, upload sketch, yêu cầu ảnh (tư thế, cảm xúc, cỡ cảnh, phong cách, chế độ màu), tách nền, chấm điểm tương đồng, Artist accept/edit/reject, lưu thành Element lớp artwork độc lập | Sinh ảnh nhân vật qua adapter thay được; chỉ sinh nhân vật, không sinh cả cảnh; nền tảng vẫn dùng được khi không có sinh ảnh |
| **Similarity score** | Điểm tương đồng (0 đến 1) giữa một hình nhân vật với character sheet tham chiếu | Dưới ngưỡng cấu hình (0.75, đề xuất chờ giảng viên) thì gắn cờ cho người xem |
| **Three-layer consistency** | Nhất quán nhân vật ở ba lớp: văn bản (Continuity Agent, có trích bằng chứng), luật bằng code, hình ảnh (so với character sheet) | Mục tiêu chất lượng trung tâm; độ lệch được nêu ra, không bị giấu |
| **Seeded-defect set** | Tập dữ liệu gài sẵn lỗi văn bản và hình ảnh để đo | Đo precision và recall (tối thiểu 70% mỗi loại, đề xuất chờ giảng viên) so với baseline một prompt |
| **Image prompt** | Câu mô tả ảnh cho từng panel | ArtistAgent soạn. Hiện code chưa sinh ảnh; việc sinh ảnh thuộc pipeline ảnh nhân vật (lõi theo phiếu V1.2) |
| **Provider (Gemini, Mock)** | Nhà cung cấp mô hình AI; Mock là bản giả lập không cần khóa | Chạy thật hoặc demo/test |
| **Quota (AI)** | Hạn mức sử dụng AI theo Workspace: token hằng tháng, và quota sinh ảnh (số ảnh hoặc ngân sách, vì token LLM không phản ánh chi phí mô hình ảnh) | Admin kiểm soát chi phí; kiểm trước khi gọi AI hoặc sinh ảnh |
| **Prompt injection** | Nội dung trong dữ liệu cố điều khiển AI | Dữ liệu đưa vào prompt luôn được coi là dữ liệu, không phải lệnh |
| **Snapshot (RunSnapshot)** | Bản chụp bất biến ngữ cảnh lúc chạy AI | Truy vết AI đã đọc gì |

## 1.7 Lettering

| Từ khóa | Ý nghĩa | Tác dụng |
|---|---|---|
| **Dialogue balloon** | Bóng thoại | Chứa lời nhân vật |
| **Narration box** | Khung dẫn truyện | Lời dẫn |
| **SFX (Sound effect)** | Chữ tượng thanh | Hiệu ứng âm thanh |
| **Typography preset** | Bộ kiểu chữ theo Series (font, cỡ, kiểu balloon, khoảng đệm) | Đồng nhất cách trình bày |
| **Single-binding** | Một dòng thoại gắn tối đa một balloon/text | Chống nhân đôi thoại |
| **Speaker** | Người nói của một dòng thoại | Gắn đuôi balloon đúng người |
| **Text overflow** | Chữ tràn khỏi balloon | Phát cảnh báo |

## 1.8 Kỹ thuật và quy ước tài liệu

| Từ khóa | Ý nghĩa | Tác dụng |
|---|---|---|
| **Clean Architecture** | Chia bốn lớp: API, Application, Domain, Infrastructure | Domain độc lập với hạ tầng |
| **CQRS / MediatR** | Tách lệnh (Command) và truy vấn (Query), điều phối bằng MediatR | Mỗi use case một Command/Query + Handler |
| **Handler** | Nơi xử lý một use case | Là ranh giới use case của ứng dụng |
| **Result&lt;T&gt;** | Kiểu kết quả thành công/thất bại | Trả lỗi nghiệp vụ rõ ràng |
| **EF Core / PostgreSQL / Marten** | ORM quan hệ / cơ sở dữ liệu / kho sự kiện | Lưu dữ liệu |
| **JWT** | Token xác thực | Đăng nhập |
| **DTO** | Đối tượng truyền dữ liệu qua API | Hợp đồng giữa Backend và Frontend |
| **Swagger / OpenAPI** | Tài liệu API | Dev và test |
| **HANDOFF.md** | Kênh trao đổi duy nhất giữa Backend và Frontend | Ghi API, DTO, thay đổi phá vỡ |
| **BR-xx** | Business Rule (quy tắc nghiệp vụ) | Xem Phần 2 |
| **US-xx / UC-xx** | User Story / Use Case | Yêu cầu theo người dùng / theo kịch bản |
| **FR / NFR** | Functional / Non-Functional Requirement | Yêu cầu chức năng / phi chức năng |
| **E-xx** | Thực thể dữ liệu (Entity) | Mục trong mô hình dữ liệu |
| **CF1–CF5 (Core Flow)** | Năm luồng lõi của hệ thống: thiết lập, tạo nội dung, review và nhất quán, phiên bản và khôi phục, duyệt và phát hành | Nhóm yêu cầu theo luồng |
| **MVP / Extended scope** | Phạm vi bắt buộc / phạm vi mở rộng | Chia ưu tiên (xem `project-plan.md`) |

---

# PHẦN 2. QUY TẮC CỦA DỰ ÁN

Các quy tắc nghiệp vụ BR-01 đến BR-24 lấy từ đặc tả. Phần giải thích và ví dụ do tài liệu này thêm.

> **Ghi chú theo phiếu đăng ký V1.2:** (1) BR-03 đã đổi mô hình Scene/Page/Panel, xem bên dưới. (2) Lõi demo là manga trắng đen; chế độ màu là mở rộng. (3) Rút gọn thoại cho vừa balloon (Letterer Agent) thuộc lõi, dịch thoại là mở rộng. (4) Publisher là external reviewer, Producer không có quan hệ với nhà xuất bản, xuất bản làm ngoài hệ thống; xem ghi chú ở BR-24. (5) Các giá trị nghiệm thu (timeout 30 giây, precision/recall tối thiểu 70%, ngưỡng tương đồng 0.75) là đề xuất chờ xác nhận.

## 2.1 Quy tắc nghiệp vụ (BR-01 đến BR-24)

### Nhóm A. Cấu trúc nội dung và dữ liệu

| Mã | Quy tắc | Giải thích | Ví dụ |
|---|---|---|---|
| **BR-03** | Chứa nhau theo đúng cây | Cây vật lý `Series → Chapter → Page → Panel → Element` song song với cấu trúc tự sự `Chapter → Scene → Script block`. Mỗi thứ thuộc đúng một cha, không có đối tượng mồ côi. Panel thuộc đúng một Page, tối đa một Scene và tham chiếu các Script block (phiếu V1.2; code hiện còn `Page.SceneId`, cần đổi) | Không tạo Panel khi chưa có Page chứa nó |
| **BR-05** | Một dòng thoại, một text | Mỗi dòng script gắn tối đa một Balloon/Text element | Không kéo cùng câu thoại vào hai bubble; muốn hai bóng thì tách dòng thoại |
| **BR-04** | Bất biến theo hash (CAS) | Mọi file có SHA-256; client tính, server tính lại và đối chiếu trước khi nhận | File tải lên không khớp hash bị từ chối |
| **BR-13** | Chống lưu trùng asset | Cùng hash thì dùng lại asset đã có, trong phạm vi quyền | Hai panel dùng chung ảnh nền thì trỏ về một asset |

### Nhóm B. Phiên bản, lịch sử và đồng thời

| Mã | Quy tắc | Giải thích | Ví dụ |
|---|---|---|---|
| **BR-01** | Bất biến | Dữ liệu chỉ thêm; bản đã lưu không bị xóa hay ghi đè | Tải Ink v2 vẫn giữ Ink v1 |
| **BR-08** | Khôi phục không phá hủy | Khôi phục tạo bản mới ở đầu lịch sử, không xóa bản giữa | Đang ở v5 khôi phục v2 thì tạo v6 có nội dung của v2 |
| **BR-11** | Chống ghi đè đồng thời | Cập nhật dựa trên phiên bản cũ bị từ chối; xung đột phải báo ra | Hai người sửa cùng script; người lưu sau nhận lỗi xung đột |
| **BR-15** | Bible theo phiên bản chương & Biến thể vòng đời | Mỗi thay đổi Bible gắn chương có hiệu lực (`effectiveFromChapterNumber`); nhân vật hỗ trợ các biến thể vòng đời theo thời gian (ví dụ: Akira lúc nhỏ Ch.1 vs Akira trưởng thành Ch.15); kiểm tra ở chương N dùng đúng bản có hiệu lực tại N; sửa ngược về quá khứ là bản mới, không ghi đè | Akira lúc nhỏ: v1 từ Chương 1; Akira trưởng thành: v2 từ Chương 15 |
| **BR-18** | Kiểm toán đầy đủ | Mọi thao tác thay đổi dữ liệu ghi người, thời điểm, nội dung đổi; thao tác không ghi được thì bị từ chối | Truy ra ai mở khóa chương và lý do |

### Nhóm C. Phân quyền và truy cập

| Mã | Quy tắc | Giải thích | Ví dụ |
|---|---|---|---|
| **BR-07** | Hai tầng quyền | `SystemRole` (Admin hoặc User) chỉ phân biệt quản trị nền tảng, không cấp quyền nội dung; `WorkspaceRole` lưu theo từng Workspace; không có membership thì không có quyền | Admin không mặc nhiên sửa được script của Studio |
| **BR-22** | Quyền tạo Studio | User chỉ tạo Workspace khi Admin cấp `CanCreateStudio`; người tạo tự được gán Producer | Writer được Admin cấp quyền có thể tạo Studio riêng |
| **BR-23** | Hiển thị Workspace | Sau đăng nhập, User chỉ thấy Workspace mình là thành viên; User không có membership và không có quyền tạo Studio được đưa tới catalog công khai | Hà thuộc Moonlight không thấy Studio khác |
| **BR-16** | Truy cập asset ngắn hạn | Asset chưa phát hành chỉ qua link ngắn hạn, thu hồi được, gắn với assignment hoặc role của người yêu cầu, ghi mọi truy cập | URL ảnh bản thảo hết hạn sau thời gian quy định |

### Nhóm D. Quy trình sản xuất

| Mã | Quy tắc | Giải thích | Ví dụ |
|---|---|---|---|
| **BR-06** | Chuyển stage có cổng | Chuyển stage (ví dụ Ink sang Color) phải qua cổng duyệt; stage trước bị đóng băng chỉ đọc, muốn sửa phải trả về | Ink đã duyệt không sửa được trừ khi trả về |
| **BR-19** | Phụ thuộc stage theo Page/Panel | Stage sau chỉ bắt đầu khi stage trước của đúng Page/Panel đó được duyệt; các Page/Panel khác nhau chạy song song | Panel 1 đang Ink, Panel 2 đang Pencil; mỗi panel vẫn phải qua điều kiện của mình |
| **BR-20** | Điều kiện duyệt cổng | Mỗi stage có điều kiện qua cổng (Script, Artwork, Lettering, Consistency); Editor duyệt mặc định, Studio có thể đổi người duyệt; cảnh báo tràn chữ có thể được xác nhận kèm lý do thay vì chặn | Lettering qua được khi cảnh báo tràn chữ đã được xác nhận kèm lý do |
| **BR-14** | Thông báo giao việc | Tạo hoặc giao lại việc phải phát thông báo cho người nhận trước khi việc được coi là hoạt động | Producer giao Pencil cho Hà thì Hà nhận thông báo |
| **BR-12** | Khóa chương | Khi Producer duyệt và khóa, Page và Panel chỉ đọc cho tới khi Producer mở lại kèm lý do | Muốn sửa thoại sau khi khóa phải mở lại chương |

### Nhóm E. AI

| Mã | Quy tắc | Giải thích | Ví dụ |
|---|---|---|---|
| **BR-02** | Người quyết định trong AI | Kết quả AI chỉ là gợi ý; bắt buộc có người duyệt | AI đề xuất layout, Artist xem rồi chấp nhận |
| **BR-10** | Provenance AI bắt buộc | Đề xuất AI chỉ được gộp vào nội dung khi đã lưu provenance (model, tham số, prompt, ngữ cảnh); lưu ngay khi AI trả kết quả; đề xuất bị từ chối cũng được ghi | Biết bản nháp sinh từ script và phiên bản Bible nào |
| **BR-21** | Phân loại phát hiện | Mỗi finding kết thúc là Confirmed, Rejected, Ignored hoặc Needs clarification; Ignored bắt buộc ghi lý do | Editor bỏ qua cảnh báo vì đó là cảnh hồi tưởng và ghi lý do |

### Nhóm F. Xem thử và phát hành

| Mã | Quy tắc | Giải thích | Ví dụ |
|---|---|---|---|
| **BR-09** | Link xem thử hết hạn | Hết hạn thì vô hiệu vĩnh viễn, bất kể ai giữ URL | Link hết hạn thì mất quyền xem |
| **BR-17** | Xem thử gắn người nhận | Chỉ cấp cho chương đã khóa, cho một email cụ thể; watermark hiện người nhận; Producer thu hồi được trước hạn; mọi lần truy cập, kể cả bị từ chối, đều ghi log | Bình chỉ xem chương được chia sẻ; email khác mở link bị từ chối |
| **BR-24** | Đọc công khai | Series có chương Published nằm trong catalog công khai độc lập với Workspace; tài khoản Reader tách riêng; khác với link xem thử | Độc giả không thấy bản thảo đang sản xuất. **Tính năng thêm, ngoài phạm vi đăng ký V1.2:** phiếu nói xuất bản làm ở ngoài hệ thống và không có Public Reader; nhóm quyết định giữ BR này như tính năng thêm, không thuộc nghiệm thu |

### Ba đường truy cập tách biệt
Truy cập nội bộ Workspace, link xem thử, và đọc công khai là **ba đường riêng**; không dùng quyền của đường này cho đường khác (BR-24).

---

## 2.2 Quy tắc cấu hình theo từng Series

Một số quy tắc không cố định mà Producer cấu hình riêng cho từng Series:

| Quy tắc cấu hình | Nội dung | Ví dụ |
|---|---|---|
| **Hướng và thứ tự đọc** | Manga RTL, truyện LTR hoặc webtoon cuộn dọc; thứ tự panel và balloon theo cấu hình | Manga RTL thì kiểm tra thứ tự balloon từ phải sang trái |
| **Pipeline và người duyệt** | Thêm, bớt, đổi thứ tự stage; chọn người duyệt cổng | Truyện đen trắng bỏ Color; Editor duyệt Review; Producer khóa chương |
| **Typography** | Font, cỡ chữ tối thiểu, kiểu balloon, khoảng đệm | Dùng một preset cho toàn Series |
| **Thông số trang và xuất bản** | Kích thước trang, vùng an toàn, độ phân giải, định dạng xuất | Xuất PDF in 300 DPI |
| **Quyền chỉnh sửa nội dung** | Ai được đổi lời thoại và ai duyệt | Letterer đề xuất rút gọn thoại; Writer hoặc Editor duyệt thay đổi lời thoại |
| **Luật nhất quán** | Quy tắc kiểm tra (`ConsistencyRule`) bật hoặc tắt theo Series | Bật luật kiểm tra xưng hô |

---

## 2.3 Quy tắc của pipeline và giao việc (ở tầng domain)

Các quy tắc đã được cài trong domain:

- Pipeline luôn có **ít nhất 2 stage** (điểm đầu và điểm cuối); xóa stage khi chỉ còn 2 bị từ chối.
- Mỗi stage có **slug duy nhất** trong pipeline; tên có dấu được chuẩn hóa (ví dụ "Tô màu" thành `to-mau`).
- Thứ tự stage luôn liên tục 1 đến n; stage đầu và cuối được đánh dấu tự động.
- Transition mặc định đi từ stage i sang i+1 và **yêu cầu role của stage nguồn**.
- Mỗi stage là cổng duyệt có thể **trả về mọi stage phía trước**, và việc trả về **bắt buộc có comment**.
- **Producer luôn được override** khi chuyển stage.
- Xóa stage đang có việc dở dang bị từ chối (`409`).
- Chỉ **Producer** tạo được Series trong Workspace.
- Vòng đời Assignment: bắt đầu làm khi ở `Assigned` hoặc `ReworkRequired`; nộp khi `InProgress` hoặc `Assigned`; yêu cầu làm lại **bắt buộc có comment phản hồi**; duyệt thì ghi thời điểm hoàn thành.
- Page và Panel yêu cầu kích thước, DPI, thứ tự dương; xóa Page hay Element là **xóa mềm**, sự kiện vẫn nằm trong lịch sử.

---

## 2.4 Quy tắc phân biệt ba loại "duyệt"

| Loại | Ai duyệt | Kết quả |
|---|---|---|
| **Duyệt cổng stage** (BR-06, BR-20) | Editor mặc định; Studio có thể cấu hình người khác | Item sang stage kế tiếp |
| **Đạt stage cuối `Approved`** | Sau cổng Review | Item hoàn tất công đoạn sản xuất |
| **Duyệt và khóa chương** (UC-20, BR-12) | Producer | Chương `Locked`, chỉ đọc; sau đó mới xem thử và phát hành |

Chương đi qua: `InProduction → Approved → Locked → Published`; mở lại (UC-23) đưa chương về `InProduction`.

---

## 2.5 Quy tắc phân vai quyết định

| Nội dung | Người quyết định chính | Người phối hợp |
|---|---|---|
| Cốt truyện, scene, thoại | **Writer** | Editor kiểm tra logic, Producer duyệt |
| Bố cục trang, số panel, kích thước, góc máy, khoảng trống cho chữ | **Artist** (chốt layout cuối) | Writer xác nhận không mất ý; Editor kiểm tra nhịp đọc |
| Balloon, font, ngắt dòng, vị trí chữ | **Letterer** | Artist chừa chỗ; Editor review |
| Nội dung lời thoại sau khi đổi | **Writer** hoặc **Editor** duyệt | Letterer đề xuất rút gọn |
| Yêu cầu sửa, duyệt cổng stage | **Editor** | Role thực hiện sửa |
| Giao việc, khóa chương, mở lại, phát hành | **Producer** | Theo cổng đã cấu hình |
| Duyệt và khoá character sheet | **Producer** | Artist cung cấp sketch; Writer và Editor xem |
| Chấp nhận, sửa hoặc từ chối ảnh nhân vật do AI sinh | **Artist** | Editor triage báo cáo nhất quán hình ảnh |
| Tài khoản, quyền tạo Studio, cấu hình AI, giới hạn Workspace (storage, quota token, quota ảnh, session policy), retention và backup | **Admin** | Không có quyền nội dung |
| Mời thành viên vào Workspace và gán WorkspaceRole | **Producer** | Admin không làm việc này |

---

## 2.6 Quy tắc AI (chuỗi vận hành và hàng rào)

- AI **chỉ đề xuất**. Accept chỉ tạo content revision mới, **không** duyệt cổng, **không** chuyển stage, **không** khóa chương.
- Supervisor **không** viết nội dung, không áp dụng, không cấp quyền, không duyệt.
- Backend tự xác định quyền chạy (không tin client): phải là thành viên Workspace, đúng role cho loại nhiệm vụ, đúng assignment và stage (nếu đã giao việc), chương chưa khóa, đủ điều kiện ngữ cảnh, đúng phiên bản nguồn.
- Quyền chạy theo loại nhiệm vụ: `Artist` chỉ Artist chạy được; `Letterer` chỉ Letterer; `Producer` chỉ Producer; `FullScene` và `Script` do Producer hoặc Writer; `Storyboard` do Producer, Writer hoặc Artist; `Editor` do Producer hoặc Editor.
- Quyền Accept theo loại đề xuất: Script (Producer, Writer, Editor); Layout (Producer, Writer, Artist, Editor); ImagePrompt (Producer, Artist, Editor); Lettering (Producer, Writer, Letterer, Editor). Báo cáo của Editor và Producer là tham khảo, không có Accept/Reject.
- Chương không còn `InProduction` thì nhiệm vụ tạo đề xuất áp dụng được bị chặn (`409`).
- Vòng sửa của Writer tối đa 2 lượt; còn lỗi chặn thì dừng chờ người (`WaitingForHuman`).
- Thiếu dữ liệu đầu vào thì dừng chờ người (`StopForHuman`) ngay bằng code, không tốn một lần gọi mô hình.
- Output của agent được kiểm bằng code ở AI service rồi kiểm lại ở Backend: ID do code cấp, mô hình chỉ trả khóa ngắn (`L1`, `B2`, `P3`, `F4`); mọi con số `%` do code tính; giới hạn chữ của Letterer do code ước lượng.
- Dữ liệu đưa vào prompt được coi là dữ liệu, không phải lệnh. Tên và email thành viên không được đưa cho mô hình, chỉ dùng vai trò và stage.
- Mỗi lần chạy lưu step, output, phiên bản prompt kèm hash, usage, provenance; không ghi đè lịch sử.
- AI tắt, chậm, hết quota hoặc lỗi thì **quy trình thủ công vẫn dùng được**. Service AI chạy tách biệt, không gọi trong request nghiệp vụ lõi, có timeout (30 giây, đề xuất chờ giảng viên), circuit breaker và provider dự phòng; run bị gián đoạn giữ các bước đã xong và chạy tiếp từ bước lỗi.
- Editor Agent ở giai đoạn đầu chỉ tổng hợp finding và checklist do code chuẩn bị rồi nháp review report và comment từ chối; quyết định duyệt hoặc từ chối luôn do Editor.
- Output gốc của AI không bị sửa: chỉnh sửa tạo revision dẫn xuất; đề xuất bị từ chối cũng được lưu.
- Khi tích hợp ảnh: ảnh AI tạo ra và ảnh tách nền là **asset dẫn xuất** có nguồn, chỉ vào panel sau khi người chấp nhận.

---

## 2.7 Quy tắc lưu trữ

- Dữ liệu có cấu trúc lưu trong **PostgreSQL**; file nhị phân lưu ở **object storage private**; hai bên nối bằng `AssetId` kèm SHA-256.
- Ảnh gốc **không bao giờ ghi đè**; tên theo hash nội dung.
- Deduplication chỉ **trong phạm vi một Workspace** để giữ ranh giới tenant.
- Không có URL công khai cố định; xem ảnh qua URL ký ngắn hạn sau khi kiểm quyền, và ghi log truy cập.
- Server tự tính lại SHA-256 sau khi tải, không tin hash của client.
- Asset đã được revision tham chiếu **không bao giờ xóa**; chỉ dọn upload bỏ dở theo chính sách lưu giữ do Admin đặt.
- Mỗi lần sửa script phải tạo `ContentRevision` mới cùng transaction với việc cập nhật `ScriptLine`.

---

## 2.8 Quy tắc kỹ thuật cho người viết code (Backend)

Từ `CLAUDE.md` của Backend:

1. Đọc code hiện có trước khi viết.
2. Xác định Use Case, Business Rule (BR-xx) và Requirement bị ảnh hưởng.
3. Theo Clean Architecture và MediatR CQRS; Handler là ranh giới use case.
4. Không tạo service hay interface riêng cho từng tính năng; ưu tiên dùng lại.
5. Không sửa code không liên quan.
6. Giữ lịch sử append-only (provenance) và optimistic concurrency.
7. Kiểm quyền Workspace **ở server**.
8. Output AI cần người quyết định.
9. Viết hoặc cập nhật test tự động khi đổi quy tắc nghiệp vụ.
10. Không thêm thư viện mới nếu thứ có sẵn đã giải quyết được.
11. Không đọc hay lộ bí mật (`.env`, khóa, mật khẩu DB) khi không cần.
12. Không gửi mã nguồn hay file cục bộ cho dịch vụ lạ; hỏi trước khi bật MCP server có mạng mới.

**Thứ tự ưu tiên khi các hướng dẫn mâu thuẫn:**

1. Quy tắc nghiệp vụ và yêu cầu (`requirements`, BR)
2. Bảo mật và toàn vẹn dữ liệu
3. Kiến trúc dự án hiện có
4. Skill riêng của PanelForge
5. Skill kỹ thuật chung
6. Đơn giản hóa (Ponytail)

Mức thấp không bao giờ ghi đè mức cao.

**Phối hợp Frontend:** mọi thay đổi API (route, DTO, đổi tên field, đổi kiểu) phải ghi vào `HANDOFF.md`; thay đổi phá vỡ ghi vào mục "Nhật ký thay đổi breaking".

---

## 2.9 Ai kiểm cái gì: Backend và AI

| Việc | Do ai làm |
|---|---|
| Quyền, liên kết dữ liệu, dependency, khóa chương, phiên bản, hết hạn link, hash | **Backend (quy tắc cứng, bằng code)** |
| Đo chữ tràn, chồng lấn, thiếu liên kết, sai stage, quá hạn, tính phần trăm tiến độ | **Code** (LLM chỉ diễn giải kết quả) |
| Đánh giá cốt truyện, nhịp kể, hình ảnh, continuity | **AI hỗ trợ** |
| Quyết định cuối | **Người có quyền** |

---

## 2.10 Quy tắc bổ sung theo phiếu V1.2 (ngoài BR-01 đến BR-24)

Các quy tắc sau nằm trong phiếu đăng ký nhưng chưa có mã BR riêng:

- **Character sheet khoá.** Producer duyệt và khoá một phiên bản làm tham chiếu duy nhất, versioned theo chương. Bản mới có hiệu lực từ chương được chọn; đã khoá thì chỉ thay thế bằng bản mới, không sửa. Agent và các kiểm tra luôn đọc bản hiệu lực tại chương đang làm.
- **Giới hạn Workspace do Admin đặt** (storage GB, token AI hằng tháng, quota sinh ảnh, session policy) được kiểm **trước** thao tác nó điều khiển (upload, gọi AI, sinh ảnh, đăng nhập hoặc làm mới token). Vượt giới hạn thì trả lỗi rõ ràng, không lỗi ngầm và không chi phí vô hạn. Producer và Admin đều xem được mức dùng.
- **Nhất quán nhân vật ba lớp** (văn bản, luật bằng code, hình ảnh) là mục tiêu chất lượng trung tâm. Chất lượng được **đo** trên tập lỗi gài sẵn; precision và recall tối thiểu 70% và ngưỡng tương đồng 0.75 là **đề xuất chờ giảng viên xác nhận**.
- **Ảnh nhân vật do AI sinh** đi qua adapter thay được, luôn qua bước Accept, Edit hoặc Reject có provenance, và chỉ là nhân vật (nền, bố cục, balloon lắp trong editor từ Element có cấu trúc). Không có công cụ vẽ tự do.
- **Lõi demo là manga trắng đen.** Chế độ màu (stage Color, kiểm nhất quán màu) là cấu hình mở rộng và không thuộc nghiệm thu.
- **Rút gọn thoại cho vừa balloon** thuộc lõi; dịch thoại và Producer Agent là mở rộng.
- **Xuất bản làm ngoài hệ thống.** Publisher là external reviewer; Producer không có quan hệ với nhà xuất bản.

---

## 2.11 Tóm tắt

- **Cấu trúc:** Workspace → Series → Chapter, trong Chapter là cây vật lý Page → Panel → Element và cấu trúc tự sự Scene → Script block song song; Panel tham chiếu Script block; không có đối tượng mồ côi.
- **Quyền:** hai tầng, không có membership thì không có quyền; Admin không đọc nội dung, chỉ Producer mời thành viên.
- **Sản xuất:** pipeline có cổng duyệt, dependency theo từng Page/Panel, chương khóa chỉ đọc.
- **Lịch sử:** chỉ thêm, khôi phục là tạo bản mới, xung đột phải báo.
- **AI:** chỉ đề xuất, có provenance, người quyết định, AI hỏng không chặn quy trình.
- **Truy cập:** nội bộ, xem thử, đọc công khai là ba đường riêng.
- **Lưu trữ:** PostgreSQL cho dữ liệu có cấu trúc, object storage private cho ảnh, nối bằng `AssetId` và SHA-256.
