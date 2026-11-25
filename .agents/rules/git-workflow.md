---
trigger: always_on
---
# Git Branch & Multi-Agent Workflow (THBMinh)

## Luồng nhánh (promotion flow)

```
THBMinh  ──merge──▶  dev  ──merge──▶  stg  ──(stg chạy OK)──merge──▶  main
(code)              (tích hợp)        (môi trường giống production)     (production)
```

- **`THBMinh`**: nhánh code. Mọi thay đổi đều được commit ở đây (có ở cả BE `PanelForge_FA26SE251` và FE `panelforge-fe`).
- **`dev`**: nhánh tích hợp. Khi xong việc trên `THBMinh` → merge `THBMinh` vào `dev`.
- **`stg`**: staging, environment giống production. Chỉ nhận merge từ `dev`.
- **`main`**: production. Chỉ nhận merge từ `stg`, và **chỉ sau khi `stg` đã được xác nhận chạy OK**.
- Không bao giờ commit trực tiếp lên `dev`, `stg`, `main`; không merge nhảy cóc (ví dụ `THBMinh` → `main`, `dev` → `main`). Không force-push, không rebase lịch sử đã push.
- Agent chỉ tự merge/push tới `dev`. Đẩy `dev` → `stg` và `stg` → `main` cần người dùng xác nhận rõ ràng.

## Làm việc với agent

- **Một agent**: làm trực tiếp trên `THBMinh` (`git checkout THBMinh`).
- **Nhiều agent (multi-agent / subagent / session song song)**: mỗi agent làm trong **git worktree riêng**, trên nhánh con tách từ `THBMinh`:
  - `git worktree add ../.worktrees/<repo>-<task> -b THBMinh-<task> THBMinh`
  - Mỗi agent chỉ sửa file thuộc task của mình; hai agent không sửa cùng file; không sửa working tree chính khi agent khác đang chạy.
- **Khi hoàn thành task**:
  1. Commit trong worktree → `git checkout THBMinh && git merge --no-ff THBMinh-<task>`, giải quyết conflict, chạy build/test.
  2. `git push origin THBMinh`.
  3. Merge vào dev: `git checkout dev && git pull && git merge --no-ff THBMinh && git push origin dev`, rồi quay lại `THBMinh`.
  4. Dọn dẹp: `git worktree remove <path>` và `git branch -d THBMinh-<task>`.
