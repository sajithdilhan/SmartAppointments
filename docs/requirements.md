# Smart Appointment & Queue Management System

Business Requirements and Solution Architecture Document
Prepared for: Sajith Dilhan | Portfolio Practice Project | .NET Microservices

## 1. Executive Summary

This document defines the functional requirements, architecture, service boundaries, integration patterns, non-functional requirements, and MVP scope for a Smart Appointment and Queue Management System. The goal is to build an interview-ready .NET microservices practice project within 2-3 weeks, focusing on practical backend architecture rather than over-engineered production complexity.

- Customers can register, view available slots, book appointments, cancel bookings, join walk-in queues, and view queue status.
- Staff can manage daily appointments, call queue numbers, and update appointment or queue statuses.
- Admins can configure branches, services, slots, staff, and view operational reports.
- The solution demonstrates synchronous HTTP communication, asynchronous messaging, idempotency, rate limiting, resiliency, observability, authentication, authorization, database-per-service, and background processing.

## 2. Business Context

Service-based organizations such as clinics, salons, vehicle service centers, government counters, and consultation centers often operate with both scheduled appointments and walk-in customers. Manual queue handling can lead to duplicate bookings, missed notifications, poor visibility, and weak reporting. This system provides a digital backend platform to manage both appointments and walk-in queues.

## 3. Actors and Roles

| Role | Responsibilities |
|---|---|
| Customer | Register/login, search slots, book/cancel own appointments, join queue, view own queue status and notification history. |
| Staff | View assigned branch appointments and queue, call next customer, mark completed, served, no-show, or cancelled. |
| Admin | Manage branches, service types, staff, slot generation/configuration, and view reports. |

## 4. Scope

### 4.1 In Scope

- JWT-based authentication and role-based authorization.
- Appointment booking, cancellation, completion, and no-show marking.
- Walk-in queue join, call next, serve, cancel, and no-show flows.
- Availability and slot management.
- Async notifications using RabbitMQ.
- Reporting read model updated through events.
- Idempotency for create appointment and join queue endpoints.
- Outbox pattern for reliable event publication.
- Logging, correlation IDs, health checks, and OpenTelemetry-ready tracing.
- Docker Compose based local development only.

### 4.2 Out of Scope

- Frontend UI.
- Cloud hosting.
- Kubernetes deployment.
- Real SMS/email integration.
- Complex OAuth/OIDC identity provider.
- Full event sourcing.
- Payment processing.

## 5. High-Level Architecture

```text
                        +----------------+
                        | Client/Swagger |
                        +--------+-------+
                                 |
                                 v
                        +----------------+
                        |  API Gateway   |
                        | YARP + Auth +  |
                        | Rate Limiting  |
                        +--------+-------+
                                 |
       +-------------------------+-------------------------+
       |                         |                         |
       v                         v                         v
+--------------+          +--------------+          +--------------+
| Auth Service |          | Booking      |          | Queue Service|
| Users + JWT  |          | Service      |          | Walk-in Queue|
+--------------+          +------+-------+          +------+-------+
                                  |                         |
                                  v                         |
                           +--------------+                 |
                           | Availability |                 |
                           | Service      |                 |
                           +------+-------+                 |
                                  |                         |
                                  +------------+------------+
                                               |
                                               v
                                        +-------------+
                                        |  RabbitMQ   |
                                        +------+------+
                                               |
                       +-----------------------+-----------------------+
                       |                                               |
                       v                                               v
              +----------------+                              +----------------+
              | Notification   |                              | Reporting      |
              | Service        |                              | Service        |
              +----------------+                              +----------------+
```

## 6. Service Responsibilities

| Service | Primary Responsibility | Data Store |
|---|---|---|
| API Gateway | Single entry point, routing, auth validation, rate limiting, correlation ID forwarding. | None |
| Auth Service | User registration, login, password hashing, JWT generation, roles. | auth_db |
| Availability Service | Branches, service types, working hours, slot generation, reserve/release slot. | availability_db |
| Booking Service | Appointment lifecycle, idempotency, outbox, sync call to availability. | booking_db |
| Queue Service | Walk-in queue lifecycle, queue number generation, idempotency, outbox. | queue_db |
| Notification Service | Consume events and create simulated email/SMS/in-app notifications. | notification_db |
| Reporting Service | Consume events and maintain reporting read models using Dapper. | reporting_db |

## 7. Core Business Workflows

### 7.1 Customer Books Appointment

1. Customer logs in and searches available slots.
2. Customer submits booking request with Idempotency-Key.
3. Booking Service validates the request and checks idempotency record.
4. Booking Service calls Availability Service to reserve the slot.
5. Availability Service reduces available capacity or marks slot booked.
6. Booking Service creates appointment and outbox message in same transaction.
7. Outbox worker publishes AppointmentBooked event to RabbitMQ.
8. Notification Service creates confirmation notification.
9. Reporting Service updates daily appointment statistics.

### 7.2 Customer Cancels Appointment

1. Customer submits cancellation request.
2. Booking Service validates ownership and status.
3. Booking Service updates appointment status to Cancelled.
4. Booking Service calls Availability Service to release the slot.
5. Booking Service publishes AppointmentCancelled event through outbox.
6. Notification and Reporting services process the event.

### 7.3 Customer Joins Walk-in Queue

1. Customer selects branch and service.
2. Queue Service checks active queue constraints.
3. Queue Service creates queue item and generates queue number per branch per day.
4. Queue Service saves QueueJoined event in outbox.
5. Notification Service sends simulated queue confirmation.
6. Reporting Service updates queue metrics.

### 7.4 Staff Calls Next Customer

1. Staff requests next queue item for assigned branch.
2. Queue Service selects earliest Waiting item.
3. Status changes to Called.
4. Queue Service publishes QueueCalled event.
5. Notification Service creates call notification.

## 8. Functional Requirements

### Authentication

| ID | Requirement | Description |
|---|---|---|
| FR-AUTH-001 | Register Customer | The system shall allow customers to register using full name, email, phone number, and password. |
| FR-AUTH-002 | Login | The system shall authenticate users and issue JWT access tokens. |
| FR-AUTH-003 | Role-Based Access | The system shall restrict APIs based on Customer, Staff, and Admin roles. |

### Availability Management

| ID | Requirement | Description |
|---|---|---|
| FR-AVL-001 | Manage Branches | Admin shall create and update branches. |
| FR-AVL-002 | Manage Service Types | Admin shall create and update services with duration and active status. |
| FR-AVL-003 | Generate Slots | Admin shall generate slots for branch, service, date range, duration, and capacity. |
| FR-AVL-004 | Search Available Slots | Customer shall search available slots by branch, service, and date. |
| FR-AVL-005 | Reserve/Release Slot | Internal APIs shall allow Booking Service to reserve and release slots. |

### Appointment Booking

| ID | Requirement | Description |
|---|---|---|
| FR-BKG-001 | Create Appointment | Customer shall book an available slot with Idempotency-Key. |
| FR-BKG-002 | Prevent Duplicate Booking | Repeated requests with the same idempotency key shall return the original result. |
| FR-BKG-003 | View Own Appointments | Customer shall view own appointment history. |
| FR-BKG-004 | Staff View Branch Appointments | Staff shall view appointments for assigned branch and date. |
| FR-BKG-005 | Cancel Appointment | Customer shall cancel own appointment before appointment start time. |
| FR-BKG-006 | Complete / No-Show | Staff shall mark appointments as Completed or NoShow after appointment start time. |

### Walk-in Queue

| ID | Requirement | Description |
|---|---|---|
| FR-QUE-001 | Join Queue | Customer shall join a branch and service queue with Idempotency-Key. |
| FR-QUE-002 | Generate Queue Number | System shall generate unique queue numbers per branch per day. |
| FR-QUE-003 | View Queue Status | Customer shall view queue number, status, customers ahead, and estimated waiting time. |
| FR-QUE-004 | Staff Queue Management | Staff shall view active queue, call next, mark served, cancel, or no-show. |

### Notifications and Reporting

| ID | Requirement | Description |
|---|---|---|
| FR-NOT-001 | Notification History | System shall store simulated email/SMS/in-app notification records. |
| FR-RPT-001 | Daily Appointment Summary | Admin shall view appointment counts by branch and service. |
| FR-RPT-002 | Queue Waiting Time Report | Admin shall view average waiting time and service popularity. |

## 9. Key Business Rules

| Area | Rules |
|---|---|
| Booking | Customer cannot book unavailable slots; cannot double-book same service/time; cancellation allowed only before start time; create request must be idempotent. |
| Availability | Slot cannot be reserved if capacity is zero; available capacity cannot be negative; inactive services cannot have new slots. |
| Queue | Customer can have only one active queue item per branch; queue number unique per branch/day; only Waiting items can be called. |
| Notification | Notifications are event-driven; duplicate event messages must not create duplicate notifications. |

## 10. Non-Functional Requirements

| Category | Requirement |
|---|---|
| Security | JWT authentication, role-based authorization, password hashing, input validation, no sensitive data in logs. |
| Reliability | Timeout, retry, circuit breaker for HTTP calls; outbox pattern; consumer idempotency/inbox table. |
| Observability | Serilog structured logs, correlation IDs, health endpoints, OpenTelemetry traces, Seq log viewer. |
| Performance | Pagination, indexes, Dapper read models, rate limiting, avoid chatty service calls. |
| Maintainability | Clean Architecture in Booking Service, DTOs, validation, consistent API response model, meaningful events. |

## 11. Architecture Patterns

### 11.1 Idempotency

Apply idempotency to `POST /appointments` and `POST /queue/join`. This prevents duplicate records when clients retry due to network timeouts.

```text
IdempotencyRecords
------------------
Id
IdempotencyKey
UserId
RequestHash
ResponseBody
StatusCode
CreatedAt
ExpiresAt
```

### 11.2 Outbox Pattern

Booking Service and Queue Service shall store domain changes and outbox messages in the same database transaction. A background worker publishes pending messages to RabbitMQ and marks them processed.

```text
OutboxMessages
---------------
Id
EventType
Payload
Status
CreatedAt
ProcessedAt
RetryCount
```

### 11.3 Inbox / Consumer Idempotency

Notification and Reporting services shall track processed MessageId values to avoid duplicate processing if RabbitMQ redelivers messages.

### 11.4 Resiliency

- Timeout for Booking -> Availability calls.
- Retry with exponential backoff for transient failures.
- Circuit breaker when Availability Service repeatedly fails.
- Graceful error response if slot reservation cannot be confirmed.

### 11.5 Rate Limiting

| Endpoint/Area | Suggested Limit |
|---|---|
| Login | 5 requests per minute per IP |
| Create appointment | 10 requests per minute per user |
| Join queue | 5 requests per minute per user |
| Search slots | 30 requests per minute per user |

## 12. Data Ownership and Databases

Use database-per-service logically. For local development, use one PostgreSQL container with multiple databases.

```text
auth_db
availability_db
booking_db
queue_db
notification_db
reporting_db
```

## 13. API Endpoint Draft

### Auth Service

```http
POST /api/auth/register
POST /api/auth/login
GET  /api/auth/me
```

### Availability Service

```http
POST /api/branches
GET  /api/branches

POST /api/services
GET  /api/services

POST /api/slots/generate
GET  /api/slots/available?branchId=&serviceId=&date=

POST /internal/slots/{slotId}/reserve
POST /internal/slots/{slotId}/release
```

### Booking Service

```http
POST /api/appointments
GET  /api/appointments/{id}
GET  /api/appointments/my
GET  /api/appointments/branch/{branchId}?date=
POST /api/appointments/{id}/cancel
POST /api/appointments/{id}/complete
POST /api/appointments/{id}/no-show
```

### Queue Service

```http
POST /api/queue/join
GET  /api/queue/my-active
GET  /api/queue/{id}
GET  /api/queue/branch/{branchId}

POST /api/queue/branch/{branchId}/call-next
POST /api/queue/{id}/served
POST /api/queue/{id}/cancel
POST /api/queue/{id}/no-show
```

### Notification Service

```http
GET /api/notifications/my
GET /api/notifications/{id}
```

### Reporting Service

```http
GET /api/reports/appointments/daily?branchId=&date=
GET /api/reports/appointments/status-summary?branchId=&from=&to=
GET /api/reports/queue/average-waiting-time?branchId=&from=&to=
GET /api/reports/services/popular?from=&to=
```

## 14. Domain Events

- `AppointmentBooked`
- `AppointmentCancelled`
- `AppointmentCompleted`
- `AppointmentNoShow`
- `QueueJoined`
- `QueueCalled`
- `QueueServed`
- `QueueCancelled`

Example event envelope:

```json
{
  "messageId": "b91e7d5a-6c7d-4c82-93ff-98d34fa8f001",
  "eventType": "AppointmentBooked",
  "occurredAt": "2026-07-05T10:15:00Z",
  "data": {
    "appointmentId": "9f61c5e8-2bc1-4f74-bcc2-927fcf851a11",
    "customerId": "7d5d4e8c-02ef-47ce-b23a-42fd5f3ed901",
    "branchId": "PG",
    "serviceId": "VEHICLE_INSPECTION",
    "startTime": "2026-07-05T10:00:00Z",
    "endTime": "2026-07-05T10:30:00Z"
  }
}
```

## 15. Technology Stack

| Area | Technology |
|---|---|
| Runtime | .NET 8 or .NET 9 |
| API | ASP.NET Core Web API |
| Gateway | YARP Reverse Proxy |
| Database | PostgreSQL |
| Data Access | EF Core for transactional writes, Dapper for reports |
| Messaging | RabbitMQ |
| Cache / Rate Limiting | Redis |
| Logging | Serilog + Seq |
| Tracing | OpenTelemetry, optional Jaeger or Aspire Dashboard |
| Resiliency | Polly |
| Validation | FluentValidation |
| Testing | xUnit + Testcontainers |
| Local Orchestration | Docker Compose, optional .NET Aspire AppHost |

## 16. MVP Scope and Build Plan

### Must Have

- Auth Service with JWT
- Availability Service with branches, services, slots
- Booking Service with appointment booking/cancellation
- Queue Service with join queue/call next
- Notification Service consuming events
- RabbitMQ integration
- PostgreSQL databases
- Docker Compose
- Serilog + Seq
- Correlation ID
- Basic health checks
- Idempotency for booking and queue join
- Outbox pattern in Booking Service

### Should Have

- Reporting Service using Dapper
- OpenTelemetry traces
- Polly retry/circuit breaker
- Redis rate limiting
- Consumer idempotency/inbox table
- Integration tests with Testcontainers

### 3-Week Plan

| Week | Goal | Key Deliverables |
|---|---|---|
| Week 1 | Foundation and core booking | Solution structure, Docker Compose, Gateway, Auth, Availability, Booking, JWT, slot search, appointment booking. |
| Week 2 | Messaging, queue, reliability | RabbitMQ, Outbox, Notification Service, Queue Service, idempotency, Polly policies. |
| Week 3 | Observability, reporting, polish | Reporting Service, Dapper reports, Serilog/Seq, correlation ID, health checks, tracing, integration tests, README. |

## 17. Suggested Solution Structure

```text
SmartAppointments.sln

src/
  ApiGateway/SmartAppointments.Gateway
  Services/Auth/Auth.Api
  Services/Availability/Availability.Api
  Services/Booking/Booking.Api
  Services/Queue/Queue.Api
  Services/Notification/Notification.Api
  Services/Reporting/Reporting.Api
  BuildingBlocks/SmartAppointments.BuildingBlocks

tests/
  Booking.Tests
  Queue.Tests
  Availability.Tests
  Integration.Tests
```

## 18. Interview Talking Point

After completing the project, it can be described as follows:

> I built a Smart Appointment and Queue Management System using .NET microservices. The system supports appointment booking and walk-in queue workflows. I used synchronous HTTP communication between Booking and Availability services because booking requires immediate slot confirmation. I used RabbitMQ for asynchronous communication to Notification and Reporting services because those operations do not need to block the booking request. I implemented idempotency keys, the Outbox pattern, consumer idempotency, Polly resiliency policies, JWT role-based auth, structured logging, correlation IDs, health checks, OpenTelemetry-ready tracing, EF Core for writes, and Dapper for reporting queries.
