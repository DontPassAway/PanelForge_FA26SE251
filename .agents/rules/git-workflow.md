---
trigger: always_on
---
# Git Branch & Multi-Agent Workflow (THBMinh)

- **Nhánh làm việc chính: `THBMinh`** (có ở cả BE `PanelForge_FA26SE251` và FE `panelforge-fe`). Mọi thay đổi đều đi vào `THBMinh`; không commit/push thẳng lên `main`.
- **Một agent**: làm việc trực tiếp trên `THBMinh` (`git checkout THBMinh`).
- **Nhiều agent (multi-agent / subagent / session song song)**: mỗi agent làm việc **tách biệt trong git worktree riêng**, tách nhánh con từ `THBMinh`:
  - `git worktree add ../.worktrees/<repo>-<task> -b THBMinh-<task> THBMinh`
  - Mỗi agent chỉ sửa file thuộc task của mình; hai agent không được sửa cùng file.
  - Không agent nào sửa trực tiếp working tree chính khi agent khác đang chạy.
- **Khi hoàn thành**: commit trong worktree → merge vào `THBMinh` (`git checkout THBMinh && git merge --no-ff THBMinh-<task>`), giải quyết conflict, chạy build/test, rồi `git push origin THBMinh`. Sau đó dọn dẹp: `git worktree remove <path>` và `git branch -d THBMinh-<task>`.
- Không force-push, không rebase lại lịch sử đã push trên `THBMinh`.
