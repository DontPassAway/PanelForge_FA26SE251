# Nhờ test: giảm token & sửa lỗi JSON bị cắt của AI agents

Nhánh: `feature/ai-rule-parity` · Commit: `e99ca9f`, `428b4b1`, `a9f3213`, `bac3291` · Ngày 2026-11-23

Không đổi API, DB, DTO. Frontend không cần sửa gì.

## Đã thay đổi gì

Gemini 2.5 có chế độ "suy nghĩ" (thinking). Token suy nghĩ vẫn bị tính phí và bị trừ vào giới hạn 4096 token đầu ra. Hậu quả là có lúc agent trả về JSON bị cắt ngang, run lỗi `INVALID_OUTPUT`.

| Agent | Trước | Sau |
|---|---|---|
| Supervisor, Copilot (4 tác vụ) | thinking mặc định, trần 4096 | `reasoning_effort: none` (2.5 Pro: `low`), trần 2048 |
| Writer, Continuity | thinking mặc định | `reasoning_effort: low`, trần 4096 |
| Storyboard, Editor, Artist | không đổi | không đổi |

Ngoài ra:
- Khối Bible trong prompt: mỗi mục một dòng, bỏ bớt nhãn lặp lại.
- Writer: thẻ Bible (`refs`) có cụm chữ không nằm trong dòng (ví dụ gắn tên cho đại từ "cậu") giờ **bị bỏ** thay vì làm hỏng cả bản nháp. Backend vốn đã xử lý như vậy (`ScriptBibleRefs`). Prompt Writer v3 dặn không gắn thẻ cho đại từ.

## Đã kiểm được

- `dotnet test PanelForge.slnx`: 1532/1532 pass.
- Chạy thật trên `gemini-2.5-flash` (workflow Script):
  - Writer: trước bị cắt JSON 2/2 lần → giờ thành công ngay lần đầu.
  - Supervisor gọi Gemini với `none`: Gemini chấp nhận, ~1,5 giây (trước đây trung bình 2,6 giây).
  - Continuity lượt 0: thành công ngay lần đầu, 8,5 giây.

## Chưa kiểm được → nhờ mọi người

1. **Continuity lượt sửa (lượt 1)**: chỗ từng bị cắt JSON. Lần chạy cuối dừng vì key hết quota (HTTP 429) trước khi tới bước này.
2. **Copilot** (rewrite / shorten / metadata / stub draft / impact) với `none`: chưa gọi thật lần nào, vì dữ liệu dev mình đang dùng có kịch bản đã chốt nên Copilot trả 409.
3. **Chất lượng Continuity** sau khi giảm thinking: có bỏ sót mâu thuẫn với Bible không.
4. **`gemini-2.5-pro`** (nếu workspace của bạn cho dùng): bước nhẹ gửi `low` vì Pro không tắt hẳn thinking được.

## Cách test

### 1. Lấy code và chạy

```powershell
git fetch origin
git checkout feature/ai-rule-parity
git pull origin feature/ai-rule-parity
dotnet test PanelForge.slnx
```

Chạy 2 service (2 terminal), cấu hình như bình thường (README §AI, `AiService:Provider=Gemini`, key Gemini của workspace):

```powershell
dotnet run --project src/PanelForge.AI
```

```powershell
dotnet run --project src/PanelForge.API --launch-profile http
```

### 2. Chạy trên giao diện

Mỗi mục chỉ cần thử 1–2 lần, tránh tốn quota:

- **Workflow Script** (Writer → Continuity, có lượt sửa): chọn một scene **chưa chốt kịch bản**, viết brief đủ để Continuity tìm ra lỗi chặn, như vậy sẽ có lượt sửa. Đây là mục quan trọng nhất (mục 1 ở trên).
- **Copilot Writer**: chọn một dòng thoại → "Viết lại" / "Rút gọn".
- **Publisher metadata**, **Bible stub draft**, **Impact ripple**: nếu bạn đang có màn hình đó.

### 3. Xem kết quả từng bước trong DB

```sql
-- run mới nhất
select id, workflow_type, status, error_code, left(error_message, 150)
from ai_workflow_runs order by created_at desc limit 5;

-- từng bước của một run
select step_index, agent, round, status, prompt_tokens, completion_tokens,
       round(extract(epoch from completed_at_utc - started_at_utc)::numeric, 1) as sec,
       repair_attempts,
       parameters_json::json->>'reasoningEffort' as effort,
       parameters_json::json->>'maxOutputTokens' as max_out,
       error_code
from ai_agent_steps where run_id = '<run-id>' order by step_index;
```

**Đạt khi:**
- Không có bước nào `INVALID_OUTPUT` với lỗi "JSON không hợp lệ ... open JSON object" (dấu hiệu bị cắt).
- Cột `effort`: Supervisor = `none`, Writer/Continuity = `low`, các agent khác để trống.
- Copilot trả gợi ý bình thường, không lỗi 400 từ Gemini.

**Lưu ý:** `completion_tokens` Gemini trả về **không gồm** token thinking, nên đừng dùng cột này để so tiết kiệm. Hãy so cột `sec` và số lần `repair_attempts`.

## Báo lại

Comment vào issue/nhóm chat kèm:
- `run_id`, workflow, model
- Kết quả bảng `ai_agent_steps` ở trên (copy text)
- Nếu Continuity bỏ sót lỗi rõ ràng: đoạn script + mục Bible liên quan

Lỗi 429 / `PROVIDER_UNAVAILABLE` là do hết quota key, không phải lỗi code. Chờ một lúc rồi thử lại.
