# PanelForge

An AI-assisted manga and webtoon production management platform with a structured content model and full revision provenance.

- **What it is:** A structured workflow system replacing fragmented manga production tools with granular content tracking, versioned Series Bibles, and human-in-the-loop AI assistance.
- **Quick start:** Run `docker compose up -d postgres-db` then `dotnet run --project src/PanelForge.API --launch-profile http-docker` ([details](#getting-started)).
- **Documentation:** Full specifications and guides are in [`docs/`](docs/README.md).

---

## Overview

Traditional manga production fragments scripts, artwork, and communication across disparate tools. PanelForge unifies production through structured data and traceable collaboration:

- **Structured Content Model:** `Series → Chapter → Page → Panel → Content` (a hidden default Scene per chapter, D2), plain-text script lines bound 1-1 to balloons (BR-05).
- **Script lifecycle:** `Draft ↔ InReview → Frozen` with immutable script versions (save = commit, submit = push); Producer starts production at Gate 1 (Q15).
- **Chapter-scoped Series Bible:** speakers auto-match Bible names/aliases (unknown speakers become draft entries), narration references are highlighted by the Writer, designs are locked as Canon variants and frozen per chapter at Gate 1 (Q16, BR-35).
- **Page-scoped production:** Layout + Pencil, then Ink ∥ Lettering per assigned page, panel-level rework at Gate 2, chapter approval at Gate 3 (BR-12, BR-19, BR-31).
- **Immutable Provenance:** Append-only revisions (BR-01, BR-08), optimistic concurrency (BR-11), content-addressed SHA-256 assets (BR-04, BR-13), mandatory audit logging (BR-18).
- **Release & monetization:** language editions (`ChapterEdition`), one platform Publisher sets VND prices, Producer publishes (BR-32–34).
- **Human-in-the-Loop AI:** Seven specialist agents coordinated by a Supervisor suggest actions with recorded provenance; human approval is mandatory (BR-02, BR-10). See [`docs/ai/ai-multi-agent-demo.md`](docs/ai/ai-multi-agent-demo.md).

Business specification, plans and architecture live only in `panelforge-hub/docs/` (start with `V2.6/PANELFORGE_MASTER_SPECIFICATION_V2_6.md`).

---

## Architecture

Clean Architecture adhering to strict dependency flow (`API → Application → Domain`, `Infrastructure → Application/Domain`):

| Layer / Component | Technology & Responsibility |
|---|---|
| **PanelForge.API** | ASP.NET Core Web API (.NET 9), REST controllers, JWT/Firebase auth, Swagger |
| **PanelForge.Application** | CQRS with MediatR, use cases (Commands/Queries), `Result<T>` |
| **PanelForge.Domain** | Entities, aggregates, domain events, business invariants (zero dependencies) |
| **PanelForge.Infrastructure** | EF Core (relational data), Marten (event-sourced page canvas), external integrations |
| **PanelForge.AI** | Multi-agent service (`http://127.0.0.1:5055`), Gemini/Mock providers; no direct DB access |
| **PanelForge.AI.Contracts** | Shared contracts between API and AI service |
| **Panel Splitter** | In-process splitting via OpenCvSharp4 (`PanelForge.PanelSplitter` is parity reference only) |

Full architectural guide: [`docs/architecture/clean-architecture-structure.md`](../panelforge-hub/docs/architecture/clean-architecture-structure.md).

---

## Getting Started

### 1. Prerequisites
- .NET SDK 9.x
- Docker Desktop (or local PostgreSQL 17)
- Node.js LTS 20+ (for [`panelforge-fe`](../panelforge-fe/README.md))

### 2. Configuration
```bash
cp src/PanelForge.API/appsettings.Development.Example.json src/PanelForge.API/appsettings.Development.json
```
Edit `appsettings.Development.json`:
- `Database:Target`: `Docker` uses `ConnectionStrings:PostgreSQLDocker`; `Local` uses `ConnectionStrings:PostgreSQL`.
- `JwtSettings:Secret`: Minimum 32 characters (or set via `dotnet user-secrets`).

### 3. Start Database
```bash
docker compose up -d postgres-db
```
PostgreSQL 17 runs on port `5432` (user `postgres`, password `12345`, db `panelforge`). If port 5432 is occupied, use `docker-compose.override.yml` to map `5433:5432`.

### 4. Run API
```bash
dotnet run --project src/PanelForge.API --launch-profile http-docker   # or http-local
```
- API: `http://localhost:5044` | Swagger: `http://localhost:5044/swagger`
- Applies EF Core migrations and seeds master data automatically on startup.
- CORS allows `http://localhost:3000`, `5173`, and `5174`.

### 5. Run AI Service (Optional)
Manual production operates without AI. Default provider is `Mock` (no key required):
```bash
dotnet user-secrets set "AiService:ServiceKey" "<shared-key>" --project src/PanelForge.API
dotnet user-secrets set "AiService:ServiceKey" "<shared-key>" --project src/PanelForge.AI
dotnet run --project src/PanelForge.AI   # http://127.0.0.1:5055
```
For Gemini, set `AiService:Provider=Gemini` and configure Workspace keys. Demo data: `AiService__SeedDemoData=true`.

### 6. Tests & Frontend
- Tests: `dotnet test PanelForge.slnx`
- Frontend: See [Frontend README](../panelforge-fe/README.md) (`npm run dev` at `http://localhost:3000`).

---

## Troubleshooting

| Symptom | Resolution |
|---|---|
| `Connection string 'PostgreSQLDocker' chưa được cấu hình` | `appsettings.Development.json` missing or target connection string unset |
| Cannot connect to database | Verify `docker compose ps` and that port (5432/5433) matches connection string |
| Port 5044 already in use | Stop active API process (`Get-Process PanelForge.API \| Stop-Process`) |
| Browser shows CORS errors | Run frontend on allowed origin (`http://localhost:3000`, `5173`, or `5174`) |
| AI calls return 401/403 | Ensure `AiService:ServiceKey` matches between API and AI projects |

---

## Documentation

Business specification, plans, standards and architecture: **`panelforge-hub/docs/` only**. This repo keeps operational docs under [`docs/`](docs/README.md):

| Folder | What you will find |
|---|---|
| [`docs/database/`](docs/database/) | [Database setup](docs/database/setup-database.md) |
| [`docs/setup/`](docs/setup/) | [Cloudflare R2 setup](docs/setup/R2_STORAGE_SETUP.md) |
| [`docs/ai/`](docs/ai/) | [AI multi-agent demo](docs/ai/ai-multi-agent-demo.md) |
| [`docs/research/`](docs/research/) | Background research on manga production (reference only) |



**Business specification** for all three repositories: `panelforge-hub/docs/V2.6/PANELFORGE_MASTER_SPECIFICATION_V2_6.md` (in the `panelforge-hub` repo). **Who is working on what:** `panelforge-hub/docs/tracking/TEAM_WORKBOARD.md`; update it when you start and finish a task, including backend work. Do not add plan files under `docs/`.

---

## Current Status

- **Working:** JWT/OTP/Firebase auth, studio admin, workspaces/members, series-to-element hierarchy, Marten event-sourced canvas, pipeline templates & transitions, versioned Series Bible, AI multi-agent service (7 agents/workflows, provenance), read-only public catalog.
- **In Progress / Planned:** Assignment workflows & stage gates, content-addressed asset storage, region-anchored annotations, recipient-bound preview links, chapter lock/publish/export (PDF, CBZ, webtoon), workload dashboards.

---

## Project Information

| Item | Information |
|---|---|
| Project name | PanelForge |
| English title | PanelForge: An AI-Assisted Manga Production Management Platform with a Structured Content Model and Full Revision Provenance |
| Vietnamese title | PanelForge: Nền tảng quản lý sản xuất truyện tranh Manga theo mô hình nội dung có cấu trúc, hỗ trợ cộng tác và truy vết phiên bản với AI |
| Project type | Software Engineering Capstone Project (09/2026 – 03/2027) |
| Supervisor | Nguyễn Tấn Phúc |
| Students | Bùi Ngọc Tâm<br>Huỳnh Công Hoà<br>Nguyễn Tô Trung Kiên<br>Trần Huỳnh Bảo Minh<br>Phạm Thành Danh |
| Repository | `PanelForge_FA26SE251` |
| License | Academic Capstone Project, PanelForge Team (rights-clean content policy) |
