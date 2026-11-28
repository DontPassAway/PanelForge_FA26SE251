# Admin Portal — Audit & Resource Management (2026-11-28)

Phạm vi: Admin Portal, Resource Allocation (Storage + AI Quota), Resource Request. Một file thay cho 5 file đề xuất
(audit, implementation, UI test, permission matrix, resource management) để tránh lặp nội dung.


> **Gộp vào `feature/admin-b2b` (28/11):** phần tài nguyên chạy trên model của OPS-20..23 (`quota_policies` append-only,
> `quota_increase_requests`), không dùng bảng `resource_policies` / `resource_requests` và migration `AdminResourceManagement`.
> Endpoint thật: `GET/PUT /api/admin/quota-policy`, `GET /api/admin/resources`, `PUT /api/admin/workspaces/{id}/resources`,
> `GET /api/admin/quota-requests`, `POST .../quota-requests/{id}/approve|reject`, Producer `GET/POST /api/workspaces/{id}/quota-requests`.
> Màn "Thanh toán" của Admin bị bỏ vì tài khoản nhận tiền là của nền tảng và do Publisher cấu hình (OPS-10, quyết định C1).
> Chi tiết: hub `docs/note/plans/10_PLATFORM_OPS_PLAN.md`.

## 1. Tóm tắt

- **Đã sửa P0:** Producer tự chọn Storage Quota khi tạo Studio (`POST /api/workspaces` nhận `storageQuotaBytes`, gửi `0` = không giới hạn;
  UI còn bày "gói" Basic/Team/Enterprise 100/300/500 GB — ngoài phạm vi, giống gói B2B). Quota giờ lấy từ **Resource Policy** do Admin đặt.
  Dữ liệu dev đang có 2 Studio "không giới hạn" do lỗi này (KomaForge Studio, Studio Moonlight) — **không tự đổi**, Admin chỉnh trong Tài nguyên nếu cần.
- **Mới:** Resource Policy mặc định, trang Admin › Tài nguyên (cấp phát theo Studio / chính sách / yêu cầu tăng), luồng Resource Request
  (Producer gửi, Admin duyệt/từ chối), Overview có KPI tài nguyên thật và danh sách "Cần xử lý".
- **Gộp:** màn "Lưu trữ & quota" (`?tab=storage`) trùng chức năng sửa quota → xoá, chuyển hướng sang `?tab=resources`.
- **Đã sửa P0 thứ hai:** `RequireWorkspaceRoleFilter` từng cho **Admin đi qua mọi endpoint theo Workspace** (gồm sửa/publish chương,
  sửa cấu hình ngôn ngữ Series…), trái BR-07 (dù `WorkspaceAuthorizationService` đã chặn Admin). Giờ Admin nhận 403 trên mọi endpoint Workspace,
  trừ GET có `[AllowAdminOversight]` — hiện chỉ `GET /api/workspaces/{id}/reader-stats` (BR-32: Admin là người xem Dashboard độc giả).
  `UpdateSeriesLanguages` cũng bỏ ngoại lệ Admin. Admin Portal chỉ dùng `/api/admin/*` nên không bị ảnh hưởng.

## 2. Ma trận gap

| Module | Hiện có | Vấn đề | Hành động | Ưu tiên |
|---|---|---|---|---|
| Tạo Studio | Producer chọn quota 100/300/500 GB | Tự cấp quota, `0` = vô hạn | FIX: dùng Resource Policy | P0 |
| Phân quyền Workspace | Filter cho Admin bypass | Admin vào được endpoint sản xuất | FIX: 403, opt-in đọc `[AllowAdminOversight]` | P0 |
| Storage quota | `TryReserveStorage` khi upload artwork/thumbnail | Màn sửa quota riêng, không lý do | MERGE vào Tài nguyên, bắt buộc lý do | P1 |
| AI quota | Kiểm tra trước khi gọi provider, reset tháng UTC | Sửa quota nằm trong màn Cấu hình AI 2.1k dòng | EXTEND: sửa trong Tài nguyên (màn AI giữ nguyên) | P2 |
| Default policy | Hard-code 1.000.000 token, client chọn storage | Không cấu hình được | IMPLEMENT | P1 |
| Resource request | Không có | — | IMPLEMENT | P1 |
| Overview | 4 KPI người dùng + lối tắt | Không có số liệu tài nguyên, không có "cần xử lý" | EXTEND | P2 |
| Audit log | Tự động old/new mọi entity | Không có lý do điều chỉnh | Ghi `RESOURCE_ALLOCATION_UPDATED` kèm lý do | P2 |
| Publisher (Q12) | 1 tài khoản | — | KEEP | — |
| Finance / Q13 | WIP `PaymentSettings` (chưa commit, của người dùng) | Spec Q13 (đổi 2026-11-15): **Publisher** cấu hình NH nền tảng; WIP hiển thị NH ở Admin (chỉ đọc) | Không đụng, cần xác nhận | — |
| System Health | Không có dữ liệu giám sát thật | — | OMIT (không bịa uptime/latency) | — |

## 3. Hành vi Resource Management

| Quy tắc | Cài đặt |
|---|---|
| Đơn vị | Storage: byte (`bigint`); AI: token/ảnh mỗi tháng. `0` = không giới hạn (giữ quy ước cũ). |
| Mặc định khi chưa lưu policy | 20 GB, 500.000 token, ảnh không giới hạn, AI bật, cảnh báo 80%. Không ghi DB cho tới khi Admin lưu. |
| Studio mới | Storage + AI config khởi tạo theo policy hiện hành. |
| Đổi policy | Chỉ ảnh hưởng Studio tạo sau; quota Studio cũ giữ nguyên (có test). |
| Admin chỉnh Studio | Một lần lưu cho Storage + Token + Ảnh + bật/tắt AI; lý do bắt buộc; audit `RESOURCE_ALLOCATION_UPDATED` (old/new + lý do). |
| Hạ quota dưới mức dùng | Cảnh báo trên UI; **không xoá file**; upload mới bị chặn (`HasStorageCapacity`). |
| Kỳ AI | Reset khi sang tháng UTC (theo `AiUsageService`). Overview coi bộ đếm tháng cũ chưa reset = 0. Storage không reset. |
| Trạng thái | `Ok` / `NearLimit` (≥ ngưỡng) / `Exhausted` (≥ 100%), lấy mức xấu nhất trong Storage/Token/Ảnh. Badge có icon + chữ. |
| Resource Request | Producer gửi (lý do bắt buộc, mức mới > mức hiện tại, mỗi loại tối đa 1 Pending). Admin duyệt → áp quota + đổi trạng thái trong cùng `SaveChanges`; từ chối bắt buộc lý do. Duyệt 2 lần → 409 (kiểm trạng thái + `xmin`). Audit `RESOURCE_REQUEST_APPROVED/REJECTED`. |
| Thanh toán | Không có. Không gói, không mua tài nguyên. |

## 4. Ma trận quyền (endpoint mới)

| Endpoint | Admin | Producer | Vai trò sản xuất khác | Publisher |
|---|---|---|---|---|
| `GET/PUT /api/admin/resource-policy` | ✅ | ❌ 403 | ❌ | ❌ |
| `GET /api/admin/resources` | ✅ | ❌ | ❌ | ❌ |
| `PUT /api/admin/workspaces/{id}/resources` | ✅ | ❌ | ❌ | ❌ |
| `GET /api/admin/resource-requests`, `POST …/{id}/approve|reject` | ✅ | ❌ | ❌ | ❌ |
| `GET /api/workspaces/{id}/resource-requests` | ❌ 403 (dùng `/api/admin/resource-requests`) | ✅ Studio mình | ❌ | ❌ |
| `POST /api/workspaces/{id}/resource-requests` | ❌ 403 | ✅ Studio mình | ❌ | ❌ |
| Mọi endpoint `[RequireWorkspaceRole]` khác | ❌ 403; chỉ GET có `[AllowAdminOversight]` (reader-stats) | theo vai trò | theo vai trò | theo vai trò |

## 5. Thay đổi

**DB (migration `20261128033023_AdminResourceManagement`, chỉ thêm bảng):** `resource_policies`, `resource_requests` (FK `studio_workspaces`, index `(workspace_id, status)`, `xmin`).

**API (mới):** `AdminResourcesController` (tách khỏi `AdminController` vì file đó đang có WIP chưa commit); 2 action trong `WorkspacesController`.
**API (đổi):** `CreateWorkspaceRequest` bỏ `storageQuotaBytes` (client cũ gửi lên sẽ bị bỏ qua, không lỗi). Enum mới: `ResourceType`, `ResourceRequestStatus`, `ResourceHealth`.

**Backend:** `Features/Resources/*` (2 query, 4 command, `ResourceAllocation` dùng chung cho tạo Studio / chỉnh tay / duyệt); `CreateWorkspaceCommand` dùng policy.

**Frontend:** `services/resource.service.ts`; `roles/admin/views/modules/resources/*` (ResourcesView, ConfigureStudioModal, ResourcePolicyPanel, ResourceRequestsPanel, ResourceUi); Overview thêm KPI + "Cần xử lý"; sidebar "Lưu trữ & quota" → "Tài nguyên"; xoá `StorageBackupSettings.tsx`; Producer: bỏ chọn gói quota khi tạo Studio, thêm "Yêu cầu thêm tài nguyên" trong khối Tài nguyên của Studio; i18n vi/en (parity 100%).

## 6. Kết quả kiểm thử

Baseline trước khi sửa: BE build OK, **1717 pass / 9 fail** (8 test tích hợp Postgres lỗi xác thực DB tạm + `CreatePanel_WhenPageExists…` NRE trong mock). FE typecheck có sẵn 1 lỗi ở `roles/publisher/components/multilingual.ts`.

| Test ID | Module | Kịch bản | Kỳ vọng | Thực tế | Trạng thái | Bằng chứng |
|---|---|---|---|---|---|---|
| BE-01 | Policy | Chưa lưu → trả mặc định | 20 GB / 500k / 80% | đúng | PASS | `ResourceManagementTests` |
| BE-02 | Tạo Studio | Quota theo policy, không theo client | 20 GB, 500k token | đúng | PASS | idem |
| BE-03 | Policy | Đổi policy không đổi Studio cũ | Studio cũ giữ 10 GB | đúng | PASS | idem |
| BE-04 | Policy | Giá trị âm / ngưỡng 0, 100 | Lỗi, không ghi | đúng | PASS | idem (4 case) |
| BE-05 | Cấp phát | Chỉnh 2 quota + audit lý do | Audit có old/new + lý do | đúng | PASS | idem |
| BE-06 | Cấp phát | Thiếu lý do / số âm | Lỗi | đúng | PASS | idem |
| BE-07 | Storage | Hạ quota dưới mức dùng | Giữ UsedStorage, chặn upload mới | đúng | PASS | idem |
| BE-08 | Request | Mức thấp hơn / thiếu lý do / Pending trùng | Lỗi / 409 | đúng | PASS | idem |
| BE-09 | Request | Duyệt áp quota; duyệt lần 2 | Áp quota; 409 | đúng | PASS | idem |
| BE-10 | Request | Từ chối thiếu ghi chú / có ghi chú | Lỗi / Rejected, quota giữ | đúng | PASS | idem |
| BE-11 | Overview | NearLimit/Exhausted/pending/tổng | Đếm đúng | đúng | PASS | idem |
| BE-12 | Overview | Bộ đếm AI tháng trước | Token = 0, Storage giữ | đúng | PASS | idem |
| BE-13 | Quyền | Controller Admin-only; request Producer-only | Thuộc tính đúng | đúng | PASS | `ResourceEndpointAuthorizationTests` |
| BE-ALL | Hồi quy | Toàn bộ suite | Không thêm lỗi | 1740 pass / 9 fail (= baseline) | PASS | `dotnet test` |
| FE-ALL | Hồi quy | Toàn bộ Vitest | Không thêm lỗi | 1537 pass / 10 fail; AppShell (nhãn menu đổi) đã sửa → 57/57 pass khi chạy lại. Còn lại có sẵn, ngoài vùng sửa: WorkspaceListView (2, đã xác nhận fail cả khi bỏ thay đổi), i18n "Canon Locked", SplitPanelStrip, ArtistChapterSelectionView, PublisherChapterManageView | PASS (không lỗi mới) | `vitest run` |
| FE-01..07 | Tài nguyên | Hiển thị, tìm kiếm, lỗi + thử lại, validate lý do, cảnh báo dưới mức dùng, lỗi server, duyệt/từ chối | Như mô tả | đúng | PASS | `ResourcesView.test.tsx` |
| FE-08..09 | Overview | "Cần xử lý" → `?tab=resources&view=requests`; `?tab=storage` → Tài nguyên | Điều hướng đúng | đúng | PASS | `AdminDashboard.test.tsx` |
| UI-01 | Overview | Sidebar, KPI thật (dev DB) | Render, không lỗi | đúng | PASS | Browser 1440×900 |
| UI-02 | Tài nguyên | Bảng cấp phát 4 Studio | Đúng số liệu DB | đúng | PASS | Browser |
| UI-03 | Tài nguyên | Lưu thiếu lý do → lưu 15 GB có lý do | Báo lỗi; cập nhật + toast + audit | đúng | PASS | Browser + `GET /admin/audit-logs` |
| UI-04 | API trực tiếp | Số âm / thiếu lý do / ngưỡng 150 / request không tồn tại | 400/400/400/404 | đúng | PASS | fetch trong trình duyệt |
| UI-05 | Quyền | Admin gửi request Producer | 403 | lần đầu trả 400 (lọt filter) → sửa filter → 403 BR-07 | PASS (sau sửa) | fetch trong trình duyệt |
| UI-05b | Quyền | Admin `GET /workspaces/{id}/usage`; `GET …/reader-stats`; `GET /admin/resources` | 403; 200; 200 | đúng | PASS | fetch trong trình duyệt |
| BE-14 | Quyền | Filter: Admin POST/PUT/GET không opt-in, POST có opt-in → 403; GET có opt-in → qua | Như mô tả | đúng | PASS | `RequireWorkspaceRoleAdminTests` (5) |
| UI-06 | Responsive | 768×1024, 390×844 | Không tràn ngang trang | `scrollWidth - clientWidth = 0` | PASS | Browser |
| UI-07 | Producer | Gửi yêu cầu từ khối Tài nguyên Studio | Tạo Pending | — | NOT RUN (cần đổi sang tài khoản Producer) | — |
| UI-08 | Viewport | 1920, 1280, 1024 | — | — | NOT RUN | — |

## 7. Còn lại / ngoài phạm vi

- Nếu còn màn Admin nào cần đọc dữ liệu Workspace qua route `/api/workspaces/...`, gắn `[AllowAdminOversight]` cho đúng GET đó (đã rà FE Admin: chỉ reader-stats).
- Chưa có: giới hạn số request AI/tháng, requests-per-minute, kích thước upload tối đa, chặn upload trực tiếp R2 (hiện upload đi qua API nên đã bị chặn bởi `TryReserveStorage`), "reservation" token AI trước khi gọi provider (hiện kiểm tra quota trước, ghi token thật sau).
- Màn Cấu hình AI vẫn có ô sửa token quota riêng — hai nơi cùng sửa một giá trị; nên bỏ ô đó hoặc trỏ về Tài nguyên.
- Trang chi tiết User/Studio (`/admin/users/:id`, `/admin/studios/:id`), System Health: chưa làm.
- Audit tự ghi bởi handler (`RESOURCE_*`) chưa có `user_email` (chỉ `user_id`).
- `GET /admin/resource-requests` chưa phân trang (tối đa 500 dòng; có ghi `shortcut:`).
