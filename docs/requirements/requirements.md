# PanelForge - System Requirements

---

## Table of Contents

- [1. Problem Statement](#1-problem-statement)
- [2. Proposed Solution](#2-proposed-solution)
- [3. Functional Requirements](#3-functional-requirements)
  - [3.1 Administrator](#31-administrator)
  - [3.2 Producer / Series Owner](#32-producer--series-owner)
  - [3.3 Writer](#33-writer)
  - [3.4 Artist](#34-artist)
  - [3.5 Letterer](#35-letterer)
  - [3.6 Editor / Reviewer](#36-editor--reviewer)
  - [3.7 External Reviewer](#37-external-reviewer)
  - [3.7b Public Reader](#37b-public-reader)
  - [3.8 Authenticated Users](#38-authenticated-users)
- [4. Core Domain Requirements](#4-core-domain-requirements)
- [5. AI Requirements](#5-ai-requirements)
- [6. Non-Functional Requirements](#6-non-functional-requirements)
- [7. Security Requirements](#7-security-requirements)
- [8. Data and Provenance Requirements](#8-data-and-provenance-requirements)

---

# 1. Problem Statement

Modern Manga, comic, and webtoon production is commonly supported by fragmented general-purpose tools.

A distributed team may use:

- Word processors for scripts
- Photoshop or Clip Studio for artwork
- Spreadsheets for schedules
- Shared drives for assets
- Chat applications for feedback

This fragmentation creates several problems.

### 1.1 Unstructured Representation

Scripts and artwork are stored separately without reliable machine-readable relationships.

The system cannot easily determine:

- Which dialogue belongs to which panel
- Which speaker owns a dialogue line
- Which character appears in a panel
- Which asset version is currently approved

### 1.2 Fragmented Collaboration

Comments and assignments may be distributed across different tools.

Feedback can become detached from the exact artwork version it concerns.

### 1.3 Continuity Inconsistency

Long-running series require consistency across:

- Characters
- Locations
- Props
- Terminology
- Style rules
- Plot facts

Manual tracking becomes increasingly difficult as the series grows.

### 1.4 Revision History Loss

Creative files may be overwritten or duplicated manually.

This makes it difficult to reconstruct:

- Who made a change
- When it happened
- What changed
- Which version was approved

### 1.5 Invisible Production Status

Tracking only at file or page level does not provide sufficient visibility into panel-level progress.

### 1.6 Missing AI Provenance

AI-assisted production requires traceability of:

- Model
- Parameters
- Input
- Output
- Accepting user
- Human modifications

---

# 2. Proposed Solution

PanelForge represents Manga production as structured, versioned, and traceable data.

The core hierarchy is:

```text
Studio Workspace
  └── Series (many per Workspace)
       └── Chapter
            └── Scene
                 └── Page
                      └── Panel
                           └── Element
```

A Studio Workspace is the tenant boundary: it owns its members, roles, AI configuration, and one or more Series. Each Series has its own pipeline, Series Bible, and chapters.

The platform combines:

1. Structured content modelling
2. Series Bible
3. Production workflow
4. Panel-level review
5. Version control
6. Provenance tracking
7. AI-assisted production
8. Structured export

---

# 3. Functional Requirements

## 3.1 Administrator

The Administrator shall be able to:

### User Management

- Create users
- Update users
- Disable users
- Manage roles
- Manage permissions
- Grant or revoke the permission to create a Studio (CanCreateStudio)

The Administrator does not invite members into a Workspace and has no content access in it. Only the Producer invites members and assigns WorkspaceRoles.

### AI Configuration

- Configure AI provider credentials
- Configure available AI models
- Configure usage quotas
- Configure fallback strategies
- Monitor AI usage

### Storage Configuration

- Configure storage quotas
- Configure retention policies
- Configure backup schedules
- Monitor storage usage

### System Configuration

- Configure pipeline stage templates
- Configure element types
- Configure export presets
- Configure typography presets
- Configure consistency rules

### Audit

- View system audit logs
- Review user actions
- Review AI usage
- Review security-related activities

---

## 3.2 Producer / Series Owner

The Producer or Series Owner shall be able to:

### Workspace and Series Management

- Create and manage a Studio Workspace (where permitted)
- Create multiple Series inside one Workspace
- Configure Series
- Configure release calendars
- Configure production workflow
- Configure team roles

### Team Management

- Invite members
- Assign members to roles
- Assign chapter-level tasks
- Assign panel-level tasks

### Scheduling

- Create deadlines
- Track overdue work
- Configure dependencies
- Monitor production progress

### Series Bible

- Create Series Bible entries
- Update characters
- Update locations
- Update props
- Update terminology
- Update style rules
- Record established plot facts

### Production Monitoring

- View dashboards
- View panel-level progress
- View workload
- View burndown
- View overdue items

### Approval

- Approve chapters
- Reject chapters
- Lock chapters
- Reopen a locked chapter with a recorded reason
- Issue preview links for locked chapters
- Publish chapters
- Configure the approver of each stage gate (the Editor by default)
- Trigger exports
- Restore previous versions
- Review provenance reports

---

## 3.3 Writer

The Writer shall be able to:

### Script Management

- Create structured scripts
- Create scenes
- Create panel descriptions
- Create shot notes
- Create dialogue blocks
- Assign speakers
- Link content to Series Bible entries

### AI Assistance

- Request script-to-structure breakdown
- Review AI suggestions
- Edit AI suggestions
- Accept AI suggestions
- Reject AI suggestions

### Versioning

- View script history
- Compare revisions
- Restore previous revisions

---

## 3.4 Artist

The Artist shall be able to:

- View assigned tasks
- View production briefs
- View references
- View deadlines
- Create thumbnail layouts
- Create panel layouts (the Artist owns the final page layout, panel size and position, camera angle, and space for lettering; the Writer proposes scene and panel structure and pacing, and the Editor checks reading flow)
- Upload artwork versions (artwork produced in external tools, attached to the Panel and the current pipeline stage)
- Remove the background from an uploaded image (see 5.9)
- Request AI-generated draft images (see 5.8)
- Track production stages
- Access Series Bible references
- Access shared assets
- Respond to change requests
- Submit panels for review

Artists may work through stages such as:

```text
Thumbnail
   ↓
Pencil
   ↓
Ink
   ↓
Color
```

---

## 3.5 Letterer

The Letterer shall be able to:

- Create dialogue balloons
- Resize dialogue balloons
- Style dialogue balloons
- Create narration boxes
- Create sound effects
- Bind text elements to script lines
- Bind text elements to speakers
- Apply typography presets

The system should provide warnings for:

- Text overflow
- Artwork overlap
- Reading-order problems

---

## 3.6 Editor / Reviewer

The Editor or Reviewer shall be able to:

### Review

- Review panels
- Review pages
- Review chapters
- Compare versions
- Review production stages

### Annotations

- Add region-anchored comments
- Add drawn annotations
- Attach comments to panel versions

### Consistency

- Run Series Bible consistency checks
- Review character continuity
- Review terminology
- Review established facts

### Change Requests

- Issue change requests
- Track change-request status
- Review submitted changes
- Approve changes
- Reject changes

### Approval

- Approve submissions
- Reject submissions
- Approve stage gates, which moves an item to the next stage (approving a stage is separate from the Producer approving and locking a chapter)
- Triage each consistency finding as Confirmed, Rejected, Ignored (with a recorded reason), or Needs clarification

---

## 3.7 External Reviewer

An External Reviewer is a named recipient of a preview link. They have no account and are not a WorkspaceRole.

External reviewers shall be able to:

- Open a time-limited preview link issued to their email address
- Access watermarked content (the watermark shows the recipient)
- Read the preview of a Locked chapter
- Provide chapter-level feedback
- Provide page-level feedback

A preview link is issued only for a Locked chapter. The Producer can revoke it before expiry, and every access attempt, including denied ones, is logged.

External reviewers shall not receive full production workspace access.

Feedback is triaged by the Producer or Editor. If a change is needed, the Producer reopens the chapter with a recorded reason, the change goes through the normal workflow, and the chapter is locked again.

---

## 3.7b Public Reader

A Public Reader reads released work through the public catalog, separately from the Workspace and from preview links.

Public readers shall be able to:

- Browse the public catalog of Series that have Published chapters
- Register a Reader account (separate from Workspace users)
- Read Published chapters in the Series reading direction
- Follow a Series and, where the Series owner enables paid access, subscribe to read every Published chapter

Only Published chapters are visible. Public Reader access, Workspace access, and preview-link access are three separate paths and must not reuse one another's authorization.

---

## 3.8 Authenticated Users

Authenticated users shall be able to:

- Authenticate
- Manage their profile
- Configure notifications
- View assignments
- View mentions
- Receive review notifications
- Receive deadline notifications
- Search scripts
- Search panels
- Search Series Bible entries
- Search comments
- View activity feeds
- View accessible version history

---

# 4. Core Domain Requirements

## 4.1 Hierarchy Integrity

The system shall enforce:

```text
Studio Workspace
  ↓
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

An Element must belong to exactly one Panel.

A Panel must belong to exactly one Page.

A Page must belong to exactly one Scene.

A Scene must belong to exactly one Chapter.

A Chapter must belong to exactly one Series.

A Series must belong to exactly one Studio Workspace, and a Workspace may contain many Series.

---

## 4.2 No Orphan Entities

The system shall prevent orphaned content entities.

For example:

```text
Element
  └── must reference Panel
```

An Element cannot exist independently without its parent Panel.

---

## 4.3 Reading Order

The system shall preserve reading order for:

- Panels
- Dialogue
- Narration
- Sound effects
- Other relevant elements

---

## 4.4 Version Integrity

Committed versions shall not be silently overwritten.

Previous versions must remain reconstructable.

---

## 4.5 Series Bible Integrity

Series Bible information must remain versioned and traceable.

Changes to continuity-critical information should be auditable.

---

# 5. AI Requirements

## 5.1 Script-to-Structure Breakdown

The system shall support AI-assisted conversion of script content into proposed:

- Scenes
- Pages
- Panels
- Panel descriptions
- Dialogue relationships

The generated structure must be reviewable by a human.

---

## 5.2 Grounded Consistency Checking

The AI should use Series Bible information as grounding context.

Potential checks include:

- Character consistency
- Location consistency
- Terminology
- Style rules
- Established plot facts

---

## 5.3 Layout Suggestions

The system may provide AI-generated layout suggestions.

The user must be able to:

- Accept
- Edit
- Reject

the suggestion.

---

## 5.4 Character Continuity

The system may use vision-based similarity analysis to help identify possible character continuity problems.

This feature is a checking aid. Its findings are suggestions for the Editor, not automatic rejections.

---

## 5.5 Balloon-Fit Analysis

The system may identify:

- Text overflow
- Artwork overlap
- Poor text placement
- Reading-order issues

---

## 5.6 Human-in-the-Loop

AI output must not automatically become final production content.

The required flow is:

```text
AI
 ↓
Suggestion
 ↓
Human Review
 ├── Accept
 ├── Edit
 └── Reject
```

---

## 5.7 Graceful AI Failure

If the AI provider is:

- Unavailable
- Slow
- Rate-limited
- Temporarily failing

the manual production workflow must remain usable.

---

## 5.8 AI-Generated Draft Images

The system may generate draft images for a Panel from its script description, Series Bible references, and Series style rules.

- A generated image is an AI Suggestion. It never becomes the Panel's artwork until a human accepts it (5.6).
- Generation is asynchronous (6.4) and the result is stored as a new artwork version; no existing version is overwritten.
- The generation request and result record provenance (section 8): model, parameters, prompt and reference inputs, output asset, accepting user, and human modifications.
- Generated images are intended as drafts and references (for example Thumbnail-stage drafts or backgrounds), not as a replacement for the Artist's work.
- The Administrator controls which image providers and models are enabled and their quotas (3.1). Workspace content is sent only to providers the Administrator has configured.
- If generation is unavailable, uploading artwork manually remains fully usable (5.7).

---

## 5.9 Background Removal

An Artist may upload an image and request background removal.

- The result is stored as a new asset derived from the original; the original is retained unchanged.
- The derived asset records its source asset, the operation, and the tool or model used (section 8).
- The result may be used as an `ARTWORK_LAYER` or character element on a Panel after the Artist reviews and accepts it.
- Processing is asynchronous for large images (6.4).
- Background removal does not require an external provider if a self-hosted model is used; the choice is a deployment decision.

---

# 6. Non-Functional Requirements

## 6.1 Revision Integrity

No committed revision may be silently overwritten or deleted.

---

## 6.2 Complete Provenance

Every state-changing operation should record:

- Actor
- Timestamp
- Entity
- Operation
- Version

AI operations should additionally record:

- Model
- Parameters
- Input context
- Output
- Accepting user
- Human modifications

---

## 6.3 Concurrent Editing

The system shall prevent lost updates.

Optimistic concurrency control should be used where appropriate.

Conflicts must be explicitly detected and surfaced.

---

## 6.4 Performance

Normal workspace operations should remain responsive.

Expensive operations should be processed asynchronously.

Examples:

- AI generation
- AI image generation and background removal
- Consistency analysis
- Large exports
- Image processing

---

## 6.5 Scalability

The platform should support:

- Long-running series
- Large numbers of chapters
- Large panel counts
- Multiple artwork versions
- Large binary assets

Content-addressed storage should support asset deduplication.

---

## 6.6 Configurability

The following should be configurable without redeployment:

- Pipeline stages
- Permissions
- Element types
- Typography presets
- Consistency rules
- Export presets

---

## 6.7 Usability

The workspace should support:

- Direct manipulation
- Long creative sessions
- Dark theme
- Tablet review
- Brief connectivity loss
- Vietnamese and English

---

## 6.8 Portability

The structured data model should support:

- Open JSON schema
- Import
- Export
- Standard delivery formats

---

## 6.9 Maintainability

The system should use modular components with automated tests and CI/CD integration.

---

# 7. Security Requirements

## 7.1 Authentication

Authenticated access shall be required for production workspace operations.

---

## 7.2 Authorization

Authorization shall consider:

- User role
- Workspace membership
- Assignment
- Resource ownership
- Resource state

---

## 7.3 External Access

External preview links should support:

- Issuing only for a Locked chapter
- Binding to a named recipient email
- Short expiration
- Revocation
- Watermarking that shows the recipient
- Access logging, including denied attempts

---

## 7.3b Public Reader Access

Published Series are discoverable in a public catalog independent of Workspace membership. Public Reader accounts are separate from Workspace users, and only Published chapters are exposed.

---

## 7.4 Unreleased Work

Unreleased content must be protected against unauthorized access.

---

## 7.5 Audit

Security-sensitive and production-critical operations should be auditable.

---

# 8. Data and Provenance Requirements

PanelForge must preserve enough information to reconstruct the production history of an artifact.

The system should be able to answer:

```text
Who changed it?
When did they change it?
What changed?
Which version was changed?
What was the previous state?
Was AI involved?
Which AI model was used?
Who accepted the AI result?
How much did a human modify it?
```

The provenance system is therefore considered a core architectural requirement rather than an optional reporting feature.

---

## Core Requirement Summary

PanelForge must provide:

```text
Structured Content
        +
Series Bible
        +
Production Workflow
        +
Panel-Level Review
        +
Immutable Versioning
        +
AI Assistance
        +
AI Provenance
        +
Secure Collaboration
        =
PanelForge Requirements
```