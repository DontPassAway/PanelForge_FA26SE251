# PanelForge

**An AI-assisted manga production management platform with a structured content model and full revision provenance.**

PanelForge manages manga, comic and webtoon production as **structured, versioned and traceable data** instead of a pile of loose files. A chapter is stored down to individual panels and elements (balloons, narration boxes, sound effects, character instances, artwork layers), moves through a configurable production pipeline, and every change, human or AI, leaves a permanent, attributable record.

> **Status:** in development (September 2026 – March 2027). Software Engineering Capstone Project. See [Current status](#9-current-status) for exactly what works today.

---

## Table of Contents

1. [The problem](#1-the-problem)
2. [What PanelForge does](#2-what-panelforge-does)
3. [Roles](#3-roles)
4. [How a page is produced](#4-how-a-page-is-produced)
5. [Core concepts and rules](#5-core-concepts-and-rules)
6. [AI assistance](#6-ai-assistance)
7. [Architecture and tech stack](#7-architecture-and-tech-stack)
8. [Getting started](#8-getting-started)
9. [Current status](#9-current-status)
10. [Documentation](#10-documentation)
11. [Project scope](#11-project-scope)
12. [Project information](#12-project-information)
13. [License](#13-license)

---

## 1. The problem

A distributed manga team typically works across a word processor (scripts), Photoshop or Clip Studio (artwork), spreadsheets (schedules), shared drives (assets) and chat (feedback). That fragmentation causes:

| Problem | Consequence |
|---|---|
| Scripts and artwork stored separately | The system cannot tell which dialogue belongs to which panel, who speaks it, or which character appears |
| Feedback scattered across tools | Comments become detached from the exact artwork version they refer to |
| No single source of truth for the story | Hair colours, names and plot facts drift over hundreds of chapters |
| Files overwritten or duplicated by hand | Nobody can reconstruct who changed what, when, or which version was approved |
| Tracking at file or page level only | Panel-level progress is invisible |
| AI used without records | No trace of which model, input and human edits produced a result |

---

## 2. What PanelForge does

### Structured content model

```text
Studio Workspace
  └── Series                (many per Workspace)
       └── Chapter
            └── Scene
                 └── Page
                      └── Panel
                           └── Element   (balloon, narration box, SFX, character, artwork layer)
```

Every entity belongs to exactly one parent; orphans are rejected. Scripts, panels, characters and dialogue keep explicit, machine-readable relationships, so you can ask "which line is in this balloon, and who says it?"

### Versioned Series Bible

A per-series repository of characters, locations, props, terminology, style rules and established plot facts. Every fact carries an **effective-from chapter**, so a check on chapter 3 uses the facts true at chapter 3, and a later change is stored as a new version rather than an overwrite. It is both a human reference and the grounding source for AI consistency checks. Details: [`docs/domain/series-bible.md`](docs/domain/series-bible.md).

### Configurable production pipeline

Each series gets its own pipeline, cloned from an admin-managed template:

```text
Script → Thumbnail → Pencil → Ink → Color → Letter → Review → Approved
Writer   Artist      Artist   Artist Artist  Letterer Editor   Editor
```

Stages, their order and the approver of each gate are configurable (a black-and-white series simply drops *Color*). The workflow supports role-based assignments, deadlines, dependencies, guarded stage transitions, approval gates, change requests and **panel-level progress**. Pages and panels progress independently and in parallel; within one page or panel, a stage starts only when the previous stage is approved.

### Panel-anchored review

Feedback is attached to a **region of a specific panel version**: comments, drawn annotations and change requests, with version comparison and approve/reject decisions. Every consistency finding ends as *Confirmed*, *Rejected*, *Ignored* (with a recorded reason) or *Needs clarification*.

### Immutable versioning and provenance

* Append-only history: stored revisions are never overwritten or deleted.
* Restoring an old version creates a **new** version at the head of history.
* Optimistic concurrency: an update based on a stale version is rejected and the conflict is surfaced, never silently overwritten.
* Full audit trail: who, when, what changed. AI operations also record model, parameters, prompt and input context, the accepting user and human modifications.
* Binary assets are content-addressed (SHA-256), which gives integrity checks and deduplication.

### Human-in-the-loop AI

AI proposes; people decide. See [AI assistance](#6-ai-assistance).

### Secure sharing and delivery

* **Preview links** for external reviewers: issued only for a *locked* chapter, bound to a named recipient email, time-limited, revocable, watermarked with the recipient, and every access (including denied attempts) is logged.
* **Public catalog** of *published* series for readers, kept completely separate from workspace and preview access.
* Export targets: print-ready images, PDF, CBZ, vertical-scroll webtoon, open JSON.

---

## 3. Roles

PanelForge has two levels of access. `SystemRole` (Administrator or User) only separates platform administration from ordinary accounts and **grants no content rights by itself**. `WorkspaceRole` is stored per user per Workspace and grants actions inside that Workspace; with no membership there is no access.

| Role | Level | Main responsibilities |
|---|---|---|
| **Administrator** | System | Accounts, permission to create Studios, AI provider/model/quota configuration, storage settings, shared templates, audit. Does not invite members and has no content access |
| **Producer / Series Owner** | Workspace | Creates series, configures the pipeline, invites members and assigns roles, assigns work, sets deadlines, locks and reopens chapters, issues preview links, publishes |
| **Writer** | Workspace | Structured scripts, scenes, dialogue, speakers, Bible links, AI script breakdown. Proposes scene/panel structure and pacing |
| **Artist** | Workspace | Owns the final page layout, panel size and position, camera angle, space for lettering; uploads artwork |
| **Letterer** | Workspace | Balloons, narration, SFX, typography, reading order; binds text to script lines |
| **Editor / Reviewer** | Workspace | Review, annotations, consistency checks, change requests, approves stage gates |
| **External Reviewer** | Link only | Views a locked chapter through a recipient-bound, watermarked link and sends feedback. No account |
| **Public Reader** | Separate account | Reads published chapters through the public catalog |

Only the Producer invites members. A user may create a Studio only if an Administrator has granted that permission; the creator automatically becomes its Producer.

A full walk-through of every role with a worked example: [`docs/roles/role-flow-examples.md`](docs/roles/role-flow-examples.md).

---

## 4. How a page is produced

1. **Setup.** An Administrator grants a user permission to create a Studio. The user creates the Workspace (and becomes Producer), then creates a Series; the pipeline is cloned from a template and an empty Series Bible is created. The Producer invites members and builds the Bible.
2. **Script.** The Writer writes structured script lines (optionally with AI breakdown). An Editor approves the *Script* gate.
3. **Layout and artwork.** The Artist fixes the layout, draws Thumbnail, Pencil, Ink and Color in an external tool, and uploads each version against the right page or panel. The Editor reviews with region-anchored comments and change requests; the Artist revises and resubmits.
4. **Lettering.** The Letterer places balloons on the canvas and binds each to one script line and speaker. The system warns about text overflow, overlap with key artwork and reading-order problems.
5. **Review and consistency.** The Editor runs the AI consistency check against the Series Bible and triages each finding, then approves the *Review* gate.
6. **Lock.** When all pages and panels are approved, the Producer approves and **locks** the chapter (read-only; reopening requires a recorded reason).
7. **Preview and publish.** The Producer sends recipient-bound preview links for the locked chapter, triages feedback (reopening if a fix is needed), then publishes and exports.

Three different "approvals" are deliberately kept apart: approving a **stage gate** (the item moves to the next stage), reaching the final **Approved** stage, and the Producer **approving and locking the chapter**.

---

## 5. Core concepts and rules

The project is governed by 24 business rules (BR-01 to BR-24). The most important ones:

| Rule | In short |
|---|---|
| BR-01 / BR-08 | History is append-only; restore creates a new version |
| BR-03 | Strict containment: Element → Panel → Page → Scene → Chapter → Series |
| BR-05 | One dialogue line binds to at most one balloon/text element |
| BR-06 / BR-19 / BR-20 | Guarded stage transitions; per-page/panel dependencies; gate approval conditions |
| BR-07 / BR-22 / BR-23 | Two-level access control; Studio creation permission; users only see their own Workspaces |
| BR-11 | Optimistic concurrency: stale updates are rejected |
| BR-12 | A locked chapter is read-only until reopened with a recorded reason |
| BR-15 | The Series Bible is versioned by effective chapter |
| BR-02 / BR-10 / BR-21 | AI only suggests; provenance is mandatory; every finding is triaged by a person |
| BR-04 / BR-13 / BR-16 | SHA-256 verified assets; deduplication; short-lived, logged asset access |
| BR-09 / BR-17 / BR-24 | Expiring, recipient-bound previews; public reading kept separate |
| BR-18 | Complete audit trail |

The full glossary and every rule, with plain-language explanations and examples (Vietnamese): [`docs/requirements/glossary-and-rules.md`](docs/requirements/glossary-and-rules.md).

---

## 6. AI assistance

AI is an assistant, never a decision maker:

```text
Request → Backend checks permissions and snapshots context
        → Supervisor picks the next specialist agent
        → Agent output is validated by code (AI service, then Backend)
        → Suggestion with full provenance
        → Human: Accept / Edit / Reject
```

* **Seven specialist agents:** Writer, Continuity, Storyboard, Artist, Letterer, Editor, Producer, coordinated by a Gemini-powered **Supervisor** that is not a role and cannot write content, approve, or move a stage.
* **Seven workflow types:** `FullScene`, `Script`, `Storyboard`, `Artist`, `Letterer`, `Editor`, `Producer`, each with its own valid agent chain and allowed roles.
* **Guardrails in code:** the set of allowed next actions is computed by deterministic rules; the model's choice is filtered, and the Backend re-validates everything. Numbers (percentages, character limits) are computed by code, and the model only returns short keys, never real IDs.
* **Accept does only what it says:** it creates a new content revision. It does not approve a gate, move a stage or lock a chapter.
* **Graceful failure:** if AI is disabled, slow or rate-limited, manual production keeps working.
* **Providers:** `Mock` (default, no API key, deterministic fixtures) or `Gemini`, configured per Workspace by an Administrator with quotas and usage records.

Design and demo guide: [`docs/ai/ai-multi-agent-demo.md`](docs/ai/ai-multi-agent-demo.md).

---

## 7. Architecture and tech stack

```text
┌────────────────────────────────────────────────────────────┐
│  Frontend (panelforge-fe): React 19, Vite, Tailwind        │
│  Script editor · Panel canvas · Dashboard · Review tools   │
└──────────────────────────────┬─────────────────────────────┘
                               │ REST + JWT
┌──────────────────────────────▼─────────────────────────────┐
│  PanelForge.API         controllers, auth, Swagger         │
├────────────────────────────────────────────────────────────┤
│  PanelForge.Application CQRS (MediatR): commands, queries  │
├────────────────────────────────────────────────────────────┤
│  PanelForge.Domain      entities, aggregates, rules, events│
├────────────────────────────────────────────────────────────┤
│  PanelForge.Infrastructure  EF Core, Marten, auth, email   │
└───────────┬──────────────────────────────┬─────────────────┘
            │                              │ HTTP (127.0.0.1)
   ┌────────▼─────────┐          ┌─────────▼──────────────────┐
   │ PostgreSQL 17    │          │ PanelForge.AI              │
   │ • relational     │          │ Supervisor + 7 agents,     │
   │   (EF Core)      │          │ Gemini or Mock; no direct  │
   │ • event store    │          │ database access            │
   │   (Marten)       │          └────────────────────────────┘
   └──────────────────┘          PanelForge.AI.Contracts (shared contract)
```

| Area | Technology |
|---|---|
| Backend | .NET 9, ASP.NET Core Web API |
| Architecture | Clean Architecture (API, Application, Domain, Infrastructure), CQRS with MediatR, `Result<T>` |
| Persistence | PostgreSQL 17; EF Core for relational data; **Marten** event store for the page canvas (event sourcing) |
| Auth | JWT with refresh tokens, OTP, Firebase identity verification |
| AI | Separate `PanelForge.AI` service (Gemini or Mock), shared `PanelForge.AI.Contracts` |
| Frontend | React 19, Vite, Tailwind, TanStack Query, dnd-kit (separate project `panelforge-fe`) |
| Testing | xUnit (backend); Vitest and Testing Library (frontend) |
| CI/CD | GitHub Actions (build and test against a PostgreSQL service container), Docker |

### Repository layout

```text
PanelForge_FA26SE251/
├── src/
│   ├── PanelForge.API              REST controllers, Swagger
│   ├── PanelForge.Application      use cases (commands/queries), interfaces
│   ├── PanelForge.Domain           entities, aggregates, domain events, enums
│   ├── PanelForge.Infrastructure   EF Core, Marten, migrations, seeders, integrations
│   ├── PanelForge.AI               multi-agent AI service
│   └── PanelForge.AI.Contracts     contracts shared by API and AI service
├── test/PanelForge.UnitTests       xUnit tests
├── docs/                           documentation (see below)
├── docker-compose.yml, Dockerfile
└── .github/workflows/ci.yml        CI pipeline
```

---

## 8. Getting started

### 8.1 Prerequisites

* .NET 9 SDK
* PostgreSQL 17, either installed locally or through Docker (Docker Desktop and Compose)
* Git
* Node.js (LTS) for the frontend

### 8.2 Clone and configure

```bash
git clone <repository-url>
cd PanelForge_FA26SE251
```

Create `src/PanelForge.API/appsettings.Development.json` from `appsettings.Development.Example.json` (or `appsettings.Example.json`) and choose a database target. Never commit secrets: keep JWT secrets, SMTP passwords, Firebase keys and AI service keys in environment variables or `dotnet user-secrets`.

```bash
dotnet user-secrets set "JwtSettings:Secret" "<at-least-32-characters>" --project src/PanelForge.API
```

Step-by-step database setup (Vietnamese): [`docs/database/setup-database.md`](docs/database/setup-database.md).

### 8.3 Start the database

```bash
docker compose up -d postgres-db
docker compose ps
```

The API applies EF Core migrations automatically on startup.

### 8.4 Run the backend

```bash
dotnet restore
dotnet run --project src/PanelForge.API --launch-profile http
```

Swagger UI: <http://localhost:5044/swagger>

### 8.5 Run the AI service (optional)

AI features are optional; manual production works without them. The default provider is `Mock` and needs no API key.

Use the same service key for both projects, then start the AI service on `127.0.0.1:5055`:

```bash
dotnet user-secrets set "AiService:ServiceKey" "<random-key>" --project src/PanelForge.API
dotnet user-secrets set "AiService:ServiceKey" "<random-key>" --project src/PanelForge.AI
dotnet run --project src/PanelForge.AI
```

To use Gemini, an Administrator configures the key and allowed models per Workspace, and the backend sets `AiService:Provider` to `Gemini`. For optional demo data (Development only), set `AiService__SeedDemoData=true`. See [`docs/ai/ai-multi-agent-demo.md`](docs/ai/ai-multi-agent-demo.md).

### 8.6 Run the frontend

The web client lives in the separate `panelforge-fe` project:

```bash
npm install
npm run dev        # http://localhost:3000
```

### 8.7 Run the tests

```bash
dotnet test PanelForge.slnx          # backend
npm run quality                      # frontend: typecheck, lint, tests
```

---

## 9. Current status

*As of 4 October 2026, based on reading the code.*

**Working today**

* Authentication (JWT, refresh tokens, OTP lockout, Firebase login), user and Studio-permission administration, audit logs
* Workspaces and members (Producer-only invitations), Series, Chapters, Scenes, Pages, Panels and Elements APIs
* Page canvas persisted as an append-only event stream (Marten)
* Per-series pipeline cloned from templates, with stage add/edit/delete/reorder and generated guarded transitions
* Series Bible with chapter-versioned revisions; per-series typography presets and consistency rules
* AI multi-agent service: seven agents, seven workflows, role/assignment/stage/lock checks, suggestions with provenance, accept/reject
* Read-only public catalog endpoints for published series

**In progress or planned**

* Assignment API (assign, submit, review, rework) and stage-transition endpoints, including dependencies and approval gates
* Asset storage: upload, content-addressed object storage, artwork versions, background removal, AI draft images
* Region-anchored annotations and change requests
* Recipient-bound preview links, reader accounts and subscriptions
* Notifications and activity feed
* Chapter approve / lock / reopen / publish and export (PDF, CBZ, webtoon, JSON)
* Production dashboard, burndown and workload

---

## 10. Documentation

Everything lives under [`docs/`](docs/README.md), organised by topic.

| Folder | What you will find |
|---|---|
| [`docs/requirements/`](docs/requirements/) | [Requirements](docs/requirements/requirements.md), the original [specification with BR/US/UC/FR/NFR](docs/requirements/PanelForge_Capstone_Content.md), and a [glossary and rules](docs/requirements/glossary-and-rules.md) |
| [`docs/roles/`](docs/roles/) | [Role workflows](docs/roles/role-workflows.md) and [worked examples for every role](docs/roles/role-flow-examples.md) |
| [`docs/domain/`](docs/domain/) | [Series Bible](docs/domain/series-bible.md) |
| [`docs/architecture/`](docs/architecture/) | [Clean architecture structure](docs/architecture/clean-architecture-structure.md), [implementation and QA](docs/architecture/implementation-qa.md) |
| [`docs/storage/`](docs/storage/) | [Asset storage architecture](docs/storage/storage-architecture.md) and [storage strategy](docs/storage/storage-strategy.md) |
| [`docs/ai/`](docs/ai/) | [AI multi-agent design and demo](docs/ai/ai-multi-agent-demo.md) |
| [`docs/database/`](docs/database/) | [Database setup](docs/database/setup-database.md) |
| [`docs/archive/`](docs/archive/) | History: merged change logs (CF1, CF4, CF5), the registration-form V1.2 scope and the old project plan. Current scope is spec V2.6 (`panelforge-fe/docs/PANELFORGE_MASTER_SPECIFICATION_V2_6.md`). |
| [`docs/research/`](docs/research/) | Research on roles and supporting tools |

Several documents are written in Vietnamese; the specification and project plan are in English.

**Business specification** for all three repositories: `panelforge-fe/docs/PANELFORGE_MASTER_SPECIFICATION_V2_6.md` (in the frontend repo). **Who is working on what:** `panelforge-fe/docs/TEAM_WORKBOARD.md`; update it when you start and finish a task, including backend work. Do not add plan files under `docs/`.

---

## 11. Project scope

**Mandatory MVP:** structured content model, versioned Series Bible, production pipeline, panel-anchored review, provenance subsystem, AI script-to-structure breakdown, grounded consistency checking.

**Extended scope:** AI layout suggestions, vision-based character continuity, balloon-fit analysis, background removal for uploaded artwork, human-reviewed AI draft images, richer real-time collaboration.

**Out of scope:** publishing AI-generated artwork without human review, replacing the artist's production stages with AI output, fully automated page generation, and using copyrighted commercial manga as demonstration material without permission. Demonstration content is self-authored, openly licensed, or used with written permission.

---

## 12. Project information

| Item | Information |
|---|---|
| Project name | PanelForge |
| English title | PanelForge: An AI-Assisted Manga Production Management Platform with a Structured Content Model and Full Revision Provenance |
| Vietnamese title | PanelForge: Nền tảng quản lý sản xuất truyện tranh Manga theo mô hình nội dung có cấu trúc, hỗ trợ cộng tác và truy vết phiên bản với AI |
| Project type | Software Engineering Capstone Project |
| Academic period | 09/2026 – 03/2027 |
| Supervisor | Nguyễn Tấn Phúc |
| Student | Bùi Ngọc Tâm |
| Student code | SE170009 |
| Repository | `PanelForge_FA26SE251` |

---

## 13. License

Academic Capstone Project, PanelForge Team.

Demonstration content must comply with the project's rights-clean content policy. No copyrighted commercial manga should be ingested without appropriate permission.
