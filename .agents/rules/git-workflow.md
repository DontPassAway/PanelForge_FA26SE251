---
trigger: always_on
---
# Git Branch & Multi-Agent Workflow (THBMinh)

## Luồng nhánh (promotion flow)

```
THBMinh  ──PR (review)──▶  dev  ──merge──▶  stg  ──(stg chạy OK)──merge──▶  main
(code)              (tích hợp)        (môi trường giống production)     (production)
```

- **`THBMinh`**: nhánh code. Mọi thay đổi đều được commit ở đây (có ở cả BE `PanelForge_FA26SE251` và FE `panelforge-fe`).
- **`dev`**: nhánh tích hợp. Code từ `THBMinh` vào `dev` **chỉ qua Pull Request** (base `dev` ← compare `THBMinh`) và phải có thành viên khác review/approve trước khi merge. Không `git merge` vào `dev` ở máy local, không `git push origin dev`.
- **`stg`**: staging, environment giống production. Chỉ nhận merge từ `dev`.
- **`main`**: production. Chỉ nhận merge từ `stg`, và **chỉ sau khi `stg` đã được xác nhận chạy OK**.
- Không bao giờ commit trực tiếp lên `dev`, `stg`, `main`; không merge nhảy cóc (ví dụ `THBMinh` → `main`, `dev` → `main`). Không force-push, không rebase lịch sử đã push.
- Agent **không** tự merge/push vào `dev`, `stg`, `main`. Agent chỉ commit trên `THBMinh`; push `THBMinh` và mở PR vào `dev` chỉ khi người dùng yêu cầu. Người review bấm merge PR trên GitHub. `dev` → `stg` và `stg` → `main` cũng cần người dùng xác nhận rõ ràng.

## Làm việc với agent

- **Một agent**: làm trực tiếp trên `THBMinh` (`git checkout THBMinh`).
- **Nhiều agent (multi-agent / subagent / session song song)**: mỗi agent làm trong **git worktree riêng**, trên nhánh con tách từ `THBMinh`:
  - `git worktree add ../.worktrees/<repo>-<task> -b THBMinh-<task> THBMinh`
  - Mỗi agent chỉ sửa file thuộc task của mình; hai agent không sửa cùng file; không sửa working tree chính khi agent khác đang chạy.
- **Khi hoàn thành task**:
  1. Commit trong worktree → `git checkout THBMinh && git merge --no-ff THBMinh-<task>`, giải quyết conflict, chạy build/test.
  2. Khi người dùng yêu cầu: `git push origin THBMinh`, rồi mở Pull Request `THBMinh` → `dev` (`gh pr create --base dev --head THBMinh`, hoặc trang `https://github.com/<owner>/<repo>/compare/dev...THBMinh`). Mô tả PR ghi việc đã làm, API/DB/enum thay đổi, kết quả build/test.
  3. Chờ review; sửa theo góp ý bằng commit mới trên `THBMinh` rồi push lại (PR tự cập nhật). Không tự merge PR.
  4. Dọn dẹp: `git worktree remove <path>` và `git branch -d THBMinh-<task>`.
