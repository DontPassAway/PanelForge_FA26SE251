# Tài liệu PanelForge

Mục lục các tài liệu, sắp xếp theo thư mục.

| Thư mục | Nội dung | File |
|---|---|---|
| [`requirements/`](requirements/) | Yêu cầu, phạm vi, đặc tả gốc, từ khóa và quy tắc | [`requirements.md`](requirements/requirements.md), [`PanelForge_Capstone_Content.md`](requirements/PanelForge_Capstone_Content.md) (BR, US, UC, FR, NFR gốc), [`PanelForge_5_Core_Flows.md`](requirements/PanelForge_5_Core_Flows.md) (5 core flow, cập nhật theo research), [`glossary-and-rules.md`](requirements/glossary-and-rules.md) (từ khóa và quy tắc bằng tiếng Việt) |
| [`roles/`](roles/) | Các role và luồng làm việc | [`role-workflows.md`](roles/role-workflows.md) (luồng theo từng role), [`role-flow-examples.md`](roles/role-flow-examples.md) (ví dụ xuyên suốt) |
| [`domain/`](domain/) | Mô hình nội dung và nghiệp vụ | [`series-bible.md`](domain/series-bible.md) |
| [`architecture/`](architecture/) | Kiến trúc và triển khai | [`clean-architecture-structure.md`](architecture/clean-architecture-structure.md), [`implementation-qa.md`](architecture/implementation-qa.md) |
| [`storage/`](storage/) | Lưu trữ ảnh, cốt truyện và luồng lưu trữ | [`storage-architecture.md`](storage/storage-architecture.md), [`storage-strategy.md`](storage/storage-strategy.md), [`image_storage_architecture.md`](storage/image_storage_architecture.md) |
| [`ai/`](ai/) | AI multi-agent | [`ai-multi-agent-demo.md`](ai/ai-multi-agent-demo.md) |
| [`database/`](database/) | Cài đặt cơ sở dữ liệu | [`setup-database.md`](database/setup-database.md) |
| [`archive/`](archive/) | Tài liệu đã xong hoặc đã bị thay thế, giữ làm lịch sử | [`archive/implementation/`](archive/implementation/): đặc tả thay đổi CF1, CF4, CF5 (đã merge; việc còn lại đã chuyển vào `Github/note/plans/`) và `PanelForge_Scope_V1.2.md`; [`archive/requirements/project-plan.md`](archive/requirements/project-plan.md). Phiếu đăng ký V1.2 không còn là căn cứ phạm vi; phạm vi hiện hành theo spec V2.6. |
| [`research/`](research/) | Nghiên cứu công cụ, role và quyết định thiết kế | [`RESEARCH_ROLES_AND_TOOLS.md`](research/RESEARCH_ROLES_AND_TOOLS.md) (công cụ ngoài), [`panelforge-design-research.md`](research/panelforge-design-research.md) (quy trình, mô hình dữ liệu, pipeline, versioning, AI; có bằng chứng và backlog), [`consistency-design.md`](research/consistency-design.md) (thiết kế kiểm tra nhất quán: quy tắc, dữ liệu, triage, kiểm thử), [`PanelForge_Research_Cau_Truc_Pipeline.md`](research/PanelForge_Research_Cau_Truc_Pipeline.md) (mô hình cấu trúc nội dung Scene/Page/Panel và pipeline sản xuất), [`PanelForge_Research_Nghiep_Vu.md`](research/PanelForge_Research_Nghiep_Vu.md) (nghiệp vụ, giá trị, nhóm người dùng) |

## Nên đọc theo thứ tự nào

1. [`glossary-and-rules.md`](requirements/glossary-and-rules.md): từ khóa và quy tắc.
2. `Github/note/PANELFORGE_MASTER_SPECIFICATION_V2_6.md`: phạm vi và nghiệp vụ hiện hành; [`requirements.md`](requirements/requirements.md): yêu cầu gốc.
3. [`role-workflows.md`](roles/role-workflows.md) và [`role-flow-examples.md`](roles/role-flow-examples.md): ai làm gì.
4. [`series-bible.md`](domain/series-bible.md), [`storage-strategy.md`](storage/storage-strategy.md), [`ai-multi-agent-demo.md`](ai/ai-multi-agent-demo.md): các phần chuyên sâu.

## Kế hoạch triển khai

Kế hoạch triển khai của cả Backend, Frontend và Mobile **không nằm trong repo này**. Chúng ở thư mục workspace gốc:

- `Github/note/PANELFORGE_MASTER_SPECIFICATION_V2_6.md`: đặc tả nghiệp vụ hiện hành (bảng "Nhật ký quyết định" ở đầu file).
- `Github/note/PANELFORGE_V2_6_IMPLEMENTATION_PLAN.md`: plan tổng, quyết định đã chốt, changelog chung.
- `Github/note/plans/`: một plan cho mỗi module (`SCR`, `BIB`, `ASG`, `ART`, `REV`, `REL`, `RDR`, `UIQ`).

Không tạo file plan mới trong `docs/`. Tài liệu kế hoạch cũ đã xong thì chuyển vào `archive/`.

## Ghi chú

- `CLAUDE.md`, `README.md` của Backend và `HANDOFF.md` ở thư mục gốc giữ nguyên vị trí vì có vai trò cố định.
- Các tài liệu nhắc nhau bằng tên file (ví dụ `requirements.md`); tên file là duy nhất nên tìm theo bảng trên.
