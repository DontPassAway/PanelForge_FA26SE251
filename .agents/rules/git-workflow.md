---
trigger: always_on
---
# Git Branch & Multi-Agent Workflow (nhánh theo máy)

- **Nhánh làm việc: `{work-branch}`** — nhánh do người code trên máy này chọn (mỗi người push nhánh riêng), lưu ở `git config --local panelforge.workBranch` (chỉ trên máy này, không commit). Đầu phiên chạy `git config --local --get panelforge.workBranch`: có giá trị → dùng, không hỏi lại; trống → hỏi người dùng tên nhánh rồi lưu bằng `powershell -File ../panelforge-hub/.agents/scripts/work_branch.ps1 -Action set -Branch <tên>`; repo đang ở nhánh khác → hỏi trước khi checkout. Không commit/push lên `main` hay nhánh của người khác; không tạo nhánh `feature/*` để làm việc thường ngày.
- **Một agent:** làm trực tiếp trên `{work-branch}` trong repo chính (`git checkout {work-branch}`), không tạo worktree.
- **Nhiều agent song song (multi-agent / subagent / session song song):** mỗi agent làm trong **git worktree riêng**, tách nhánh con từ `{work-branch}`:
  - `git worktree add ../.worktrees/<repo>-<task> -b {work-branch}-<task> {work-branch}`
  - Mỗi agent chỉ sửa file thuộc task của mình; hai agent không được sửa cùng file.
  - Không agent nào sửa trực tiếp working tree chính khi agent khác đang chạy.
  - Khi xong: commit trong worktree → `git checkout {work-branch} && git merge --no-ff {work-branch}-<task>` → giải quyết conflict, build → `git worktree remove <path>` và `git branch -d {work-branch}-<task>`. Không push nhánh `{work-branch}-<task>` lên remote.
- **Lấy code mới:** khi working tree sạch, `git pull origin {work-branch}` (còn thay đổi thì commit hoặc `git stash` rồi `stash pop` sau khi pull).
- **Push:** chỉ `git push origin {work-branch}` khi hoàn thành 1 module lớn hoặc khi user yêu cầu trực tiếp. Không push sau mỗi prompt/commit nhỏ.
- Không force-push, không rebase lại lịch sử đã push trên `{work-branch}`.
