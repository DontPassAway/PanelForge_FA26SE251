# PanelForge_FA26SE251

An AI-Assisted Manga Production Management Platform with a Structured Content Model and Full Revision Provenance.

---

## Table of Contents

* [1. Project Overview](#1-project-overview)
* [2. Project Information](#2-project-information)
* [3. Core Features](#3-core-features)
* [4. User Roles](#4-user-roles)
* [5. System Architecture](#5-system-architecture)
* [6. Technical Documentation](#6-technical-documentation)
* [7. Getting Started](#7-getting-started)
* [8. Project Scope](#8-project-scope)
* [9. Project Plan](#9-project-plan)
* [10. Expected Deliverables](#10-expected-deliverables)
* [11. Academic Context](#11-academic-context)
* [12. License](#12-license)

---

## 1. Project Overview

PanelForge is an AI-assisted Manga production management platform designed to manage Manga, comic, and webtoon production as structured, versioned, and traceable data rather than as a collection of independent files.

The platform is designed for distributed creative teams consisting of writers, artists, letterers, editors, and producers who collaborate on the same series and chapters.

The core idea of PanelForge is to represent a Manga chapter as queryable structured data down to individual panels and elements such as dialogue balloons, narration boxes, sound effects, character instances, and artwork layers.

### Core Pillars

**Structured Representation**

Manga content is represented using an explicit hierarchy:

```text
Series
  └── Chapter
       └── Scene
            └── Page
                 └── Panel
                      └── Element
```

**Production Pipeline**

Creative work is managed through configurable production stages with role-based assignments, deadlines, dependencies, approval gates, and panel-level progress tracking.

**AI Assistance and Provenance**

AI provides production assistance such as script breakdown and consistency checking while maintaining a human-in-the-loop workflow and recording the provenance of AI-assisted operations.

---

## 2. Project Information

| Item             | Information                                                                                                                              |
| ---------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Project Name     | PanelForge                                                                                                                               |
| English Title    | PanelForge: An AI-Assisted Manga Production Management Platform with a Structured Content Model and Full Revision Provenance             |
| Vietnamese Title | PanelForge: Nền tảng quản lý sản xuất truyện tranh Manga theo mô hình nội dung có cấu trúc, hỗ trợ cộng tác và truy vết phiên bản với AI |
| Project Type     | Software Engineering Capstone Project                                                                                                    |
| Academic Period  | 09/2026 – 03/2027                                                                                                                        |
| Supervisor       | Nguyễn Tấn Phúc                                                                                                                          |
| Student          | Bùi Ngọc Tâm                                                                                                                             |
| Student Code     | SE170009                                                                                                                                 |

---

## 3. Core Features

### 3.1 Structured Manga Content Model

PanelForge uses a strict hierarchical content model:

```text
Series
  └── Chapter
       └── Scene
            └── Page
                 └── Panel
                      └── Element
```

Elements may represent:

* Dialogue balloons
* Narration boxes
* Sound effects
* Character instances
* Artwork layers

This allows scripts, panels, characters, dialogue, and visual content to maintain explicit machine-readable relationships.

---

### 3.2 Versioned Series Bible

The Series Bible is a versioned repository containing:

* Characters
* Locations
* Props
* Terminology
* Style rules
* Established plot facts

It acts as both a human reference source and a grounding source for AI-powered consistency checking.

---

### 3.3 Collaborative Production Pipeline

PanelForge supports configurable production workflows such as:

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

The workflow supports:

* Role-based assignments
* Deadlines
* Dependencies
* Stage transitions
* Panel-level progress
* Approval gates
* Change requests

---

### 3.4 Panel-Anchored Review

Reviews are attached directly to a specific region of a specific panel version.

Reviewers can:

* Add comments
* Draw annotations
* Create change requests
* Compare versions
* Approve submissions
* Reject submissions

This prevents feedback from becoming detached from the artwork it refers to.

---

### 3.5 Human-in-the-Loop AI

PanelForge uses AI as an assistant rather than an autonomous decision maker.

AI-assisted capabilities include:

* Script-to-structure breakdown
* Panel planning
* Layout suggestions
* Series Bible consistency checking
* Character continuity checking
* Balloon-fit analysis

AI output follows:

```text
AI Suggestion
      ↓
Human Review
      ↓
Accept / Edit / Reject
      ↓
Provenance Record
```

---

### 3.6 Immutable Versioning

PanelForge maintains complete revision history through:

* Append-only event logging
* Versioned domain entities
* Content-addressed assets
* Version comparison
* Rollback
* Historical reconstruction

AI-assisted operations additionally record relevant AI provenance.

---

### 3.7 Multi-Format Export

PanelForge is designed to support export to:

* Print-ready page images
* PDF
* CBZ
* Vertical-scroll webtoon output
* Open JSON

---

## 4. User Roles

| Role                    | Main Responsibilities                                      |
| ----------------------- | ---------------------------------------------------------- |
| Administrator           | Users, workspaces, permissions, AI configuration, audit    |
| Producer / Series Owner | Series setup, workflow, assignments, approvals             |
| Writer                  | Structured scripts, scenes, panels, dialogue, AI breakdown |
| Artist                  | Layouts, thumbnails, artwork, production stages            |
| Letterer                | Balloons, narration, SFX, typography, reading order        |
| Editor / Reviewer       | Review, annotations, consistency checks, approval          |
| External Stakeholder    | Watermarked previews and feedback                          |
| Authenticated User      | Profile, notifications, search, activity history           |

---

## 5. System Architecture

PanelForge follows a modular architecture separating the core production domain from supporting services.

```text
┌─────────────────────────────────────────────────────────────┐
│                       Frontend App                          │
│                                                             │
│ Script Editor | Panel Canvas | Dashboard | Review Tool     │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ↓
┌─────────────────────────────────────────────────────────────┐
│                          API Layer                          │
└──────────────────────────────┬──────────────────────────────┘
                               │
         ┌─────────────────────┼─────────────────────┐
         ↓                     ↓                     ↓
┌────────────────┐    ┌────────────────┐    ┌────────────────┐
│  Domain Core   │    │Workflow Engine │    │ Access Control │
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

                 ┌─────────────────────────┐
                 │       AI Service        │
                 │                         │
                 │ LLM / Vector DB /       │
                 │ Consistency Analysis    │
                 └─────────────────────────┘
```

The architecture is designed around Domain-Driven Design, CQRS, Event Sourcing, immutable versioning, workflow modelling, concurrency control, provenance, and modular service boundaries.

---

## 6. Technical Documentation

Detailed technical documentation is separated into four documents.

### Requirements

[Functional & Non-Functional Requirements](docs/requirements.md)

Contains:

* Problem statement
* Functional requirements
* Role-based requirements
* Non-functional requirements
* Security requirements
* Revision and provenance requirements

### Architecture

[System Architecture & Technical Foundations](docs/architecture.md)

Contains:

* Architectural overview
* Domain-Driven Design
* Aggregate modelling
* Event Sourcing
* CQRS
* Content-Addressed Storage
* Workflow state machines
* Concurrency control
* AI service architecture
* Provenance architecture

### Implementation & QA

[Implementation & Quality Assurance](docs/implementation-qa.md)

Contains:

* Backend implementation
* Database
* Authentication
* Image pipeline
* Export pipeline
* Unit testing
* Integration testing
* Security testing
* Concurrency testing
* CI/CD

### Project Plan

[Project Planning & Deliverables](docs/project-plan.md)

Contains:

* MVP scope
* Extended scope
* Out-of-scope items
* Work packages
* Project timeline
* Deliverables
* Demonstration dataset
* Success criteria

---

## 7. Getting Started

### 7.1 Prerequisites

The current development environment requires:

* .NET 9 SDK
* Docker Desktop
* Docker Compose
* PostgreSQL 17
* Git

Additional services may be required depending on the selected implementation of AI, object storage, background processing, and vector search.

---

### 7.2 Clone Repository

```bash
git clone <repository-url>
cd PanelForge_FA26SE251
```

---

### 7.3 Start Infrastructure

Start the required infrastructure using Docker Compose:

```bash
docker compose up -d postgres-db
```

Check running containers:

```bash
docker compose ps
```

---

### 7.4 Restore Dependencies

```bash
dotnet restore
```

---

### 7.5 Run Backend

```bash
dotnet run --project src/PanelForge.API
```

---

### 7.6 Access Swagger

After the API has started, open:

```text
https://localhost:7274/swagger
```

The exact port may change depending on the local development configuration.

---

## 8. Project Scope

### 8.1 Mandatory MVP

The mandatory MVP focuses on:

1. Structured Manga content model
2. Versioned Series Bible
3. Production pipeline
4. Panel-anchored review
5. Provenance subsystem
6. Script-to-structure breakdown
7. Grounded consistency checking

---

### 8.2 Extended Scope

Potential extended features include:

* AI layout suggestions
* Vision-based character continuity
* Balloon-fit analysis
* Advanced collaborative editing
* More advanced real-time collaboration

---

### 8.3 Out of Scope

PanelForge deliberately excludes autonomous generative artwork from the core project scope.

AI focuses on:

* Structure
* Text
* Metadata
* Consistency
* Production assistance

Vision capabilities are intended for similarity-based continuity checking rather than autonomous artwork generation.

Commercial copyrighted Manga should not be used as demonstration material without appropriate permission.

---

## 9. Project Plan

The project is divided into five major work packages:

| Package | Focus                                                  |
| ------- | ------------------------------------------------------ |
| WP1     | Project Management, Domain Analysis and Content Schema |
| WP2     | Content Domain, Provenance and Version Control         |
| WP3     | Production Pipeline, Access Control and Delivery       |
| WP4     | Structured Editors and Production Workspace            |
| WP5     | AI Assistance, QA and Deployment                       |

The detailed responsibilities and project planning are documented in:

[docs/project-plan.md](docs/project-plan.md)

---

## 10. Expected Deliverables

The project is expected to deliver:

* Deployed PanelForge web platform
* Backend/API service
* Event-sourced provenance store
* Object storage integration
* AI assistance service
* Open Manga JSON schema
* Import/export tooling
* SRS
* SDD
* Database and event-store design
* Test plan and test report
* Deployment guide
* Bilingual user manual
* Demonstration dataset
* Final presentation package
* Demonstration video
* Source repository

---

## 11. Academic Context

PanelForge is a Software Engineering Capstone Project focused on applying software engineering principles to a real-world creative production problem.

Major engineering areas include:

* Domain modelling
* Software architecture
* Event-driven persistence
* CQRS
* Version control
* Concurrency control
* Workflow modelling
* Access control
* AI service integration
* Automated testing
* CI/CD
* Cloud deployment
* Data portability

---

## 12. License

Academic Capstone Project — PanelForge Team.

Demonstration content must comply with the project's rights-clean content policy.

No copyrighted commercial Manga should be ingested without appropriate permission.

---

## Project Status

**Status:** In Development

**Project Period:** September 2026 – March 2027

**Project Type:** Software Engineering Capstone Project

**Repository:** `PanelForge_FA26SE251`
