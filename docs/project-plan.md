# PanelForge - Project Plan & Scope

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
Series
  ↓
Chapter
  ↓
Scene
  ↓
Page
  ↓
Panel
  ↓
Element
```

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

# 3. Extended Scope

Features that may be implemented after the mandatory MVP include:

## 3.1 AI Layout Suggestions

AI-assisted panel planning and layout recommendations.

## 3.2 Vision-Based Character Continuity

Similarity-based checking of character appearance across panels or chapters.

## 3.3 Balloon-Fit Analysis

Automated detection of:

- Text overflow
- Artwork overlap
- Poor placement
- Reading-order issues

## 3.4 Advanced Collaboration

Potential future capabilities include:

- Real-time collaborative editing
- More advanced conflict resolution
- Live cursors
- Presence indicators

If real-time collaboration is too complex for the capstone scope, optimistic concurrency and explicit conflict detection remain acceptable alternatives.

---

# 4. Out of Scope

## 4.1 Autonomous Artwork Generation

PanelForge is not intended to become a generative artwork platform.

AI focuses on:

- Text
- Structure
- Metadata
- Consistency
- Production assistance

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
- Content hierarchy
- Series Bible structure
- Business rules
- Validation rules
- Architecture planning
- Event-store design
- Usability evaluation
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
- AI provenance
- Schema migration

---

## WP3 — Production Pipeline, Access Control and Delivery

### Primary Focus

```text
Workflow
Authorization
Notifications
Export
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
- Rendering
- Export

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

---

## WP5 — AI Assistance, QA and Deployment

### Primary Focus

```text
AI
Testing
Deployment
DevOps
```

### Responsibilities

- Script-to-structure breakdown
- Series Bible retrieval
- Consistency checking
- Character continuity
- Balloon-fit analysis
- AI provenance
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
- Layout suggestions where implemented
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

with production history.

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
Chapters
 ↓
Scenes
 ↓
Pages
 ↓
Panels
 ↓
Elements
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