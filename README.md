# PanelForge_FA26SE251

An AI-Assisted Manga Production Management Platform with a Structured Content Model and Full Revision Provenance.

---

## Table of Contents

- [1. Project Overview](#1-project-overview)
- [2. Project Information](#2-project-information)
- [3. Problem Statement](#3-problem-statement)
- [4. Project Objectives](#4-project-objectives)
- [5. Proposed Solution](#5-proposed-solution)
- [6. Core Features](#6-core-features)
  - [6.1 Structured Manga Content Model](#61-structured-manga-content-model)
  - [6.2 Versioned Series Bible](#62-versioned-series-bible)
  - [6.3 Collaborative Production Pipeline](#63-collaborative-production-pipeline)
  - [6.4 Panel-Anchored Review](#64-panel-anchored-review)
  - [6.5 AI Assistance](#65-ai-assistance)
  - [6.6 Provenance and Version Control](#66-provenance-and-version-control)
  - [6.7 Export and Delivery](#67-export-and-delivery)
- [7. User Roles](#7-user-roles)
- [8. Structured Manga Content Model](#8-structured-manga-content-model)
- [9. Series Bible](#9-series-bible)
- [10. Production Workflow](#10-production-workflow)
- [11. AI Assistance Architecture](#11-ai-assistance-architecture)
- [12. Version Control and Provenance](#12-version-control-and-provenance)
- [13. Collaboration and Concurrency](#13-collaboration-and-concurrency)
- [14. Access Control and Security](#14-access-control-and-security)
- [15. Functional Requirements](#15-functional-requirements)
- [16. Non-Functional Requirements](#16-non-functional-requirements)
- [17. System Architecture](#17-system-architecture)
- [18. Technical Foundations](#18-technical-foundations)
  - [18.1 Domain-Driven Design](#181-domain-driven-design)
  - [18.2 Event Sourcing and CQRS](#182-event-sourcing-and-cqrs)
  - [18.3 Immutability and Temporal Data](#183-immutability-and-temporal-data)
  - [18.4 Content-Addressed Storage](#184-content-addressed-storage)
  - [18.5 Workflow and State Machines](#185-workflow-and-state-machines)
  - [18.6 Concurrency Control](#186-concurrency-control)
  - [18.7 Schema Design and Evolution](#187-schema-design-and-evolution)
  - [18.8 AI Service Architecture](#188-ai-service-architecture)
  - [18.9 Provenance and Audit](#189-provenance-and-audit)
  - [18.10 Access Control Models](#1810-access-control-models)
  - [18.11 Modular Software Architecture](#1811-modular-software-architecture)
- [19. Practical Implementation](#19-practical-implementation)
- [20. Testing and Quality Assurance](#20-testing-and-quality-assurance)
- [21. Deployment and CI/CD](#21-deployment-and-cicd)
- [22. Project Scope](#22-project-scope)
  - [22.1 Mandatory MVP Scope](#221-mandatory-mvp-scope)
  - [22.2 Extended Scope](#222-extended-scope)
  - [22.3 Out of Scope](#223-out-of-scope)
- [23. Work Packages](#23-work-packages)
- [24. Project Timeline](#24-project-timeline)
- [25. Demonstration Dataset](#25-demonstration-dataset)
- [26. Expected Deliverables](#26-expected-deliverables)
- [27. Project Success Criteria](#27-project-success-criteria)
- [28. Repository Structure](#28-repository-structure)
- [29. Getting Started](#29-getting-started)
- [30. Documentation](#30-documentation)
- [31. Development Principles](#31-development-principles)
- [32. What Makes PanelForge Different](#32-what-makes-panelforge-different)
- [33. Future Expansion](#33-future-expansion)
- [34. Project Team](#34-project-team)
- [35. Academic Context](#35-academic-context)
- [36. License](#36-license)

---

## 1. Project Overview

PanelForge is an AI-assisted Manga production management platform designed to manage Manga, comic and webtoon production as structured, versioned and traceable data rather than as a collection of independent files.

The platform is designed for distributed creative teams consisting of writers, artists, letterers, editors and producers who work collaboratively on the same series and chapters.

PanelForge combines:

- Structured Manga content modelling
- Collaborative production management
- Version control and revision history
- Series-wide continuity management
- Panel-level review and feedback
- AI-assisted production support
- AI provenance tracking
- Structured export and delivery

The central concept of PanelForge is that a Manga chapter should be represented as **queryable structured data**, down to individual panels and elements such as dialogue balloons, narration boxes and sound effects.

---

## 2. Project Information

| Item | Information |
|---|---|
| Project Name | PanelForge |
| English Title | PanelForge: An AI-Assisted Manga Production Management Platform with a Structured Content Model and Full Revision Provenance |
| Vietnamese Title | PanelForge: Nền tảng quản lý sản xuất truyện tranh Manga theo mô hình nội dung có cấu trúc, hỗ trợ cộng tác và truy vết phiên bản với AI |
| Project Type | Capstone Project |
| Profession | Software Engineer |
| Specialty | Software Engineering |
| Duration | 09/2026 – 03/2027 |
| Supervisor | Nguyễn Tấn Phúc |
| Student | Bùi Ngọc Tâm |
| Student Code | SE170009 |

The project is registered as a Software Engineering capstone project for the period from September 2026 to March 2027.

---

## 3. Problem Statement

Modern Manga, comic and webtoon production is often distributed across multiple creative roles.

A typical team may include:

- Writer
- Artist
- Letterer
- Editor
- Producer or Series Owner

However, the production process is commonly supported by a fragmented collection of general-purpose tools.

For example:

- Scripts are stored in word processors.
- Artwork is stored in Photoshop or Clip Studio files.
- Schedules are managed using spreadsheets.
- Feedback is exchanged through chat.
- Files are stored in shared drives or cloud storage.

This approach creates several major problems.

### 3.1 Unstructured Representation

A chapter is commonly represented as flat images together with a separate script.

There is no machine-readable relationship between:

```text
Script Line
    ↓
Speaker
    ↓
Panel
    ↓
Dialogue Balloon
    ↓
Artwork
```

As a result, the system cannot reliably query, validate or automatically reason about the content.

### 3.2 Fragmented Collaboration

Assignments, versions, feedback and approvals are distributed across different tools.

Feedback can become detached from the exact artwork or version it refers to, causing confusion and unnecessary rework.

### 3.3 Continuity Errors

Long-running series must maintain consistency in:

- Character appearance
- Costumes
- Locations
- Terminology
- Honorifics
- Established facts
- Style rules

Detecting inconsistencies manually requires editors to remember information across hundreds of pages.

### 3.4 Loss of Revision History

Artwork may be overwritten or duplicated using manually created file names.

This makes it difficult to:

- Recover previous states
- Compare revisions
- Identify who changed something
- Determine when a change happened

### 3.5 Invisible Production Status

Production progress is frequently tracked at file level rather than panel level.

As a result, producers may not know which exact panels are blocking the chapter until the deadline is already at risk.

### 3.6 Missing AI Provenance

AI-assisted production introduces an additional requirement.

The system needs to know:

- Which AI model was used
- What input was provided
- What output was generated
- Which user accepted the output
- How much the output was subsequently modified by a human

Without this information, AI-assisted content creates rights, disclosure and quality-assurance risks.

The project proposal identifies these problems as the primary motivation for PanelForge.

---

## 4. Project Objectives

The main objective of PanelForge is to build a production management platform that represents Manga as structured and versioned data.

The project aims to:

1. Model Manga content using a strict hierarchical structure.
2. Connect scripts, panels, characters and visual elements through machine-readable relationships.
3. Provide a versioned Series Bible for maintaining continuity.
4. Provide a configurable production workflow for distributed creative teams.
5. Enable panel-level production tracking.
6. Bind reviews and comments directly to specific panel versions.
7. Provide AI-assisted production features without making AI responsible for final decisions.
8. Record complete provenance for AI-assisted changes.
9. Preserve complete revision history through immutable versioning.
10. Support comparison and rollback of previous versions.
11. Provide structured export formats for production and delivery.
12. Maintain a clear separation between creative content and AI-generated assistance.

---

## 5. Proposed Solution

PanelForge treats a Manga title as a structured, versioned data model rather than a collection of files.

The platform is organized around three primary pillars:

### Pillar 1: Structured Representation

The Manga is represented using an explicit hierarchy:

```text
Series
  └── Chapter
       └── Scene
            └── Page
                 └── Panel
                      └── Element
```

Each element can contain structured metadata, relationships and positioning information.

### Pillar 2: Production Pipeline

Pages and panels move through configurable production stages such as:

```text
Script
  ↓
Thumbnail
  ↓
Pencil
  ↓
Ink
  ↓
Color
  ↓
Letter
  ↓
Review
  ↓
Approved
```

The workflow supports assignments, deadlines, dependencies and progress tracking.

### Pillar 3: AI Assistance and Provenance

AI is used as an assistant rather than an autonomous creator.

AI features generate suggestions that must be:

```text
AI Suggestion
      ↓
Human Review
      ↓
Accept / Edit / Reject
      ↓
Provenance Record
```

This preserves human control while maintaining traceability of AI-assisted work.

---

# 6. Core Features

## 6.1 Structured Manga Content Model

PanelForge provides a strict hierarchy:

```text
Series
 └── Chapter
      └── Scene
           └── Page
                └── Panel
                     └── Element
```

An `Element` represents a typed unit within a panel.

Examples include:

- Dialogue balloon
- Narration box
- Sound effect
- Character instance
- Artwork layer

The structured model allows the system to understand relationships between content instead of treating everything as independent files.

---

## 6.2 Versioned Series Bible

The Series Bible is a structured repository containing series-level knowledge.

It can contain:

- Characters
- Locations
- Props
- Terminology
- Style rules
- Established plot facts

The Series Bible is versioned per chapter and serves two purposes:

1. Human reference for the production team.
2. Grounding source for automated consistency checking.

This allows AI assistance to reason about the established facts of a series instead of relying only on isolated prompts.

---

## 6.3 Collaborative Production Pipeline

PanelForge provides a configurable production workflow.

Example workflow:

```text
Script
  ↓
Thumbnail
  ↓
Pencil
  ↓
Ink
  ↓
Color
  ↓
Letter
  ↓
Review
  ↓
Approved
```

The pipeline supports:

- Role-based assignments
- Deadlines
- Dependencies
- Stage transitions
- Panel-level progress
- Workload monitoring
- Overdue tracking
- Approval gates

The exact pipeline can be configured for different studios.

---

## 6.4 Panel-Anchored Review

Review comments are attached directly to the exact region of a specific panel version.

Instead of:

```text
Chat:
"Please fix the character's hand."
```

PanelForge provides:

```text
Panel Version
     ↓
Specific Region
     ↓
Comment / Annotation
     ↓
Change Request
     ↓
Resolution
```

Reviewers can:

- Add region-based comments
- Draw annotations
- Create change requests
- Compare versions
- Approve submissions
- Reject submissions

This ensures feedback remains permanently associated with the artifact and version it concerns.

---

## 6.5 AI Assistance

PanelForge provides AI assistance for production tasks.

### Script-to-Structure Breakdown

The AI can analyze a structured script and suggest:

```text
Script
  ↓
Scenes
  ↓
Pages
  ↓
Panels
```

The writer can then review, edit or reject the result.

### Panel Planning and Layout Suggestion

AI can assist with panel planning and layout suggestions.

### Continuity Checking

The system can compare content against the Series Bible to identify potential inconsistencies.

Potential checks include:

- Character continuity
- Terminology
- Established facts
- Style rules

### Character Continuity

Vision-based analysis may be used for similarity-based continuity checking.

### Balloon-Fit Analysis

The system can assist with identifying problems such as:

- Text overflow
- Artwork overlap
- Reading-order problems

### Human-in-the-Loop Principle

AI output is never treated as final content automatically.

```text
AI Output
   ↓
Human Review
   ├── Accept
   ├── Edit
   └── Reject
```

The proposal explicitly defines AI as an assistance layer whose output requires human acceptance, editing or rejection.

---

## 6.6 Provenance and Version Control

PanelForge uses an append-only event log and content-addressed asset storage.

Each entity maintains a version history that supports:

- Version comparison
- Rollback
- Historical reconstruction
- Change tracking

AI-assisted changes include provenance information such as:

- AI model
- Parameters
- Input context
- Accepting user
- Degree of subsequent human modification

This makes AI assistance traceable throughout the production lifecycle.

---

## 6.7 Export and Delivery

PanelForge supports rendering structured content into:

- Print-ready page images
- PDF
- CBZ
- Vertical-scroll webtoon output
- Open JSON

The open JSON format allows structured Manga data to be exported and reused outside the platform.

---

# 7. User Roles

PanelForge supports multiple roles.

| Role | Main Responsibilities |
|---|---|
| Administrator | System, users, workspaces, permissions, AI configuration |
| Producer / Series Owner | Series configuration, assignments, workflow, approval |
| Writer | Scripts, scenes, panels, dialogue and story structure |
| Artist | Thumbnails, artwork, panel production |
| Letterer | Balloons, narration, SFX and typography |
| Editor / Reviewer | Review, consistency checking and approval |
| Reader / External Stakeholder | Preview and external feedback |
| Authenticated User | Profile, notifications, search and activity |

---

# 8. Structured Manga Content Model

The core domain hierarchy is:

```text
Series
 └── Chapter
      └── Scene
           └── Page
                └── Panel
                     └── Element
```

The content model is intended to make every part of the Manga addressable and queryable.

For example:

```text
Series
  └── Chapter 01
       └── Scene 02
            └── Page 04
                 └── Panel 03
                      ├── Character: John
                      ├── Dialogue Balloon
                      │    ├── Speaker: John
                      │    └── Script Line: 15
                      └── Sound Effect
```

This allows the platform to answer structured questions about the content rather than simply locating an image file.

The proposal emphasizes that the structured model is the core contribution of the project, not merely file management.

---

# 9. Series Bible

The Series Bible maintains structured knowledge about the series.

## 9.1 Character Information

Character entries can represent information required for continuity checking.

Examples:

- Character identity
- Character-related references
- Established appearance information
- Terminology
- Related facts

## 9.2 Locations

Locations provide a consistent reference for scenes and panels.

## 9.3 Props

Important objects can be recorded and referenced across chapters.

## 9.4 Terminology

The system can maintain consistent:

- Names
- Terms
- Honorifics
- Series-specific vocabulary

## 9.5 Style Rules

Style rules provide production references for creative staff.

## 9.6 Established Plot Facts

Important story facts can be recorded and used as grounding information for consistency checking.

---

# 10. Production Workflow

PanelForge models production as a configurable workflow.

Example:

```text
┌────────┐
│ Script │
└───┬────┘
    ↓
┌───────────┐
│ Thumbnail │
└─────┬─────┘
      ↓
┌────────┐
│ Pencil │
└───┬────┘
    ↓
┌──────┐
│ Ink  │
└───┬──┘
    ↓
┌───────┐
│ Color │
└───┬───┘
    ↓
┌────────┐
│ Letter │
└───┬────┘
    ↓
┌────────┐
│ Review │
└───┬────┘
    ↓
┌──────────┐
│ Approved │
└──────────┘
```

Each stage can contain:

- Assigned user
- Deadline
- Dependencies
- Status
- Review state
- Change requests

The workflow is intended to be configurable instead of being hard-coded for one studio.

---

# 11. AI Assistance Architecture

AI services are designed as assistive services separated from the deterministic core system.

Conceptually:

```text
                    PanelForge
                        |
        ┌───────────────┼───────────────┐
        ↓               ↓               ↓
 Structured         Production      Provenance
   Domain             Pipeline        System
        |               |               |
        └───────────────┼───────────────┘
                        ↓
                  AI Assistance
                        |
          ┌─────────────┼─────────────┐
          ↓             ↓             ↓
      Breakdown    Consistency    Suggestions
                    Checking
```

AI operations should support:

- Asynchronous processing
- Idempotency
- Timeout handling
- Provider fallback
- Human acceptance
- Provenance recording

The architecture deliberately prevents an unavailable AI provider from blocking the normal manual production workflow.

---

# 12. Version Control and Provenance

Versioning is a fundamental part of PanelForge rather than an optional feature.

## 12.1 Append-Only Event Log

State-changing operations are represented as immutable events.

Conceptually:

```text
Event 01
   ↓
Event 02
   ↓
Event 03
   ↓
Event 04
   ↓
Current State
```

This makes it possible to reconstruct historical states.

## 12.2 Version History

Users can:

- View previous versions
- Compare versions
- Restore previous versions
- Review changes

## 12.3 AI Provenance

AI-assisted content records:

```text
Model
Input
Parameters
Output
Accepting User
Human Modifications
```

## 12.4 No Silent Overwrite

Committed versions cannot be silently overwritten or deleted.

The proposal identifies immutable revision history as a correctness requirement because the system must be able to reconstruct previous panel states.

---

# 13. Collaboration and Concurrency

Multiple users may work on the same chapter simultaneously.

For example:

```text
Writer
   ↓
Page

Letterer
   ↓
Same Page

Editor
   ↓
Same Page
```

PanelForge therefore needs concurrency control to prevent lost updates.

The proposed approach includes:

- Optimistic concurrency
- Version checking
- Conflict detection
- Conflict visibility

Conflicts should be surfaced to users instead of being silently resolved.

Real-time collaborative editing may be reduced to optimistic locking and conflict detection if necessary without weakening the core system.

---

# 14. Access Control and Security

PanelForge manages unreleased creative content that may have significant commercial value.

Security requirements include:

- Role-based access control
- Attribute-based access control
- Assignment-based access
- Time-limited external links
- Revocable access
- Watermarked previews
- Expiring previews
- Access logging
- Audit trails

External stakeholders should be able to review content without receiving production workspace access.

---

# 15. Functional Requirements

## 15.1 Administrator

Administrators can:

- Manage user accounts
- Manage studio workspaces
- Manage roles and permissions
- Configure AI provider credentials
- Configure AI models
- Configure usage quotas
- Configure storage quotas
- Configure retention policies
- Configure backup schedules
- View audit logs
- View AI usage reports
- Manage pipeline stage templates
- Manage element types
- Manage export presets

---

## 15.2 Producer / Series Owner

Producer or Series Owner can:

- Create and configure a Series
- Configure pipeline stages
- Configure roles
- Configure release calendars
- Invite members
- Assign roles
- Assign chapter-level work
- Assign panel-level work
- Set deadlines
- Maintain the Series Bible
- Monitor production dashboards
- Monitor workload
- Monitor overdue items
- Review burndown
- Approve or reject chapters
- Lock chapters
- Trigger exports
- Restore previous versions
- Review provenance reports

---

## 15.3 Writer

Writers can:

- Create structured chapter scripts
- Edit scenes
- Create dialogue blocks
- Create direction blocks
- Request AI script breakdown
- Review AI-generated structure
- Edit AI suggestions
- Reject AI suggestions
- Define panel descriptions
- Define shot notes
- Define dialogue
- Assign speakers
- Link content to Series Bible entries
- Compare script versions
- Restore previous revisions

---

## 15.4 Artist

Artists can:

- View assigned tasks
- View briefs
- View references
- View deadlines
- Plan page layouts
- Use thumbnail editors
- Use panel-grid editors
- Request AI layout suggestions
- Upload artwork versions
- Record production stages
- Access Series Bible references
- Access shared assets
- Respond to change requests
- Mark work ready for review

---

## 15.5 Letterer

Letterers can:

- Place dialogue balloons
- Resize dialogue balloons
- Style dialogue balloons
- Place narration boxes
- Place sound effects
- Bind text elements to script lines
- Bind text elements to speakers
- Receive text overflow warnings
- Receive artwork overlap warnings
- Receive reading-order warnings
- Apply typography presets

---

## 15.6 Editor / Reviewer

Editors and reviewers can:

- Review Panels
- Review Pages
- Review Chapters
- Compare versions
- Add region-anchored comments
- Add drawn annotations
- Run consistency checks
- Review character continuity
- Review terminology
- Review established facts
- Review reading order
- Issue change requests
- Track change-request resolution
- Approve submissions
- Reject submissions

---

## 15.7 Reader / External Stakeholder

External users can:

- Open time-limited previews
- View watermarked chapters
- Read content in reading mode
- Leave chapter-level feedback
- Leave page-level feedback

They do not receive production workspace access.

---

## 15.8 All Authenticated Users

Authenticated users can:

- Authenticate
- Manage profiles
- Configure notification preferences
- Receive assignment notifications
- Receive mentions
- Receive review notifications
- Receive deadline notifications
- Search scripts
- Search panels
- Search Series Bible entries
- Search comments
- View activity feeds
- View accessible version histories

These functional requirements are defined in the project registration document.

---

# 16. Non-Functional Requirements

## 16.1 Revision Integrity

No committed version should be silently overwritten or deleted.

## 16.2 Provenance Completeness

Every state-changing operation must record:

- Who performed it
- When it happened
- What changed

AI-assisted artifacts must additionally record AI-related provenance.

## 16.3 Concurrent Editing

The system must prevent lost updates and expose conflicts.

## 16.4 Responsiveness

Normal workspace interactions should remain responsive.

Expensive operations such as:

- AI breakdown
- Consistency checking
- Export

should run asynchronously.

## 16.5 Graceful AI Degradation

The platform must remain usable when an AI provider is:

- Slow
- Rate-limited
- Temporarily unavailable

## 16.6 Protection of Unreleased Work

Unreleased assets should use:

- Short-lived links
- Revocable access
- Assignment-based access
- Watermarked previews
- Expiration
- Access logging

## 16.7 Scalability

The architecture should support a long-running series with:

- Many panels
- Many versions
- Large asset collections

Content-addressed and deduplicated assets are used as part of the proposed strategy.

## 16.8 Configurability

The following should be configurable without redeployment:

- Pipeline stages
- Permissions
- Element types
- Typography presets
- Consistency rules
- Export presets

## 16.9 Creative-Staff Usability

The workspace should:

- Favor direct manipulation
- Support long work sessions
- Support tablet review
- Provide a dark theme
- Tolerate brief connectivity loss
- Support Vietnamese and English

## 16.10 Portability

The structured content model should support:

- Open schema
- Export
- Re-import
- Standard delivery formats

## 16.11 Maintainability and Testability

The core domain, event store and provenance subsystem should be covered by automated testing and integrated into an automated development pipeline.

---

# 17. System Architecture

PanelForge follows a modular architecture separating the core domain from assistive services.

Conceptual architecture:

```text
┌───────────────────────────────────────────────┐
│                  Frontend                     │
│                                               │
│ Script Editor | Panel Canvas | Dashboard      │
│ Lettering     | Review       | Version View   │
└───────────────────────┬───────────────────────┘
                        │
                        ↓
┌───────────────────────────────────────────────┐
│                   API Layer                   │
└───────────────────────┬───────────────────────┘
                        │
          ┌─────────────┼─────────────┐
          ↓             ↓             ↓
┌────────────────┐ ┌────────────┐ ┌────────────┐
│ Domain Layer   │ │ Workflow   │ │ Access     │
│                │ │ Engine     │ │ Control    │
└───────┬────────┘ └─────┬──────┘ └─────┬──────┘
        │                │              │
        └────────────────┼──────────────┘
                         ↓
              ┌────────────────────┐
              │ Event / Persistence│
              │ Layer              │
              └─────────┬──────────┘
                        │
             ┌──────────┴──────────┐
             ↓                     ↓
      Event Store             Object Storage
             │
             ↓
       Read Projections
             │
             ↓
       Query / Dashboard

                 ┌───────────────┐
                 │ AI Assistance │
                 │ Service       │
                 └───────┬───────┘
                         ↓
                 LLM / Vector DB
```

The proposal calls for a modular backend, documented API, append-only event store, read-model projection layer and background job queue.

---

# 18. Technical Foundations

## 18.1 Domain-Driven Design

Domain-Driven Design is used to formalize the Manga hierarchy into domain aggregates with explicit invariants and transactional boundaries.

The goal is to ensure that the content model behaves as reliable structured data rather than as a collection of files.

---

## 18.2 Event Sourcing and CQRS

Event Sourcing stores domain changes as an ordered immutable sequence of events.

CQRS separates:

```text
Write Model
     ↓
Domain Events
     ↓
Read Model
```

This supports:

- Complete history
- Temporal queries
- Rollback
- Efficient read operations

---

## 18.3 Immutability and Temporal Data

Immutable persistence allows historical states to be reconstructed.

The system can distinguish between:

- When a change was made
- When a change takes effect

This supports historical comparison and reconstruction.

---

## 18.4 Content-Addressed Storage

Binary artwork can be identified using cryptographic hashes.

This provides:

- Deduplication
- Integrity verification
- Version relationships
- Efficient storage

---

## 18.5 Workflow and State Machines

The production pipeline is represented as a configurable state machine.

Transitions can be controlled using:

- Stage rules
- Role permissions
- Approval gates
- Guard conditions

---

## 18.6 Concurrency Control

PanelForge considers optimistic concurrency with:

- Version vectors
- Conflict detection
- Lost-update prevention

Operational transformation is considered as an alternative with different complexity and trade-offs.

---

## 18.7 Schema Design and Evolution

The structured Manga schema must support future evolution.

Schema versioning and migration are required so existing content and historical versions remain readable when the schema changes.

---

## 18.8 AI Service Architecture

AI is treated as a non-deterministic external service.

The architecture therefore uses:

- Suggestion-and-acceptance boundaries
- Asynchronous processing
- Idempotency
- Timeout handling
- Fallback mechanisms

This keeps the core production system deterministic and usable even when AI services fail.

---

## 18.9 Provenance and Audit

The project considers standards such as:

- W3C PROV
- Content credential specifications

The goal is to make provenance complete, standards-aligned and portable.

---

## 18.10 Access Control Models

The system combines:

- Role-Based Access Control
- Attribute-Based Access Control

This supports complex multi-role studio environments and time-limited external reviewer access.

---

## 18.11 Modular Software Architecture

The architecture separates:

```text
Core Domain
     │
     ├── Production Workflow
     ├── Versioning
     ├── Provenance
     └── Access Control
     
Assistive Services
     │
     ├── AI
     ├── Rendering
     └── Background Processing
```

The separation helps maintain system reliability while allowing assistive services to evolve independently.

The technical foundations above are derived from the theory section of the project proposal.

---

# 19. Practical Implementation

The proposed implementation includes the following major areas.

## 19.1 Structured Manga Schema

Develop a versioned and publicly readable schema.

Validate the schema against realistic Manga and webtoon chapter structures.

## 19.2 Backend

Implement:

- Documented API
- Domain model
- Event store
- Read projections
- Background job queue
- Provenance subsystem

## 19.3 Production Workspace

Implement:

- Structured script editor
- Panel canvas
- Thumbnail planner
- Page layout planner
- Annotation layer
- Version comparison view

## 19.4 AI Integration

Integrate:

- Large language model provider
- Vector database
- Script breakdown
- Series Bible retrieval
- Consistency checking
- Cost controls
- Graceful fallback

## 19.5 Image Pipeline

Support:

- Image ingestion
- Tiling
- Thumbnail generation
- Watermarking
- Multi-format export

## 19.6 Deployment

The platform should be:

- Containerized
- Tested
- Deployed to a cloud environment
- Integrated with CI/CD
- Monitored
- Backed up

The proposal also includes usability testing with comic clubs, illustration students or an independent webtoon team.

---

# 20. Testing and Quality Assurance

Testing should cover the parts of the system where correctness is most important.

## 20.1 Unit Testing

Focus on:

- Domain rules
- Aggregates
- State transitions
- Validation
- Provenance logic
- Versioning

## 20.2 Integration Testing

Test interactions between:

- API
- Domain
- Event store
- Read models
- Object storage
- AI service
- Workflow engine

## 20.3 Security Testing

Test:

- RBAC
- ABAC
- External links
- Expiration
- Revocation
- Asset access
- Audit logs

## 20.4 Concurrency Testing

Verify:

- No lost updates
- Version conflicts are detected
- Concurrent modifications are surfaced correctly

## 20.5 AI Testing

Test:

- AI availability failure
- Timeout
- Rate limiting
- Fallback
- Invalid responses
- Provenance recording

## 20.6 Usability Testing

Evaluate the production workspace with target users such as:

- Comic club members
- Illustration students
- Independent webtoon teams

---

# 21. Deployment and CI/CD

The project is intended to use an automated engineering pipeline.

Conceptually:

```text
Code
  ↓
Version Control
  ↓
Build
  ↓
Automated Tests
  ↓
Containerization
  ↓
Deployment
  ↓
Monitoring
  ↓
Backup
```

The deployment process should ensure that changes to the platform are validated before reaching the deployed environment.

---

# 22. Project Scope

## 22.1 Mandatory MVP Scope

The mandatory core consists of:

1. Structured Manga content model
2. Series Bible
3. Production pipeline
4. Panel-anchored review
5. Provenance subsystem
6. Script-to-structure breakdown
7. Grounded consistency checking

These components represent the minimum scope that demonstrates the core contribution of PanelForge.

---

## 22.2 Extended Scope

Extended features include:

- AI layout suggestion
- Vision-based character continuity tracking
- Balloon-fit analysis
- More advanced collaborative editing

Real-time co-editing may be reduced to optimistic locking and conflict detection if necessary.

---

## 22.3 Out of Scope

### Generative Artwork

PanelForge deliberately does not focus on generative artwork.

The AI layer operates primarily on:

- Structure
- Text
- Metadata

Vision models are intended for similarity-based continuity checking rather than generating artwork.

This keeps the project focused on software engineering rather than making it primarily a generative AI project.

### Copyrighted Commercial Manga

Commercial copyrighted Manga should not be used as demonstration content.

All demonstration materials should be:

- Self-authored
- Openly licensed
- Provided with written permission

---

# 23. Work Packages

The proposal defines five major work packages.

## WP1 – Project Management, Domain Analysis and Content Schema

**Primary Role:** Project Manager / Business Analyst

Responsibilities:

- Scope and requirements
- Manga production workflow analysis
- Role taxonomy
- Review practices
- Content model
- Series Bible structure
- Invariants
- Validation rules
- Architecture
- Event-store design
- Usability evaluation
- Documentation
- Demonstration dataset
- Final defense package

---

## WP2 – Content Domain, Provenance and Version Control

**Primary Role:** Backend Developer / Domain Engineer

Responsibilities:

- Series aggregate
- Chapter aggregate
- Scene aggregate
- Page aggregate
- Panel aggregate
- Element aggregate
- Series Bible domain
- Append-only event store
- Read-model projections
- Content-addressed storage
- Deduplication
- Integrity verification
- Version history
- Diff computation
- Rollback
- Human and AI provenance
- Schema migration

---

## WP3 – Production Pipeline, Access Control and Delivery

**Primary Role:** Backend Developer

Responsibilities:

- Workflow engine
- State machines
- Assignment management
- Deadline management
- Dependency management
- Review workflow
- Change requests
- Approval workflow
- Notifications
- Activity feed
- RBAC
- ABAC
- External reviewer access
- Asset access control
- Audit trail
- Rendering
- Export

---

## WP4 – Structured Editors and Production Workspace

**Primary Role:** Frontend Developer

Responsibilities:

- Structured script editor
- Panel canvas
- Element editor
- Thumbnail planner
- Page-layout planner
- Lettering canvas
- Annotation layer
- Version comparison
- Producer dashboard
- Panel-level progress
- Personal task queue
- Reading mode
- Preview mode

---

## WP5 – AI Assistance Services, QA and Deployment

**Primary Role:** Full-stack Developer / QA Engineer

Responsibilities:

- Script-to-structure breakdown
- Series Bible retrieval indexing
- Grounded consistency checking
- Character continuity
- Balloon-fit analysis
- AI suggestion review boundary
- Provenance capture
- Provider fallback
- Unit testing
- Integration testing
- Security testing
- Concurrency testing
- Usability testing
- Containerization
- CI/CD
- Cloud deployment
- Monitoring
- Backup

The project proposal specifies a five-member team structure and states that WP1 and WP2 should be front-loaded because the content schema and provenance model determine the persistence architecture used by the remaining work packages.

---

# 24. Project Timeline

The registered project duration is:

```text
September 2026
      ↓
Domain Analysis
      ↓
Content Schema
      ↓
Provenance Foundation
      ↓
Backend / Frontend Development
      ↓
Workflow and Collaboration
      ↓
AI Assistance
      ↓
Testing
      ↓
Deployment
      ↓
Pilot / Evaluation
      ↓
Final Demonstration
      ↓
March 2027
```

The project is scheduled from 09/2026 to 03/2027.

---

# 25. Demonstration Dataset

The project requires a rights-clean demonstration dataset.

The expected dataset is:

- At least 3 fully structured chapters
- Complete production history
- Structured content
- Version history
- Production stages
- Review information
- AI provenance where applicable

Demonstration content must be self-authored, openly licensed or provided with written permission.

The proposal recommends securing an independent creator or comic club as a pilot user early in the project.

---

# 26. Expected Deliverables

## 26.1 PanelForge Web Platform

A deployed multi-role web application covering:

- Series setup
- Structured content
- Production pipeline
- Panel review
- Version history
- Export

## 26.2 Backend Service and API

A modular backend providing:

- Event-sourced provenance store
- Object storage integration
- Documented REST or GraphQL API

## 26.3 AI Assistance Service

A separately deployable AI service supporting:

- Script-to-structure breakdown
- Series Bible consistency checking
- Character continuity tracking
- Panel layout suggestion
- Per-request provenance logging

## 26.4 Structured Manga Schema

An open documented JSON schema supporting:

- Manga representation
- Import
- Export

## 26.5 Documentation

Expected documentation includes:

- SRS
- SDD
- Database design
- Event-store design
- Test plan
- Test report
- Deployment guide
- Bilingual user manual

## 26.6 Demonstration Dataset

A sample series containing at least three fully structured chapters with complete production history.

## 26.7 Final Presentation Package

The final package includes:

- Demonstration video
- Slide deck
- Source code repository
- Build instructions
- Deployment instructions

These deliverables are specified in the registered project proposal.

---

# 27. Project Success Criteria

PanelForge should demonstrate that it can:

### Content

- Represent Manga as structured data.
- Maintain the Series → Chapter → Scene → Page → Panel → Element hierarchy.
- Query individual content elements.

### Collaboration

- Assign production tasks.
- Track panel-level progress.
- Manage deadlines.
- Support review and change requests.

### Versioning

- Preserve revision history.
- Compare versions.
- Restore previous versions.
- Reconstruct historical states.

### Provenance

- Record human changes.
- Record AI-assisted operations.
- Preserve provenance during export.

### AI

- Break down scripts.
- Perform grounded consistency checking.
- Provide suggestions without removing human control.
- Fail gracefully when AI services are unavailable.

### Delivery

- Export structured content.
- Produce standard Manga delivery formats.
- Provide open JSON representation.

---

# 28. Repository Structure

A conceptual repository structure is:

```text
PanelForge_FA26SE251/
│
├── src/
│   ├── backend/
│   ├── frontend/
│   ├── ai/
│   └── shared/
│
├── docs/
│   ├── SRS/
│   ├── SDD/
│   ├── architecture/
│   ├── database/
│   ├── event-store/
│   ├── testing/
│   └── deployment/
│
├── schema/
│   └── manga-json-schema/
│
├── tests/
│   ├── unit/
│   ├── integration/
│   ├── security/
│   └── concurrency/
│
├── dataset/
│   └── demo/
│
├── deployment/
│
└── README.md
```

The exact implementation structure may evolve during development.

---

# 29. Getting Started

## 29.1 Prerequisites

Before running PanelForge locally, prepare the development environment required by the selected implementation.

The project should provide documented setup instructions for:

- Backend
- Frontend
- AI assistance service
- Database
- Object storage
- Vector database
- Background jobs

## 29.2 Clone Repository

```bash
git clone <repository-url>
cd PanelForge_FA26SE251
```

## 29.3 Configure Environment

Create the required environment configuration according to the deployment documentation.

Example:

```text
Database configuration
Object storage configuration
AI provider configuration
Vector database configuration
Authentication configuration
Application configuration
```

## 29.4 Run the Backend

Follow the backend documentation to configure and start the API service.

## 29.5 Run the Frontend

Follow the frontend documentation to install dependencies and start the production workspace.

## 29.6 Run AI Services

AI services should be configured separately from the core production system.

The system should still support the manual production workflow if the AI provider is unavailable.

---

# 30. Documentation

Project documentation is expected to include:

```text
docs/
├── SRS
├── SDD
├── Database Design
├── Event Store Design
├── API Documentation
├── Structured Manga Schema
├── Test Plan
├── Test Report
├── Deployment Guide
└── User Manual
```

The documentation should describe both the technical implementation and the usage of the production platform.

---

# 31. Development Principles

PanelForge follows several core engineering principles.

## 31.1 Structured Data First

The Manga should be represented as structured data rather than simply stored as files.

## 31.2 Provenance by Design

Versioning and provenance must be designed from the beginning of development.

They should not be added as a final feature.

## 31.3 Human-in-the-Loop AI

AI suggestions require human review.

## 31.4 No Silent Data Loss

Committed history must remain reconstructable.

## 31.5 Configurable Workflow

Studios should be able to adapt their production pipeline without changing application code.

## 31.6 Open and Portable Data

The structured content model should not lock a studio into a proprietary representation.

## 31.7 Graceful AI Failure

The production workflow must continue even when AI services are unavailable.

The proposal specifically warns that provenance cannot be safely retrofitted onto a conventional mutable CRUD design after development has started.

---

# 32. What Makes PanelForge Different

PanelForge is not intended to be simply a project management application with image uploads.

A conventional system may look like:

```text
Files
+
Tasks
+
Comments
+
Users
```

PanelForge instead aims to provide:

```text
Structured Manga Model
+
Production Workflow
+
Versioned Content
+
Provenance
+
Series Knowledge
+
AI Assistance
+
Panel-Level Review
```

The fundamental difference is the structured representation.

Instead of storing:

```text
chapter_01/
├── page01.png
├── page02.png
├── page03.png
└── script.docx
```

PanelForge represents the work conceptually as:

```text
Chapter
 ├── Scene
 │    ├── Page
 │    │    ├── Panel
 │    │    │    ├── Character
 │    │    │    ├── Dialogue
 │    │    │    └── SFX
 │    │    └── Panel
 │    └── Page
 └── Scene
```

This enables the platform to reason about the Manga at a much deeper level.

The project proposal explicitly states that a file manager is not the core contribution; the important contribution is storing a chapter as queryable structured data down to the individual balloon.

---

# 33. Future Expansion

Potential future expansion areas include:

- More advanced real-time collaboration
- Advanced AI layout planning
- Vision-based continuity analysis
- More sophisticated balloon-fit analysis
- Additional export formats
- Additional production workflow templates
- Additional studio configuration capabilities
- More advanced provenance standards
- Broader interoperability with creative production tools

These extensions should remain secondary to the core structured content and provenance architecture.

---

# 34. Project Team

## Supervisor

**Nguyễn Tấn Phúc**

Lecturer  
FPT University

## Student

**Bùi Ngọc Tâm**

Student Code: `SE170009`

Role: Member

The project registration identifies Nguyễn Tấn Phúc as supervisor and Bùi Ngọc Tâm as the registered student member.

---

# 35. Academic Context

PanelForge is a Software Engineering Capstone Project.

The project focuses on applying software engineering principles to a real-world creative production problem.

Major engineering areas include:

- Domain modelling
- Software architecture
- Event-driven persistence
- CQRS
- Version control
- Concurrency control
- Workflow modelling
- Access control
- AI service integration
- Automated testing
- CI/CD
- Cloud deployment
- Data portability

The project is registered for the academic period:

```text
09/2026 – 03/2027
```

---

# 36. License

License information will be defined according to the project's final academic and repository requirements.

Demonstration Manga content must comply with the project's rights-clean demonstration policy.

No copyrighted commercial Manga should be ingested into the platform without appropriate permission.

---

# PanelForge

PanelForge aims to transform Manga production from a fragmented collection of files into a structured, collaborative and traceable digital production system.

The core idea is:

```text
Structured Content
        +
Collaborative Workflow
        +
Versioned History
        +
Provenance
        +
Series Knowledge
        +
AI Assistance
        =
PanelForge
```

The project's primary contribution is not simply managing Manga files.

It is building a system that can **understand, track, validate and preserve the structure and history of a Manga production process**.

---

## Project Status

**Status:** In Development

**Project Period:** September 2026 – March 2027

**Project Type:** Software Engineering Capstone Project

**Project:** PanelForge_FA26SE251