# Capstone Project GFA26SE140 - PanelForge


## 1. Overviews

| CAPSTONE PROJECTS INFORMATION SHEET |  |  |  |  |  |  |  |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Project Code | FA26SE251 |  |  |  |  |  |  |
| Group Code | G-05 |  |  |  |  |  |  |
| Project Name (EN) | PanelForge: An AI-Assisted Manga Production Management Platform with a Structured Content Model and Full Revision Provenance |  |  |  |  |  |  |
| Project Name (VN) | PanelForge: Nền tảng quản lý sản xuất truyện tranh Manga theo mô hình nội dung có cấu trúc, hỗ trợ cộng tác và truy vết phiên bản với AI |  |  |  |  |  |  |
| Supervisor 1 | Nguyễn Tấn Phúc (phucnt40@fpt.edu.vn) |  |  |  |  |  |  |
| Stakeholders | Manga/Comic Creators & Studios, Writers, Comic/Manga Artists, Letterers, Editors, Producers/Series Owners, External Reviewers/Readers. |  |  |  |  |  |  |
| Student 1 | Bui Ngoc Tam |  |  |  |  |  |  |
| Student 2 | Huynh Cong Hoa |  |  |  |  |  |  |
| Student 3 | Nguyen To Trung Kien |  |  |  |  |  |  |
| Student 4 | Tran Huynh Bao Minh |  |  |  |  |  |  |
| Student 5 | Pham Thanh Danh |  |  |  |  |  |  |
| Context | Distributed manga and comic production teams rely on fragmented, generic tools that store creative work as disconnected binary files. |  |  |  |  |  |  |
| Problems / Challenges | Unstructured content representation; detached review feedback and lost revision history; story continuity drift and untracked AI provenance. |  |  |  |  |  |  |
| Proposed Solutions | Structured content model, Series Bible for information storage, collaborative pipeline, panel-anchored review, AI-assisted extraction and verification, and an append-only event log for version tracking. |  |  |  |  |  |  |
| Assumptions & Constraints |  |  |  |  |  |  |  |
| ID | Type | Description | Impact / Notes |  |  |  |  |
| A-01 | Assumption | Graceful Degradation: If the AI ​​service fails, the manual workflow must still operate normally. | Requires the design of a circuit breaker mechanism and asynchronous processing. |  |  |  |  |
| C-01 | Constraint | No destructive updates: No saved version is allowed to be silently deleted or overwritten. | The append-only architecture (Event Sourcing) is mandatory. |  |  |  |  |
| C-02 | Constraint | No Commercial Copyright Ingestion: Demo data must be copyright-free or original work. | It is prohibited to upload copyrighted commercial comics to the platform. |  |  |  |  |


## 2. Busiess Rules

| No. | Rule ID | Business Rule Description | Category | Rationale / Purpose | Source | Core Flow |
| --- | --- | --- | --- | --- | --- | --- |
| 1.0 | BR-01 | Immutability: Data follows an append-only mechanism; stored versions are neither deleted nor overwritten. | Data / Database | Preserve edit history and ensure integrity tracking. | Document | CF1, CF4 |
| 2.0 | BR-02 | Human Decision in AI: AI results are merely "suggestions"; human intervention is mandatory for approval. | Process / AI | Ensure human oversight of final quality. | Document | CF2, CF3 |
| 3.0 | BR-03 | Strict Hierarchical Containment: Element belongs to Panel, Panel belongs to Page, Page belongs to Scene, Scene belongs to Chapter, Chapter belongs to Series. | DataStructural / Validation | There are no "orphaned" entities lacking a valid relationship. | Document | CF1, CF2 |
| 4.0 | BR-04 | Content-Addressable Invariant: Every uploaded file is identified by an SHA-256 hash; the client computes it and the server recomputes and verifies it before accepting the file. | Data / Storage | Deduplication and integrity verification of binary assets. | Document | CF2, CF4 |
| 5.0 | BR-05 | Dialogue Line Single-Binding: Each dialogue line is assigned a maximum of one Balloon or Text element. | Validation | This prevents dialogue duplication and manages dialogue splitting. | Document | CF2 |
| 6.0 | BR-06 | Guarded Transitions: State transitions (e.g., from Ink to Color) must pass through approval gates. | Process / Workflow | Freeze the previous process stage to Read-Only; a rollback is required to make edits. | Document | CF2, CF3, CF5 |
| 7.0 | BR-07 | Two-Level Access Control: (1) SystemRole (Administrator \| User), stored on the User, only distinguishes platform administration from normal accounts and grants no content rights by itself. (2) WorkspaceRole (Producer, Writer, Artist, Letterer, Editor), stored per user in WorkspaceMember (workspace_members.role), grants access and actions inside that specific Workspace. Every workspace action is permitted only if the acting User has a WorkspaceMember row for that Workspace with a Role that allows it; there is no implicit access.<br> | Security / Access Control | Prevent unauthorized access to unpublished creative work. | Document | All |
| 8.0 | BR-08 | Non-Destructive Rollback: Restoring a previous version creates a new version at the head of the history; it never deletes the versions in between. | Data / Versioning | Guarantee that the full revision history remains reconstructible at all times. | Document | CF4 |
| 9.0 | BR-09 | Preview Link Expiry: A time-limited preview link becomes permanently invalid once its expiration time passes, regardless of who holds the URL. | Security / Access Control | Protect unreleased artwork from leaking after the intended review window. | Document | CF5 |
| 10.0 | BR-10 | Mandatory AI Provenance: An AI-generated suggestion cannot be merged into the working content until its provenance record (model, parameters, prompt, input context) is stored. The record is stored as soon as the AI returns its result, and rejected suggestions are recorded too. | Process / AI | Ensure every AI contribution remains traceable and auditable after acceptance. | Document | CF2, CF3, CF4 |
| 11.0 | BR-11 | Optimistic Concurrency: An update to an entity is rejected if the version the user is editing is not the current version; the conflict must be surfaced, never silently overwritten. | Process / Concurrency | Prevent lost updates when multiple contributors edit the same Page at once. | Document | CF2, CF3, CF4 |
| 12.0 | BR-12 | Approval Lock: Once a Producer approves and locks a Chapter, its Pages and Panels become read-only until the Producer explicitly reopens (unlocks) the Chapter with a recorded reason. | Process / Workflow | Protect an approved chapter from accidental post-approval edits. | Document | CF4, CF5 |
| 13.0 | BR-13 | Asset De-duplication: If an uploaded file's SHA-256 hash matches an existing Asset, the system links the new reference to the existing Asset instead of storing a duplicate copy. | Data / Storage | Control storage growth as a long-running series accumulates versions. | Document | CF2, CF4 |
| 14.0 | BR-14 | Mandatory Assignment Notification: Creating or reassigning a task must generate a notification to the assigned user before the task is considered active. | Process / Notification | Ensure no assignment is missed by the responsible contributor. | Document | CF2 |
| 15.0 | BR-15 | Chapter-Versioned Series Bible: Every change to a Series Bible entry carries an effective-from Chapter. A consistency check on Chapter N uses the entry versions effective at Chapter N, and a retroactive correction is stored as a new version, never as an overwrite. | Data / Versioning | Avoid false continuity findings when a fact legitimately changes later in the series, while keeping every earlier state reconstructible. | Document; Core Flow analysis | CF1, CF3 |
| 16.0 | BR-16 | Short-Lived Asset Access: Unreleased assets are served only through short-lived, revocable links tied to the requester's assignment or role, and every access is logged. | Security / Access Control | Prevent leaks of unpublished artwork and allow investigation after an incident. | Document | CF2, CF5 |
| 17.0 | BR-17 | Recipient-Bound Preview: A preview link is issued only for a Locked Chapter, to a named recipient email; its watermark shows that recipient and the Producer can revoke it before expiry. Every access attempt, including denied ones, is logged. | Security / Access Control | Make any leak traceable to a recipient, let Readers see exactly the version that will be exported, and allow early revocation. Extends BR-09. | Core Flow analysis | CF5 |
| 18.0 | BR-18 | Complete Audit Trail: Every state-changing operation records the actor, the timestamp and what changed; an operation that cannot be recorded is rejected. | Data / Audit | Provenance is only trustworthy when it is complete, and it must survive export. | Document | CF4 |
| 19.0 | BR-19 | Stage Dependency per Page/Panel: A stage may start only when the preceding stage of the same Page or Panel is approved; different Pages and Panels progress independently and in parallel. | Process / Workflow | Allow parallel production across Pages while keeping the required order inside each Page. Complements BR-06. | Core Flow analysis | CF2, CF3 |
| 20.0 | BR-20 | Stage Gate Approval: An item leaves a stage only when the gate of that stage passes (Script, Artwork, Lettering, Consistency). The Editor approves gates by default and the studio may configure another approver per stage. A lettering overflow warning may be acknowledged with a recorded reason instead of blocking submission. | Process / Workflow | Give every stage a clear, auditable pass condition without letting false warnings stall production. Complements BR-06. | Core Flow analysis | CF2, CF3 |
| 21.0 | BR-21 | Finding Triage: Every consistency finding ends as Confirmed, Rejected, Ignored or Needs clarification; Ignored requires a recorded reason. | Process / AI | Keep the human decision on every AI finding explicit and auditable (BR-02). | Core Flow analysis | CF3 |
| 22.0 | BR-22 | Studio Creation Permission: A User may create a new Workspace (Studio) only if an Administrator has granted that User the CanCreateStudio permission. On creation, the system automatically inserts a workspace_members row for the creator with role = Producer. | Process / Security | Keep Studio creation an Administrator-controlled privilege instead of a right every registered User has by default. | Core Flow analysis (this session) | CF1 |
| 23.0 | BR-23 | Workspace Visibility Default: After login the system reads the User's SystemRole. An Administrator lands on the Admin dashboard. A User lands on "Your Workspaces," listing only the Workspaces where a workspace_members row exists for that User (i.e., Studios a Producer invited them into). A User with no workspace_members row and without CanCreateStudio sees no Studio and is directed to the public catalog of Published Series. | Process / Access Control | Match the landing screen to what BR-07 and BR-22 actually allow the User to do, and avoid exposing Studios the User has no membership in. | Core Flow analysis (this session) | CF1 |
| 24.0 | BR-24 | Public Reader Access: A Published Series is discoverable in a public catalog, including the Mobile app, independent of Workspace membership. An external Reader may register a separate Reader account and, if the Series owner enables paid access, subscribe to follow and read every Published chapter of that Series. This is distinct from a PreviewLink (BR-09, BR-17), which grants a named recipient one-time, time-limited access to a pre-publish Locked chapter without any account. | Business / Access Control | Support the Studio -> Publish -> Reader-registers-and-reads growth loop on Mobile, separately from the internal pre-publish review mechanism. | Core Flow analysis (this session) | CF5 |


## 3. User Stories

| No. | Story ID | As a… | I want… | So that… | Priority | Core Flow |
| --- | --- | --- | --- | --- | --- | --- |
| 1.0 | US-01 | Producer | Create and manage a series bible. | Humans and AI have an encyclopedia to cross-reference the consistency of characters and settings. | High | CF1 |
| 2.0 | US-02 | Writer | Write a structured script and use AI for the breakdown. | I can quickly assign dialogue directly to the characters. | High | CF2 |
| 3.0 | US-03 | Artist | Plan the layout and upload artwork images to each specific panel or page. | Progress is tracked in detail at the frame level rather than the master file level. | High | CF2 |
| 4.0 | US-04 | Editor | Draw bounding boxes and add comments directly onto specific details within the storyboard version. | The feedback is bound to the specific area requiring modification. | High | CF3 |
| 5.0 | US-05 | Letterer | Link the text to the original script within the speech bubble. | I can receive automatic alerts if the text overflows (Balloon Overflow). | Medium | CF2 |
| 6.0 | US-06 | Reader | receive a watermarked preview link with a limited time-to-live (TTL) | I can read a preview while the draft remains secure and protected against leaks. | Medium | CF5 |
| 7.0 | US-07 | Administrator | grant or revoke a User's permission to create a Studio (CanCreateStudio), and manage user accounts. | Studio creation stays an administrator-controlled privilege instead of something every registered User can do (BR-22). | High | CF1 |
| 8.0 | US-08 | Administrator | configure AI provider credentials, model selection and usage quota per workspace. | AI spending and provider access stay under control. | Medium | CF1 |
| 9.0 | US-09 | Administrator | view the system-wide audit log and AI usage report. | I can investigate incidents and monitor compliance. | Medium | CF1 |
| 10.0 | US-10 | Producer | monitor a production dashboard with per-stage progress and overdue items. | I can find the real bottleneck before a deadline is at risk. | High | CF2 |
| 11.0 | US-11 | Producer | approve or reject a submitted chapter and lock it once approved. | an approved chapter cannot be modified by accident. | High | CF5 |
| 12.0 | US-12 | Producer | restore any entity to a previous version. | a mistaken edit or overwrite can always be undone. | Medium | CF4 |
| 13.0 | US-13 | Writer | link script entities such as characters and locations to the Series Bible. | the script stays consistent with established facts. | Medium | CF2 |
| 14.0 | US-14 | Writer | compare two versions of a script and restore an earlier revision. | I can recover earlier dialogue after an unwanted change. | Medium | CF4 |
| 15.0 | US-15 | Artist | request an AI layout suggestion for a page. | I have a starting composition instead of a blank grid. | Medium | CF2 |
| 16.0 | US-16 | Artist | see a personal task queue with the brief, references and deadline of each assignment. | I always know what to work on next. | High | CF2 |
| 17.0 | US-17 | Editor | run an automated consistency check against the Series Bible before approving a chapter. | continuity errors are caught before publication, not after. | High | CF3 |
| 18.0 | US-18 | Editor | issue a change request from a comment and track it through to resolution. | requested fixes are not lost in chat. | Medium | CF3 |
| 19.0 | US-19 | Reader | leave chapter-level or page-level feedback without accessing the production workspace. | I can give input without seeing unfinished internal work. | Low | CF5 |
| 20.0 | US-20 | Authenticated user | search across scripts, panels, Series Bible entries and comments. | I can find what I need without browsing folder by folder. | Medium | All |
| 21.0 | US-21 | Producer | Create a Series and configure its pipeline stages, roles and release calendar. | the studio's own workflow is modelled in the platform without any code change. | High | CF1 |
| 22.0 | US-22 | Producer | Assign a Panel, Page or Chapter to a team member with a deadline and dependencies. | every piece of work has a clear owner and due date and appears in that person's task queue. | High | CF2 |
| 23.0 | US-23 | Producer | Export an approved chapter as print-ready pages, PDF, CBZ, vertical-scroll webtoon and open JSON. | the chapter can be delivered in standard formats and the studio can take its structured data out of the platform. | Medium | CF5 |
| 24.0 | US-24 | Authenticated user | Receive in-app and email notifications for assignments, mentions, reviews and deadlines. | I never miss work that is waiting on me. | Medium | All |
| 25.0 | US-25 | Authenticated user | View the version history of an entity and compare any two versions side by side. | I can see who changed what and when, and judge whether to restore an earlier version. | High | CF4 |
| 26.0 | US-26 | Producer | Review the provenance report of a chapter. | I can state exactly which parts were AI-assisted and how much a human modified them. | High | CF4 |
| 27.0 | US-27 | Writer / Artist / Letterer | Respond to a change request and mark it resolved with a new version. | the Editor can verify that the requested fix was made in exactly the area that was flagged. | Medium | CF3 |
| 28.0 | US-28 | Producer | Reopen a locked chapter with a recorded reason. | corrections can be made after approval or after external feedback without losing any history. | Medium | CF5 |
| 29.0 | US-29 | Producer / Editor | Triage Reader feedback into a change request or a note for a later version. | useful feedback is acted on without being lost, and the chapter is only reopened when a change is really needed. | Medium | CF5 |
| 30.0 | US-30 | Reader (Mobile, external) | register a Reader account, browse the public catalog and subscribe to a Published Series. | I can follow and keep reading a studio's work as new chapters are published, paying for access where the studio requires it. | Medium | CF5 |


## 4. Usecases

| No. | Use Case ID | Use Case Name | Actor | Description | Preconditions | Main Flow | Alternate Flow | Postconditions | Related Req. | Priority | Status | Core Flow |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1.0 | UC-01 | Manage User Account & Role | Administrator | Administrator creates, edits or deactivates a user account and assigns the user's role within a workspace. | Administrator is authenticated with admin privileges. | 1. Admin opens User Management → 2. Selects/creates a user → 3. Sets role and workspace membership → 4. System saves and notifies the user. | Duplicate email → system shows error and blocks creation. | User account exists with the correct role and access rights. | FR-Admin-01 | High | Draft | CF1 |
| 2.0 | UC-02 | Configure AI Provider & Quota | Administrator | Administrator sets the AI provider credentials, selected model and monthly usage quota for a workspace. | Administrator is authenticated; workspace exists. | 1. Admin opens AI Settings → 2. Enters provider key and selects model → 3. Sets quota → 4. System validates and saves. | Invalid credentials → system rejects and shows connection error. Quota exhausted or provider unavailable → AI features are disabled for the workspace and the manual path stays available. | Workspace is configured to call the AI provider within quota. | FR-Admin-02 | Medium | Draft | CF1 |
| 3.0 | UC-03 | Create Series & Configure Pipeline | Producer | Producer creates a new Series and defines its pipeline stages, roles and release calendar. | Producer is authenticated and owns a workspace. | 1. Producer opens New Series → 2. Enters series metadata (genre, format, reading direction) → 3. Defines pipeline stages, roles and release calendar → 4. Invites members and assigns their roles → 5. Enables consistency rules and typography presets → 6. System creates the Series. | Missing required field → system highlights the field and blocks save. | A new Series exists with a configured production pipeline, its members and their roles. | FR-Producer-01, FR-Producer-03 | High | Draft | CF1 |
| 4.0 | UC-04 | Maintain Series Bible | Producer | Producer creates or updates characters, locations, props and terminology entries in the Series Bible. | Series exists; Producer has edit rights. | 1. Producer opens Series Bible → 2. Adds or edits an entry → 3. System stores the change as a new entry version effective from a chapter and saves. | Conflicting concurrent edit → system flags the version conflict for resolution. Retroactive correction → stored as a new version effective from an earlier chapter; earlier versions stay readable. | Series Bible entry is created or updated and versioned; no earlier version is overwritten. | FR-Producer-02 | High | Draft | CF1 |
| 5.0 | UC-05 | Assign Task & Deadline | Producer | Producer assigns a Panel, Page or Chapter to a team member with a deadline and dependencies. | Series and team members exist. | 1. Producer selects an item → 2. Chooses assignee, deadline, priority and dependencies → 3. System saves and notifies the assignee before the task becomes active. | Assignee unavailable/overloaded → system warns but allows override. Dependency would create a cycle → system rejects it. | Assignment exists and appears in the assignee's task queue. | FR-Producer-03 | High | Draft | CF2 |
| 6.0 | UC-06 | Write Structured Script & Request AI Breakdown | Writer | Writer drafts a chapter script in structured blocks and requests an AI breakdown into Scenes, Pages and Panels. | Chapter exists; Writer is assigned to it. | 1. Writer drafts the script and links entities to the Series Bible → 2. Requests AI breakdown, or breaks the script down manually → 3. AI proposes structure and its provenance is stored as soon as it returns → 4. Writer accepts, edits or rejects the result and the decision is recorded → 5. Writer submits the Script stage for review. | AI service unavailable, slow or over quota → Writer continues breaking down the script manually. Concurrent edit of the same script → version conflict is surfaced. | Script is saved and, if accepted, Scenes/Pages/Panels and script lines are created with provenance recorded; the Script stage is Submitted. | FR-Writer-01, FR-Writer-02, FR-Writer-03, FR-Writer-04 | High | Draft | CF2 |
| 7.0 | UC-07 | Plan Layout & Upload Artwork | Artist | Artist plans the panel layout for a page and uploads artwork versions for review. | Artist is assigned to the Page/Panel and the previous stage of that Page/Panel is approved. | 1. Artist opens the task (brief, references, Series Bible) → 2. Plans layout or requests AI layout suggestion (extended scope) → 3. Uploads artwork for the current stage; the client hashes it and the server verifies the hash → 4. Submits the stage for review. | Duplicate hash → system reuses the existing Asset via a new reference. Client and server hashes differ → upload is rejected. | A new artwork Version exists and the item is Submitted for review; the stage advances only after gate approval. | FR-Artist-01, FR-Artist-02, FR-Artist-03, FR-Artist-04, FR-Artist-05 | High | Draft | CF2 |
| 8.0 | UC-08 | Place & Bind Text Elements | Letterer | Letterer places dialogue balloons and sound-effect elements on the panel canvas and binds each to its source script line. | The Color stage of the Panel is approved and script lines are available. | 1. Letterer places a balloon, caption or sound effect → 2. Binds it to a script line/speaker → 3. System checks text overflow, reading order and overlap with the Key Artwork Region → 4. Letterer resolves the warnings, or acknowledges them with a reason → 5. Submits the stage for review. | Text overflows balloon → system raises a warning for the Letterer to resize or edit; it does not hard-block submission. | Text Elements are placed, bound and free of unresolved warnings (or the warnings are acknowledged with a reason). | FR-Letterer-01, FR-Letterer-02, FR-Letterer-03, FR-Letterer-04 | Medium | Draft | CF2 |
| 9.0 | UC-09 | Review Submission & Annotate | Editor/Reviewer | Editor reviews a submitted Panel, Page or Chapter, compares it with an earlier version and attaches region-anchored comments. | Item has been submitted for review. | 1. Editor opens the submission (status In Review) → 2. Compares versions side by side → 3. Attaches comments/annotations anchored to the version and region → 4. Approves the stage gate or issues a Change Request. | Editor issues a Change Request → item returns to Changes Requested; after resolution it is Resubmitted and reviewed again. | Item is Approved and the next stage opens, or a Change Request is logged against it. | FR-Editor-01, FR-Editor-02, FR-Editor-04 | High | Draft | CF3 |
| 10.0 | UC-10 | Run AI Consistency Check | Editor/Reviewer | Editor triggers an automated check of a chapter against the Series Bible for character, terminology and continuity errors. | Series Bible exists; chapter content is submitted for review. | 1. Editor requests a consistency check → 2. AI service scans content against the Series Bible version effective at this chapter and the enabled rules → 3. System lists findings → 4. Editor triages each finding as Confirmed, Rejected, Ignored (reason required) or Needs clarification. | AI service unavailable → system informs the Editor and allows a fully manual review to proceed; the review record notes that no AI findings exist. | A triaged list of continuity findings is attached to the chapter's review record. | FR-Editor-03 | Medium | Draft | CF3 |
| 11.0 | UC-11 | Generate Time-Limited Preview Link | Producer | Producer generates a watermarked, expiring preview link so a Reader can view a chapter without workspace access. | Chapter is Approved and Locked, and Producer has sharing rights. | 1. Producer selects Generate Preview → 2. Enters the recipient email and expiry → 3. System creates a watermarked link bound to that recipient → 4. Producer shares the link. | Link accessed after expiry, or after revocation → system denies access and logs the attempt. | A logged, revocable preview link is available until it expires or is revoked. | FR-Reader-01 | Medium | Draft | CF5 |
| 12.0 | UC-12 | Export & Deliver Chapter | Producer | Producer exports an approved chapter into page images, PDF, CBZ, vertical-scroll webtoon or open JSON format. | Chapter is approved and locked. | 1. Producer selects Export → 2. Chooses an export preset (page images, PDF, CBZ, webtoon or JSON) → 3. System renders the export job asynchronously from the Locked version and shows its status → 4. Producer downloads the result. | Rendering fails → system reports the error and allows a retry; the chapter stays Locked. | An export file in the chosen format is available for download; the JSON export carries the provenance. | FR-Producer-08 | Medium | Draft | CF5 |
| 13.0 | UC-13 | Restore Previous Version (Rollback) | Producer (Writer for own script) | Producer restores any entity, or a Writer restores an earlier revision of their own script, to an earlier recorded version after an unwanted change. | Entity has at least one prior version in its history. | 1. User opens version history → 2. Selects a prior version → 3. Confirms restore → 4. System creates a new current version copying that state. | Entity is locked/approved → the Producer must first reopen (unlock) the chapter with a reason (UC-23) before restore proceeds. | A new version equal to the selected past state becomes the current version; no history is deleted. | FR-Producer-06, FR-Writer-05 | Medium | Draft | CF4 |
| 14.0 | UC-14 | Receive Notification on Assignment or Review | All authenticated users | System notifies a user in-app and by email when they are assigned, mentioned, or when a review/deadline event affects them. | User has an active account with notification preferences set. | 1. A triggering event occurs (assignment, mention, review, deadline) → 2. System generates a Notification → 3. User receives in-app/email alert. | User has muted that notification type → system suppresses the alert but still logs the event. | A Notification record exists and is visible in the user's activity feed. | FR-All-02 | Low | Draft | All |
| 15.0 | UC-15 | View Audit Log & AI Usage Report | Administrator | Administrator reviews the system-wide audit log and the AI usage report of a workspace to investigate incidents and monitor spending and compliance. | Administrator is authenticated with admin privileges. | 1. Admin opens Audit & Usage → 2. Filters by workspace, user, entity, action or date range → 3. System lists audit events and AI usage against quota → 4. Admin exports the filtered view if needed. | No record matches the filter → system shows an empty result. | The requested audit and usage data is displayed; no data is modified. | FR-Admin-04 | Medium | Draft | CF1 |
| 16.0 | UC-16 | Monitor Production Dashboard | Producer | Producer monitors per-stage progress down to Panel level, workload per member, overdue items and burndown to find the bottleneck before a deadline is at risk. | Series exists and has assignments. | 1. Producer opens the Dashboard for a Series or Chapter → 2. System shows progress per pipeline stage, workload per member, overdue items and burndown → 3. Producer drills into a bottleneck Page or Panel → 4. Producer reassigns work or adjusts a deadline (UC-05). | No assignments yet → system shows an empty dashboard with a hint to assign work. | Producer knows where the bottleneck is; no data is modified unless the Producer reassigns work. | FR-Producer-04 | High | Draft | CF2 |
| 17.0 | UC-17 | Resolve Change Request | Writer / Artist / Letterer | The assignee opens a Change Request raised by the Editor, reads the region-anchored comment, delivers a corrected version and marks the request as resolved. | A Change Request is assigned to the user. | 1. Assignee opens the Change Request from the task queue or a notification → 2. Views the annotated region on the target version → 3. Uploads or edits a new version → 4. Marks the request Resolved and resubmits the item for review. | Assignee disagrees → adds a reply comment and the Editor decides; the Producer arbitrates if they still disagree. | A new version exists, the Change Request is Resolved pending the Editor's confirmation, and the item is Resubmitted. | FR-Artist-05, FR-Editor-04 | Medium | Draft | CF3 |
| 18.0 | UC-18 | View Version History & Compare Versions | All authenticated users | User opens the version history of any entity they may access and compares two versions side by side. | User has access to the entity and the entity has at least two versions. | 1. User opens History → 2. System lists versions with author, time and an AI-assisted / human label → 3. User selects two versions → 4. System shows a side-by-side comparison (text diff or image comparison) and highlights AI-assisted parts. | Entity has only one version → system shows that version without a comparison. | User has viewed the history and comparison; no data is modified. | FR-All-04, FR-Writer-05, FR-Editor-01 | High | Draft | CF4 |
| 19.0 | UC-19 | View Chapter Provenance Report | Producer | Producer opens the provenance report of a chapter: every contributor and every AI-assisted artifact with model, parameters, input context, accepting user and the degree of subsequent human modification. | Chapter exists and has recorded history. | 1. Producer opens the Provenance Report of a Chapter → 2. System aggregates the event log → 3. System shows contribution per user, the AI-assisted share and the AI record of each artifact → 4. Producer exports the report together with the structured export. | Report is large → system generates it asynchronously and notifies the Producer when it is ready. | A provenance report is available; no data is modified. | FR-Producer-07, FR-05 | High | Draft | CF4 |
| 20.0 | UC-20 | Approve & Lock Chapter | Producer | Producer approves a chapter that has passed the Editor's final gate and locks it so that its Pages and Panels become read-only. | All Pages have passed the final review gate and no Change Request is open. | 1. Producer opens the chapter approval screen → 2. System shows gate status, open Change Requests and consistency findings → 3. Producer approves the chapter → 4. Producer locks the chapter and the system makes its Pages and Panels read-only and records the event. | Open Change Requests or unresolved findings exist → system blocks approval and lists them. Producer rejects → chapter stays InProduction and the reason is recorded. | Chapter is Approved and Locked; edits are rejected until it is reopened (UC-23). | FR-Producer-05 | High | Draft | CF5 |
| 21.0 | UC-21 | Read Preview & Submit Feedback | Reader | An external Reader opens a time-limited, watermarked preview link, reads the chapter in reading mode and leaves chapter-level or page-level feedback without accessing the production workspace. | Reader holds a valid, unexpired and non-revoked preview link. | 1. Reader opens the link → 2. System validates token, expiry and revocation and logs the access → 3. Reader reads the watermarked chapter in reading mode → 4. Reader submits feedback on the chapter or on a Page. | Link expired or revoked → system denies access and logs the attempt (see UC-11). | Feedback is stored against the PreviewLink and visible to the Producer; the access is logged. | FR-Reader-01, FR-Reader-02 | Medium | Draft | CF5 |
| 22.0 | UC-22 | Search Content | All authenticated users | User searches across scripts, panels, Series Bible entries and comments and opens a result. | User is authenticated and belongs to a workspace. | 1. User enters a query → 2. System searches only content the user may access → 3. System lists grouped results (scripts, panels, Bible entries, comments) → 4. User opens a result. | No result → system shows an empty state and suggests broadening the query. | User has located the item; results never include content outside the user's permissions. | FR-All-03 | Medium | Draft | All |
| 23.0 | UC-23 | Reopen (Unlock) Chapter | Producer | Producer reopens an approved or locked chapter, with a recorded reason, so that corrections can be made after Reader feedback or a discovered error. | Chapter is Approved or Locked and the Producer is authorised. | 1. Producer selects Reopen on the chapter → 2. System requires a reason → 3. Producer confirms → 4. System returns the chapter to InProduction, makes its Pages and Panels editable, revokes the preview links of the old version and records an unlock event; affected items are then restored (UC-13) or sent back with Change Requests (UC-09). | Producer cancels or leaves the reason empty → chapter stays locked. | Chapter is editable again; the earlier approved state remains in history; the unlock event with its reason is recorded. | FR-Producer-05, FR-Producer-06 | Medium | Draft | CF5 |
| 24.0 | UC-24 | Triage Reader Feedback | Producer / Editor | Producer or Editor reads the feedback that Readers left on a preview link and decides for each item whether it is a note for a later version or a change that needs a Change Request. | At least one Reader feedback item exists for the chapter. | 1. Producer or Editor opens the feedback of a PreviewLink → 2. Reads each item with its chapter or page → 3. Marks it as a note for a later version or as needing a change → 4. For a needed change the Producer reopens the chapter (UC-23) and a Change Request is created from the feedback. | Feedback is unclear → the item is kept open with a question for the Reader's organiser (no reply is sent through the platform). | Every feedback item is triaged; Change Requests exist for the changes that are needed. | FR-Reader-02, FR-Editor-04, FR-Producer-05 | Medium | Draft | CF5 |


## 5. Requirements

| No. | Req ID | Type | Requirement Description | Category | Priority | Core Flow |
| --- | --- | --- | --- | --- | --- | --- |
| 1.0 | FR-01 | FR | The system must manage content according to a hierarchical structure: Series -> Chapter -> Scene -> Page -> Panel -> Element. | Content Model | High | CF1, CF2 |
| 2.0 | FR-02 | FR | The system must provide a workflow (pipeline) capable of handling status transitions (Script, Thumbnail, Pencil, Ink, etc.) and tracking progress per Page and Panel; stages run in parallel across Pages and each stage passes an approval gate. | Workflow | High | CF1, CF2, CF5 |
| 3.0 | FR-03 | FR | The system must allow the editor to perform a panel-anchored review (selecting a specific area and adding comments directly onto the drawing details). | Collaboration | High | CF3 |
| 4.0 | FR-04 | FR | The system must integrate AI to analyze scenarios and perform continuity checks against the Series Bible (layout suggestion and image-based continuity checks are extended scope). | AI Layer | High | CF2, CF3 |
| 5.0 | FR-05 | FR | The system must record a detailed version history (provenance), including the level of contribution and the AI ​​prompt. | Versioning | High | CF4 |
| 6.0 | NFR-01 | NFR | Concurrency: Uses optimistic concurrency to allow multiple users to work on a page simultaneously without data loss. | Reliability | High | CF2 |
| 7.0 | NFR-02 | NFR | Data integrity: Silent deletion or overwriting of previous versions is strictly prohibited (Immutability). | Security/Data | High | CF4, CF5 |
| 8.0 | NFR-03 | NFR | Fault-tolerant design (Graceful Degradation): The manual system must remain operational if the AI ​​service fails or the API responds slowly. | Reliability | Medium | CF2, CF3 |
| 9.0 | FR-Admin-01 | FR | The system must allow the Administrator to manage user accounts, studio workspaces, roles and permission sets. | Administration | High | CF1 |
| 10.0 | FR-Admin-02 | FR | The system must allow the Administrator to configure AI provider credentials, model selection and usage quota per workspace. | Administration | Medium | CF1 |
| 11.0 | FR-Admin-03 | FR | The system must allow the Administrator to configure storage quota, retention policy and backup schedule. | Administration | Medium | Support |
| 12.0 | FR-Admin-04 | FR | The system must allow the Administrator to view the system-wide audit log and the AI usage report. | Administration | Medium | CF1 |
| 13.0 | FR-Admin-05 | FR | The system must allow the Administrator to manage master data: pipeline stage templates, element types and export presets. | Administration | Medium | Support |
| 14.0 | FR-Producer-01 | FR | The system must allow the Producer to create and configure a Series with its pipeline stages, roles, release calendar and reading direction (right-to-left, left-to-right or vertical scroll). | Series Management | High | CF1 |
| 15.0 | FR-Producer-02 | FR | The system must allow the Producer to create and maintain the Series Bible (characters, locations, props, terminology, style rules and established plot facts), versioned per chapter. | Series Management | High | CF1 |
| 16.0 | FR-Producer-03 | FR | The system must allow the Producer to invite members, assign roles and set per-chapter or per-panel assignments with deadlines and dependencies. | Workflow | High | CF1, CF2 |
| 17.0 | FR-Producer-04 | FR | The system must provide the Producer with a production dashboard showing per-stage progress, workload, overdue items and burndown. | Workflow | High | CF2 |
| 18.0 | FR-Producer-05 | FR | The system must allow the Producer to approve or reject a chapter, lock it, and reopen it with a recorded reason. | Workflow | High | CF5 |
| 19.0 | FR-Producer-06 | FR | The system must allow the Producer to restore any entity to a previous version; a restore creates a new version and never deletes history. | Versioning | Medium | CF4 |
| 20.0 | FR-Producer-07 | FR | The system must allow the Producer to review the provenance report of a chapter. | Versioning | High | CF4 |
| 21.0 | FR-Producer-08 | FR | The system must allow the Producer to trigger export of a locked chapter to page images, PDF, CBZ, vertical-scroll webtoon and open JSON that carries the provenance. | Delivery | Medium | CF5 |
| 22.0 | FR-Writer-01 | FR | The system must provide a structured script editor with scene, dialogue and direction blocks. | Content Model | High | CF2 |
| 23.0 | FR-Writer-02 | FR | The system must allow the Writer to request an AI breakdown of a script into Scenes, Pages and Panels and to accept, edit or reject the result. | AI Layer | High | CF2 |
| 24.0 | FR-Writer-03 | FR | The system must allow the Writer to define panel descriptions, shot notes, dialogue lines and speaker assignment. | Content Model | High | CF2 |
| 25.0 | FR-Writer-04 | FR | The system must allow the Writer to link script entities to Series Bible entries. | Content Model | Medium | CF2 |
| 26.0 | FR-Writer-05 | FR | The system must allow the Writer to compare script versions and restore a previous revision. | Versioning | Medium | CF4 |
| 27.0 | FR-Artist-01 | FR | The system must give the Artist a personal task queue with the brief, references and deadline of each assigned Panel or Page. | Workflow | High | CF2 |
| 28.0 | FR-Artist-02 | FR | The system must allow the Artist to plan page layout in the thumbnail and panel-grid editor and to mark Key Artwork Regions (AI layout suggestion is extended scope). | Content Model | High | CF2 |
| 29.0 | FR-Artist-03 | FR | The system must allow the Artist to upload artwork versions for a Panel or Page and record the production stage of each upload. | Content Model | High | CF2 |
| 30.0 | FR-Artist-04 | FR | The system must give the Artist access to Series Bible reference sheets and the shared asset library inside the workspace. | Content Model | Medium | CF2 |
| 31.0 | FR-Artist-05 | FR | The system must allow the Artist to respond to change requests and mark an item ready for review. | Collaboration | Medium | CF2, CF3 |
| 32.0 | FR-Letterer-01 | FR | The system must allow the Letterer to place, resize and style dialogue balloons, narration boxes and sound-effect Elements on the panel canvas. | Content Model | Medium | CF2 |
| 33.0 | FR-Letterer-02 | FR | The system must allow the Letterer to bind each text Element to its source script line and speaker. | Content Model | Medium | CF2 |
| 34.0 | FR-Letterer-03 | FR | The system must warn the Letterer when text overflows a balloon, breaks reading order or overlaps a Key Artwork Region marked by the Artist (automatic detection of important areas is extended scope). | Quality Assurance | Medium | CF2 |
| 35.0 | FR-Letterer-04 | FR | The system must allow the Letterer to apply and manage typographic presets per Series. | Content Model | Low | CF2 |
| 36.0 | FR-Editor-01 | FR | The system must allow the Editor to review a submitted Panel, Page or Chapter and compare it side by side with any earlier version. | Collaboration | High | CF3 |
| 37.0 | FR-Editor-02 | FR | The system must allow the Editor to attach region-anchored comments and drawn annotations to a specific version. | Collaboration | High | CF3 |
| 38.0 | FR-Editor-03 | FR | The system must allow the Editor to run a consistency check and triage each finding (Confirmed, Rejected, Ignored, Needs clarification) on character continuity, terminology, facts and reading order. | AI Layer | Medium | CF3 |
| 39.0 | FR-Editor-04 | FR | The system must allow the Editor to issue change requests, track their resolution, and approve or reject the submission. | Collaboration | High | CF3 |
| 40.0 | FR-Reader-01 | FR | The system must provide a time-limited, watermarked, revocable preview link, issued to a named recipient for a locked chapter, that opens the chapter in reading mode. | Security | Medium | CF5 |
| 41.0 | FR-Reader-02 | FR | The system must allow a Reader to leave chapter-level or page-level feedback without accessing the production workspace. | Collaboration | Low | CF5 |
| 42.0 | FR-All-01 | FR | The system must allow every authenticated user to authenticate and manage the personal profile and notification preferences. | Access Control | Medium | Support |
| 43.0 | FR-All-02 | FR | The system must send in-app and email notifications for assignments, mentions, reviews and deadlines. | Notification | Medium | All |
| 44.0 | FR-All-03 | FR | The system must allow every authenticated user to search across scripts, panels, Series Bible entries and comments. | Search | Medium | All |
| 45.0 | FR-All-04 | FR | The system must allow every authenticated user to view the activity feed and version history of any entity they can access. | Versioning | High | CF4 |
| 46.0 | NFR-04 | NFR | Completeness of provenance: every state-changing operation must record who performed it, when and what changed; every AI-assisted artifact must carry a machine-readable record of model, input and extent of human editing, and the record must survive export. | Security/Data | High | CF4 |
| 47.0 | NFR-05 | NFR | Workspace responsiveness: opening a chapter and manipulating the panel canvas must feel immediate; breakdown, consistency checking and export must run asynchronously with visible progress and never block the screen (measurable targets to be agreed). | Performance | High | All |
| 48.0 | NFR-06 | NFR | Protection of unreleased work: assets must be reachable only through short-lived, revocable links tied to an assignment; external previews must be watermarked and expiring; all access must be logged. | Security | High | CF2, CF5 |
| 49.0 | NFR-07 | NFR | Scale of content and asset model: the data model and storage must accommodate a long-running series with many panels and many versions per panel without architectural change, using deduplicated content-addressed assets. | Scalability | Medium | CF2, CF4 |
| 50.0 | NFR-08 | NFR | Configurability without redeployment: pipeline stages, role permissions, element types, typographic presets, consistency rules and export presets must be editable through the administration interface. | Maintainability | Medium | CF1 |
| 51.0 | NFR-09 | NFR | Usability for creative staff: favour direct manipulation over forms, offer a dark theme, tolerate brief loss of connectivity without losing entered work, and provide a bilingual Vietnamese and English interface. | Usability | Medium | All |
| 52.0 | NFR-10 | NFR | Portability of the content model: the structured content must be exportable and re-importable through a documented open JSON schema, and delivery formats must be standard. | Portability | Medium | CF5 |
| 53.0 | NFR-11 | NFR | Maintainability and testability: the domain model, event store and provenance subsystem must be covered by automated tests and built through an automated pipeline. | Maintainability | Medium | All |


## 6.Entities

| Entity ID | Entity Name | Relationships & Description | Core Flow |
| --- | --- | --- | --- |
| E-01 | Series | The root entity represents a comic book series. It carries the reading direction (right-to-left, left-to-right or vertical scroll) and the pipeline configuration. | CF1 |
| E-02 | Chapter | Every Chapter must belong to exactly one Series (N-1 relationship with Series). State: InProduction, Approved, Locked or Published; reopening (unlocking) returns it to InProduction (UC-23). | CF2, CF5 |
| E-03 | Scene | Every Scene must belong to exactly one Chapter (N-to-1 relationship with Chapter). | CF2 |
| E-04 | Page | Every Page must belong to exactly one Scene (N-1 relationship with Scene) and has a sequential `page_number` attribute. | CF2 |
| E-05 | Panel | Every Panel must belong to exactly one Page. It has a `reading_order_index` attribute (RTL, LTR or Top-down, following the reading direction of the Series). | CF2, CF3 |
| E-06 | Element | Every Element (speech bubble, caption, sound effect, character instance, artwork layer) must belong to exactly one Panel and has an ElementType (E-30); a text Element is bound to at most one ScriptLine (BR-05). | CF2 |
| E-07 | SeriesBible | An encyclopedia containing characters, locations, props, terminology, style rules and plot facts. Its content is held as BibleEntry (E-22) records, each versioned by BibleEntryVersion (E-23) with an effective-from Chapter (BR-15). | CF1, CF3 |
| E-08 | Asset | Unique identification via SHA-256 hash (CAS), with reuse via Asset Reference instead of new allocation. | CF2, CF4 |
| E-09 | User | A person with an authenticated account. Carries a SystemRole (Administrator \| User) and, when SystemRole = User, a CanCreateStudio permission flag set by an Administrator (BR-22). Participates in zero or more Workspaces through a WorkspaceMember row (E-12). | CF1 |
| E-10 | Workspace (Studio) | The tenant boundary for a studio; every Series, WorkspaceMember and configuration belongs to exactly one Workspace. Created only by a User whose CanCreateStudio flag is true (BR-22); the creator is automatically added as Producer. | CF1 |
| E-11 | SystemRole | A platform-level role on User: Administrator or User only. Administrator manages accounts, CanCreateStudio grants, AI configuration and the audit log, and holds no WorkspaceRole and no content-creation rights (BR-07). | CF1 |
| E-12 | WorkspaceMember (workspace_members) | Resolves the many-to-many relationship between User and Workspace, carrying the User's WorkspaceRole (Producer, Writer, Artist, Letterer or Editor) in that Workspace via its role column. Columns: id, workspace_id, user_id, role, joined_at, plus standard audit/soft-delete columns (CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, DeletedAt, DeletedBy, IsDeleted). A User with no row here for a given Workspace has no access to it (BR-07). | CF1 |
| E-13 | Assignment | Links exactly one User to exactly one Panel, Page or Chapter task, carrying a deadline, a priority and optional dependencies (BR-19). | CF2 |
| E-14 | PipelineStage | A configurable, ordered stage (e.g. Script, Thumbnail, Pencil, Ink, Color, Letter, Review, Approved) that a Panel/Page/Chapter must pass through in sequence. | CF1, CF2, CF3 |
| E-15 | Comment / Annotation | A region-anchored note or drawn markup bound to exactly one Version of a Panel, Page or Element. | CF3 |
| E-16 | ChangeRequest | Raised from a Comment or from Reader feedback; tracks a requested fix on exactly one target Version, with an assignee and a deadline, through to resolution or rejection. | CF3 |
| E-17 | Version / ProvenanceEvent | An immutable, append-only record of a state-changing operation on any entity; if AI-assisted, it also stores the model, parameters, prompt (or its hash), input context, accepting user and the degree of human modification. | CF4 |
| E-18 | PreviewLink | A time-limited, watermarked, revocable link granting a named Reader read-only access to exactly one Locked Chapter. Status: Active, Expired or Revoked. | CF5 |
| E-19 | ExportJob | An asynchronous rendering request that produces a PDF, CBZ, page-image, webtoon or JSON output from exactly one Locked Chapter version. Status: Queued, Processing, Completed, Failed or Cancelled; the JSON output carries the provenance. | CF5 |
| E-20 | Notification | Generated for exactly one User in response to an Assignment, mention, review or deadline event. | All |
| E-21 | ScriptLine | A single dialogue, narration or direction line of a Chapter script. It belongs to exactly one Chapter script and, after breakdown, is anchored to one Panel; its speaker references a Series Bible character, and it is bound to at most one Element (BR-05). | CF2 |
| E-22 | BibleEntry | A single character, location, prop, terminology, style-rule or plot-fact record of a SeriesBible. Every BibleEntry belongs to exactly one SeriesBible and has one or more BibleEntryVersions. | CF1 |
| E-23 | BibleEntryVersion | An immutable version of a BibleEntry with an effective-from Chapter (BR-15). A consistency check reads the version that is effective at the Chapter under review. | CF1, CF3 |
| E-24 | AssetReference | A reference from a Version, Panel or Page to an Asset (E-08). Many AssetReferences may point to one Asset, so a file uploaded again with the same SHA-256 hash is linked instead of stored twice (BR-13). | CF2, CF4 |
| E-25 | AISuggestion | An asynchronous AI request and its proposed result (script breakdown, layout suggestion or consistency check) with status (Pending, Succeeded, Failed, Accepted, Edited, Rejected), model, parameters, prompt, input context and accepting user. It cannot be merged into working content until its provenance is stored (BR-10). | CF2, CF3 |
| E-26 | ConsistencyFinding | A single issue reported by a consistency check (character, terminology, fact or reading order). It points to the checked Chapter, Panel or Element and to the BibleEntryVersion it conflicts with, and stores the Editor's triage decision: Confirmed, Rejected, Ignored (with a reason) or Needs clarification (BR-21). | CF3 |
| E-27 | ReaderFeedback | Chapter-level or page-level feedback submitted by a Reader through exactly one PreviewLink; a Producer or Editor triages it as a note for a later version or turns it into a ChangeRequest. | CF5 |
| E-28 | AuditLog | System-wide record of administrative actions and access events, including asset access and preview-link access with denied attempts. It complements Version / ProvenanceEvent (E-17), which records content changes. | CF1, CF4 |
| E-29 | AIUsageRecord | One record per AI call (workspace, feature, model, token count or cost, status), used to enforce the per-workspace quota and to build the AI usage report. | CF1 |
| E-30 | ElementType | Master data defining a kind of Element (dialogue balloon, narration box, sound effect, character instance, artwork layer) and its allowed properties. Editable by the Administrator without redeployment. | CF1 |
| E-31 | TypographyPreset | A named set of lettering styles (font, size, balloon style) defined per Series and applied by the Letterer to text Elements. | CF1, CF2 |
| E-32 | ExportPreset | Master data describing an output format and its options (PDF, CBZ, page images, vertical-scroll webtoon, JSON) used by an ExportJob. | CF1, CF5 |
| E-33 | ConsistencyRule | A configurable check (character, terminology, fact, reading order) that a Producer enables and tunes per Series and that a consistency check applies. | CF1, CF3 |
| E-34 | StageProgress | The state of one PipelineStage for exactly one Page or Panel: NotStarted, InProgress, Submitted, InReview, ChangesRequested, Resubmitted or Approved. It is the unit of panel-level progress tracking on the dashboard and of gate approval (BR-06, BR-19, BR-20). | CF2, CF3 |
| E-35 | WorkspaceRole | A named permission set scoped to one Workspace: Producer, Writer, Artist, Letterer or Editor. Stored per user in WorkspaceMember.role (E-12), never on the User itself. Producer additionally has the right to invite members and set their WorkspaceRole. | CF1 |
| E-36 | ReaderAccount / ReaderSubscription | An external, Mobile-facing account distinct from Workspace User (E-09). Lets a Reader browse the public catalog of Published Series and, if the Series owner enables paid access, subscribe to follow and read every Published chapter of a Series (BR-24). Independent of PreviewLink (E-18), which serves pre-publish reviewers without any account. | CF5 |


## 7. Resources

| Resource ID | Type | Name / Title | Link (URL) | Owner / Contact | Notes |
| --- | --- | --- | --- | --- | --- |
| RS-01 | Technology - Frontend | React (Vite) / Next.js, HTML5 Canvas & Konva.js, Tailwind CSS | https://react.dev/ ; https://konvajs.org/ ; https://tailwindcss.com/ | Frontend Team | Builds the interactive panel canvas, lettering workspace and general UI. |
| RS-02 | Technology - Backend | ASP.NET Core Web API (.NET 8) with MediatR (CQRS) & Clean Architecture | https://dotnet.microsoft.com/en-us/apps/aspnet ; https://github.com/jbogard/MediatR | Backend Team | Core API layer; MediatR implements the CQRS command/query pipeline. |
| RS-03 | Database & Caching | PostgreSQL with pgvector | https://www.postgresql.org/ ; https://github.com/pgvector/pgvector | Backend Team | Relational storage plus vector indexing for Series Bible RAG (consistency checking). |
| RS-04 | Database & Caching | Firebase (Authentication & Realtime Notifications) | https://firebase.google.com/docs/auth | Backend / Frontend Team | User authentication and real-time in-app notifications. |
| RS-05 | Database & Caching | Redis (Distributed Caching & Background Queues, Hangfire) | https://redis.io/ ; https://www.hangfire.io/ | Backend Team | Background job workers for rendering and AI-fallback processing. |
| RS-06 | Storage (Assets) | Cloudflare R2 / AWS S3 | https://developers.cloudflare.com/r2/ ; https://aws.amazon.com/s3/ | Backend / DevOps Team | Object storage for content-addressed (CAS) manga production assets. |
| RS-07 | IDEs / Editors | Visual Studio 2022, Visual Studio Code, pgAdmin 4 | https://visualstudio.microsoft.com/ ; https://code.visualstudio.com/ ; https://www.pgadmin.org/ | Whole team | Primary development environments. |
| RS-08 | Diagramming | PlantUML, Draw.io, Visual Paradigm | https://plantuml.com/ ; https://app.diagrams.net/ ; https://www.visual-paradigm.com/ | Whole team | UML, architecture and workflow diagrams for the documentation set. |
| RS-09 | Documentation | Microsoft Office 365, Google Docs / Sheets / Slides | https://www.microsoft.com/microsoft-365 ; https://docs.google.com/ | Whole team | Report writing, tracking and presentation materials. |
| RS-10 | Version Control | GitHub (source code & PR review), Google Drive (documents & large media) | https://github.com/ ; https://drive.google.com/ | Whole team | GitFlow branching model; Google Drive for docs and large media assets. |
| RS-11 | CI/CD & Deployment | AWS (EC2 / ECS), Cloudflare (DNS / CDN / R2), Docker, GitHub Actions | https://aws.amazon.com/ ; https://www.cloudflare.com/ ; https://www.docker.com/ ; https://github.com/features/actions | DevOps / Backend Team | Automated build, test and deployment pipeline. |
| RS-12 | Project Management | Jira, GitHub Projects, Discord | https://www.atlassian.com/software/jira ; https://github.com/features/issues ; https://discord.com/ | Whole team | Sprint backlog, task boards and team communication (Daily Scrum, Sprint events). |
| RS-13 | Testing & API Tools | Swagger / OpenAPI, Postman, xUnit, Playwright, Vitest | https://swagger.io/ ; https://www.postman.com/ ; https://xunit.net/ ; https://playwright.dev/ ; https://vitest.dev/ | QA / Dev Team | API documentation/testing, .NET unit tests, React unit tests and end-to-end tests. |
| RS-14 | Code Quality | SonarCloud, StyleCop, ESLint | https://sonarcloud.io/ ; https://github.com/StyleCop/StyleCop ; https://eslint.org/ | Whole dev team | Static analysis enforcing coding conventions as part of the Shift-Left Testing strategy. |


## 8.References

| Ref ID | Type | Title / Description | Link (URL) | Source / Author | Notes |
| --- | --- | --- | --- | --- | --- |
| REF-01 | Architecture / Article | The Clean Architecture | https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html | Robert C. Martin | Layered backend architecture basis for the ASP.NET Core Web API. |
| REF-02 | Architecture / Article | CQRS Pattern | https://martinfowler.com/bliki/CQRS.html | Martin Fowler | Basis for the MediatR command/query separation in the backend. |
| REF-03 | Architecture / Article | Event Sourcing Pattern | https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing | Microsoft Architecture Center | Basis for the append-only event store and revision provenance engine. |
| REF-04 | Architecture / Book | Domain-Driven Design (DDD) | https://www.domainlanguage.com/ddd/ | Eric Evans | Theory for the Series-to-Element aggregate boundaries and invariants. |
| REF-05 | Standard | W3C PROV Standard | https://www.w3.org/TR/prov-overview/ | W3C | Standard followed for the human/AI provenance tagging engine (BR-10). |
| REF-06 | Architecture / Guide | Feature-Sliced Design (FSD) | https://feature-sliced.design/ | FSD Community | Frontend architecture standard for organizing the React/TypeScript codebase. |
| REF-07 | Theory / Article | Version Vectors for Conflict Detection | https://en.wikipedia.org/wiki/Version_vector | Distributed Systems literature | Theoretical basis for entity-level optimistic concurrency control (BR-11). |
| REF-08 | Standard / Theory | Role-Based Access Control (RBAC) | https://csrc.nist.gov/projects/role-based-access-control | NIST | Basis for the workspace role model combined with attribute-based link security. |
| REF-09 | Process Standard | Conventional Commits & GitFlow Branching Model | https://www.conventionalcommits.org/ ; https://nvie.com/posts/a-successful-git-branching-model/ | Conventional Commits community; Vincent Driessen | Commit message and branching conventions used in the source-code management plan. |
| REF-10 | Process / Article | Shift-Left Testing | https://www.techtarget.com/searchsoftwarequality/definition/shift-left-testing | TechTarget | Basis for the quality management approach (defect prevention, static review, unit/integration testing). |


## 9. Q&A

| No. | Phase | Student Question | Category | Priority | Answered by | Answer / Guidance | Reference | Status | Date Asked | Date Answered |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1.0 | Requirement | "Our team plans to design the Page Editor to allow dragging and dropping isolated character assets (transparent PNGs) and backgrounds directly into panel polygons. Would this shift risk diluting our core scope into a basic graphic design tool rather than the structured, version-controlled production management platform we initially registered?" | Requirement | High | Supervisor : PhucNT | Theo như giới hạn đề tài đã đăng ký trong Report 1, hệ thống không hướng tới việc xây dựng một phần mềm vẽ hay thiết kế đồ họa (như Clip Studio Paint hay Photoshop). Trọng tâm đóng góp kỹ thuật cốt lõi của PanelForge là mô hình dữ liệu có cấu trúc  và hệ thống lưu vết lịch sử bản dịch . Việc tập trung quá nhiều vào các công cụ vẽ vời, thao tác hình ảnh (kéo thả graphic) sẽ đi ngược lại tiêu chí EX-04 và EX-02. Nên giữ giao diện Page Editor ở mức sắp xếp layout cơ bảnvà quản lý tiến độ, thay vì biến nó thành một phần mềm graphic design phức tạp. |  |  | 2026-09-16 00:00:00 | 2026-09-16 00:00:00 |


## 10. Core Flows

| Flow ID | Flow Name | Objective | Actors | Main Flow (use cases) | Result | Main Exceptions | Depends on / Feeds |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CF1 | Studio, Series & Series Bible Setup | Set up the working environment (accounts, workspace, access, AI configuration) and the Series foundation (pipeline, release calendar, chapter-versioned Series Bible) that every later flow relies on. | Administrator, User (Studio owner candidate / Producer once a Studio exists). External system: AI Provider (configured only, not yet called). | 1. User logs in; the system reads SystemRole from the User record (Administrator or User) and routes accordingly (UC-01).<br> 2. If Administrator: lands on the Admin dashboard - manage accounts, grant/revoke a User's CanCreateStudio permission, configure the AI provider/models/quota per Workspace, review the audit log (UC-02, UC-15, BR-22).<br> 3. If User with CanCreateStudio = true: may create a new Workspace (Studio); the system inserts the creator into workspace_members with role = Producer (BR-22).<br> 4. If User with CanCreateStudio = false, or a Producer/invited member on later logins: lands on "Your Workspaces" - only Studios where a workspace_members row exists for this User (BR-23). A User with no membership anywhere instead sees the public catalog of Published Series.<br> 5. Producer creates the Series: genre, format, reading direction, pipeline stages, release calendar (UC-03).<br> 6. Producer invites members: each invite creates a workspace_members row carrying the WorkspaceRole (Producer, Writer, Artist, Letterer or Editor) for that Workspace only.<br> 7. Producer builds the Series Bible (UC-04): each entry is a BibleEntry, each change a BibleEntryVersion effective from a Chapter (BR-15).<br> 8. Producer enables consistency rules and typography presets for the Series.<br> 9. System records the first version of every entry with creator and time (BR-18).<br> 10. Administrator reviews the audit log and AI usage at any time (UC-15).<br> 11. The Series is ready to receive assignments. | A Studio with correctly authorised members (via workspace_members), pipeline, release calendar and a first Series Bible version, ready for Flow 2. Administrator holds no workspace_members row in any Studio and never creates content. | - No workspace_members row means no access to that Workspace or its Series (BR-07, BR-23).<br> - A User without CanCreateStudio cannot create a Studio; they only see Studios they were invited into and the public catalog of Published Series (BR-22, BR-23).<br> - Bible entries are never overwritten; a retroactive correction is a new version effective from an earlier chapter (BR-01, BR-15).<br> - Invalid AI configuration or exhausted quota: AI features are disabled, the Series stays active and the manual path works (A-01, NFR-03).<br> - The Administrator cannot read creative content; emergency access is audited (default, to be confirmed).<br> - Pipeline, element types, presets and consistency rules are editable without redeployment (NFR-08). | Feeds CF2 (assignments, pipeline) and CF3 (Bible used for checks). Feeds CF5 (Studio's Published Series feed the public catalog and Reader subscriptions). |
| CF2 | Chapter Production | Turn assigned Chapter work into finished content: structured script, layout, artwork and lettering. Each Page or Panel moves through its stages independently, so Pages are produced in parallel. | Producer, Writer, Artist, Letterer, AI Service (script breakdown; layout suggestion is extended scope). | 1. Producer creates Assignments for a Chapter, Page or Panel with assignee, deadline, priority and dependencies (UC-05, BR-19).<br> 2. System notifies the assignee when work is assigned or changed, and before deadlines (BR-14, UC-14).<br> 3. Writer writes the script in the structured editor and links entities to the Series Bible (UC-06).<br> 4. Writer splits the script into Scenes, Pages, Panels and ScriptLines by AI breakdown or manually.<br> 5. With AI: the job runs asynchronously; provenance (model, parameters, input context) is stored as soon as the AISuggestion returns (BR-10).<br> 6. Writer accepts, edits or rejects; the decision and the degree of editing are recorded, rejected suggestions too (BR-02).<br> 7. Writer submits the Script stage to Flow 3.<br> 8. When the Script gate passes, the Pages of that Scene open for the Artist page by page, not for the whole Chapter.<br> 9. Artist plans layout and uploads artwork per stage: Thumbnail, Pencil, Ink, Color (UC-07).<br> 10. Client hashes SHA-256, server recomputes and verifies; a duplicate reuses the Asset through a new AssetReference (BR-04, BR-13). Assets are reachable only through short-lived links (BR-16).<br> 11. Artist submits the stage.<br> 12. When Color is approved, Letterer places balloons, captions and SFX and binds each to its ScriptLine and speaker (UC-08).<br> 13. System warns on text overflow, reading order and overlap with the Key Artwork Region.<br> 14. Producer monitors progress by stage, panel, workload and overdue items (UC-16).<br> 15. Every finished stage is submitted to Flow 3. | Pages and Panels hold the content of the stage and are Submitted, ready for review. | - AI error, timeout or exhausted quota: notice shown, Writer breaks the script down manually; work is never blocked (NFR-03).<br> - AI output that has not been approved never becomes official content (BR-02, BR-10).<br> - Two people edit the same Page: the update is rejected by expectedVersion and a conflict dialog lets the user choose or merge (BR-11).<br> - Duplicate hash reuses the Asset but records the new place of use; client and server hashes that differ are rejected.<br> - A stage cannot start before the previous stage of the same Page or Panel is approved; other Pages are unaffected (BR-19).<br> - A dependency cycle or an Assignment without assignee is rejected.<br> - Text overflow raises a warning; the Letterer fixes it or submits with an acknowledgement reason (BR-20).<br> - Brief loss of connectivity: entered work is kept locally and synchronised later (NFR-09). | Depends on CF1. Hands every stage to CF3 for its gate. Every change is recorded by CF4. |
| CF3 | Quality Control (Review, Consistency Check, Change Request) | Catch errors before content moves on: region-anchored review of an exact version, consistency checking against the Series Bible as it stands at that chapter, and Change Requests followed to resolution. | Editor / Reviewer (main), Writer / Artist / Letterer (assignees), AI Service, Producer (arbitrates disagreements; approves gates if configured). | 1. The assignee submits the stage from Flow 2; the Editor is notified (UC-14).<br> 2. Editor opens the item (In Review) and compares it side by side with the previous version (UC-09, UC-18).<br> 3. Editor attaches Comments/Annotations anchored to the exact version and region of a Page, Panel or Element (UC-09).<br> 4. Editor runs the Consistency Check (UC-10): the AI compares with the Series Bible version effective at this chapter (BR-15) and the enabled ConsistencyRules.<br> 5. System creates ConsistencyFindings; Editor triages each as Confirmed, Rejected, Ignored (reason required) or Needs clarification (BR-21).<br> 6. If acceptable: Editor approves the stage gate and the next stage opens (BR-06, BR-19, BR-20).<br> 7. If not: Editor creates a ChangeRequest linked to the item, the faulty version, the annotation, the responsible person and a deadline.<br> 8. The assignee resolves it (UC-17): views the flagged region, fixes or uploads a new version, marks it Resolved; if in disagreement, replies and the Editor decides.<br> 9. The item becomes Resubmitted and In Review again; repeat until the gate passes.<br> 10. Every step is recorded (Flow 4). | Only content that passed review and consistency check opens the next stage; every Change Request is Resolved. | - AI consistency check fails or is overloaded: the Editor reviews manually and can still approve; the record notes that no AI findings exist (NFR-03).<br> - Editor and assignee disagree: the Producer arbitrates.<br> - Two people edit an item under review: handled by BR-11.<br> - A finding cannot be Ignored without a recorded reason (BR-21).<br> - Overdue Change Requests appear on the dashboard and trigger a notification.<br> - Reader feedback that needs a change (from Flow 5) enters here as a Change Request.<br> - MVP checks text and structure only; image-based checks (scar, eye colour, accessories) are extended scope. | Depends on CF2. Feeds CF5 (approved Chapter). Loops back to CF2 through Change Requests. |
| CF4 | Version Control & Provenance (backbone) | Backbone of the platform: every state-changing operation is stored as an append-only event, so any past state can be reconstructed and every AI contribution disclosed. | All roles (recorded automatically); Producer (Restore, Provenance Report); Writer (Restore own script); AI Service (source of AI events). | 1. Every state-changing operation in Flows 1, 2, 3 and 5 produces an append-only event (Version / ProvenanceEvent, AuditLog). If it cannot be recorded, the operation is rejected (BR-18). This recording is a system mechanism, not a use case.<br> 2. Each event stores actor, time, entity, version before and after, change, and source (human or AI).<br> 3. For AI changes it also stores provider, model, parameters, AISuggestion, input context (prompt reference or hash), accepting user and degree of human modification (BR-10).<br> 4. Assets are stored immutably by SHA-256; Versions reference them through AssetReference (BR-04, BR-13).<br> 5. Users view history and compare two versions (UC-18).<br> 6. Restore: the Producer selects an old version and the system creates a new version copying that state; nothing is deleted (UC-13, BR-08).<br> 7. A Locked chapter must first be reopened with a reason (UC-23) before restore or edits (BR-12).<br> 8. Producer creates the Provenance Report of a Chapter (UC-19).<br> 9. Provenance travels with the JSON export so it can be verified outside the platform (NFR-04, NFR-10). | Every past state of every entity can be rebuilt and compared; the origin of AI content and the degree of human intervention are traceable. | - Recording fails: the operation is rejected and the user is told; there are no silent changes (BR-18).<br> - Restore on a Locked entity requires reopening the chapter first.<br> - expectedVersion mismatch: the update is rejected and the conflict is shown (BR-11).<br> - An AI result without provenance cannot be merged (BR-10).<br> - Continuous drag-and-drop writes an event only on drop, to keep the event store small (NFR-07).<br> - A large Provenance Report is generated asynchronously (NFR-05).<br> - Users see history only of entities they may access (BR-07). Editor sees the Provenance Report read-only (default, to be confirmed).<br> - Immutability: no deletion or overwrite; "delete" is a marking event; current state is derived from the event sequence. | Used by CF1, CF2, CF3 and CF5. |
| CF5 | Approval, Release & Export | Approve and lock a Chapter, share it safely with external Readers, collect feedback, then deliver it in standard formats, or reopen it when a change is needed. | Producer, Editor (triages feedback with the Producer), Reader - PreviewLink recipient (external, no account, pre-publish), Reader - Mobile (external, registers a ReaderAccount, may subscribe/pay, post-publish only), Export Service. | 1. Producer checks that every Page is Approved at the final stage, no mandatory Change Request is open and severe findings are handled (UC-20).<br> 2. Producer approves the Chapter and locks it: Pages and Panels become read-only (UC-20, BR-12).<br> 3. Producer creates a PreviewLink bound to a named recipient email, with expiry and a watermark showing the recipient; it can be revoked (UC-11, BR-09, BR-17).<br> 4. PreviewLink Reader opens the link (no account); the system validates token, expiry and revocation and logs every access (UC-21, BR-16, BR-17). Reading follows the reading direction of the Series.<br> 5. PreviewLink Reader submits feedback at chapter or page level, stored with the PreviewLink.<br> 6. Producer or Editor triages the feedback: note for a later version, or change needed (UC-24).<br> 7. Change needed: Producer reopens the Chapter with a reason (UC-23); feedback becomes a Change Request in Flow 3; fixes or restores follow Flow 4; the Chapter passes its gates and is approved and locked again.<br> 8. No change needed: Producer creates an ExportJob (UC-12) with an ExportPreset (page images, PDF, CBZ, vertical-scroll webtoon, JSON). It exports from exactly the Locked version, recorded in the job.<br> 9. The Export Service runs asynchronously and shows Queued, Processing, Completed, Failed or Cancelled; a notification arrives when done (UC-14, NFR-05).<br> 10. Producer downloads the result and/or Publishes the Chapter according to the Series policy; the Chapter becomes Published (BR-24).<br> 11. A Published Series appears in the public catalog, including Mobile. A Mobile Reader registers a ReaderAccount, browses freely, and - if the Series owner enabled paid access - subscribes to follow and read every Published chapter of that Series.<br> 12. Mobile Reader may also leave chapter-level feedback on Published content; Producer/Editor triage it the same way as PreviewLink feedback (step 6). | The Chapter is approved, locked, safely previewed and delivered in standard formats (with provenance in the JSON), or reopened with a full audit trail; once Published it is discoverable and, where enabled, subscribable by external Readers on Mobile. | - A PreviewLink that is expired or revoked is denied and the attempt is logged (BR-09, BR-17).<br> - A PreviewLink Reader reaches content only through the link, never the Workspace (BR-07, NFR-06); a Mobile Reader reaches only Published content, never pre-publish Locked chapters.<br> - Feedback after Lock is triaged into a Change Request (Reopen) or a note for a later version.<br> - Open Change Requests or unresolved severe findings block approval and are listed (UC-20); a Producer rejection keeps the Chapter InProduction with a reason.<br> - Export failure: error shown, retry allowed, the Chapter stays Locked (UC-12).<br> - No Export or Publish before Lock.<br> - Reopen needs a reason; an empty reason or cancel keeps the Chapter Locked (UC-23).<br> - On Reopen the preview links of the old version are revoked (proposal, to be confirmed).<br> - A Mobile Reader without an active paid subscription (when the Series requires one) cannot open chapters beyond any free preview (extended scope, to be confirmed). | Depends on CF3. Loops back to CF3 and CF4 on feedback. Consumes Studios/Series created in CF1. |
| All | Cross-cutting | Capabilities used inside every flow. | All authenticated users | UC-14 Receive Notification on Assignment or Review<br> UC-22 Search Content | Users are informed and can find any content they may access. | Search returns only content the user is allowed to see (BR-07). | Serves CF1-CF5. |


## 11. Traceability

| TRACEABILITY MATRIX: Core Flow → Use Case → User Stories, Business Rules, Requirements, Entities |  |  |  |  |  |  |  |  |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Core Flow, name, actor and priority are pulled live from sheet "4. Usecases". The four ID columns are the trace links (edit them here when a link changes; sheet "12. Coverage Check" re-checks them). |  |  |  |  |  |  |  |  |
| Core Flow | Use Case ID | Use Case Name | Actor | Priority | User Stories | Business Rules | Requirements (FR / NFR) | Entities |
| CF1 | UC-01 | Manage User Account & Role | Administrator | High | US-07 | BR-07 | FR-Admin-01 | E-09, E-10, E-11, E-12 |
| CF1 | UC-02 | Configure AI Provider & Quota | Administrator | Medium | US-08 | BR-07 | FR-Admin-02, NFR-03 | E-10, E-29 |
| CF1 | UC-03 | Create Series & Configure Pipeline | Producer | High | US-21 | BR-03, BR-07 | FR-Producer-01, FR-Producer-03, FR-01, FR-02, NFR-08 | E-01, E-14, E-11, E-12, E-31, E-33 |
| CF1 | UC-04 | Maintain Series Bible | Producer | High | US-01 | BR-01, BR-11, BR-15 | FR-Producer-02, FR-01, NFR-01 | E-07, E-22, E-23, E-17 |
| CF1 | UC-15 | View Audit Log & AI Usage Report | Administrator | Medium | US-09 | BR-18 | FR-Admin-04, NFR-04 | E-28, E-29 |
| CF2 | UC-05 | Assign Task & Deadline | Producer | High | US-22 | BR-07, BR-14, BR-19 | FR-Producer-03, FR-02 | E-13, E-14, E-02, E-04, E-05, E-20 |
| CF2 | UC-06 | Write Structured Script & Request AI Breakdown | Writer | High | US-02, US-13 | BR-02, BR-03, BR-05, BR-10, BR-11 | FR-Writer-01, FR-Writer-02, FR-Writer-03, FR-Writer-04, FR-01, FR-04, FR-05, NFR-03 | E-02, E-03, E-04, E-05, E-06, E-21, E-25, E-17 |
| CF2 | UC-07 | Plan Layout & Upload Artwork | Artist | High | US-03, US-15, US-16 | BR-04, BR-06, BR-11, BR-13, BR-16, BR-19, BR-20 | FR-Artist-01, FR-Artist-02, FR-Artist-03, FR-Artist-04, FR-Artist-05, FR-02, NFR-01, NFR-06, NFR-07 | E-04, E-05, E-08, E-24, E-14, E-34, E-17, E-25 |
| CF2 | UC-08 | Place & Bind Text Elements | Letterer | Medium | US-05 | BR-05, BR-11, BR-19, BR-20 | FR-Letterer-01, FR-Letterer-02, FR-Letterer-03, FR-Letterer-04, NFR-01 | E-06, E-21, E-30, E-31, E-34 |
| CF2 | UC-16 | Monitor Production Dashboard | Producer | High | US-10 | BR-07, BR-19 | FR-Producer-04, FR-02 | E-13, E-14, E-05, E-34 |
| CF3 | UC-09 | Review Submission & Annotate | Editor/Reviewer | High | US-04, US-18 | BR-06, BR-07, BR-11, BR-20 | FR-Editor-01, FR-Editor-02, FR-Editor-04, FR-03, FR-02 | E-15, E-16, E-14, E-34, E-17 |
| CF3 | UC-10 | Run AI Consistency Check | Editor/Reviewer | Medium | US-17 | BR-02, BR-10, BR-15, BR-20, BR-21 | FR-Editor-03, FR-04, NFR-03 | E-07, E-23, E-25, E-26, E-33 |
| CF3 | UC-17 | Resolve Change Request | Writer / Artist / Letterer | Medium | US-27, US-18 | BR-06, BR-11 | FR-Artist-05, FR-Editor-04 | E-16, E-15, E-17 |
| CF4 | UC-13 | Restore Previous Version (Rollback) | Producer (Writer for own script) | Medium | US-12, US-14 | BR-01, BR-08, BR-12 | FR-Producer-06, FR-Writer-05, FR-05, NFR-02 | E-17, E-08 |
| CF4 | UC-18 | View Version History & Compare Versions | All authenticated users | High | US-25 | BR-01, BR-07 | FR-All-04, FR-Writer-05, FR-Editor-01, FR-05 | E-17 |
| CF4 | UC-19 | View Chapter Provenance Report | Producer | High | US-26 | BR-10, BR-18 | FR-Producer-07, FR-05, NFR-04 | E-17, E-25, E-02 |
| CF5 | UC-20 | Approve & Lock Chapter | Producer | High | US-11 | BR-06, BR-12 | FR-Producer-05, FR-02 | E-02, E-17 |
| CF5 | UC-11 | Generate Time-Limited Preview Link | Producer | Medium | US-06 | BR-07, BR-09, BR-17 | FR-Reader-01, NFR-06 | E-18, E-02, E-28 |
| CF5 | UC-21 | Read Preview & Submit Feedback | Reader | Medium | US-06, US-19 | BR-09, BR-17 | FR-Reader-01, FR-Reader-02, NFR-06 | E-18, E-27, E-28 |
| CF5 | UC-24 | Triage Reader Feedback | Producer / Editor | Medium | US-29 | BR-12, BR-17 | FR-Reader-02, FR-Editor-04, FR-Producer-05 | E-27, E-16, E-02 |
| CF5 | UC-23 | Reopen (Unlock) Chapter | Producer | Medium | US-28 | BR-08, BR-12, BR-18 | FR-Producer-05, FR-Producer-06, NFR-02 | E-02, E-17, E-18 |
| CF5 | UC-12 | Export & Deliver Chapter | Producer | Medium | US-23 | BR-12 | FR-Producer-08, NFR-05, NFR-10 | E-19, E-02, E-32, E-08 |
| All | UC-14 | Receive Notification on Assignment or Review | All authenticated users | Low | US-24 | BR-14 | FR-All-02 | E-20 |
| All | UC-22 | Search Content | All authenticated users | Medium | US-20 | BR-07 | FR-All-03, NFR-05 | E-21, E-05, E-22, E-15 |


## 12. Coverage Check

| COVERAGE CHECK: is every user story, business rule, requirement and entity traced to at least one use case? |  |  |  |  |  |  |  |  |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Counts are computed from sheet "11. Traceability". NFRs that are cross-cutting may legitimately have no use case. |  |  |  |  |  |  |  |  |
| Items NOT COVERED: | 3.0 |  |  |  |  |  |  |  |
| Type | ID | Referenced by (use cases) | Status |  |  |  |  |  |
| User Story | US-01 | 1.0 | OK |  |  |  |  |  |
| User Story | US-02 | 1.0 | OK |  |  |  |  |  |
| User Story | US-03 | 1.0 | OK |  |  |  |  |  |
| User Story | US-04 | 1.0 | OK |  |  |  |  |  |
| User Story | US-05 | 1.0 | OK |  |  |  |  |  |
| User Story | US-06 | 2.0 | OK |  |  |  |  |  |
| User Story | US-07 | 1.0 | OK |  |  |  |  |  |
| User Story | US-08 | 1.0 | OK |  |  |  |  |  |
| User Story | US-09 | 1.0 | OK |  |  |  |  |  |
| User Story | US-10 | 1.0 | OK |  |  |  |  |  |
| User Story | US-11 | 1.0 | OK |  |  |  |  |  |
| User Story | US-12 | 1.0 | OK |  |  |  |  |  |
| User Story | US-13 | 1.0 | OK |  |  |  |  |  |
| User Story | US-14 | 1.0 | OK |  |  |  |  |  |
| User Story | US-15 | 1.0 | OK |  |  |  |  |  |
| User Story | US-16 | 1.0 | OK |  |  |  |  |  |
| User Story | US-17 | 1.0 | OK |  |  |  |  |  |
| User Story | US-18 | 2.0 | OK |  |  |  |  |  |
| User Story | US-19 | 1.0 | OK |  |  |  |  |  |
| User Story | US-20 | 1.0 | OK |  |  |  |  |  |
| User Story | US-21 | 1.0 | OK |  |  |  |  |  |
| User Story | US-22 | 1.0 | OK |  |  |  |  |  |
| User Story | US-23 | 1.0 | OK |  |  |  |  |  |
| User Story | US-24 | 1.0 | OK |  |  |  |  |  |
| User Story | US-25 | 1.0 | OK |  |  |  |  |  |
| User Story | US-26 | 1.0 | OK |  |  |  |  |  |
| User Story | US-27 | 1.0 | OK |  |  |  |  |  |
| User Story | US-28 | 1.0 | OK |  |  |  |  |  |
| User Story | US-29 | 1.0 | OK |  |  |  |  |  |
| Business Rule | BR-01 | 3.0 | OK |  |  |  |  |  |
| Business Rule | BR-02 | 2.0 | OK |  |  |  |  |  |
| Business Rule | BR-03 | 2.0 | OK |  |  |  |  |  |
| Business Rule | BR-04 | 1.0 | OK |  |  |  |  |  |
| Business Rule | BR-05 | 2.0 | OK |  |  |  |  |  |
| Business Rule | BR-06 | 4.0 | OK |  |  |  |  |  |
| Business Rule | BR-07 | 9.0 | OK |  |  |  |  |  |
| Business Rule | BR-08 | 2.0 | OK |  |  |  |  |  |
| Business Rule | BR-09 | 2.0 | OK |  |  |  |  |  |
| Business Rule | BR-10 | 3.0 | OK |  |  |  |  |  |
| Business Rule | BR-11 | 6.0 | OK |  |  |  |  |  |
| Business Rule | BR-12 | 5.0 | OK |  |  |  |  |  |
| Business Rule | BR-13 | 1.0 | OK |  |  |  |  |  |
| Business Rule | BR-14 | 2.0 | OK |  |  |  |  |  |
| Business Rule | BR-15 | 2.0 | OK |  |  |  |  |  |
| Business Rule | BR-16 | 1.0 | OK |  |  |  |  |  |
| Business Rule | BR-17 | 3.0 | OK |  |  |  |  |  |
| Business Rule | BR-18 | 3.0 | OK |  |  |  |  |  |
| Business Rule | BR-19 | 4.0 | OK |  |  |  |  |  |
| Business Rule | BR-20 | 4.0 | OK |  |  |  |  |  |
| Business Rule | BR-21 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-01 | 3.0 | OK |  |  |  |  |  |
| Requirement | FR-02 | 6.0 | OK |  |  |  |  |  |
| Requirement | FR-03 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-04 | 2.0 | OK |  |  |  |  |  |
| Requirement | FR-05 | 4.0 | OK |  |  |  |  |  |
| Requirement | NFR-01 | 3.0 | OK |  |  |  |  |  |
| Requirement | NFR-02 | 2.0 | OK |  |  |  |  |  |
| Requirement | NFR-03 | 3.0 | OK |  |  |  |  |  |
| Requirement | FR-Admin-01 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Admin-02 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Admin-03 | 0.0 | NOT COVERED |  |  |  |  |  |
| Requirement | FR-Admin-04 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Admin-05 | 0.0 | NOT COVERED |  |  |  |  |  |
| Requirement | FR-Producer-01 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Producer-02 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Producer-03 | 2.0 | OK |  |  |  |  |  |
| Requirement | FR-Producer-04 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Producer-05 | 3.0 | OK |  |  |  |  |  |
| Requirement | FR-Producer-06 | 2.0 | OK |  |  |  |  |  |
| Requirement | FR-Producer-07 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Producer-08 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Writer-01 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Writer-02 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Writer-03 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Writer-04 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Writer-05 | 2.0 | OK |  |  |  |  |  |
| Requirement | FR-Artist-01 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Artist-02 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Artist-03 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Artist-04 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Artist-05 | 2.0 | OK |  |  |  |  |  |
| Requirement | FR-Letterer-01 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Letterer-02 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Letterer-03 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Letterer-04 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Editor-01 | 2.0 | OK |  |  |  |  |  |
| Requirement | FR-Editor-02 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Editor-03 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-Editor-04 | 3.0 | OK |  |  |  |  |  |
| Requirement | FR-Reader-01 | 2.0 | OK |  |  |  |  |  |
| Requirement | FR-Reader-02 | 2.0 | OK |  |  |  |  |  |
| Requirement | FR-All-01 | 0.0 | NOT COVERED |  |  |  |  |  |
| Requirement | FR-All-02 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-All-03 | 1.0 | OK |  |  |  |  |  |
| Requirement | FR-All-04 | 1.0 | OK |  |  |  |  |  |
| Requirement | NFR-04 | 2.0 | OK |  |  |  |  |  |
| Requirement | NFR-05 | 2.0 | OK |  |  |  |  |  |
| Requirement | NFR-06 | 3.0 | OK |  |  |  |  |  |
| Requirement | NFR-07 | 1.0 | OK |  |  |  |  |  |
| Requirement | NFR-08 | 1.0 | OK |  |  |  |  |  |
| Requirement | NFR-09 | 0.0 | Cross-cutting |  |  |  |  |  |
| Requirement | NFR-10 | 1.0 | OK |  |  |  |  |  |
| Requirement | NFR-11 | 0.0 | Cross-cutting |  |  |  |  |  |
| Entity | E-01 | 1.0 | OK |  |  |  |  |  |
| Entity | E-02 | 8.0 | OK |  |  |  |  |  |
| Entity | E-03 | 1.0 | OK |  |  |  |  |  |
| Entity | E-04 | 3.0 | OK |  |  |  |  |  |
| Entity | E-05 | 5.0 | OK |  |  |  |  |  |
| Entity | E-06 | 2.0 | OK |  |  |  |  |  |
| Entity | E-07 | 2.0 | OK |  |  |  |  |  |
| Entity | E-08 | 3.0 | OK |  |  |  |  |  |
| Entity | E-09 | 1.0 | OK |  |  |  |  |  |
| Entity | E-10 | 2.0 | OK |  |  |  |  |  |
| Entity | E-11 | 2.0 | OK |  |  |  |  |  |
| Entity | E-12 | 2.0 | OK |  |  |  |  |  |
| Entity | E-13 | 2.0 | OK |  |  |  |  |  |
| Entity | E-14 | 5.0 | OK |  |  |  |  |  |
| Entity | E-15 | 3.0 | OK |  |  |  |  |  |
| Entity | E-16 | 3.0 | OK |  |  |  |  |  |
| Entity | E-17 | 10.0 | OK |  |  |  |  |  |
| Entity | E-18 | 3.0 | OK |  |  |  |  |  |
| Entity | E-19 | 1.0 | OK |  |  |  |  |  |
| Entity | E-20 | 2.0 | OK |  |  |  |  |  |
| Entity | E-21 | 3.0 | OK |  |  |  |  |  |
| Entity | E-22 | 2.0 | OK |  |  |  |  |  |
| Entity | E-23 | 2.0 | OK |  |  |  |  |  |
| Entity | E-24 | 1.0 | OK |  |  |  |  |  |
| Entity | E-25 | 4.0 | OK |  |  |  |  |  |
| Entity | E-26 | 1.0 | OK |  |  |  |  |  |
| Entity | E-27 | 2.0 | OK |  |  |  |  |  |
| Entity | E-28 | 3.0 | OK |  |  |  |  |  |
| Entity | E-29 | 2.0 | OK |  |  |  |  |  |
| Entity | E-30 | 1.0 | OK |  |  |  |  |  |
| Entity | E-31 | 2.0 | OK |  |  |  |  |  |
| Entity | E-32 | 1.0 | OK |  |  |  |  |  |
| Entity | E-33 | 2.0 | OK |  |  |  |  |  |
| Entity | E-34 | 4.0 | OK |  |  |  |  |  |


## 13. Change Log

| CHANGE LOG (this version vs. the uploaded Casptone_Project_GFA26SE140_PanelForge.xlsx) |  |  |  |  |  |
| --- | --- | --- | --- | --- | --- |
| Colour legend: light green = row added; light yellow = existing cell modified. Every modified existing cell is listed below with its previous text. Content now matches the corrected document PanelForge_5_Core_Flows.md. |  |  |  |  |  |
| No. | Sheet | Location | Type | Description | Previous text (modified cells) |
| 1.0 | 2. Busiess Rules; 3. User Stories; 4. Usecases; 5. Requirements; 6.Entities | New last column | Added | Column "Core Flow" (CF1-CF5, All, Support) on every row. |  |
| 2.0 | 2. Busiess Rules | Rows 16-22 | Added | BR-15 Chapter-Versioned Series Bible, BR-16 Short-Lived Asset Access, BR-17 Recipient-Bound Preview, BR-18 Complete Audit Trail, BR-19 Stage Dependency per Page/Panel, BR-20 Stage Gate Approval, BR-21 Finding Triage. |  |
| 3.0 | 3. User Stories | Rows 22-30 | Added | US-21 to US-29: create Series, assign work, export, notifications, version history, provenance report, resolve change request, reopen chapter, triage Reader feedback. |  |
| 4.0 | 4. Usecases | Rows 16-25 | Added | UC-15 to UC-24: audit log and AI usage, dashboard, resolve change request, version history and compare, provenance report, approve and lock, read preview and feedback, search, reopen chapter, triage Reader feedback. |  |
| 5.0 | 5. Requirements | Rows 10-54 | Added | 37 detailed FRs FR-Admin-01 ... FR-All-04 (the IDs already used in "Related Req." of sheet 4) and NFR-04 to NFR-11 from the registered proposal. |  |
| 6.0 | 6.Entities | Rows 22-35 | Added | E-21 ScriptLine, E-22 BibleEntry, E-23 BibleEntryVersion, E-24 AssetReference, E-25 AISuggestion, E-26 ConsistencyFinding, E-27 ReaderFeedback, E-28 AuditLog, E-29 AIUsageRecord, E-30 ElementType, E-31 TypographyPreset, E-32 ExportPreset, E-33 ConsistencyRule, E-34 StageProgress (state of each stage per Page/Panel). |  |
| 7.0 | 10. Core Flows; 11. Traceability; 12. Coverage Check; 13. Change Log | New sheets | Added | Five core flows (objective, actors, main flow, result, exceptions), stage gates, state machines and live coverage counts; UC to US/BR/Requirement/Entity trace matrix; coverage check; this log. |  |
| 8.0 | 2. Busiess Rules | B14 (BR13) | Modified | Rule ID typo fixed: BR-13. | BR13 |
| 9.0 | 2. Busiess Rules | C5 (BR-04) | Modified | Aligned with the corrected core flows (new text is in the cell). | Content-Addressable Invariant: Every uploaded file is identified by an SHA-256 hash. |
| 10.0 | 2. Busiess Rules | C11 (BR-10) | Modified | Aligned with the corrected core flows (new text is in the cell). | Mandatory AI Provenance: An AI-generated suggestion cannot be merged into the working content until its provenance record (model, prompt, input context) is stored. |
| 11.0 | 2. Busiess Rules | C13 (BR-12) | Modified | Aligned with the corrected core flows (new text is in the cell). | Approval Lock: Once a Producer approves and locks a Chapter, its Pages and Panels become read-only until an authorized rollback explicitly reopens them. |
| 12.0 | 4. Usecases | H3 (UC-02) | Modified | Aligned with the corrected core flows (new text is in the cell). | Invalid credentials → system rejects and shows connection error. |
| 13.0 | 4. Usecases | G4 (UC-03) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Producer opens New Series → 2. Enters series metadata → 3. Defines pipeline stages and roles → 4. System creates the Series. |
| 14.0 | 4. Usecases | I4 (UC-03) | Modified | Aligned with the corrected core flows (new text is in the cell). | A new Series exists with a configured production pipeline. |
| 15.0 | 4. Usecases | J4 (UC-03) | Modified | Aligned with the corrected core flows (new text is in the cell). | FR-Producer-01 |
| 16.0 | 4. Usecases | G5 (UC-04) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Producer opens Series Bible → 2. Adds or edits an entry → 3. System versions the entry and saves. |
| 17.0 | 4. Usecases | H5 (UC-04) | Modified | Aligned with the corrected core flows (new text is in the cell). | Conflicting concurrent edit → system flags the version conflict for resolution. |
| 18.0 | 4. Usecases | I5 (UC-04) | Modified | Aligned with the corrected core flows (new text is in the cell). | Series Bible entry is created or updated and versioned. |
| 19.0 | 4. Usecases | G6 (UC-05) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Producer selects an item → 2. Chooses assignee and deadline → 3. System saves and notifies the assignee. |
| 20.0 | 4. Usecases | H6 (UC-05) | Modified | Aligned with the corrected core flows (new text is in the cell). | Assignee unavailable/overloaded → system warns but allows override. |
| 21.0 | 4. Usecases | G7 (UC-06) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Writer drafts script → 2. Requests AI breakdown → 3. AI proposes structure → 4. Writer reviews, edits or rejects the result. |
| 22.0 | 4. Usecases | H7 (UC-06) | Modified | Aligned with the corrected core flows (new text is in the cell). | AI service unavailable → Writer continues breaking down the script manually. |
| 23.0 | 4. Usecases | I7 (UC-06) | Modified | Aligned with the corrected core flows (new text is in the cell). | Script is saved and, if accepted, Scenes/Pages/Panels are created with provenance recorded. |
| 24.0 | 4. Usecases | J7 (UC-06) | Modified | Aligned with the corrected core flows (new text is in the cell). | FR-Writer-01, FR-Writer-02 |
| 25.0 | 4. Usecases | F8 (UC-07) | Modified | Aligned with the corrected core flows (new text is in the cell). | Artist is assigned to the Page/Panel. |
| 26.0 | 4. Usecases | G8 (UC-07) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Artist opens the task → 2. Plans layout or requests AI layout suggestion → 3. Uploads artwork → 4. Marks item ready for review. |
| 27.0 | 4. Usecases | H8 (UC-07) | Modified | Aligned with the corrected core flows (new text is in the cell). | Upload fails/duplicate hash → system reuses existing Asset via reference. |
| 28.0 | 4. Usecases | I8 (UC-07) | Modified | Aligned with the corrected core flows (new text is in the cell). | A new artwork Version exists and the item's pipeline stage advances. |
| 29.0 | 4. Usecases | J8 (UC-07) | Modified | Aligned with the corrected core flows (new text is in the cell). | FR-Artist-01, FR-Artist-02, FR-Artist-03 |
| 30.0 | 4. Usecases | F9 (UC-08) | Modified | Aligned with the corrected core flows (new text is in the cell). | Panel artwork exists; script lines are available. |
| 31.0 | 4. Usecases | G9 (UC-08) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Letterer places a balloon → 2. Binds it to a script line/speaker → 3. System checks for overflow/overlap → 4. Letterer resolves warnings. |
| 32.0 | 4. Usecases | H9 (UC-08) | Modified | Aligned with the corrected core flows (new text is in the cell). | Text overflows balloon → system raises a warning for the Letterer to resize or edit. |
| 33.0 | 4. Usecases | I9 (UC-08) | Modified | Aligned with the corrected core flows (new text is in the cell). | Text Elements are placed, bound and free of unresolved overflow warnings. |
| 34.0 | 4. Usecases | J9 (UC-08) | Modified | Aligned with the corrected core flows (new text is in the cell). | FR-Letterer-01, FR-Letterer-02 |
| 35.0 | 4. Usecases | G10 (UC-09) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Editor opens the submission → 2. Compares versions side by side → 3. Attaches comments/annotations → 4. Approves or issues a change request. |
| 36.0 | 4. Usecases | H10 (UC-09) | Modified | Aligned with the corrected core flows (new text is in the cell). | Editor rejects the item → system creates a Change Request and reopens the pipeline stage. |
| 37.0 | 4. Usecases | I10 (UC-09) | Modified | Aligned with the corrected core flows (new text is in the cell). | Item is approved and advances, or a Change Request is logged against it. |
| 38.0 | 4. Usecases | J10 (UC-09) | Modified | Aligned with the corrected core flows (new text is in the cell). | FR-Editor-01, FR-Editor-04 |
| 39.0 | 4. Usecases | G11 (UC-10) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Editor requests a consistency check → 2. AI service scans content against Series Bible → 3. System lists findings → 4. Editor triages each finding. |
| 40.0 | 4. Usecases | H11 (UC-10) | Modified | Aligned with the corrected core flows (new text is in the cell). | AI service unavailable → system informs the Editor and allows a fully manual review to proceed. |
| 41.0 | 4. Usecases | F12 (UC-11) | Modified | Aligned with the corrected core flows (new text is in the cell). | Chapter exists and Producer has sharing rights. |
| 42.0 | 4. Usecases | G12 (UC-11) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Producer selects Generate Preview → 2. Sets expiry → 3. System creates a watermarked link → 4. Producer shares the link. |
| 43.0 | 4. Usecases | H12 (UC-11) | Modified | Aligned with the corrected core flows (new text is in the cell). | Link accessed after expiry → system denies access and logs the attempt. |
| 44.0 | 4. Usecases | E13 (UC-12) | Modified | Aligned with the corrected core flows (new text is in the cell). | Producer exports an approved chapter into print-ready pages, PDF, CBZ or vertical-scroll webtoon format. |
| 45.0 | 4. Usecases | G13 (UC-12) | Modified | Aligned with the corrected core flows (new text is in the cell). | 1. Producer selects Export → 2. Chooses output format → 3. System renders the export job asynchronously → 4. Producer downloads the result. |
| 46.0 | 4. Usecases | H13 (UC-12) | Modified | Aligned with the corrected core flows (new text is in the cell). | Rendering fails → system reports the error and keeps the chapter's approved state unchanged. |
| 47.0 | 4. Usecases | I13 (UC-12) | Modified | Aligned with the corrected core flows (new text is in the cell). | An export file in the chosen format is available for download. |
| 48.0 | 4. Usecases | D14 (UC-13) | Modified | Aligned with the corrected core flows (new text is in the cell). | Producer |
| 49.0 | 4. Usecases | E14 (UC-13) | Modified | Aligned with the corrected core flows (new text is in the cell). | Producer or Editor restores an entity to an earlier recorded version after an unwanted change. |
| 50.0 | 4. Usecases | H14 (UC-13) | Modified | Aligned with the corrected core flows (new text is in the cell). | Entity is locked/approved → system requires an explicit unlock step before restore proceeds. |
| 51.0 | 4. Usecases | J14 (UC-13) | Modified | Aligned with the corrected core flows (new text is in the cell). | FR-Producer-06 |
| 52.0 | 5. Requirements | D3 (FR-02) | Modified | Aligned with the corrected core flows (new text is in the cell). | The system must provide a workflow (pipeline) capable of handling status transitions (Script, Thumbnail, Pencil, Ink, etc.) and tracking progress. |
| 53.0 | 5. Requirements | D5 (FR-04) | Modified | Aligned with the corrected core flows (new text is in the cell). | The system must integrate AI to analyze scenarios, suggest layouts, and perform continuity checks. |
| 54.0 | 6.Entities | C2 (E-01) | Modified | Aligned with the corrected core flows (new text is in the cell). | The root entity represents a comic book series. |
| 55.0 | 6.Entities | C3 (E-02) | Modified | Aligned with the corrected core flows (new text is in the cell). | Every Chapter must belong to exactly one Series (N-1 relationship with Series). |
| 56.0 | 6.Entities | C6 (E-05) | Modified | Aligned with the corrected core flows (new text is in the cell). | Every Panel must belong to exactly one Page. It has a `reading_order_index` attribute (RTL or Top-down). |
| 57.0 | 6.Entities | C7 (E-06) | Modified | Aligned with the corrected core flows (new text is in the cell). | Every Element (speech bubble, sound effect) must belong to exactly one Panel. |
| 58.0 | 6.Entities | C8 (E-07) | Modified | Aligned with the corrected core flows (new text is in the cell). | An encyclopedia containing characters, locations, props, and plot rules. |
| 59.0 | 6.Entities | C14 (E-13) | Modified | Aligned with the corrected core flows (new text is in the cell). | Links exactly one User to exactly one Panel, Page or Chapter task, carrying a deadline and optional dependencies. |
| 60.0 | 6.Entities | C16 (E-15) | Modified | Aligned with the corrected core flows (new text is in the cell). | A region-anchored note or drawn markup bound to exactly one Version of a Panel or Page. |
| 61.0 | 6.Entities | C17 (E-16) | Modified | Aligned with the corrected core flows (new text is in the cell). | Raised from a Comment; tracks a requested fix on exactly one target Version through to resolution or rejection. |
| 62.0 | 6.Entities | C18 (E-17) | Modified | Aligned with the corrected core flows (new text is in the cell). | An immutable, append-only record of a state-changing operation on any entity; if AI-assisted, it also stores the model, prompt, input context and accepting user. |
| 63.0 | 6.Entities | C19 (E-18) | Modified | Aligned with the corrected core flows (new text is in the cell). | A time-limited, watermarked, revocable link granting a Reader read-only access to exactly one Chapter. |
| 64.0 | 6.Entities | C20 (E-19) | Modified | Aligned with the corrected core flows (new text is in the cell). | An asynchronous rendering request that produces a PDF, CBZ, page-image or webtoon output from exactly one approved Chapter version. |
| 65.0 | 8.References | F6 | Modified | Wrong Business Rule cross-reference fixed (new text is in the cell). | Standard followed for the human/AI provenance tagging engine (BR-11). |
| 66.0 | 8.References | F8 | Modified | Wrong Business Rule cross-reference fixed (new text is in the cell). | Theoretical basis for entity-level optimistic concurrency control (BR-06). |
| 67.0 | Decision / Open item |  | Decision | Chapter is Locked before the Preview link is created (UC-11 precondition changed to "Approved and Locked", BR-17). Reader therefore sees exactly the version that is exported. Confirm with the team and the supervisor. |  |
| 68.0 | Decision / Open item |  | Decision | Administrator cannot read creative content (default); emergency access is audited. |  |
| 69.0 | Decision / Open item |  | Decision | Editor sees the Provenance Report read-only (default). |  |
| 70.0 | Decision / Open item |  | Decision | Reopen revokes the preview links of the old version (UC-23). |  |
| 71.0 | Decision / Open item |  | Decision | A Published chapter is final in the MVP. |  |
| 72.0 | Decision / Open item |  | Decision | The Editor approves the Script gate (default, configurable per stage, BR-20). |  |
| 73.0 | Decision / Open item |  | Open | FR-Admin-03 (storage, retention, backup), FR-Admin-05 (master data) and FR-All-01 (authentication and profile) have no use case; tagged "Support". Add use cases if they are in scope for the demo. |  |
| 74.0 | Decision / Open item |  | Open | The Artwork Gate needs a minimum image resolution; no requirement defines it yet. |  |
| 75.0 | Decision / Open item |  | Open | NFR-05 has no measurable target for "feels immediate". Agree numbers (for example page-open time) with the supervisor. |  |
| 76.0 | Decision / Open item |  | Open | AI layout suggestion (FR-Artist-02), automatic overlap detection (FR-Letterer-03) and image-based continuity checks (FR-04) are extended scope in the proposal; confirm they stay out of the minimum viable scope. |  |
| 77.0 | Decision / Open item |  | Open | Sheet and file names contain typos ("Busiess Rules", "Casptone"). Left unchanged so existing links and references keep working. |  |
