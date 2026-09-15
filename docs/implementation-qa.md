# PanelForge - Implementation & Quality Assurance

---

## Table of Contents

- [1. Implementation Blueprint](#1-implementation-blueprint)
- [2. Backend](#2-backend)
- [3. Database](#3-database)
- [4. Authentication and Security](#4-authentication-and-security)
- [5. Email and Notification](#5-email-and-notification)
- [6. Image and Asset Pipeline](#6-image-and-asset-pipeline)
- [7. Export Pipeline](#7-export-pipeline)
- [8. AI Implementation](#8-ai-implementation)
- [9. Testing Strategy](#9-testing-strategy)
- [10. Quality Gates](#10-quality-gates)
- [11. CI/CD](#11-cicd)
- [12. Deployment](#12-deployment)
- [13. Monitoring and Reliability](#13-monitoring-and-reliability)

---

# 1. Implementation Blueprint

The implementation is divided into several technical areas:

```text
Frontend
   ↓
API
   ↓
Application / Domain
   ↓
Persistence
   ↓
Infrastructure

Supporting Services:
- AI
- Image Processing
- Export
- Email
- Background Jobs
```

---

# 2. Backend

## 2.1 Runtime

Current backend direction:

```text
.NET 9
ASP.NET Core Web API
```

---

## 2.2 Architecture

The backend should follow a modular architecture separating:

```text
API
Application
Domain
Infrastructure
```

Conceptually:

```text
PanelForge.API
      ↓
PanelForge.Application
      ↓
PanelForge.Domain
      ↑
PanelForge.Infrastructure
```

The Domain layer should remain independent from infrastructure implementations wherever practical.

---

## 2.3 API

The backend should expose documented APIs for:

- Authentication
- Users
- Series
- Chapters
- Scenes
- Pages
- Panels
- Elements
- Series Bible
- Workflow
- Assignments
- Reviews
- Comments
- Versions
- Provenance
- AI jobs
- Exports

Swagger/OpenAPI should be provided for development and testing.

---

# 3. Database

## 3.1 Database

Current database direction:

```text
PostgreSQL 17
```

---

## 3.2 ORM

The backend uses:

```text
Entity Framework Core
```

where appropriate for relational persistence.

---

## 3.3 Persistence Responsibilities

Persistence must support:

- Domain state
- Version information
- Audit information
- Workflow state
- User and workspace relationships
- Review data
- Provenance data

---

## 3.4 Database Principles

Database design should prioritize:

- Referential integrity
- Transactional consistency
- Explicit relationships
- Version tracking
- Migration support

---

# 4. Authentication and Security

## 4.1 Authentication

The application uses token-based authentication.

Current direction:

```text
JWT Authentication
```

---

## 4.2 Authorization

Authorization should support:

- Role-based authorization
- Resource-level checks
- Workspace membership
- Assignment-based access

---

## 4.3 Secret Management

Sensitive configuration must not be committed to source control.

Examples include:

- Database credentials
- JWT secrets
- SMTP credentials
- AI provider keys
- Storage credentials

Local development may use environment variables or development secret storage.

Container deployments should use appropriate secret isolation mechanisms.

---

## 4.4 Asset Security

Private assets should not be exposed through permanent public URLs.

The system should support:

- Short-lived access
- Revocation
- Watermarking
- Authorization checks
- Access logging

---

# 5. Email and Notification

The application may provide email notifications for:

- Assignments
- Mentions
- Review requests
- Change requests
- Deadline reminders
- Approval results

Current SMTP direction:

```text
SMTP
Port: 587
```

The email implementation should be abstracted behind an application service such as:

```text
IEmailService
```

so the infrastructure provider can be replaced without modifying domain logic.

---

# 6. Image and Asset Pipeline

The image pipeline is responsible for:

- Image ingestion
- Metadata extraction
- Thumbnail generation
- Asset versioning
- Watermarking
- Storage
- Retrieval

Conceptually:

```text
Upload
  ↓
Validation
  ↓
Processing
  ↓
Hash
  ↓
Object Storage
  ↓
Thumbnail
  ↓
Available Asset
```

---

## 6.1 Content Hashing

Assets may be identified using SHA-256 content hashes.

```text
Binary Asset
    ↓
SHA-256
    ↓
Content Identifier
```

This supports deduplication and integrity checking.

---

# 7. Export Pipeline

The export system should support:

- Print-ready images
- PDF
- CBZ
- Vertical-scroll webtoon output
- Open JSON

Conceptually:

```text
Structured Chapter
       ↓
Export Configuration
       ↓
Validation
       ↓
Rendering
       ↓
Packaging
       ↓
Output
```

Large exports should be handled asynchronously.

---

# 8. AI Implementation

## 8.1 AI Service Boundary

AI operations should be separated from the deterministic core.

```text
PanelForge Core
      ↓
AI Application Service
      ↓
Background Job
      ↓
AI Provider
```

---

## 8.2 AI Features

Initial AI implementation should focus on:

### Script Breakdown

```text
Script
  ↓
AI Analysis
  ↓
Scene / Page / Panel Suggestions
```

### Consistency Checking

```text
Content
  +
Series Bible
  ↓
AI Analysis
  ↓
Potential Inconsistencies
```

### Human Review

```text
AI Result
   ↓
Review
   ├── Accept
   ├── Edit
   └── Reject
```

---

## 8.3 AI Provenance

Each AI operation should record relevant metadata such as:

- Model identifier
- Parameters
- Input context
- Output
- User
- Acceptance status
- Human modification

---

# 9. Testing Strategy

Testing should cover domain logic, application behavior, infrastructure integration, security, concurrency, and AI failure scenarios.

---

## 9.1 Unit Testing

Unit tests should focus on:

- Domain entities
- Aggregate rules
- Business rules
- State transitions
- Validation
- Versioning
- Provenance logic

External dependencies should be mocked where appropriate.

Examples:

```text
IEmailService
IPanelForgeDbContext
IPasswordHasher
```

---

## 9.2 Domain Rule Testing

Important domain rules include:

```text
Element must belong to Panel
Panel must belong to Page
Page must belong to Scene
Scene must belong to Chapter
Chapter must belong to Series
```

Tests should verify that invalid structures are rejected.

---

## 9.3 Workflow Testing

Test:

- Valid transitions
- Invalid transitions
- Role restrictions
- Approval gates
- Rejection flows
- Change-request flows

---

## 9.4 Versioning Testing

Test:

- Version creation
- Version comparison
- Version restoration
- Historical reconstruction
- Concurrent updates

---

## 9.5 Integration Testing

Integration tests should cover:

- API endpoints
- Database access
- Authentication
- Authorization
- Transactions
- Event persistence
- Read projections

Dockerized PostgreSQL can be used for integration testing.

---

## 9.6 Concurrency Testing

Test scenarios such as:

```text
User A reads Version 10
User B reads Version 10

User A saves Version 11

User B attempts to save Version 10

Expected:
Conflict detected
```

The system must not silently overwrite Version 11.

---

## 9.7 Security Testing

Security testing should cover:

- Authentication
- Authorization
- RBAC
- Resource access
- External preview links
- Expiration
- Revocation
- Asset access
- Secret handling

---

## 9.8 AI Testing

AI tests should cover:

- Provider timeout
- Provider failure
- Rate limiting
- Invalid AI response
- Missing output
- Retry behavior
- Fallback behavior
- Provenance recording

---

## 9.9 Export Testing

Verify:

- Generated PDF
- Generated CBZ
- Generated page images
- Open JSON validation
- Reading order
- Asset references
- Watermarking

---

# 10. Quality Gates

A change should pass appropriate quality checks before being merged.

Minimum checks should include:

```text
Build
  ↓
Unit Tests
  ↓
Integration Tests
  ↓
Static Checks
  ↓
Container Build
```

Pull requests should not be merged when mandatory checks fail.

---

# 11. CI/CD

The project should use GitHub Actions for automated CI/CD.

Conceptually:

```text
Developer
    ↓
Git Push
    ↓
GitHub
    ↓
GitHub Actions
    ↓
Build
    ↓
Unit Tests
    ↓
Integration Tests
    ↓
Docker Build
    ↓
Container Registry
    ↓
Deployment
```

---

## 11.1 Pull Request Pipeline

Pull requests should execute:

1. Restore dependencies
2. Build
3. Unit tests
4. Integration tests
5. Validation checks

---

## 11.2 Main Branch Pipeline

Changes merged into the deployment branch may execute:

1. Build
2. Test
3. Docker image build
4. Image push
5. Deployment

---

## 11.3 PostgreSQL CI Service

Integration tests may run against a PostgreSQL service container.

Conceptually:

```text
GitHub Actions Runner
        │
        ├── .NET Application
        │
        └── PostgreSQL Container
```

---

# 12. Deployment

The application should be containerized.

Example:

```text
Docker
 ├── Backend
 ├── PostgreSQL
 └── Supporting Services
```

The exact production topology may evolve as deployment requirements are finalized.

---

# 13. Monitoring and Reliability

The deployed system should provide sufficient observability for:

- API failures
- Database failures
- Background job failures
- AI failures
- Export failures
- Authentication failures

Logs should contain enough context to diagnose problems without exposing sensitive information.

---

## Reliability Principles

### AI Failure

AI failure must not block manual production.

### Database Failure

Critical operations should fail safely rather than silently losing state.

### Background Job Failure

Jobs should support retry or explicit failure reporting.

### Export Failure

Failed exports should be reported to users and should not corrupt the underlying structured content.

---

## Implementation Summary

```text
.NET 9
   +
ASP.NET Core
   +
PostgreSQL 17
   +
Entity Framework Core
   +
JWT Authentication
   +
Docker
   +
GitHub Actions
   +
Automated Testing
   +
AI Services
   =
PanelForge Implementation
```