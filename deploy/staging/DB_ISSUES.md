# Vấn đề DB cần xử lý trước khi deploy

Ghi ngày 2026-11-28. File này **chỉ ghi lại** các vấn đề, chưa sửa gì trên DB (không tạo hay chạy migration nào).
Số liệu lấy từ thư mục `src/PanelForge.Infrastructure/Persistence/Migrations` trên các nhánh remote cùng ngày.

| Nhánh | Số migration |
|---|---|
| `main` | 98 |
| `dev` | 112 |
| `feature/workflow-fix` | 123 |

API chạy `db.Database.MigrateAsync()` khi khởi động (`Program.cs`), nên **bất kỳ image mới nào được deploy đều tự áp migration còn thiếu vào DB thật** của môi trường đó.

---

## 1. `dev` và `feature/workflow-fix` lệch migration hai chiều

- **Vấn đề:**
  - `dev` có 3 migration mà `feature/workflow-fix` chưa có: `20261128050000_MoveMangaColorGateToInk`, `20261128051000_AddPanelBalloonCheck`, `20261128063022_BackfillBibleConceptEventPointers`.
  - `feature/workflow-fix` có 14 migration mà `dev` chưa có: `AddWorkspaceAiConfigConcurrencyToken`, `ReclassifyAdminRefundTransactions`, `AddPaymentProviderReference`, `AddPlatformBankAccounts`, `AddQuotaPoliciesAndIncreaseRequests`, `DropStudioBankColumns`, `AddQuotaPolicyAiDefaultAndWarning`, `AddAiWorkflowRunReservedTokens`, `AddAiRequestLimits`, `AddUploadLimitPolicies`, `AddStudioPackagePrices`, `AddPlatformEmailSettings`, `AddPlatformEmailTemplates`, `SplitEmailTemplateIntoPlainTextParts`.
- **Rủi ro:**
  - Khi gộp, `PanelForgeDbContextModelSnapshot.cs` chắc chắn xung đột. Gộp sai thì migration sau sinh ra thay đổi lạ, có thể xoá hoặc tạo lại bảng.
  - Thứ tự timestamp xen kẽ: 3 migration của `dev` (05:00 đến 06:30) nằm giữa các migration của `feature/workflow-fix`. DB đã chạy bản `feature/workflow-fix` sẽ áp 3 migration này "lùi thời gian". EF vẫn chạy được, nhưng cần chắc hai bên không sửa cùng bảng hoặc cột.
- **Đề xuất:**
  - Gộp `dev` vào `feature/workflow-fix` (hoặc ngược lại) qua PR, giữ thay đổi của cả hai bên trong snapshot.
  - Sau khi gộp, chạy thử `dotnet ef migrations add CheckEmpty` trên máy dev: phải ra **migration rỗng**, rồi xoá migration thử đó đi.
  - Chạy `dotnet test` và khởi động API trên một bản sao DB `dev`.
- **Ai quyết:** người merge PR `feature/workflow-fix` → `dev`.

## 2. `DropStudioBankColumns` xoá dữ liệu

- **Vấn đề:** migration `20261128042048_DropStudioBankColumns` xoá các cột `bank_account_name`, `bank_account_no`, `bank_bin`, … khỏi `studio_workspaces`. Dữ liệu ngân hàng của Studio được thay bằng `platform_bank_accounts`.
- **Rủi ro:** khi lên DB thật, các cột và dữ liệu trong đó mất hẳn. Rollback image không lấy lại được. Bản cũ nếu chạy lại sẽ lỗi vì thiếu cột.
- **Đề xuất:**
  - Trước khi deploy bản có migration này, kiểm tra trên DB thật xem còn Studio nào có dữ liệu ngân hàng. Nếu còn, export ra (CSV) hoặc chuyển sang `platform_bank_accounts`.
  - Nhờ `deploy.sh` mới (PR #40), luôn có bản `pg_dump` trước khi deploy.
- **Ai quyết:** chủ nghiệp vụ thanh toán / người duyệt deploy `main`.

## 3. `dev` → `main` mang 14 migration lên DB thật

- **Vấn đề:** `main` (bản chính thức) đang thiếu 14 migration mà `dev` có. Lần promote `dev` → `main` tiếp theo sẽ áp cả 14 migration này vào DB chính thức ngay khi API khởi động. Khi `feature/workflow-fix` vào `dev`, con số này tăng thêm 14 nữa, trong đó có mục 2.
- **Rủi ro:** migration lỗi giữa chừng hoặc chạy lâu làm API không lên. Migration có xoá dữ liệu thì không quay lại được.
- **Đề xuất:**
  - Deploy bằng `deploy.sh` mới (PR #40): có `pg_dump` trước khi deploy và rollback image khi health check hỏng.
  - Chạy thử trước trên bản sao DB chính thức: restore bản dump mới nhất vào một Postgres tạm, chạy API bản mới trỏ vào đó, xem log migration.
  - Chọn giờ ít người dùng.
- **Ai quyết:** người duyệt promote `dev` → `main`.

## 4. Rollback image không hoàn tác migration

- **Vấn đề:** `deploy.sh` (PR #40) rollback về image `:<env>-previous` khi API bản mới không lên, nhưng DB có thể đã được bản mới migrate.
- **Rủi ro:** image cũ chạy trên schema mới có thể lỗi, ví dụ thiếu cột đã bị xoá hoặc gặp bảng lạ. Khi đó API vẫn không lên dù đã rollback.
- **Đề xuất:** làm theo mục "Backup và rollback" trong `README.md`: dừng API, `pg_restore` bản dump mà log in ra, chạy lại. Không tự động hoá bước này, vì restore làm mất dữ liệu ghi sau thời điểm dump.
- **Ai quyết:** người trực deploy.

## 5. Migration trùng giữa bản local và PR đang mở

- **Vấn đề:**
  - Trên máy dev có nhánh local `THBMinh-task-24` … `THBMinh-task-28`, chưa push, với các migration `AddEmailDeliveryLogs` (09:52) và `AddEmailTemplateOverrides` (13:13).
  - Chức năng tương ứng đã có trên remote bằng migration khác: `AddPlatformEmailTemplates` và `SplitEmailTemplateIntoPlainTextParts` trên `feature/workflow-fix`, và `20261128095114_AddEmailDeliveryLogs` trong PR BE #39.
- **Rủi ro:** nếu ai gộp nhánh local vào sẽ có hai bảng mẫu email, hai migration `AddEmailDeliveryLogs` trùng tên lớp, và snapshot lệch.
- **Đề xuất:**
  - **Không** push hay gộp các nhánh local đó.
  - Phần chức năng còn thiếu (lọc theo ngày, email-health) được đưa lên PR #39 mà không thêm migration nào.
- **Ai quyết:** đã quyết: giữ bản remote.

## 6. Test tích hợp Postgres không chạy được trên máy dev

- **Vấn đề:** 8 test `*PostgresIntegrationTests` (`BibleWorkflowPostgresIntegrationTests`, `RefreezeScriptPostgresIntegrationTests`) lỗi `28P01: password authentication failed for user "postgres"` trên máy dev Windows.
- **Rủi ro:** các test kiểm tra migration chạy sạch trên Postgres thật (`EndToEnd_MigrationsClean...`) không được chạy, nên lỗi migration chỉ lộ ra khi deploy.
- **Đề xuất:** cấu hình connection string hoặc mật khẩu Postgres cho test trên máy dev (theo hướng dẫn test của repo), hoặc bảo đảm CI có Postgres service và chạy các test này trước khi merge vào `dev`.
- **Ai quyết:** người phụ trách CI / môi trường dev.

## 7. Kiểm tra migration trên nhánh `deploy-b2b` (sau khi gộp `trungkien`)

Nhánh `deploy-b2b` = `origin/dev` + PR #40 (backup/rollback) + `origin/trungkien` (5 commit "feat(review)"). Các bước dưới đây chỉ kiểm tra trên code, không đụng DB nào.

- **Gộp:** `git merge --no-ff origin/trungkien` tự gộp sạch, kể cả `PanelForgeDbContextModelSnapshot.cs`. Không có xung đột.
- **Build:** `dotnet build PanelForge.slnx`: 0 lỗi.
- **Unit test** (bỏ các test cần Postgres, xem mục 6): 1820/1820 pass.
- **Số migration:** 113. So với `origin/dev`, có thêm đúng 1 migration là `20261128060626_AddStageTaskChangeRequests` (từ `trungkien`) và không thiếu migration nào của `dev` (`trungkien` vốn thiếu `BackfillBibleConceptEventPointers` của `dev`, nay đã có sau khi gộp). So với `origin/main`, có thêm 15 migration.
- **Snapshot khớp model:** chạy thử `dotnet ef migrations add DeployCheck` (connection string mẫu, không kết nối DB) ra **migration rỗng**. Mình đã xoá migration thử này và không commit.
- **Timestamp và tên:** không trùng timestamp, không trùng tên lớp.
- **Designer:** 7 migration không có file `.Designer.cs`: `BackfillSeriesMembersFromAssignments`, `SyncSeriesMemberRolesWithWorkspace`, `AddReviewerNameToPreviewFeedback`, `AddSeriesIsCompleted`, `AddChapterViews`, `AddScriptLineKindAndPanelDirection`, `MoveMangaColorGateToInk`. Cả 7 đều là migration viết tay và đã có sẵn `[DbContext]` + `[Migration("…")]` trong file `.cs`, nên EF vẫn nhận. Không phải lỗi, chỉ cần biết khi review.
- **Thao tác có thể mất dữ liệu** trong 15 migration mới so với `main` (chỉ tính phần `Up`):

| Migration | Thao tác | Ghi chú |
|---|---|---|
| `20261126095525_SimplifyBibleEntrySchema` | `DropColumn` ×6 (`bible_entries`: `aliases`, `code`, `is_active`, `priority`, `strict_check`, `subtitle`), `DropIndex`, `Sql` | **Mất dữ liệu** các cột này trên DB thật. Kiểm tra / export trước khi promote lên `main`. |
| `20261126175800_VariantScopedBibleVersioning` | `DropIndex`, `Sql` ×2 | Đổi index và chuyển dữ liệu bằng SQL tay |
| `20261125065631_MoveStudioAiKeysToSharedCredentials` | `Sql` | Chuyển AI key của Studio sang bảng dùng chung |
| `20261126134429_AssignAutomationFoundation` | `Sql` | Backfill dữ liệu |
| `20261128050000_MoveMangaColorGateToInk` | `Sql` | Cập nhật dữ liệu gate |
| `20261128063022_BackfillBibleConceptEventPointers` | `Sql` ×2 | Backfill dữ liệu |

  `20261128060626_AddStageTaskChangeRequests` (từ `trungkien`) chỉ thêm, không xoá hay sửa dữ liệu.
- **Đề xuất:**
  - Trước khi promote `deploy-b2b` / `dev` lên `main`, chạy thử 15 migration này trên một bản sao DB chính thức (restore từ bản `pg_dump` mới nhất) và xem log migration.
  - Riêng `SimplifyBibleEntrySchema`: xác nhận không còn cần dữ liệu 6 cột trên. Nếu cần thì export ra trước.
- **Ai quyết:** người duyệt deploy `main`.

## 8. Gộp `feature/admin-b2b` và `claude/project-thread-0wp0dx` vào `deploy-b2b`

Ghi ngày 2026-11-28. Chỉ kiểm tra trên code và một Postgres tạm trong máy cloud, không đụng DB thật nào.

- **Gộp:** `feature/admin-b2b` (23 commit) rồi `claude/project-thread-0wp0dx` (kho tài nguyên Producer, D12) đều tự gộp sạch ở BE, kể cả `PanelForgeDbContextModelSnapshot.cs`.
- **Build:** 0 lỗi. **Test:** 1932/1932 pass, lần này **có cả** 8 test `*PostgresIntegrationTests` (chạy trên Postgres 16 tạm), nên `EndToEnd_MigrationsClean...` xác nhận toàn bộ migration chạy sạch từ DB rỗng.
- **Snapshot khớp model:** `dotnet ef migrations add` thử ra migration rỗng, đã xoá, không commit.
- **Số migration:** 125 (thêm 12 so với mục 7):
  `AddWorkspaceAiConfigConcurrencyToken`, `ReclassifyAdminRefundTransactions`, `AddPaymentProviderReference`, `AddPlatformBankAccounts`, `AddQuotaPoliciesAndIncreaseRequests`, `DropStudioBankColumns`, `AddQuotaPolicyAiDefaultAndWarning`, `AddAiWorkflowRunReservedTokens`, `AddAiRequestLimits`, `AddUploadLimitPolicies`, `AddStudioPackagePrices` (từ `admin-b2b`) và `20261128083317_AddStudioResourcePools` (từ `0wp0dx`).
- **Timestamp xen kẽ:** 11 migration của `admin-b2b` (03:50 đến 06:56) nằm xen giữa `AddPanelBalloonCheck` (05:10), `AddStageTaskChangeRequests` (06:06), `BackfillBibleConceptEventPointers` (06:30) đã có trên `deploy-b2b`. EF áp mọi migration còn thiếu nên vẫn chạy; không thấy hai bên sửa cùng cột.
- **Thao tác có thể mất / đổi dữ liệu** (phần `Up`):

| Migration | Thao tác | Ghi chú |
|---|---|---|
| `20261128042048_DropStudioBankColumns` | `Sql` (chép giá trị cũ vào `audit_logs` với action `STUDIO_BANK_INFO_ARCHIVED`) rồi `DropColumn` ×4 trên `studio_workspaces` (`bank_account_name`, `bank_account_no`, `bank_bin`, `bank_name`) | **Mất cột** trên DB thật. Giá trị cũ còn trong `audit_logs` dạng chuỗi, nhưng rollback image cũ sẽ lỗi vì thiếu cột (xem mục 4). Export các cột này trước khi deploy. |
| `20261128035356_ReclassifyAdminRefundTransactions` | `Sql` | Đổi loại các giao dịch hoàn tiền do Admin tạo. Kiểm tra số dòng bị đổi trên bản sao DB. |

  10 migration còn lại chỉ thêm bảng / cột / index.
- **Đề xuất:** chạy thử 12 migration này trên bản sao DB chính thức trước khi promote, export 4 cột ngân hàng của `studio_workspaces`.
- **Ai quyết:** chủ nghiệp vụ thanh toán / người duyệt deploy `main`.

---

## Ghi chú deploy

- PR #41 (Caddyfile: bỏ rewrite `/api/hubs/` → `/hubs/`, chặn `/internal/*`) **không** có trong nhánh `deploy-b2b` này. Nó phải đi cùng PR #35 (`feature/assign-automation`), nơi hub đã chuyển sang `/api/hubs/ai-stream`. Trên `dev`, hub vẫn ở `/hubs/ai-stream`; bỏ rewrite lúc này sẽ làm hỏng SignalR.
- Phần chặn `/internal/*` có thể tách ra làm sớm hơn nếu cần, vì nó không phụ thuộc đường dẫn hub.
