---
trigger: always_on
---
# Git Branch & Multi-Agent Workflow (trungkien)

- **Nhánh làm việc: `trungkien`.** Mọi thay đổi commit vào `trungkien` (có ở cả BE `PanelForge_FA26SE251` và FE `panelforge-fe`). Không commit/push lên `main`; không tạo nhánh `feature/*` hay `THBMinh` để làm việc.
- **Một agent:** làm trực tiếp trên `trungkien` trong repo chính (`git checkout trungkien`), không tạo worktree.
- **Nhiều agent song song (multi-agent / subagent / session song song):** mỗi agent làm trong **git worktree riêng**, tách nhánh con từ `trungkien`:
  - `git worktree add ../.worktrees/<repo>-<task> -b trungkien-<task> trungkien`
  - Mỗi agent chỉ sửa file thuộc task của mình; hai agent không được sửa cùng file.
  - Không agent nào sửa trực tiếp working tree chính khi agent khác đang chạy.
  - Khi xong: commit trong worktree → `git checkout trungkien && git merge --no-ff trungkien-<task>` → giải quyết conflict, build → `git worktree remove <path>` và `git branch -d trungkien-<task>`. Không push nhánh `trungkien-<task>` lên remote.
- **Lấy code mới:** khi working tree sạch, `git pull origin trungkien` (còn thay đổi thì commit hoặc `git stash` rồi `stash pop` sau khi pull).
- **Push:** chỉ `git push origin trungkien` khi hoàn thành 1 module lớn hoặc khi user yêu cầu trực tiếp. Không push sau mỗi prompt/commit nhỏ.
- Không force-push, không rebase lại lịch sử đã push trên `trungkien`.
