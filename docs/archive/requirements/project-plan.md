# PanelForge - Project Plan & Scope

> **Đã thay thế (2026-11-12):** Phiếu đăng ký V1.2 không còn dùng làm căn cứ phạm vi. Phạm vi và nghiệp vụ hiện hành theo `panelforge-fe/docs/PANELFORGE_MASTER_SPECIFICATION_V2_6.md`; việc đang làm ghi ở `panelforge-fe/docs/TEAM_WORKBOARD.md`. Tài liệu này giữ làm lịch sử.

> **Updated to match the capstone registration form V1.2 (06/10/2026).** The mandatory MVP, extended scope, out-of-scope items and work-package split below follow that form. Acceptance values (timeout 30 seconds, precision and recall of at least 70%, similarity threshold 0.75, Task Success Rate of at least 80%) are proposals to be confirmed with the supervisor.

---

## Table of Contents

- [1. Project Scope](#1-project-scope)
- [2. Mandatory MVP](#2-mandatory-mvp)
- [3. Extended Scope](#3-extended-scope)
- [4. Out of Scope](#4-out-of-scope)
- [5. Work Packages](#5-work-packages)
- [6. Project Timeline](#6-project-timeline)
- [7. Expected Deliverables](#7-expected-deliverables)
- [8. Demonstration Dataset](#8-demonstration-dataset)
- [9. Testing and Evaluation](#9-testing-and-evaluation)
- [10. Project Success Criteria](#10-project-success-criteria)
- [11. Development Priorities](#11-development-priorities)
- [12. Future Expansion](#12-future-expansion)

---

# 1. Project Scope

PanelForge focuses on building a structured Manga production management platform rather than a general-purpose file manager.

The system combines:

```text
Structured Manga Model
        +
Series Bible
        +
Production Pipeline
        +
Panel Review
        +
Version Control
        +
Provenance
        +
AI Assistance
        +
Export
```

---

# 2. Mandatory MVP

The mandatory MVP consists of the following capabilities.

## 2.1 Structured Manga Content Model

The system must support:

```text
Studio Workspace
  ↓
Series (many per Workspace)
  ↓
Chapter ─┬─ Page ── Panel ── Element        (physical tree)
         └─ Scene ── Script block           (narrative structure)

Panel ──references──▶ Script block
```

A Scene does not contain Pages. A Page may hold Panels of different Scenes, and a Scene may span several Pages. A Scene is optional for manual work; the agent workflows operate on a Scene.

---

## 2.2 Versioned Series Bible

The MVP must support structured and versioned information about:

- Characters
- Locations
- Props
- Terminology
- Style rules
- Established plot facts

---

## 2.3 Production Pipeline

The system must support configurable workflow stages.

Example:

```text
Script
 → Thumbnail
 → Pencil
 → Ink
 → Color
 → Letter
 → Review
 → Approved
```

---

## 2.4 Panel-Anchored Review

The MVP must support:

- Panel-level review
- Region comments
- Annotations
- Change requests
- Approval
- Rejection

---

## 2.5 Provenance Subsystem

The system must record:

- User
- Timestamp
- Action
- Version
- Previous state
- AI metadata where applicable

---

## 2.6 Script-to-Structure Breakdown

The MVP should provide AI-assisted script breakdown into structured Manga content.

AI results must be reviewable by users.

---

## 2.7 Grounded Consistency Checking

The MVP should use Series Bible information to support consistency checking.

---

## 2.8 Workspace Governance

The Administrator grants and revokes the right to create Studio Workspaces and sets, for each Workspace, a storage limit (GB), a monthly AI token quota with AI enablement, an image-generation quota and a session policy. Limits are checked before the operation they govern, and a usage screen shows the Producer and the Administrator the amount used against each limit.

---

## 2.9 Multi-Agent AI Workflow

Role-based agents share one model through a single gateway: Writer, Continuity, Storyboard and Editor in an explicit workflow (at most two repair loops back to Writer), plus the Artist Agent for the character image flow. Agents only suggest; a human accepts, edits or rejects every output. In the first stage the Editor Agent only assembles findings and a checklist prepared by code. The AI provenance chain runs from Workflow Run to Agent Step, Suggestion, Human Decision and Content Revision. The Series Bible is read as versioned memory: the entries in the revision in force at the chapter are put into the agent context through a structured query, and vector retrieval is added only when that context exceeds the model context budget (threshold to be agreed with the supervisor).

---

## 2.10 Character Consistency and Character Image Pipeline

Locked character sheets (versioned per chapter), a three-layer consistency check (textual, rule-based by code, visual), and sketch-to-image generation demonstrated on at least one character, followed by cut-out of the character from a simple background and a similarity check against the reference sheet. Image generation sits behind a replaceable adapter and may use the API of a hosted provider other than Gemini.

---

## 2.11 Series Synopsis, Story Arcs and Chapter Outline

The Producer records the synopsis and story arcs of a Series; the Writer outlines each chapter and writes scene scripts from the outline.

---

## 2.12 Dialogue Shortening and Black-and-White Demonstration

Balloon-overflow warnings are computed by code, and the Letterer Agent shortens dialogue to fit a balloon within a length limit. The core demonstration is black-and-white manga.

---

# 3. Extended Scope

Features that may be implemented after the mandatory MVP include:

## 3.1 AI Layout Suggestions

AI-assisted panel planning and layout recommendations.

> V1.2: the Storyboard Agent (Pages and Panels with reading order and shot scale) is part of the core workflow. Only advanced layout optimisation remains extended.

## 3.2 Vision-Based Character Continuity

Similarity-based checking of character appearance across panels or chapters.

> V1.2: this is the visual layer of the three-layer character consistency check and belongs to the core (section 2.10).

## 3.3 Balloon-Fit Analysis

Automated detection of:

- Text overflow
- Artwork overlap
- Poor placement
- Reading-order issues

> V1.2: overflow, overlap and reading-order warnings are computed by code and belong to the core. Translation of dialogue into other languages within the balloon length limit is extended.

## 3.4 Advanced Collaboration

Potential future capabilities include:

- Real-time collaborative editing
- More advanced conflict resolution
- Live cursors
- Presence indicators

If real-time collaboration is too complex for the capstone scope, optimistic concurrency and explicit conflict detection remain acceptable alternatives.

## 3.5 Artwork Upload and Background Removal

An Artist uploads artwork produced in external tools and may remove the background of an uploaded image.

- The original asset is kept unchanged; the result is a new derived asset linked to its source.
- The Artist reviews the result before using it on a Panel.

See `requirements.md` section 5.9 and `storage-architecture.md`.

## 3.6 AI-Generated Draft Images

AI may generate draft or reference images for a Panel.

- Output is a Suggestion that a human must accept, edit, or reject.
- It is stored as a new artwork version with full provenance.
- It does not replace the Artist's Pencil, Ink, or Color work.

This is lower priority than the mandatory MVP and the AI features in sections 2.6 and 2.7.

> V1.2: generation of **character images** from a locked character sheet and a sketch is part of the core (section 2.10). Draft images for a whole Panel, a whole scene or a whole page remain extended or out of scope.

See `requirements.md` section 5.8.

---

# 4. Out of Scope

## 4.1 Autonomous Artwork Generation

PanelForge is not intended to become a generative artwork platform.

AI-generated images are allowed only as human-reviewed output (character images in the core, section 2.10; drafts, section 3.6). AI generates character images only; backgrounds, page layout and balloons are assembled in the editor from structured Elements. The following remain out of scope:

- Publishing AI-generated artwork without human review
- Replacing the Artist's production stages with AI output
- Fully automated page or chapter generation
- Generating a whole scene with characters and background

AI primarily focuses on:

- Text
- Structure
- Metadata
- Consistency
- Production assistance
- Draft images (human-reviewed)

---

## 4.2 Copyrighted Commercial Manga

Commercial Manga must not be used as demonstration material without appropriate permission.

Demonstration data should be:

- Self-authored
- Openly licensed
- Provided with written permission

---

## 4.3 AI as Final Decision Maker

AI must not automatically approve production content.

Human review remains part of the production workflow.

---

## 4.4 Publication, Free Drawing and Unsupported Structures

- **Publication** takes place outside the platform. PanelForge prepares chapter versions and an export package; a publisher is an external reviewer who gives feedback through a time-limited preview link. The Producer has no relationship with a publisher. (The existing Public Reader and public catalog, BR-23 and BR-24, go beyond the registration form. The team keeps them as an extra feature that is outside the registered scope and not part of acceptance.)
- **No free-drawing tool.** Artists draw in external software and upload their work.
- **Not supported:** interleaved scenes, an infinite vertical-scroll webtoon editing canvas (pages built in the editor can still be exported as vertical-scroll images), and free-form panels whose reading order cannot be inferred.

---

# 5. Work Packages

The project is divided into five major work packages.

---

## WP1 — Project Management, Domain Analysis and Content Schema

### Primary Focus

```text
Project Management
Domain Analysis
Requirements
Content Schema
```

### Responsibilities

- Project scope
- Requirements
- Manga production workflow analysis
- User roles
- Review practices
- Content hierarchy (Workspace, Series, Chapter, the Page/Panel/Element tree, and the Scene/Script block structure)
- Series Bible structure
- Business rules
- Validation rules
- Architecture planning
- Event-store design
- Workspace governance rules and agent specifications
- Test plan, system-level and acceptance testing (developers write the unit and integration tests of their own packages)
- Usability evaluation (protocol and execution with target users)
- Documentation
- Demonstration dataset
- Final presentation package

---

## WP2 — Content Domain, Provenance and Version Control

### Primary Focus

```text
Domain
Versioning
Provenance
Persistence
```

### Responsibilities

- Series aggregate
- Chapter aggregate
- Scene aggregate
- Script block
- Page aggregate
- Panel aggregate
- Element aggregate
- Series Bible
- Domain events
- Event store
- Read projections
- Content-addressed storage
- Deduplication
- Version history
- Diff
- Rollback
- AI provenance (workflow run, agent step, suggestion, decision, content revision)
- Schema migration
- Rendering and export pipeline (page images, PDF, CBZ, vertical-scroll images, structured JSON), once the content domain has stabilised

---

## WP3 — Production Pipeline, Access Control and Deployment

### Primary Focus

```text
Workflow
Authorization
Notifications
Deployment
```

### Responsibilities

- Workflow engine
- State machine
- Assignments
- Deadlines
- Dependencies
- Change requests
- Approval workflow
- Notifications
- Activity feed
- RBAC
- ABAC
- External reviewer access
- Asset access control
- Audit trail
- Workspace limit enforcement (storage, AI quota, session policy) and authorisation of Workspace creation
- Preview links for external reviewers
- Containerization
- CI/CD
- Cloud deployment
- Monitoring
- Backup

---

## WP4 — Structured Editors and Production Workspace

### Primary Focus

```text
Frontend Workspace
Creative Editors
Review Interface
```

### Responsibilities

- Structured script editor
- Panel canvas
- Element editor
- Thumbnail planner
- Page layout planner
- Lettering canvas
- Annotation layer
- Version comparison
- Producer dashboard
- Panel-level progress
- Personal task queue
- Reading mode
- Preview mode
- Workspace usage screen
- Character sheet and frame template screens

---

## WP5 — AI Assistance Services and AI Evaluation

### Primary Focus

```text
AI
Agents
Image pipeline
Evaluation
```

### Responsibilities

- Multi-agent AI service: Writer, Continuity, Storyboard and Editor workflow with bounded repair loops, plus the Artist Agent and the Letterer Agent (dialogue shortening)
- Single LLM gateway with quota, usage and provider fallback
- Series Bible memory (entries in the revision in force at the chapter, read through a structured query; vector retrieval only when the context exceeds the model context budget) and grounded consistency checking
- Character image pipeline (locked sheets, sketch-to-image through a replaceable adapter, cut-out, visual consistency check)
- Suggestion review boundary with provenance capture
- AI suggestion review screen and AI provenance view
- Evaluation of the agents on a seeded-defect dataset against a single-prompt baseline
- Balloon-fit analysis (deterministic checks by code)
- Extended scope: translation of dialogue, Producer Agent, whole-page generation

---

# 6. Project Timeline

The project duration is:

```text
September 2026
        ↓
Domain Analysis
        ↓
Content Schema
        ↓
Provenance Foundation
        ↓
Backend Development
        ↓
Frontend Development
        ↓
Production Workflow
        ↓
Collaboration
        ↓
AI Assistance
        ↓
Testing
        ↓
Deployment
        ↓
Pilot Evaluation
        ↓
Final Demonstration
        ↓
March 2027
```

---

## Phase 1 — Domain Analysis

Focus:

- Requirements
- User roles
- Manga production workflow
- Domain terminology
- Business rules
- Content hierarchy

Deliverables:

- Requirements
- Domain model
- Business rules
- Initial architecture

---

## Phase 2 — Schema and Provenance

Focus:

- Structured Manga schema
- Aggregate boundaries
- Event model
- Versioning
- Provenance

Deliverables:

- JSON schema
- Domain model
- Event definitions
- Persistence design

---

## Phase 3 — Core Platform

Focus:

- Authentication
- Users
- Series
- Chapters
- Scenes
- Pages
- Panels
- Elements
- Series Bible

Deliverables:

- Backend APIs
- Database
- Core frontend

---

## Phase 4 — Production Workflow

Focus:

- Workflow
- Assignments
- Deadlines
- Reviews
- Change requests
- Approval

Deliverables:

- Workflow engine
- Producer dashboard
- Review system

---

## Phase 5 — AI Assistance

Focus:

- Script breakdown
- Series Bible retrieval
- Consistency checking
- AI provenance
- Human-in-the-loop workflow

Deliverables:

- AI service
- AI integration
- Provenance records

---

## Phase 6 — QA and Deployment

Focus:

- Automated testing
- Security
- Concurrency
- CI/CD
- Docker
- Deployment
- Monitoring

Deliverables:

- Test reports
- Deployment environment
- CI/CD pipeline

---

# 7. Expected Deliverables

## 7.1 Web Platform

A deployed PanelForge platform supporting:

- Series management
- Structured content
- Production workflow
- Review
- Version history
- Export

---

## 7.2 Backend API

A documented backend providing:

- REST API or equivalent
- Domain operations
- Event persistence
- Read projections
- Authentication
- Authorization
- Provenance

---

## 7.3 AI Service

A separately manageable AI assistance service supporting:

- Script breakdown
- Series Bible consistency checking
- Character continuity
- Multi-agent workflow (Writer, Continuity, Storyboard, Editor, Artist; Letterer for dialogue shortening)
- Character image pipeline (sketch-to-image generation, cut-out, visual consistency check)
- AI provenance

---

## 7.4 Open Manga Schema

A documented JSON schema supporting:

- Structured Manga representation
- Export
- Import
- Schema versioning

---

## 7.5 Documentation Package

Expected documentation:

```text
SRS
SDD
Database Design
Event Store Design
API Documentation
Test Plan
Test Report
AI Evaluation Report
Deployment Guide
User Manual
```

The user manual should support Vietnamese and English where required.

---

## 7.6 Demonstration Dataset

The project should provide at least:

```text
3 fully structured Manga chapters
```

with production history, including a locked character sheet with images generated from the artist's sketches, AI-assisted steps with their provenance, and at least one chapter version created from feedback.

---

## 7.7 Final Presentation Package

Expected final package:

- Demo video
- Slide deck
- Source code repository
- Build instructions
- Deployment instructions

---

# 8. Demonstration Dataset

The demonstration dataset must be rights-clean.

Acceptable sources include:

- Self-authored content
- Openly licensed content
- Content provided with written permission

Commercial copyrighted Manga should not be used without appropriate authorization.

---

## Dataset Requirements

The dataset should demonstrate:

```text
Series
 ↓
Chapters ─┬─ Pages ── Panels ── Elements
          └─ Scenes ── Script blocks
```

It should also demonstrate:

- Version history
- Production stages
- Assignments
- Reviews
- Change requests
- AI assistance
- Provenance
- Export

---

# 9. Testing and Evaluation

The project should evaluate both technical correctness and practical usability.

---

## 9.1 Technical Evaluation

Evaluate:

- Domain correctness
- Versioning
- Provenance
- Workflow transitions
- Authorization
- Concurrency
- API correctness
- AI failure handling
- Export correctness

---

## 9.2 Usability Evaluation

Usability is judged by a Task Success Rate of at least 80% on the main flows (proposed and to be confirmed with the supervisor).

Potential evaluation participants include:

- Comic club members
- Illustration students
- Independent webtoon creators

Evaluation areas:

- Script editing
- Panel planning
- Review
- Production tracking
- Series Bible usage
- AI assistance
- Version comparison

---

# 10. Project Success Criteria

PanelForge should be considered successful when it demonstrates the following.

## Content

- Manga is represented as structured data.
- Hierarchical relationships are enforced.
- Individual elements can be queried.

## Production

- Tasks can be assigned.
- Deadlines can be tracked.
- Panel-level progress can be monitored.
- Workflow transitions are controlled.

## Collaboration

- Reviews are attached to specific panel versions.
- Change requests can be tracked.
- Approval gates work correctly.

## Versioning

- Previous versions remain accessible.
- Changes can be compared.
- Previous states can be restored.

## Provenance

- Human changes are traceable.
- AI operations are traceable.
- AI model information is retained.
- Human modification is recorded.

## AI

- Script breakdown works.
- Consistency checking uses Series Bible grounding.
- Human review is mandatory.
- AI failure does not block manual work.
- Character consistency is measured on a seeded-defect set (precision and recall) and by a similarity score against the reference sheet; deviations are surfaced, never hidden.
- Agents are evaluated against a single-prompt baseline.

## Export

- Structured JSON can be exported.
- Standard Manga delivery formats can be generated.

---

# 11. Development Priorities

The project should prioritize foundational architecture before advanced features.

Recommended order:

```text
1. Domain Analysis
        ↓
2. Content Schema
        ↓
3. Business Rules
        ↓
4. Provenance Model
        ↓
5. Persistence
        ↓
6. Core APIs
        ↓
7. Production Workflow
        ↓
8. Frontend Workspace
        ↓
9. AI Assistance
        ↓
10. Advanced Features
        ↓
11. QA
        ↓
12. Deployment
```

---

## Why Schema Comes First

The content schema determines how the system stores:

- Series
- Chapters
- Scenes
- Pages
- Panels
- Elements
- Relationships

Changing the schema late can affect:

- Database
- Domain model
- API
- Frontend
- Event store
- AI grounding
- Export

---

## Why Provenance Comes Early

Provenance should not be added as a final feature.

The system must be designed from the beginning to preserve:

```text
Who
When
What
Version
AI involvement
Human modification
```

This is especially important because immutable revision history and AI provenance are fundamental requirements of the project.

---

# 12. Future Expansion

Potential future development includes:

- Advanced real-time collaboration
- More sophisticated conflict resolution
- Advanced AI layout planning
- Vision-based continuity analysis
- Improved balloon-fit analysis
- Additional export formats
- More studio workflow templates
- Advanced provenance standards
- Additional creative-tool integrations
- Broader open-schema interoperability

Future features should not compromise the core principles of:

```text
Structured Data
Traceability
Human Control
Version Integrity
Portability
```

---

# Project Planning Summary

```text
PanelForge
│
├── WP1: Domain & Schema
│
├── WP2: Domain & Provenance
│
├── WP3: Workflow & Access Control
│
├── WP4: Production Workspace
│
└── WP5: AI, QA & Deployment
```

The first development priority is to establish the structured content model and provenance architecture because these two foundations influence the persistence design and the rest of the platform.