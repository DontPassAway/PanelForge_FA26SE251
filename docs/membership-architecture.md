# Workspace / Series Membership (2026-10-31)

## Hai tầng, hai câu hỏi khác nhau

| Tầng | Trả lời câu hỏi | Bảng |
|---|---|---|
| Workspace Membership | Người này có **vai trò chuyên môn** gì trong Studio? (nguồn role duy nhất) | `workspace_members` |
| Series Membership | Người này có **được vào** Series này không? (ranh giới truy cập) | `series_members` |

Producer / Owner của Workspace luôn truy cập mọi Series của Workspace. Admin hệ thống không có quyền Workspace (BR-07).

## Quy tắc

1. **Role kế thừa từ Workspace.** `series_members.role` luôn bằng role Workspace. `POST /api/series/{seriesId}/members` vẫn nhận `role` để không phá client cũ nhưng **bỏ qua** nó. Quyền theo route Series/Chapter/Page/Panel (`RequireWorkspaceRole` → `HasSeriesRoleAsync`) xét role Workspace.
2. **Tự thêm khi giao việc.** `CreateAssignment`, `UpdateAssignment` (đổi người nhận) và `TransferAssignmentScope` gọi `AssignmentRules.EnsureSeriesMemberAsync` sau khi đã kiểm tra người nhận (thành viên Workspace, đã chấp nhận lời mời, đúng role của stage). Membership và Assignment lưu trong **cùng một** `SaveChanges` → hoặc cả hai, hoặc không gì. Đã là thành viên thì dùng lại, không tạo dòng mới.
3. **Một dòng hiệu lực / (series, user).** Unique index `ix_series_members_series_user (series_id, user_id) WHERE is_deleted = false`. Hai lần giao việc đồng thời: một bên bị index chặn → `CreateAssignment` trả **409** "vui lòng giao việc lại" (không 500, không lưu nửa vời).
4. **Đổi role Workspace** (`PUT /api/workspaces/{id}/members/{userId}`) đồng bộ role của mọi Series Membership của người đó trong Workspace, cùng một lần lưu.
5. **Gỡ / chưa Active.** `RemoveWorkspaceMember` gỡ người đó khỏi mọi Series của Workspace. Ngoài ra `IsSeriesMemberAsync` / `HasSeriesRoleAsync` yêu cầu Workspace Membership còn **Active** → membership Series sót lại (Pending, Rejected, đã gỡ) không mở được tài nguyên. Assignment, Revision, Artwork, lịch sử không bị xoá.
6. **Thêm tay vẫn giữ.** Producer có thể thêm trước người vào Series (màn "Thành viên bộ truyện"), chỉ chọn người, không chọn role.

## Đối soát dữ liệu cũ (chạy lại an toàn)

| Migration | Việc làm | Dev DB 2026-10-31 |
|---|---|---|
| `20261031090000_BackfillSeriesMembersFromAssignments` | Thêm Series Membership cho người đang có Assignment mà chưa là thành viên; role = role Workspace; bỏ qua người không Active trong Workspace; `NOT EXISTS` nên không trùng. Down xoá đúng các dòng có `created_by = 'backfill:assignments-2026-10-31'`. | 35 assignment → **11** cặp (series, user) được tạo |
| `20261031100000_SyncSeriesMemberRolesWithWorkspace` | Đặt `series_members.role` = role Workspace nếu lệch. | 0 dòng lệch |

Truy vấn kiểm tra sau đối soát (dev DB): assignment thiếu membership 0; role lệch 0; membership của người không còn Active 0; assignee không còn Active 0; dòng trùng 0.

## Frontend

- `SeriesMembersModal`: bỏ ô chọn role khi thêm và nút "Đổi vai trò"; badge hiển thị role Workspace.
- `CreateAssignmentModal`: danh sách người nhận lấy từ thành viên **Active của Workspace** (không còn bắt phải vào Series trước), lọc theo role của stage; có dòng nhắc "sẽ được tự thêm vào đội Series".

## Test

- BE: `AssignmentTests` (tự thêm / dùng lại membership, sai role stage, người Pending, 409 khi trùng đồng thời), `SeriesMembershipRulesTests` (cần cả Series + Workspace Active, membership sót lại bị chặn, role theo Workspace, thêm tay kế thừa role, đổi role đồng bộ). Toàn bộ: 1107/1107.
- FE: `CreateAssignmentModal.test`, `SeriesMembersModal.test` cập nhật theo hành vi mới. Toàn bộ: 949/949, `tsc` sạch.
