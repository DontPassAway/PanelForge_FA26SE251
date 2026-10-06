# AI Multi-Agent: 7 agent chuyên môn + Gemini Supervisor

Nguồn thiết kế: `HuongDan/AI_Research/AI_MultiAgent_TongHop.md`. Tài liệu này mô tả **phần đã triển khai thật** trong Backend + Frontend, cách chạy và những chỗ khác với tài liệu thiết kế. Hướng B (Gemini làm Supervisor) vẫn là nền; bản này mở rộng từ một workflow demo (Writer → Continuity → Storyboard → Editor) sang **7 agent** và **7 loại nhiệm vụ**.

> **Đối chiếu phiếu V1.2 (06/10/2026):** Artist Agent hiện chỉ soạn prompt ảnh, chưa sinh ảnh; pipeline ảnh nhân vật (character sheet khoá, sinh ảnh từ sketch, tách nền, so tương đồng) là lõi nhưng chưa triển khai. Letterer Agent rút gọn thoại là lõi; dịch thoại và Producer Agent là mở rộng. Layout hiện áp dụng vào Scene chưa có Page (mô hình cũ `Page.SceneId`); phiếu V1.2 đổi sang Panel tham chiếu Scene.

- AI chỉ **đề xuất**. Accept/Reject/RequestChanges là quyết định của người có quyền. Accept chỉ tạo content revision; **không** Approve Chapter, **không** chuyển production stage.
- Backend chính (`PanelForge.API`, :5044) sở hữu quyền, quota, dữ liệu, provenance và quyết định. `PanelForge.AI` (:5055, chỉ nghe `127.0.0.1`) chạy workflow, gọi model và báo từng step về Backend; **không truy cập PostgreSQL**. `PanelForge.AI.Contracts` là hợp đồng dùng chung.

## 1. Kiến trúc tổng quát

```
Người dùng gửi yêu cầu (workflowType + brief)
  → Backend xác thực quyền (membership + vai trò + assignment + chương khoá) và chụp context (snapshot bất biến)
  → CODE (WorkflowRules) tính hành động hợp lệ theo loại workflow
  → Gemini Supervisor chọn hành động (agent / Finish / StopForHuman) + lý do + chỉ đạo
  → agent chuyên môn chạy, output được code kiểm (AI service) rồi Backend kiểm lại
  → lưu step / output / prompt version+hash / usage / provenance
  → người có quyền duyệt kết quả (đề xuất áp dụng được) hoặc đọc báo cáo (tham khảo)
```

Không workflow nào ép cả 7 agent chạy. **Supervisor là thành phần điều phối riêng, không phải ProducerAgent.**

### Bảy agent và role

| Agent | Role hỗ trợ | Việc làm | Kết quả |
|---|---|---|---|
| Writer | Writer | viết/sửa script từ brief + Bible (vòng sửa ≤ 2 lượt) | `ScriptDraft` → đề xuất **Script** |
| Continuity | Writer, Editor | đối chiếu script với Bible, finding có bằng chứng | `ContinuityReport` |
| Storyboard | Writer, Artist | chia Page/Panel, góc máy, thứ tự đọc | `StoryboardLayout` → đề xuất **Layout** |
| **Artist** | Artist | soạn prompt ảnh cho từng panel của layout đã duyệt (không sinh ảnh) | `ArtistPromptSet` → đề xuất **ImagePrompt** |
| **Letterer** | Letterer | rút gọn thoại cho vừa bubble, giữ ý và giọng nhân vật | `LetteringSuggestionSet` → đề xuất **Lettering** |
| Editor | Editor | báo cáo review (checklist do code gom) | `ReviewReport` — **báo cáo tham khảo** |
| **Producer** | Producer | báo cáo tiến độ, rủi ro, đề xuất xử lý từ snapshot do Backend tính | `ProductionReport` — **báo cáo tham khảo** |

`WorkspaceRole` vẫn chỉ gồm Producer, Writer, Artist, Letterer, Editor. Continuity, Storyboard và Supervisor **không** là role.

### Bảy loại nhiệm vụ (workflow)

| `workflowType` | Chuỗi hợp lệ (`WorkflowRules`) | Ai được chạy | Điều kiện context | Kết thúc |
|---|---|---|---|---|
| `FullScene` (mặc định, = demo cũ) | Writer → Continuity (≤2 lượt sửa) → Storyboard → Editor | Producer, Writer | Series có Bible | Finish sau Editor |
| `Script` | Writer → Continuity (≤2 lượt sửa) | Producer, Writer | Series có Bible | Finish ngay sau Continuity sạch; còn finding chặn → StopForHuman |
| `Storyboard` | Continuity (script có sẵn) → Storyboard → Editor | Producer, Writer, Artist | Scene có script, **chưa có Page** | Finish sau Editor; finding chặn → StopForHuman (không có Writer) |
| `Artist` | Artist | **chỉ Artist** | layout đã Accept + panel còn tồn tại | Finish sau Artist |
| `Letterer` | Letterer | **chỉ Letterer** | có thoại gắn với panel của layout đã Accept | Finish sau Letterer |
| `Editor` | Continuity → Editor | Producer, Editor | Scene có script | Finish sau Editor |
| `Producer` | Producer | **chỉ Producer** | (cấp Chapter, không cần Scene/Bible) | Finish sau Producer |

Producer **không** mặc nhiên có quyền Artist/Letterer; Producer chạy được Script/Storyboard/FullScene (giữ chính sách Producer điều phối Series của demo cũ), Editor (báo cáo chỉ đọc) và Producer.

## 2. Quyền, định tuyến và hàng rào

### Quyền chạy (Backend tự xác định, không tin client)

`RunWorkflowCommand` + `AiWorkflowFacts` (cùng bộ luật với API availability, nên UI và Backend không thể lệch nhau):

1. Thành viên Workspace của Series, vai trò thuộc tập được phép của loại nhiệm vụ (bảng trên) → nếu không **403**.
2. Chapter thuộc Series, Scene thuộc Chapter (BR-03) → 404.
3. **Assignment** (vai trò khác Producer): nếu Chapter đã giao việc vai trò này thì người chạy phải là người được giao, assignment phải đang `Assigned/InProgress/ReworkRequired` và **stage** hiện tại phải dành cho vai trò đó (`PipelineStage.AllowedRole`); nếu không → **403**. Chapter chưa giao việc cho vai trò đó thì việc mở cho mọi thành viên cùng vai trò.
4. **Chương khoá** (BR-12): nhiệm vụ tạo đề xuất áp dụng được (FullScene/Script/Storyboard/Artist/Letterer) cần Chapter `InProduction` → **409**. Editor/Producer (báo cáo chỉ đọc) vẫn chạy được.
5. Điều kiện context (bảng trên) → **409** kèm lý do rõ (vd "Scene chưa có layout đã Accept").
6. Version nguồn (`chapterVersion`, `baseScriptRevisionId`, BR-11) → 409 nếu cũ. AI chưa bật / quota / model → 403/429/400 như trước.

Request chỉ có `workflowType` (không có role hay danh sách agent). Route cũ `POST …/workflows/demo/run` giữ nguyên, mặc định `FullScene`.

### Quyền duyệt theo **loại** đề xuất

| Loại | Accept / Reject / RequestChanges |
|---|---|
| Script | Producer, Writer, Editor |
| Layout | Producer, Writer, Artist, Editor |
| ImagePrompt | Producer, Artist, Editor |
| Lettering | Producer, Writer, Letterer, Editor |

`AiSuggestionDto.decisionRoles` + `canDecide` do Backend tính cho người đang xem; Frontend chỉ hiện nút khi `canDecide`. Báo cáo Editor/Producer **không phải đề xuất**: không có Accept/Reject, không tạo content revision.

### Hàng rào `WorkflowRules.cs` (một nguồn sự thật cho cả hai service)

`AllowedActions(workflowType, inputs, steps, maxRounds)`:
- chuỗi + điều kiện Finish **riêng từng workflow** (không còn "luôn sau Editor"); vòng sửa Writer tối đa 2 lượt; script mới/sửa luôn qua Continuity; Storyboard chỉ khi Continuity gần nhất sạch; còn finding chặn thì chỉ `Writer` (còn lượt) hoặc `StopForHuman`;
- thiếu dữ kiện đầu vào (`WorkflowInputs`: script/layout/panel/thoại/tiến độ) → hành động duy nhất là `StopForHuman`; AI service lập quyết định này bằng **code** (lý do "Thiếu context…", không tốn lần gọi model) nhưng vẫn lưu thành step Supervisor;
- loại workflow lạ hoặc bước không có đường tiến hợp lệ → `StopForHuman`, không bao giờ rơi vào một agent.

AI service lọc lựa chọn của Supervisor; lựa chọn sai được **một lần sửa**, sau đó code ghi đè bằng hành động an toàn đầu tiên và đánh dấu `guardrailOverridden: true`. **Backend kiểm lại**: quyết định ngoài danh sách bị từ chối (400); một agent chỉ được ghi nhận nếu quyết định liền trước chọn đúng agent đó, đúng thuộc workflow, đúng thứ tự/parent; kết thúc run chỉ qua `Finish`/`StopForHuman` hợp lệ. Supervisor không viết nội dung, không áp dụng, không cấp quyền, không Approve, không chuyển stage.

## 3. Ba agent mới

Mỗi agent có: prompt `.md` nhúng vào assembly kèm `<!-- version: N -->` + hash (`PromptRef = Prompts/<tên>.md@vN#sha256:…`), contract input/output, validator của code (dùng ở AI service và lại ở Backend), thực thi Gemini (`AgentRunner`, một lần sửa JSON), fixture Mock, lưu output/usage/provenance.

| Agent | Prompt | Context do code dựng (`RunSnapshot`) | Code kiểm (`ContentRules`) |
|---|---|---|---|
| Artist | `artist.md@v1` | layout đã Accept (panel thật ↔ mô tả/góc máy/nhân vật/dòng script), style rules của Series (`ConsistencyRule` đang bật), Bible hiệu lực tại chương (reference = entry Bible) | mỗi panel đúng một prompt; `panelId` thuộc layout nền; reference chỉ lấy từ Bible của run (model chỉ chọn khoá `B#`); **mọi nhân vật trong panel phải được tham chiếu**; độ dài prompt; layout revision khớp |
| Letterer | `letterer.md@v1` | thoại + người nói + panel + bubble (ghép theo nội dung trùng khớp, mỗi bubble một dòng) + giới hạn chữ **ước lượng** (`LetteringLimits`: theo kích thước bubble, hoặc diện tích panel) | `sourceScriptLineId` tồn tại; **single-binding** (mỗi dòng/bubble tối đa một đề xuất); bubble/panel/`originalText`/`maxChars` khớp số liệu của code; `suggestedText` ngắn hơn bản gốc và ≤ giới hạn; **không** dùng nhận định "vừa bubble" của LLM — `withinEstimatedLimit` do code tính |
| Producer | `producer.md@v1` | snapshot tiến độ do code tính (`AiProgressBuilder`): trạng thái chương, ngày phát hành, assignment theo trạng thái, quá hạn/sắp hạn, bị chặn, điểm nghẽn stage, nội dung đã có, đề xuất AI đang chờ; **%** hoàn thành do code tính; `DataGaps` khi thiếu dữ liệu | mọi rủi ro/đề xuất phải trích khoá fact `F#` có thật; **mọi con số `%` trong văn bản phải là số do code tính** (LLM không tự tính); `dataGaps`/`facts` trong báo cáo phải đúng snapshot; không đưa tên/email thành viên ra khỏi hệ thống (chỉ vai trò + stage) |

Áp dụng (Accept):

- **ImagePrompt** → ghi `ContentRevision(ImagePrompt)` append-only (mapping panel → prompt → reference). Chặn khi: version cũ, chương khoá, layout nền đã có revision mới hơn, panel không còn tồn tại (liên kết sai), payload không còn hợp lệ. Không sinh ảnh, không đụng Bible/layout/script.
- **Lettering** → tạo revision **Script mới** (append-only; thoại gốc còn trong revision/baseline cũ) và cập nhật `ScriptLine`. Chặn khi: version cũ, chương khoá, script/layout có revision mới hơn, dòng thoại đã bị sửa so với `originalText` AI đã đọc (liên kết sai). Không sửa bubble trên canvas.
- **Layout** từ workflow `Storyboard` không có Script cha: dựa trên script có sẵn đúng `baseScriptRevisionId`; script đổi sau khi AI chạy → 409.

## 4. Dữ liệu, migration, API, contract

- Migration bổ sung **`20261002105126_AddAiWorkflowTypes`**: `ai_workflow_runs.workflow_type varchar(30) NOT NULL DEFAULT 'FullScene'` (run cũ đọc như FullScene) và `scene_id` cho phép null (báo cáo cấp Chapter). Không xoá dữ liệu, không reset DB.
- Enum: `AiAgentType` + `Artist/Letterer/Producer`; `AiWorkflowType` (mới); `AiSuggestionKind` + `ImagePrompt/Lettering`; `ContentRevisionKind` + `ImagePrompt`. Lưu dạng chuỗi nên không đổi dữ liệu cũ.
- Contract mới (`PanelForge.AI.Contracts`): `ArtistPromptSet`, `LetteringSuggestionSet`, `ProductionReport`, `ArtistContext`, `LetteringContext`, `ProgressContext`, `WorkflowInputs`, `LetteringLimits`; `AgentContext` thêm `WorkflowType/ExistingLayout/Artist/Lettering/Progress` (tuỳ chọn, có mặc định → JSON/snapshot cũ vẫn đọc được).
- API mới: `POST /api/series/{id}/ai/workflows/run` (body có `workflowType`); `GET /api/series/{id}/ai/workflows/availability?chapterId=&sceneId=`. `AiRunDto` thêm `workflowType`, `sceneId` nullable; `AiSuggestionDto` thêm `decisionRoles`, `canDecide`. Chi tiết trong `HANDOFF.md`.

## 5. Chạy Backend và AI service

DB: Backend tự `Migrate` khi khởi động. Cổng: Backend `5044`, AI `5055` (`Kestrel:Endpoints:Http:Url = http://127.0.0.1:5055`).

Cấu hình Backend (section `AiService`, xem `appsettings.Example.json`): `Provider` (`Mock` mặc định | `Gemini`), `GeminiModel` (`gemini-2.5-flash`), `ServiceUrl`, `ServiceKey` (**user-secrets**, cùng giá trị cho cả hai project), `MaxRevisionRounds` (2), `RunStaleAfterSeconds` (300), `SeedDemoData` (false, opt-in, chỉ Development).

```powershell
cd "D:\Program Files\Downloads\Project SEP490\Backend\PanelForge_FA26SE251"
$key = [Convert]::ToBase64String((1..24 | % { Get-Random -Maximum 256 }) -as [byte[]])
dotnet user-secrets set "AiService:ServiceKey" $key --project src/PanelForge.API
dotnet user-secrets set "AiService:ServiceKey" $key --project src/PanelForge.AI
```

Terminal 1 — AI service: `$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --project src/PanelForge.AI`.
Terminal 2 — Backend: `$env:AiService__SeedDemoData="true"; dotnet run --project src/PanelForge.API --launch-profile http` (restart để chạy migration `AddAiWorkflowTypes`).
Kiểm tra: `GET http://127.0.0.1:5055/health/ai`; Swagger `http://localhost:5044/swagger`. Dừng cả hai sau khi demo (đừng để chiếm cổng 5044/5055).

Dữ liệu mẫu (opt-in, idempotent, không sửa dữ liệu có sẵn): Studio Moonlight (`a1000000-…0001`), Series "Thành phố mưa" (`…0002`, RTL), Chapter 3 (`…0003`), một Scene (`…0004`), Bible 5 mục. Thành viên: producer@, writer@, artist@, editor@ và **letterer@** (nếu tài khoản dev có; DB đã seed trước bản này không có Letterer — Producer mời `letterer@panelforge.com` vai trò Letterer qua `POST /api/workspaces/{id}/members`). Mật khẩu dev chung ghi trong `HANDOFF.md`/`DevUserSeeder`. Bible của **An** ghi: *"An chỉ nhận được chìa khóa bạc từ Chương 5; trước Chương 5, An chưa từng sở hữu hay nhìn thấy chiếc chìa khóa này."* — Mock Writer cố ý viết sai lượt đầu để Continuity bắt được.

## 6. Demo từng nhiệm vụ trên Frontend

Route `/workspaces/:w/series/:s/ai-agents` (menu "AI Multi-Agent": Producer, Writer, Artist, Letterer, Editor khi ở trong Series). Màn hình: thanh Supervisor + 7 agent (agent không thuộc nhiệm vụ được làm mờ), form chọn nhiệm vụ (mỗi nhiệm vụ hiện agent, role cần có, loại kết quả, "Chạy được / Chưa chạy được"), khung **điều kiện còn thiếu** (Quyền / Cấu hình / Trạng thái / Thiếu dữ liệu), từng step (input/output/prompt/token), đề xuất kèm nút quyết định đúng quyền, lịch sử revision (Script / Layout / ImagePrompt).

Thứ tự demo (mỗi bước đăng nhập đúng tài khoản):
1. **Writer** → "Scene đầy đủ": Writer sai → Continuity bắt (bằng chứng Bible) → Writer sửa → Storyboard → Editor. Accept Script rồi Accept Layout (Writer).
   - "Viết script": dừng sau Continuity sạch (không Storyboard/Editor). Tick "ép Writer không sửa" → WaitingForHuman sau 2 lượt; tick "leo thang sớm" → Supervisor chọn StopForHuman.
   - "Storyboard từ script có sẵn" chỉ chạy được khi Scene có script và **chưa có Page**.
2. **Artist** → "Prompt ảnh cho panel": xem prompt từng panel + reference Bible + lưu ý nhất quán; Accept (Artist/Editor/Producer) → revision ImagePrompt. Writer thử Accept → không có nút / 403.
3. **Letterer** → "Rút gọn thoại": so sánh gốc/đề xuất (từ bị lược gạch ngang), chip "ước lượng ≤ N ký tự" và "chưa gắn bubble"/"bubble …"; Accept → revision Script mới.
4. **Editor** → "Review biên tập": báo cáo + checklist do code tính; không có Accept/Reject.
5. **Producer** → "Báo cáo tiến độ": headline, rủi ro (kèm khoá fact nguồn), đề xuất, **dữ liệu còn thiếu**, danh sách fact do hệ thống tính; không có Accept/Reject.
6. Thử sai quyền: Writer chọn "Prompt ảnh" → khung "Chưa thể chạy… Quyền: Vai trò Writer không được chạy nhiệm vụ này", nút Chạy bị khoá.

## 7. Mock và Gemini

**Mock (mặc định)**: không cần API key, không gọi mạng; fixture xác định cho cả 7 agent + Supervisor (`Demo/Fixtures/MockFixtures.cs`). `provider = "Mock"`, `model = "mock-fixture-v1"`; không giả lập token (`promptTokens/completionTokens = null`, không ghi `AiUsageRecord`). Mock vẫn bắt buộc: thành viên đúng role, `WorkspaceAiConfig` tồn tại và `IsEnabled = true`.

**Gemini**: Admin cấu hình key + model được phép (`PUT /api/Admin/workspaces/{id}/ai-config`, key lưu mã hoá, không log); Backend: `$env:AiService__Provider = "Gemini"` (+ `AiService__GeminiModel`). Mỗi run kiểm `IAiUsageService`; mỗi step ghi `AiUsageRecord` đúng một lần (`feature = AiAgents.<Agent>`). Hạn mức free tier (đã gặp thật): `gemini-2.5-flash` **20 request/ngày/model**; AI service giãn cách 6,5 giây giữa hai lần gọi và retry 3 lần với backoff; lỗi 429 còn lại thành step lỗi `PROVIDER_UNAVAILABLE` (run `Failed`), **không** tự rơi về Mock, không retry liên tục. Số lần gọi model mỗi run: 2 × số agent + vòng sửa (Artist/Letterer/Producer: 2 lần/run).

## 8. Chịu lỗi, an toàn, provenance

Giữ nguyên các cơ chế của Hướng B: AI tắt → `503 AI_UNAVAILABLE` (các chức năng khác vẫn chạy); `AiRunSweeper` đánh dấu run dở là `Interrupted`; step được Backend lưu (ack) rồi mới chạy bước kế, callback idempotent theo `(runId, stepIndex)`; output model là JSON typed có `schemaVersion`, một lần sửa rồi lỗi rõ ràng; dữ liệu trong prompt là DỮ LIỆU (chống prompt injection), model chỉ trả khoá ngắn (`L1`, `B2`, `P3`, `F4`) — **ID do code cấp**; provenance: snapshot Bible + revision IDs, `promptVersion/promptRef`, parameters, input/output, hash payload, decision (người/lúc/lý do/`resultRevisionId`), revision có `sourceSuggestionId` + mapping. Payload bất biến; không ghi đè lịch sử.

## 9. Kiểm thử và kết quả

- `dotnet build PanelForge.slnx`: 0 warning, 0 error. `dotnet test PanelForge.slnx`: **519/519 pass** (bản trước 363; +156 test, gồm hàng rào theo loại workflow, validator của 3 agent mới, ma trận quyền × nhiệm vụ, assignment/stage/chương khoá/context, Mock end-to-end từng workflow qua hai phía hợp đồng, output bịa bị chặn, Supervisor chọn agent ngoài workflow, Accept bị chặn khi stale/khoá/liên kết sai, run cũ tương thích).
- Frontend: `npm run typecheck`, `npm run lint`, `npm run test:run` → **123/123 pass** (bản trước 75).
- **Chạy thật hai service** (Backend :5044 + AI :5055 + PostgreSQL cục bộ, DB tạm `panelforge_ai_e2e`, đã xoá sau khi chạy; provider Mock): đủ 7 loại workflow, ma trận quyền (Writer→Artist 403, Producer→Artist 403, Writer Accept ImagePrompt 403, Artist Accept Lettering 403…), Accept ImagePrompt/Lettering tạo revision (`ImagePromptv1`, `Scriptv2`), Script dừng ở WaitingForHuman và StopForHuman sớm, Storyboard trên Scene đã có Page → 409. Giao diện kiểm bằng trình duyệt với tài khoản Writer (chọn nhiệm vụ, điều kiện thiếu, chạy Script) và Letterer (chạy + so sánh thoại).

## 10. Giới hạn còn lại / chưa kiểm chứng

- **Gemini thật chưa chạy cho ba agent mới** (Artist, Letterer, Producer): môi trường kiểm thử không có API key Gemini (key nằm trong DB Docker của người dùng, hiện không chạy). Ba agent mới và Supervisor v2 chỉ được kiểm bằng Mock + unit/integration test; chất lượng prompt với Gemini thật và khả năng model tuân thủ JSON/`B#`/`F#`/giới hạn ký tự **chưa được quan sát**. Hướng B với Gemini thật (4 agent cũ + Supervisor v1) đã chạy ở đợt trước. Cách chạy thử: mục 7.
- Giới hạn chữ của Letterer là **ước lượng** (`LetteringLimits`: font 26px, 0,55 em/ký tự, dòng 1,3 em); bubble chỉ ghép được với dòng thoại khi nội dung trùng khớp (canvas Marten không có liên kết cứng ScriptLine ↔ element). Không sửa bubble trên canvas.
- Artist chỉ soạn prompt; chưa có sinh ảnh. `ImagePrompt` revision chưa có màn hình xem lại riêng ngoài danh sách revision.
- Producer chỉ dùng số liệu assignment/deadline/nội dung của Chapter; chưa có burndown, workload theo người. Không đưa tên thành viên cho LLM.
- Không có resume run, SSE (polling 2s), huỷ run chủ động, Ollama fallback. `RequestChanges` chỉ ghi quyết định, không tự chạy lại.
- Test dùng EF InMemory: không kiểm được concurrency token/transaction thật của PostgreSQL (phần này chỉ kiểm bằng lần chạy thật bên trên). `AiRunSweeper` chỉ kiểm bằng unit test.
- Seed demo chỉ thêm Letterer cho workspace mới seed; DB đã seed trước đây cần mời Letterer thủ công. Không có dữ liệu mẫu assignment (Producer báo cáo sẽ nêu `DataGaps` cho tới khi có assignment thật).
- `appsettings.json` đang chứa thông tin nhạy cảm (JWT secret, SMTP password) trong repo — ngoài phạm vi, nên chuyển sang user-secrets.

## 11. Danh sách file chính đã thay đổi (đợt mở rộng 7 agent)

Contracts: `AiJson.cs` (hằng agent/workflow), `Payloads.cs`, `WireContracts.cs`, `WorkflowRules.cs` (viết lại theo loại workflow), `ContentRules.cs` (+3 validator), `LetteringLimits.cs` (mới).
Domain/Infrastructure: `AiAgentType.cs` (+`AiWorkflowType`), `AiSuggestionStatus.cs` (kind mới), `AiWorkflowRun.cs`, `AiWorkflowRunConfiguration.cs`, migration `AddAiWorkflowTypes`, `AiDemoSeeder.cs` (+Letterer).
Application (`Features/AiAgents`): `Common/{AiWorkflowPolicy,AiWorkflowFacts,AiSnapshotBuilder,AiProgressBuilder,AiContentValidator,AiRunHelpers}.cs`, `Commands/{RunWorkflow,RecordAgentStep,ReviewSuggestion}`, `Queries/{GetAgentContext,GetRun,GetSuggestions,GetWorkflowAvailability}`, `Models/{AiAgentDtos,RunSnapshot}.cs`. API: `AiAgentsController.cs`.
AI service: `Agents/{ArtistAgent,LettererAgent,ProducerAgent}.cs` (mới), `SupervisorAgent.cs`, `EditorAgent.cs`, `AgentModels.cs`, `Prompts/{artist,letterer,producer}.md` (mới) + `supervisor.md@v2`, `Workflows/MultiAgentWorkflow.cs` (thay `MangaDemoWorkflow`), `Demo/Fixtures/MockFixtures.cs`.
Test: `WorkflowTypeRulesTests`, `NewAgentContentRulesTests`, `WorkflowRoutingTests`, `NewWorkflowsEndToEndTests`, `ReviewNewKindsTests`, `AiHarness` (mở rộng).
Frontend: `services/aiAgents.service.ts`, `components/ai/{aiViewUtils.ts,AgentOutputViews.tsx,AgentPipelineStrip.tsx,StepOutputView.tsx,SuggestionCard.tsx}`, `views/ai/AiMultiAgentView.tsx`, `layouts/GlobalAppShell.tsx` (menu Artist/Letterer) + test mới.
