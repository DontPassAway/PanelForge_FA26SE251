# PanelForge - System Requirements

> Aligned with the registration form V1.2 as of 06/10/2026. Section 2.1 is the scope baseline; where another section is older or more detailed, 2.1 decides what is in scope. Acceptance values (30-second timeout, 70% precision and recall, 0.75 similarity threshold, 80% Task Success Rate) are proposed and to be confirmed with the supervisor.

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
            ├── Page ── Panel ── Element        (physical tree, the main one)
            └── Scene ── Script block           (narrative structure, parallel)

Panel ──references──▶ Script block   (the Scene of a Panel follows from the blocks it carries)
```

A Studio Workspace is the tenant boundary: it owns its members, roles, AI configuration, and one or more Series. Each Series has its own pipeline, Series Bible, and chapters.

A Scene does not contain Pages. A Page may hold Panels of different Scenes, and a Scene may span several Pages (registration form V1.2, content model).

The platform combines:

1. Structured content modelling
2. Series Bible
3. Production workflow
4. Panel-level review
5. Version control
6. Provenance tracking
7. AI-assisted production through role-based agents
8. Structured export

## 2.1 Scope Baseline (registration form V1.2)

This section records the scope agreed in the capstone registration form, version 1.2. Where another section of this document is more detailed or older, this baseline decides what is in scope.

**Core:** structured content model, Series Bible, production pipeline, panel-anchored review, provenance subsystem, workspace governance, the multi-agent workflow Writer → Continuity → Storyboard → Editor with human review, the Artist Agent, the character pipeline (locked character sheets, three-layer consistency check, sketch-to-image generation demonstrated on at least one character), balloon-overflow warnings computed by code, and AI shortening of dialogue to fit a balloon. The core demonstration is black-and-white manga.

**Extended:** colour mode (Color stage and colour consistency checks), translation of dialogue into other languages within the balloon length limit, the Producer Agent, generation of whole pages or whole scenes, and real-time co-editing (reducible to optimistic locking with conflict detection).

**Not supported:** interleaved scenes (cross-cutting between two scenes inside one sequence), an infinite vertical-scroll webtoon editing canvas (pages built in the editor can still be exported as vertical-scroll images), free-form panels whose reading order cannot be inferred, and a free-drawing tool (artists draw in external software and upload their work).

**Publication:** publication takes place outside the platform. PanelForge prepares chapter versions and an export package, and a publisher, as an external reviewer, gives feedback through a time-limited preview link. The Public Reader and public catalog (3.7b) are an extra feature outside this scope baseline.

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
- Enable or disable AI for each Workspace
- Configure the monthly AI token quota of each Workspace
- Configure the image-generation quota of each Workspace (a number of images or a budget, because LLM tokens do not cover the cost of an image model)
- Configure fallback strategies
- Monitor AI usage and the AI provenance report

### Storage and Workspace Rules

- Configure the storage limit (GB) of each Workspace
- Configure the session policy of each Workspace (session lifetime, idle timeout, concurrent sessions)
- Configure retention policies
- Configure backup schedules
- Monitor the usage of storage, AI tokens and image generation of each Workspace

The Administrator keeps administrative authority over limits, AI configuration and audit, and takes no part in production.

### System Configuration

- Configure pipeline stage templates
- Configure element types
- Configure page layout (frame) templates
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
- Configure Series, including its synopsis and story arcs
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
- Approve and lock a character sheet version as the reference for a range of chapters

### AI and Usage

- Turn AI assistance on or off within the limits the Administrator allows
- Monitor, on a usage screen, storage, AI token, image-generation and session usage against the limits set for the Workspace
- Review AI usage against the quota

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
- Issue preview links for locked chapters (the Producer has no relationship with a publisher; any contact with a publisher takes place outside the platform)
- Open a new chapter version for rework when review or external feedback rejects pages, keeping every earlier version
- Publish chapters (V1.2 note: publication to a publisher takes place outside the platform; see the note in 3.7b)
- Configure the approver of each stage gate (the Editor by default)
- Trigger exports
- Restore previous versions
- Review provenance reports

---

## 3.3 Writer

The Writer shall be able to:

### Script Management

- Outline each chapter from the series synopsis and story arcs
- Create structured scripts
- Create scenes
- Create panel descriptions
- Create shot notes
- Create dialogue blocks
- Assign speakers
- Link content to Series Bible entries

### AI Assistance

- Run the Writer Agent to draft a scene script from a brief, and the Continuity Agent to check it against the Series Bible
- Run the Storyboard Agent to break an approved script into Pages and Panels (see 5.1)
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
- Create thumbnail layouts, starting from a frame template or from a layout suggestion of the Storyboard Agent
- Create panel layouts (the Artist owns the final page layout, panel size and position, camera angle, and space for lettering; the Writer proposes scene and panel structure and pacing, and the Editor checks reading flow)
- Upload artwork versions (artwork produced in external tools, attached to the Panel and the current pipeline stage)
- Remove the background from an uploaded image (see 5.9)
- Upload character sketches (actions, emotions, movements) to the Series Bible so that they serve as the reference for consistency (see 5.10)
- Request AI-generated character images in a chosen pose, emotion, shot scale (wide, medium, close-up) and drawing style, then accept, edit or reject them (see 5.8)
- Use a generated character as an independent artwork-layer element cut out from its background
- Review AI suggestions, image prompts and generated images, and give feedback that produces a new version
- Track production stages
- Access Series Bible references
- Access shared assets
- Respond to change requests
- Submit panels for review

Artists may work through stages such as the following (the Color stage applies only when colour mode is enabled; the core demonstration is black-and-white manga):

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
- Ask the Letterer Agent to shorten dialogue to fit a balloon within a length limit (core)
- Ask the Letterer Agent to prepare translations within the balloon length limit (extended scope)

The system should provide warnings, computed by code, for:

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
- Review the visual consistency report that compares a panel's depiction of a character with the locked character sheet, and triage each deviation
- Run the Editor Agent to draft a review report and a rejection comment from the findings gathered by the system, then edit and send it (the decision to approve or reject always remains with the Editor)

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

A publisher is treated as an External Reviewer: it is not a Workspace member, has no production role, and gives feedback only through the preview link. The Producer has no relationship with a publisher, and any contact takes place outside PanelForge.

External reviewers shall be able to:

- Open a time-limited preview link issued to their email address
- Access watermarked content (the watermark shows the recipient)
- Read the preview of a Locked chapter
- Provide chapter-level feedback
- Provide page-level feedback
- Provide panel-level feedback

Every piece of feedback is bound to the exact chapter, page or panel version being previewed.

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

> **Decision: extra feature outside the registered scope.** The registration form V1.2 states that publication takes place outside the platform and does not include a Public Reader, a public catalog or paid subscriptions. The team keeps this section, which describes the BR-23 catalog that already exists in the backend, as an additional feature. It is not part of the registered scope or of acceptance, and it does not change the fact that the Producer has no relationship with a publisher.

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
- View the AI provenance trail of any content the user is authorised to access

---

# 4. Core Domain Requirements

## 4.1 Hierarchy Integrity

The system shall enforce:

```text
Studio Workspace
  ↓
Series
  ↓
Chapter ─┬─ Page ── Panel ── Element       (physical tree)
         └─ Scene ── Script block          (narrative structure)

Panel ──references──▶ Script block
```

An Element must belong to exactly one Panel.

A Panel must belong to exactly one Page, and to at most one Scene.

A Page must belong to exactly one Chapter. A Page does not belong to a Scene; a Page may hold Panels of different Scenes.

A Scene must belong to exactly one Chapter. A Scene is a contiguous, non-overlapping run of Script blocks and is optional for manual work, so Pages and Panels can be produced without Scenes; the agent workflows, such as the Writer Agent drafting a scene script, operate on a Scene.

A Script block must belong to exactly one Scene and may reference at most one Panel. Script blocks can exist before any Page or Panel does. Re-paginating a Page changes only which Panel a block is assigned to.

A double-page spread is modelled as a link between two Pages, a splash is a Page with one Panel, and borderless panels are a display property rather than structure.

A reusable frame template is only a starting configuration: it initialises the Panels of a new Page, and once the Page exists its Panels are independent entities, so later changes to a template never alter existing Pages.

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

Reading order is a property separate from containment, because balloons may overlap several Panels and spreads span two Pages. A Series flag sets the reading direction (left-to-right or right-to-left).

---

## 4.4 Version Integrity

Committed versions shall not be silently overwritten.

Previous versions must remain reconstructable.

---

## 4.5 Series Bible Integrity

Series Bible information must remain versioned and traceable.

Changes to continuity-critical information should be auditable.

A character sheet version approved by the Producer is locked: it can only be superseded by a new version taking effect from a chosen chapter, never edited in place (see 5.10).

---

# 5. AI Requirements

## 5.1 Script-to-Structure Breakdown

The Storyboard Agent shall support AI-assisted conversion of an approved script into proposed:

- Pages
- Panels, with reading order and shot scale (wide, medium, close-up)
- Panel descriptions
- Links from each Panel to the source Script blocks

The Writer Agent drafts the script lines of a scene from a brief, the chapter outline and the Series Bible revision in force. The generated structure and script must be reviewable by a human.

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

This feature is a checking aid. Its findings are suggestions for the Editor, not automatic rejections. It is the visual layer of the three-layer character consistency check (5.11).

---

## 5.5 Balloon-Fit Analysis

The system may identify:

- Text overflow
- Artwork overlap
- Poor text placement
- Reading-order issues

These checks are deterministic and are performed by code, not by the model.

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

To guarantee this, the AI agents run in a service separate from the core backend, are never invoked inside a core business request, and are protected by timeouts (30 seconds, proposed and to be confirmed with the supervisor), a circuit breaker and a fallback provider. An interrupted run keeps its completed steps and resumes from the failed one.

---

## 5.8 AI-Generated Draft Images

The system may generate character images from a locked character sheet and an artist's sketch (the character image pipeline, 5.10), and may generate draft images for a Panel from its script description, Series Bible references, and Series style rules.

AI generates character images only. Backgrounds, page layout and balloons are assembled in the editor from structured Elements, and generating a whole scene or a whole page is outside the core scope.

- A generated image is an AI Suggestion. It never becomes the Panel's artwork until a human accepts it (5.6).
- Generation is asynchronous (6.4) and the result is stored as a new artwork version; no existing version is overwritten.
- The generation request and result record provenance (section 8): model, parameters, prompt and reference inputs, output asset, accepting user, and human modifications.
- Generated images are intended as drafts and references (for example Thumbnail-stage drafts or character images), not as a replacement for the Artist's work.
- The Administrator controls which image providers and models are enabled and their quotas (3.1). Workspace content is sent only to providers the Administrator has configured.
- The image model is called through a replaceable adapter. It may be the API of a hosted provider other than Gemini (the Gemini API has no free tier, so the provider and its cost are to be confirmed by the team) or a locally run model (which needs a GPU with 8 to 12 GB of memory). Image-generation quota is configured per Workspace (3.1).
- If generation is unavailable, uploading artwork manually remains fully usable (5.7).

---

## 5.9 Background Removal

An Artist may upload an image and request background removal.

- The result is stored as a new asset derived from the original; the original is retained unchanged.
- The derived asset records its source asset, the operation, and the tool or model used (section 8).
- The result may be used as an `ARTWORK_LAYER` or character element on a Panel after the Artist reviews and accepts it.
- Processing is asynchronous for large images (6.4).
- Background removal does not require an external provider if a self-hosted model is used; the choice is a deployment decision.
- In the character image pipeline this is the segmentation (cut-out) step. For an image that is already flattened, the core scope only separates a character from a simple background, with a person checking the result; it does not promise to decompose a whole page automatically into semantic Elements such as characters, background, balloons and sound effects. An Artist can also upload each layer independently and declare its Element type.

---

## 5.10 Character Sheet and Character Image Pipeline

A **character sheet** holds artist-supplied sketches of actions, emotions and movements, with personality, speech style, drawing style and colour mode (black and white in the core, colour as an extension). It is approved and locked as the single reference and is versioned per chapter. A new sheet version takes effect from a chosen chapter and, once locked, can only be superseded, never edited. The agents and the checks always read the version in force at the chapter in progress.

The character image pipeline has seven steps:

1. The artist selects a locked character sheet.
2. The artist uploads a sketch of the character.
3. The artist requests an image in a chosen pose, emotion, shot scale, drawing style and colour mode; the image model generates it with the sheet and the sketch as mandatory references.
4. A segmentation step cuts the character out of its plain background.
5. The system compares the result with the reference sheet and scores its similarity.
6. The artist accepts, edits or rejects the result.
7. An accepted result is stored as an independent artwork-layer Element and as a new content revision, so that it can be moved, versioned and replaced separately from the background and from the text balloons.

If the cut-out fails, or the artwork was drawn outside the platform, the artist can upload the layers manually and the same similarity check applies. The platform stays fully usable without generation: the sheet lock, the consistency checks and manual upload do not depend on it.

---

## 5.11 Character Consistency

Character consistency is the central quality goal. It is secured in three layers:

- **Textual:** the Continuity Agent checks scripts against the character sheet and cites its evidence (the Bible revision and the script line).
- **Rule-based:** consistency rules run by code.
- **Visual:** every submitted or generated depiction is compared with the reference sheet and flagged when it deviates.

Consistency must be measured, not asserted. Findings are evaluated on a seeded-defect set in which textual and visual deviations are injected on purpose, reporting precision and recall (target: at least 70% each, proposed and to be confirmed with the supervisor). Each generated or submitted depiction receives a similarity score against the reference sheet, and any score below a configurable threshold (0.75 on a 0-1 similarity score, proposed and to be confirmed with the supervisor) is flagged for human review. The platform does not claim perfect consistency: deviations are surfaced, never hidden.

---

## 5.12 Multi-Agent AI Assistance Layer

A team of role-based agents shares one language model through a single gateway that enforces the Workspace quota and records usage. Each agent has exactly one responsibility with a defined input and output.

| Agent | Scope | Input | Output |
|---|---|---|---|
| Writer | Core | Brief, chapter outline, Bible revision in force, earlier scripts | Script lines of a scene |
| Continuity | Core | Script, Bible | Findings, each citing its evidence |
| Storyboard | Core | Approved script | Pages and Panels with reading order, shot scale and links to source script lines |
| Editor | Core (in the first stage it only assembles findings and a checklist prepared by code) | Findings and checklist assembled by code | Draft review report and rejection comment |
| Artist | Core, separate image flow | Locked character sheet, sketch, requested pose, emotion and shot scale | Request prepared for the character image pipeline |
| Letterer | Core for dialogue shortening, extended for translation | Dialogue, balloon length limit | Shortened dialogue; translations in the extended scope |
| Producer | Extended | Progress | Explanation of progress and bottlenecks |

The main workflow is Writer, Continuity (at most two repair loops back to Writer), Storyboard, Editor. The agents never change a stage or approve a gate, and every output is a suggestion that a human must accept, edit or reject. Deterministic checks such as panel geometry, permissions, deadlines, balloon overflow and consistency rules are performed by code, not by the model.

The Series Bible is the shared memory of the agents and is read as versioned memory: the entries in the revision in force at the chapter being worked on are put into the agent context through a structured query, and vector retrieval is added when that context exceeds the model context budget (threshold to be agreed with the supervisor). The context of each run is captured as an immutable snapshot, so a finding can cite the exact Bible revision it relied on.

Agents are evaluated on a seeded-defect dataset against a single-prompt baseline: precision and recall of continuity findings, validity of storyboard output, and acceptance rate and correction effort in human review.

---

## 5.13 Dialogue Fitting

Balloon overflow, artwork overlap and reading-order problems are warned about by code. The Letterer Agent shortens dialogue so that it fits a balloon within a length limit (core). Translation of dialogue into other languages within the balloon length limit is the extended scope.

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

Provenance must survive export, so the claim remains verifiable outside the platform.

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

Usability is measured in a usability test with target users, with a Task Success Rate of at least 80% on the main flows (proposed and to be confirmed with the supervisor).

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

## 6.10 Workspace Resource Governance

Each Workspace must operate within the limits the Administrator sets for it: storage in GB, monthly AI token quota, an image-generation quota (a number of images or a budget) and the session policy. A limit is checked before the operation it governs (an upload, an AI call, an image generation, a sign-in or token renewal), and exceeding it returns a clear error instead of a silent failure or an unbounded cost. Current usage is visible to the Producer and the Administrator on a usage screen.

---

## 6.11 Measurable Character Consistency

See 5.11. Consistency is measured on a seeded-defect set (precision and recall) and by a similarity score against the reference sheet; the target values are proposed and to be confirmed with the supervisor.

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

Artwork for an unpublished chapter is commercially sensitive. Assets must be reachable only through short-lived, revocable links tied to the requester's assignment or role, external previews must be watermarked and expiring, and all access must be logged.

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

For AI, a provenance chain runs from the Workflow Run to its Agent Steps (agent, model, parameters, prompt version, input revisions), to the Suggestions they produce, to the Human Decision on each (accepted, edited or rejected, by whom, and how much was changed) and finally to the Content Revision that results. Rejected suggestions are stored as well. The original AI output is never altered; an edit creates a derived revision, and a page or chapter that is rejected reopens as a new version while the earlier versions are kept. The system can therefore answer which agent, working from which script and Bible revisions, produced a given panel and who approved it.

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