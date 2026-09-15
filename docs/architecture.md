# PanelForge - System Architecture & Technical Foundations

---

## Table of Contents

- [1. Architectural Overview](#1-architectural-overview)
- [2. Architectural Principles](#2-architectural-principles)
- [3. Domain-Driven Design](#3-domain-driven-design)
- [4. Domain Model](#4-domain-model)
- [5. Event Sourcing](#5-event-sourcing)
- [6. CQRS](#6-cqrs)
- [7. Content-Addressed Storage](#7-content-addressed-storage)
- [8. Workflow Architecture](#8-workflow-architecture)
- [9. Concurrency Control](#9-concurrency-control)
- [10. AI Architecture](#10-ai-architecture)
- [11. Provenance Architecture](#11-provenance-architecture)
- [12. Access Control Architecture](#12-access-control-architecture)
- [13. Background Processing](#13-background-processing)
- [14. Export Architecture](#14-export-architecture)
- [15. Schema Evolution](#15-schema-evolution)
- [16. Architectural Goals](#16-architectural-goals)

---

# 1. Architectural Overview

PanelForge uses a modular architecture separating:

- Presentation
- API
- Domain
- Workflow
- Access control
- Persistence
- Background processing
- AI services
- Asset storage

Conceptual architecture:

```text
┌─────────────────────────────────────────────────────────────┐
│                       Frontend App                          │
│                                                             │
│ Script Editor | Panel Canvas | Dashboard | Review Tool     │
│ Lettering     | Version View | Reading Mode | Preview       │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ↓
┌─────────────────────────────────────────────────────────────┐
│                          API Layer                          │
│                                                             │
│ Authentication | Commands | Queries | File Operations       │
└──────────────────────────────┬──────────────────────────────┘
                               │
         ┌─────────────────────┼─────────────────────┐
         ↓                     ↓                     ↓
┌────────────────┐    ┌────────────────┐    ┌────────────────┐
│  Domain Core   │    │Workflow Engine │    │ Access Control │
│                │    │                │    │                │
│ Aggregates     │    │ State Machine  │    │ RBAC / ABAC    │
│ Business Rules │    │ Assignments    │    │ Policies       │
└────────┬───────┘    └────────┬───────┘    └────────┬───────┘
         │                     │                     │
         └─────────────────────┼─────────────────────┘
                               ↓
                 ┌───────────────────────────┐
                 │    Persistence Layer      │
                 └─────────────┬─────────────┘
                               │
                ┌──────────────┴──────────────┐
                ↓                             ↓
        Event / Data Store             Object Storage
                │
                ↓
         Read Projections
                │
                ↓
           Query Models
                │
                ↓
       Dashboards / Search / Reports

                ┌───────────────────────────┐
                │      Background Jobs       │
                │                           │
                │ AI | Export | Rendering    │
                │ Image Processing | Email   │
                └──────────────┬────────────┘
                               │
                ┌──────────────┴──────────────┐
                ↓                             ↓
        AI Assistance Service            Export Pipeline
```

---

# 2. Architectural Principles

PanelForge follows the following principles.

## 2.1 Structured Content First

The content hierarchy is a core domain concern.

---

## 2.2 Provenance by Design

Revision history and provenance must exist from the beginning of the system.

---

## 2.3 Deterministic Core

Core production operations should not depend on an AI provider being available.

---

## 2.4 Human-Controlled AI

AI produces suggestions.

Humans decide whether those suggestions become part of the production state.

---

## 2.5 Immutable History

Historical states should remain reconstructable.

---

## 2.6 Modular Architecture

AI, image processing, export and other supporting services should remain replaceable without redesigning the core domain.

---

# 3. Domain-Driven Design

Domain-Driven Design is used to model the Manga production domain explicitly.

The central aggregates represent production concepts rather than database tables alone.

Possible aggregate boundaries include:

```text
Series
Chapter
Scene
Page
Panel
Series Bible
Production Task
Review
Provenance
```

Each aggregate is responsible for enforcing its own business invariants.

---

# 4. Domain Model

## 4.1 Manga Hierarchy

The core hierarchy is:

```text
Series
  └── Chapter
       └── Scene
            └── Page
                 └── Panel
                      └── Element
```

---

## 4.2 Element

An Element represents a typed unit inside a Panel.

Examples:

```text
Element
├── Dialogue Balloon
├── Narration Box
├── Sound Effect
├── Character Instance
└── Artwork Layer
```

---

## 4.3 Aggregate Integrity

The system must enforce:

```text
Element → Panel
Panel → Page
Page → Scene
Scene → Chapter
Chapter → Series
```

This prevents orphan entities.

---

## 4.4 Domain Events

Typical events may include:

```text
SeriesCreated
ChapterCreated
SceneCreated
PageCreated
PanelCreated
ElementCreated
PanelUpdated
ArtworkVersionUploaded
ReviewCreated
ChangeRequestIssued
WorkflowStageChanged
ChapterApproved
VersionRestored
AISuggestionAccepted
```

The exact event catalogue may evolve during implementation.

---

# 5. Event Sourcing

Event Sourcing stores state changes as an immutable sequence of events.

Conceptually:

```text
Command
   ↓
Domain Aggregate
   ↓
Domain Event
   ↓
Event Store
   ↓
Projection
   ↓
Read Model
```

Example:

```text
PanelCreated
     ↓
PanelDescriptionUpdated
     ↓
ArtworkVersionUploaded
     ↓
ReviewCreated
     ↓
ChangeRequestIssued
     ↓
ArtworkVersionUploaded
     ↓
PanelApproved
```

This sequence provides a historical record of the production process.

---

## 5.1 Benefits

Event Sourcing supports:

- Historical reconstruction
- Version comparison
- Auditability
- Temporal queries
- Rollback workflows
- Provenance

---

# 6. CQRS

CQRS separates write operations from read operations.

```text
                  ┌───────────────┐
Command ─────────→│  Write Model  │
                  └───────┬───────┘
                          │
                          ↓
                   Domain Events
                          │
                          ↓
                  ┌───────────────┐
                  │ Event Store   │
                  └───────┬───────┘
                          │
                          ↓
                  ┌───────────────┐
                  │ Projections   │
                  └───────┬───────┘
                          │
                          ↓
                  ┌───────────────┐
Query ───────────→│  Read Model   │
                  └───────────────┘
```

The write side prioritizes domain integrity.

The read side prioritizes efficient querying and dashboards.

---

# 7. Content-Addressed Storage

Artwork assets can be stored using content-addressed identifiers.

Conceptually:

```text
File
 ↓
SHA-256
 ↓
Content Hash
 ↓
Object Storage
```

Example:

```text
Artwork
   ↓
SHA-256
   ↓
a9c3...f18
   ↓
Object Storage
```

Benefits include:

- Deduplication
- Integrity verification
- Stable asset identity
- Version relationships
- Efficient storage

---

# 8. Workflow Architecture

The production workflow is represented as a configurable state machine.

Example:

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

A workflow transition may depend on:

- Current stage
- User role
- Assignment
- Validation
- Review result
- Approval state

Conceptually:

```text
Current State
      ↓
Transition Request
      ↓
Permission Check
      ↓
Business Rule Check
      ↓
State Transition
      ↓
Domain Event
```

---

# 9. Concurrency Control

Multiple users may modify the same content.

PanelForge should use optimistic concurrency control where appropriate.

Conceptually:

```text
User A reads Version 10
User B reads Version 10

User A saves
     ↓
Version 11

User B attempts to save Version 10
     ↓
Conflict Detected
```

The system should not silently overwrite User A's changes.

Instead, User B should be informed that the underlying version has changed.

---

# 10. AI Architecture

AI is treated as an external, non-deterministic assistance service.

```text
Core Platform
      │
      ↓
AI Job Request
      │
      ↓
Background Queue
      │
      ↓
AI Service
      │
      ├── LLM
      ├── Retrieval
      └── Vector Search
      │
      ↓
AI Result
      │
      ↓
Human Review
      │
      ├── Accept
      ├── Edit
      └── Reject
      │
      ↓
Provenance Event
```

---

## 10.1 AI Operations

Possible operations include:

```text
Script Breakdown
Consistency Checking
Character Continuity
Layout Suggestion
Balloon-Fit Analysis
```

---

## 10.2 Asynchronous AI Processing

Expensive AI operations should be asynchronous.

```text
API Request
    ↓
Create AI Job
    ↓
Return Job ID
    ↓
Background Worker
    ↓
AI Provider
    ↓
Persist Result
    ↓
Notify User
```

---

## 10.3 AI Failure Handling

The platform should handle:

- Timeout
- Provider failure
- Rate limiting
- Invalid responses
- Temporary unavailability

The core manual workflow must remain usable.

---

# 11. Provenance Architecture

Provenance is represented as structured information associated with state changes.

For normal changes:

```text
Actor
Timestamp
Entity
Operation
Version
```

For AI-assisted changes:

```text
Actor
Timestamp
Entity
Operation
Version
AI Model
Parameters
Input Context
AI Output
Acceptance
Human Modification
```

Example:

```text
AISuggestionGenerated
        ↓
AISuggestionReviewed
        ↓
AISuggestionAccepted
        ↓
HumanModified
        ↓
FinalVersionCreated
```

This makes it possible to determine how AI participated in a production artifact.

---

# 12. Access Control Architecture

PanelForge uses role and resource-aware authorization.

Conceptually:

```text
User
 │
 ├── Role
 ├── Workspace
 ├── Assignment
 └── Attributes
        │
        ↓
   Authorization
        │
        ↓
 Resource Access
```

Authorization may depend on:

- Role
- Workspace
- Assignment
- Resource ownership
- Resource state
- External access rules

---

# 13. Background Processing

Background jobs can handle operations such as:

- AI processing
- Image processing
- Thumbnail generation
- Watermarking
- Export
- Email notifications
- Consistency checking

Conceptually:

```text
Application
    ↓
Job Queue
    ↓
Worker
    ↓
Operation
    ↓
Result
    ↓
Persistence
```

Background jobs should be designed to support retry and idempotency where appropriate.

---

# 14. Export Architecture

The export system transforms structured content into delivery formats.

```text
Structured Manga
       ↓
Export Configuration
       ↓
Rendering Pipeline
       ↓
┌──────┬──────┬──────┬─────────┐
↓      ↓      ↓      ↓
PNG    PDF    CBZ    Webtoon
```

The structured representation should also support:

```text
Structured Manga
       ↓
Open JSON
```

---

# 15. Schema Evolution

The Manga schema must support future changes.

Schema evolution should consider:

- Version identifiers
- Migration strategies
- Backward compatibility
- Validation
- Historical versions

Existing content should remain readable when the schema evolves.

---

# 16. Architectural Goals

The architecture is designed to achieve:

### Reliability

Historical data remains reconstructable.

### Traceability

Changes can be associated with users, versions, and AI operations.

### Scalability

Large collections of Manga assets can be stored efficiently.

### Modularity

AI and infrastructure components can evolve independently.

### Portability

Structured Manga data can be exported.

### Maintainability

Domain logic remains separated from infrastructure concerns.

### Testability

Domain and application logic can be tested independently.

---

## Architecture Summary

```text
Structured Domain
       +
Event Sourcing
       +
CQRS
       +
Workflow Engine
       +
Content-Addressed Storage
       +
Concurrency Control
       +
AI Assistance
       +
Provenance
       +
Access Control
       =
PanelForge Architecture
```