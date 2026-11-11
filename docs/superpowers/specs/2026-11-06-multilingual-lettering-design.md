# Spec: Lettering đa ngôn ngữ (bản gốc + bản dịch JSON)

- Ngày: 2026-11-06 · Trạng thái: **Draft – chờ nhóm duyệt**
- Phạm vi: Backend (`PanelForge_FA26SE251`) + hợp đồng API cho Frontend
- Nguồn ý tưởng: đề xuất "CBZ + Custom Manifest / Comic Package v1.0" (`metadata/text-regions.json`, `translations/{lang}.json`)

## 1. Mục tiêu

1. Chữ do Editor/Letterer nhập là **bản gốc** (ngôn ngữ chuẩn của Series, mặc định `vi`). Luồng hiện tại giữ nguyên.
2. Mỗi chương có thể có nhiều **bản dịch**. Mỗi ngôn ngữ là một tài liệu JSON trỏ tới bong bóng gốc theo `elementId`, không lưu toạ độ riêng.
3. Khi chữ gốc bị sửa, bản dịch tự phát hiện là cũ (stale).
4. AI (Gemini) dịch nháp, **người duyệt** thì mới dùng (quy tắc 11).
5. Xuất bản theo gói đọc được nhiều ngôn ngữ (Comic Package). Đây là giai đoạn sau.

**Không làm** trong spec này: OCR chữ dính trong ảnh, TTS, chú giải từ vựng, trình đọc phía người dùng cuối.

## 2. Hiện trạng (đã kiểm tra code)

- Chữ nằm trong `ElementState` của `Page`, được dựng lại từ event (event-sourced): `Content`, `RenderedText`, `SourceLineText`, `ScriptLineId`, `Style`, `Version`, `IsRemoved`.
- `Series` không có trường ngôn ngữ. Toàn bộ Domain chưa có khái niệm translation/language.
- AI gợi ý đi qua `AiSuggestion` (`Kind`, `Status`, `PayloadJson`, `BaseChapterVersion`), người chấp nhận/từ chối.
- Giới hạn tràn chữ: `LetteringLimits.ForElement(widthPx, heightPx)`. Công thức đang giả định chữ Latin/Việt (`CharWidthEm = 0.55`).

## 3. Mô hình dữ liệu

### 3.1 `Series.OriginalLanguage`
- `string`, mã ISO 639-1 (`vi`, `en`, `ja`, …), mặc định `"vi"`. Migration đặt `"vi"` cho Series đã có.
- **BR-ML-01:** chỉ đổi được khi chưa chương nào có element chữ (tránh "bản gốc" bị đổi nghĩa giữa chừng).

### 3.2 `ChapterTranslation` (mới, aggregate theo chương × ngôn ngữ)

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `Id` | Guid | |
| `ChapterId` | Guid | FK |
| `LanguageCode` | string(8) | ISO 639-1, ≠ `Series.OriginalLanguage` (**BR-ML-02**) |
| `Status` | enum `Draft · InReview · Approved` | |
| `Version` | long | optimistic concurrency, tăng mỗi lần sửa |
| `EntriesJson` | jsonb | xem 3.3 |
| `ApprovedAt/By` | nullable | |
| BaseEntity | | Created/Updated/soft delete như các entity khác |

Unique `(ChapterId, LanguageCode)` trong số các dòng chưa xoá.

### 3.3 Định dạng JSON một bản dịch (`EntriesJson`, cũng là file `metadata/translations/{lang}.json` khi xuất, đường dẫn khớp với `Mobile/lib/services/cbz_package_service.dart`)

```json
{
  "language": "en",
  "sourceLanguage": "vi",
  "entries": {
    "<elementId>": {
      "text": "Where is the lantern?",
      "sourceVersion": 7,
      "sourceTextHash": "sha256-hex của RenderedText gốc lúc dịch",
      "origin": "Human",
      "aiSuggestionId": null,
      "fontScale": null
    }
  }
}
```

- `sourceVersion` + `sourceTextHash` dùng để phát hiện bản cũ. Chỉ so `Version` thì sẽ báo stale cả khi chỉ dời vị trí bong bóng, nên trạng thái câu dịch tính như sau:
  - **Missing:** element gốc có chữ nhưng chưa có entry.
  - **UpToDate:** hash khớp với chữ gốc hiện tại.
  - **Stale:** hash khác (chữ gốc đã đổi).
  - **Orphan:** element gốc đã `IsRemoved`. Entry được giữ lại, ẩn khỏi bản xuất.
- `origin`: `Human | Ai | AiEdited`. `aiSuggestionId` trỏ về gợi ý đã chấp nhận (provenance).
- `fontScale`: tuỳ chọn 0.6–1.0, để thu chữ cho vừa bong bóng ở từng ngôn ngữ, không đụng vào style gốc.

### 3.4 Lịch sử: `ChapterTranslationRevision` (append-only)
Mỗi lần lưu ghi thêm một dòng: `{ TranslationId, Version, EntriesJson, ChangedBy, ChangedAt, Reason }`. Không sửa, không xoá (quy tắc 8). Khôi phục = tạo revision mới có nội dung cũ, giống `ScriptLine`.

> Vì sao lưu cả JSON thay vì mỗi câu một dòng: đúng đề xuất "translations/{lang}.json", xuất bản chỉ việc copy, và một chương chỉ có cỡ vài trăm bong bóng. Đổi lại phải sửa theo kiểu đọc–sửa–ghi cả tài liệu, dùng `Version` để chặn ghi đè (409).

## 4. Quy tắc nghiệp vụ

| ID | Quy tắc |
|---|---|
| BR-ML-01 | `OriginalLanguage` khoá khi Series đã có chữ. |
| BR-ML-02 | Không tạo bản dịch trùng ngôn ngữ gốc; mỗi (chương, ngôn ngữ) chỉ một bản. |
| BR-ML-03 | Chỉ dịch được element loại chữ (`SPEECH_BALLOON`, `NARRATION`, … theo `LETTERING_ELEMENT_TYPES`). |
| BR-ML-04 | Sửa bản dịch phải gửi `expectedVersion`; lệch → 409 `CONFLICT`. |
| BR-ML-05 | Gợi ý AI không bao giờ tự ghi vào bản dịch: phải được người chấp nhận qua `AiSuggestion`. |
| BR-ML-06 | Chỉ `Approved` khi không còn câu **Missing** hoặc **Stale**. |
| BR-ML-07 | Bản `Approved` mà chữ gốc đổi thì không tự hạ trạng thái, nhưng API trả `staleCount > 0` và FE cảnh báo. Muốn xuất bản thì phải duyệt lại. |
| BR-ML-08 | Quyền hạn kiểm ở server theo vai trò Workspace (quy tắc 10), xem mục 5. |

## 5. Phân quyền (đề xuất mặc định, cần nhóm chốt)

| Hành động | Vai trò |
|---|---|
| Xem bản dịch | mọi thành viên Series |
| Tạo / sửa / gọi AI dịch nháp | **Editor**, Letterer |
| Gửi duyệt | Editor, Letterer |
| Duyệt (`Approved`) / trả về | **Editor**, Producer |

Chưa thêm vai trò Translator riêng. Nếu sau này cần thì chỉ thêm vào bảng trên, không phải đổi mô hình.

## 6. API (Backend → Frontend)

Tất cả cần Bearer, kiểm quyền theo bảng ở mục 5.

| Method + route | Request | Response |
|---|---|---|
| `GET /api/chapters/{chapterId}/translations` | — | `TranslationSummaryDto[] { languageCode, status, version, total, upToDate, missing, stale, orphan }` |
| `POST /api/chapters/{chapterId}/translations` | `{ languageCode }` | `TranslationDto` (201). 409 nếu đã có, 400 nếu trùng ngôn ngữ gốc |
| `GET /api/chapters/{chapterId}/translations/{lang}` | — | `TranslationDto { languageCode, sourceLanguage, status, version, items: TranslationItemDto[] }` |
| `PUT /api/chapters/{chapterId}/translations/{lang}/entries/{elementId}` | `{ text: string, fontScale?: number, expectedVersion: long }` | `TranslationDto` · 409 `CONFLICT` |
| `POST /api/chapters/{chapterId}/translations/{lang}/ai-draft` | `{ elementIds?: Guid[] }` (bỏ trống = mọi câu Missing/Stale) | `AiSuggestion` loại `Translation` (accept/reject bằng API AiSuggestion đã có) |
| `POST /api/chapters/{chapterId}/translations/{lang}/submit` | `{ expectedVersion }` | `TranslationDto` |
| `POST /api/chapters/{chapterId}/translations/{lang}/approve` | `{ expectedVersion }` | `TranslationDto` · 409 nếu vi phạm BR-ML-06 |
| `POST /api/chapters/{chapterId}/translations/{lang}/request-changes` | `{ expectedVersion, reason }` | `TranslationDto` |
| `GET /api/chapters/{chapterId}/translations/{lang}/revisions` | — | lịch sử revision |

`TranslationItemDto { elementId, pageId, pageNumber, panelId?, readingOrder?, elementType, sourceText, text?, state: "Missing"|"UpToDate"|"Stale"|"Orphan", origin?, fontScale?, maxChars }`. Sắp theo trang → `readingOrder` để màn dịch đọc theo đúng thứ tự truyện. `maxChars` lấy từ `LetteringLimits.ForElement`, có tính hệ số theo ngôn ngữ (mục 7).

## 7. Tràn chữ theo ngôn ngữ

Thêm `LetteringLimits.ForElement(widthPx, heightPx, languageCode)` với hệ số rộng ký tự: `vi/en/...` 0.55em; `ja/zh/ko` ~1.0em (chữ toàn khổ, thường viết dọc, nên `maxChars` nhỏ hơn). Cảnh báo tràn chỉ để tham khảo, không chặn lưu, giống cơ chế `OverflowAckReason` hiện có.

## 8. AI dịch nháp

- Thêm `AiSuggestionKind.Translation`. `PayloadJson = { language, entries: { elementId: { text, sourceTextHash } } }`, chỉ dùng Gemini.
- Prompt gồm: câu gốc theo thứ tự đọc, tên người nói (`SpeakerId` → Bible), và thuật ngữ đã khoá trong Series Bible, để giữ nhất quán tên riêng.
- Chấp nhận gợi ý thì ghi vào bản dịch với `origin = Ai`. Câu nào người sửa lại sau đó thì đổi thành `AiEdited`. Nếu `sourceTextHash` trong gợi ý không còn khớp chữ gốc lúc chấp nhận, câu đó bị bỏ và báo lại.

## 9. Xuất bản Comic Package (giai đoạn 3)

`GET /api/chapters/{chapterId}/export?format=comic-package&languages=vi,en` trả về một file `.zip`/`.cbz`:

```
manifest.json            { version: "1.0", seriesId, chapterId, sourceLanguage, languages[], readingDirection, pages[] }
pages/001.webp …         ảnh trang không có chữ (ảnh công đoạn cuối)
metadata/panels.json     bbox chuẩn hoá 0–1 + readingOrder (từ Panel)
metadata/text-regions.json  { elementId, pageIndex, bounds 0–1, type, style }
metadata/translations/vi.json   bản gốc
metadata/translations/en.json   chỉ các bản Approved, bỏ câu Orphan
```

Điều kiện: chương đã `Approved` và mọi ngôn ngữ được chọn đều `Approved` với `stale = 0`.

## 10. Lộ trình

1. **GĐ1 – Nền dữ liệu + dịch tay:** `Series.OriginalLanguage`, `ChapterTranslation`(+Revision), migration, API mục 6 (trừ `ai-draft`), test BR-ML-01..08.
2. **GĐ2 – AI dịch nháp:** `AiSuggestionKind.Translation`, prompt có thuật ngữ từ Bible, hệ số tràn chữ theo ngôn ngữ.
3. **GĐ3 – Xuất Comic Package:** endpoint export + manifest. FE thêm màn xem trước theo ngôn ngữ.

FE (Antigravity) cần: màn dịch hai cột (gốc | dịch) theo thứ tự đọc, có chip trạng thái từng câu, nút "AI dịch nháp" kèm màn duyệt, và canvas xem trước có ô chọn ngôn ngữ (đè chữ dịch lên vị trí bong bóng gốc).

## 11. Test (xUnit)

- Domain: BR-ML-01, 02, 06; tính trạng thái Missing/UpToDate/Stale/Orphan (sửa chữ gốc → Stale; chỉ dời bong bóng → vẫn UpToDate; xoá element → Orphan).
- Handler: 409 khi lệch version; revision chỉ được thêm, không mất; phân quyền 403 theo bảng mục 5; chấp nhận gợi ý AI có hash cũ → câu đó bị bỏ.
- Export (GĐ3): manifest đúng schema; ngôn ngữ chưa duyệt bị từ chối.

## 12. Câu hỏi mở cho nhóm

1. Có cần vai trò **Translator** riêng không, hay Editor/Letterer là đủ (mặc định ở mục 5)?
2. Bản dịch có phải là điều kiện để duyệt chương không? (Đề xuất: **không**, chỉ là điều kiện để xuất bản ngôn ngữ đó.)
3. Danh sách ngôn ngữ đầu tiên: đề xuất `en`, `ja`.
4. Tiếng Nhật viết dọc: chỉ cần lưu cờ `vertical` trong style của bản dịch, hay cần layout riêng?

## 13. Ý tưởng mở rộng (đề xuất, xếp theo giá trị / công sức)

Cột "GĐ" là giai đoạn nên làm. Ý tưởng nào chưa được nhóm chọn thì **không** nằm trong phạm vi GĐ1.

| # | Ý tưởng | Vì sao đáng làm | Cách làm dựa trên cái đã có | GĐ |
|---|---|---|---|---|
| 1 | **Bảng thuật ngữ đa ngôn ngữ từ Series Bible** | Tên nhân vật, chiêu thức, địa danh dịch lệch nhau giữa các chương là lỗi hay gặp nhất khi dịch truyện | Thêm `translations: { en: "...", ja: "..." }` vào mục Bible đã **khoá**. Khi lưu bản dịch, kiểm: câu gốc có thuật ngữ X mà câu dịch không chứa bản dịch đã khoá của X thì cảnh báo (không chặn). Dùng lại cơ chế `ConsistencyFinding` | 2 |
| 2 | **Xưng hô theo cặp nhân vật** (đặc thù tiếng Việt) | Tiếng Việt có tôi/cậu/anh/em/ta/ngươi…, sang `en` đều thành I/you. Ngược lại, sang `ja` cần chọn kính ngữ/đại từ. AI dịch hay sai chỗ này nhất | `ScriptLine` đã có người nói (`SpeakerId`). Thêm người nghe (`AddresseeId`, tuỳ chọn) và bảng "quan hệ xưng hô" trong Bible (A gọi B là gì, theo từng ngôn ngữ). Đưa vào prompt AI dịch | 2 |
| 3 | **Bộ nhớ dịch (translation memory)** | Câu lặp lại nhiều ("Cái gì?!", "Đi thôi!") hoặc câu hồi tưởng chép từ chương cũ thì không phải dịch lại | Tra theo `sourceTextHash`: nếu câu gốc giống hệt một câu đã `Approved` ở chương khác cùng Series, gợi ý sẵn bản dịch cũ (`origin = Memory`). Người vẫn phải bấm nhận | 2 |
| 4 | **Tự co chữ cho vừa bong bóng** | Bản dịch dài hơn gốc (vi→en thường dài thêm 10–30%) thì tràn bong bóng | Tìm `fontScale` lớn nhất (0.6–1.0) sao cho số ký tự ≤ `maxChars` theo ngôn ngữ, gợi ý cho người chọn. Dưới 0.6 vẫn tràn thì gắn cờ "cần rút gọn câu" | 2 |
| 5 | **Dịch ngược để kiểm tra nghĩa** | Người duyệt không biết tiếng Nhật vẫn kiểm tra được bản `ja` | AI dịch bản dịch ngược về tiếng Việt, so với câu gốc, câu nào lệch nghĩa nhiều thì tạo `ConsistencyFinding` mức cảnh báo. Không chặn duyệt | 2 |
| 6 | **Báo cho người dịch khi chữ gốc đổi** | Bản `Approved` mà chữ gốc bị sửa (BR-ML-07) thì người dịch không biết | Khi sửa element chữ của chương có bản dịch: gửi notification (hệ thống thông báo đã có) cho người đã dịch câu đó, kèm số câu bị cũ | 1 |
| 7 | **Bảng tiến độ dịch cho Producer** | Producer cần biết chương nào, ngôn ngữ nào sẵn sàng phát hành | Gom `TranslationSummaryDto` theo Series: lưới chương × ngôn ngữ, ô hiện % đúng bản và trạng thái duyệt | 1 |
| 8 | **Chữ hiệu ứng (SFX): ghi chú bên cạnh thay vì thay thế** | SFX vẽ thẳng vào tranh (ドドド, "RẦM") không xoá được, mà dịch đè lên thì xấu | Thêm loại element `SFX_GLOSS`: một nhãn nhỏ cạnh SFX gốc, chỉ hiện ở bản dịch. Bản gốc không thấy nhãn này | 3 |
| 9 | **Bấm vào bong bóng để xem chữ gốc** (trình đọc) | Đúng tinh thần "tăng tương tác" trong đề xuất của thầy: người học tiếng chạm vào để so bản gốc / bản dịch | Gói xuất đã có `text-regions.json` + nhiều `translations/*.json` theo cùng `elementId`, nên trình đọc chỉ cần đổi chữ hiển thị. Không cần thêm API | 3 |
| 10 | **Đọc thành tiếng (TTS) và chú giải từ vựng** | Hỗ trợ người học ngôn ngữ, người khiếm thị | Chữ đã tách khỏi ảnh và có người nói, nên sau này gắn giọng theo nhân vật. Chỉ ghi nhận hướng đi, chưa làm | Sau |
| 11 | **Thứ tự đọc theo ngôn ngữ** | Truyện gốc đọc phải → trái, bản tiếng Anh có thể muốn lật trái → phải | Không đảo ảnh. Chỉ lưu `readingDirection` trong manifest cho từng ngôn ngữ, mặc định lấy theo Series. Để trình đọc quyết định cách lật trang | 3 |

**Khuyến nghị:** GĐ1 làm thêm #6 và #7, vì rẻ và dùng lại thông báo / DTO có sẵn. GĐ2 ưu tiên #1 và #2: đây là chỗ PanelForge khác các công cụ dịch thông thường, vì đã có Series Bible và biết ai đang nói với ai.
