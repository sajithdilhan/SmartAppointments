# Smart Appointment & Queue Management System

A portfolio-ready .NET microservices practice project for appointment booking and walk-in queue management.

This project is designed to practise real-world backend architecture concepts such as synchronous and asynchronous service communication, idempotency, rate limiting, resiliency, observability, authentication, authorization, background processing, and database-per-service ownership.

## Business Scenario

Service-based organizations such as clinics, salons, government counters, vehicle service centers, and consultation centers often support both scheduled appointments and walk-in queues.

This system allows customers to book appointments or join a walk-in queue, while staff manage daily appointments and queue operations. Admin users configure branches, services, slots, and view reports.

## Main Features

### Customer

- Register and login
- View available appointment slots
- Book an appointment
- Cancel own appointment
- View own appointment history
- Join a walk-in queue
- View own queue status
- View notification history

### Staff

- View daily appointments for assigned branch
- Mark appointment as completed or no-show
- View active branch queue
- Call next queue number
- Mark queue item as served, cancelled, or no-show

### Admin

- Manage branches
- Manage service types
- Generate appointment slots
- Manage staff users
- View operational reports

## Architecture Overview

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

## Services

| Service | Responsibility |
|---|---|
| API Gateway | Single entry point, routing, JWT validation, rate limiting, correlation ID forwarding |
| Auth Service | User registration, login, password hashing, JWT generation, roles |
| Availability Service | Branches, service types, working hours, slot generation, slot reservation/release |
| Booking Service | Appointment lifecycle, idempotency, outbox, sync call to Availability Service |
| Queue Service | Walk-in queue lifecycle, queue number generation, idempotency, outbox |
| Notification Service | Consumes events and creates simulated email/SMS/in-app notifications |
| Reporting Service | Consumes events and maintains reporting read models using Dapper |

## Technology Stack

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

## Core Workflow: Appointment Booking

```text
Customer logs in
    ↓
Searches available slots
    ↓
Submits appointment booking request with Idempotency-Key
    ↓
Booking Service validates request
    ↓
Booking Service calls Availability Service to reserve slot
    ↓
Availability Service reserves slot
    ↓
Booking Service creates appointment and outbox message in same transaction
    ↓
Outbox worker publishes AppointmentBooked event to RabbitMQ
    ↓
Notification Service creates confirmation notification
    ↓
Reporting Service updates appointment statistics
```

## Core Workflow: Walk-in Queue

```text
Customer logs in
    ↓
Selects branch and service
    ↓
Submits join queue request with Idempotency-Key
    ↓
Queue Service validates active queue constraints
    ↓
Queue Service creates queue item and queue number
    ↓
Queue Service publishes QueueJoined event through outbox
    ↓
Notification Service creates queue confirmation
    ↓
Reporting Service updates queue metrics
```

## Important Architecture Concepts

### Idempotency

Apply idempotency to create operations that may be retried by clients:

- `POST /api/appointments`
- `POST /api/queue/join`

Suggested request header:

```http
Idempotency-Key: 8d9f9be5-1b0e-4cbb-812f-3f89c62e1d45
```

Suggested table:

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

### Outbox Pattern

Use the Outbox pattern in Booking Service and Queue Service.

```text
1. Save business data and outbox message in the same DB transaction.
2. Background worker reads pending outbox messages.
3. Worker publishes event to RabbitMQ.
4. Worker marks message as processed.
```

Suggested table:

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

### Consumer Idempotency / Inbox

Notification and Reporting services should avoid processing the same event twice.

```text
InboxMessages
--------------
MessageId
EventType
ProcessedAt
```

### Resiliency

Use Polly for service-to-service calls such as:

```text
Booking Service -> Availability Service
```

Recommended policies:

- Timeout
- Retry with exponential backoff
- Circuit breaker

### Rate Limiting

Apply rate limiting at the API Gateway.

| Area | Suggested Limit |
|---|---|
| Login | 5 requests per minute per IP |
| Create appointment | 10 requests per minute per user |
| Join queue | 5 requests per minute per user |
| Search available slots | 30 requests per minute per user |

### Observability

Each service should include:

- Structured logging with Serilog
- Correlation ID propagation
- Health check endpoint
- Request/response timing
- OpenTelemetry tracing
- Centralized log viewing using Seq

## Databases

Use database-per-service logically. For local development, use one PostgreSQL container with multiple databases.

```text
auth_db
availability_db
booking_db
queue_db
notification_db
reporting_db
```

## API Endpoint Draft

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

## Domain Events

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

## Key Business Rules

### Booking

- Customer cannot book a slot that is not available.
- Customer cannot book two appointments for the same service at the same time.
- Appointment can be cancelled only before appointment start time.
- Staff can mark appointment as completed only after appointment start time.
- Staff can mark appointment as no-show only after appointment start time.
- Booking request must be idempotent.

### Availability

- Slot cannot be reserved if available capacity is zero.
- Slot capacity cannot be negative.
- Slot belongs to one branch and one service.
- Slot duration should match the service duration.
- Inactive service cannot have new slots.

### Queue

- Customer can have only one active queue item per branch.
- Queue number should be unique per branch per day.
- Staff can only call next item from Waiting status.
- Called item can become Serving, Served, NoShow, or Cancelled.
- Queue join request must be idempotent.

## Suggested Solution Structure

```text
SmartAppointments.sln

src/
  ApiGateway/
    SmartAppointments.Gateway

  Services/
    Auth/
      Auth.Api
      Auth.Application
      Auth.Domain
      Auth.Infrastructure

    Availability/
      Availability.Api
      Availability.Application
      Availability.Domain
      Availability.Infrastructure

    Booking/
      Booking.Api
      Booking.Application
      Booking.Domain
      Booking.Infrastructure

    Queue/
      Queue.Api
      Queue.Application
      Queue.Domain
      Queue.Infrastructure

    Notification/
      Notification.Api
      Notification.Application
      Notification.Domain
      Notification.Infrastructure

    Reporting/
      Reporting.Api
      Reporting.Application
      Reporting.Infrastructure

  BuildingBlocks/
    SmartAppointments.BuildingBlocks
      Messaging
      Observability
      Security
      Resilience
      Web

tests/
  Booking.Tests
  Queue.Tests
  Availability.Tests
  Integration.Tests
```

## MVP Scope

### Must Have

- Auth Service with JWT
- Availability Service with branches, services, slots
- Booking Service with appointment booking and cancellation
- Queue Service with join queue and call next
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
- Polly retry and circuit breaker
- Redis rate limiting
- Consumer idempotency/inbox table
- Integration tests with Testcontainers

### Could Have

- Appointment reminder background job
- Refresh tokens
- Branch-based staff authorization
- gRPC between services
- .NET Aspire AppHost

## 3-Week Build Plan

### Week 1: Foundation and Core Booking

- Create solution structure
- Add Docker Compose: PostgreSQL, RabbitMQ, Redis, Seq
- Create API Gateway
- Create Auth Service
- Create Availability Service
- Create Booking Service
- Implement JWT auth
- Implement branches, services, slots
- Implement appointment booking
- Add synchronous call: Booking -> Availability

Goal: Customer can login, view slots, and book appointment.

### Week 2: Messaging, Queue, Idempotency, Reliability

- Add RabbitMQ messaging
- Add Outbox pattern in Booking Service
- Add Notification Service
- Publish `AppointmentBooked` and `AppointmentCancelled` events
- Add Queue Service
- Implement join queue and call next
- Add idempotency for appointment booking
- Add idempotency for queue join
- Add Polly retry/circuit breaker

Goal: Booking and queue workflows work with async notifications.

### Week 3: Observability, Reporting, Polish

- Add Reporting Service
- Consume appointment and queue events
- Add Dapper reports
- Add Serilog structured logging
- Add Seq dashboard
- Add correlation ID
- Add health checks
- Add OpenTelemetry tracing
- Add integration tests for key flows
- Improve README and architecture diagram

Goal: Project is portfolio-ready and interview-ready.
