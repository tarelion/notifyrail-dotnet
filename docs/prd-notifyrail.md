# NotifyRail PRD

> Status: Target MVP product requirements. For currently implemented behavior,
> see the root [README](../README.md) and the
> [HTTP API reference](reference/http-api.md).

## Purpose

NotifyRail simulates the core delivery logic of a high-volume notification
platform without connecting to a real SMS or email provider.

The project focuses on domain-relevant backend concepts: reliable delivery,
retries, delivery reports, OTP verification, provider abstraction, scheduled
sending, and callback handling.

## Problem

Notification platforms must accept message requests, process them
asynchronously, handle provider failures, avoid duplicate sends, track
per-recipient delivery state, and expose delivery reports.

The goal is not to build a clone of any existing product. The goal is to build a
small, complete service that represents common backend problems in the
messaging domain.

## Goals

- Accept transactional, campaign, and OTP message requests through HTTP
  endpoints.
- Create one delivery record per recipient.
- Process pending deliveries with a background worker.
- Simulate provider sends through a mock provider adapter.
- Retry temporary failures with backoff.
- Track delivery status and attempt history.
- Support scheduled sending.
- Support OTP send and one-time verification with TTL.
- Expose message-level and recipient-level delivery reports.
- Keep the project runnable with Docker Compose.
- Include tests for the core delivery lifecycle.

## Non-Goals

- Do not send real SMS, email, or OTP messages.
- Do not integrate with external SMS provider APIs.
- Do not copy product names, branding, UI, or private behavior.
- Do not build a frontend for the MVP.
- Do not add AI to the core delivery flow.
- Do not start with Redis, RabbitMQ, NATS, or Kafka.
- Do not implement full campaign segmentation or marketing automation.
- Do not implement billing.

## Primary User Stories

- As an API client, I can create a message for one or more recipients.
- As an API client, I can schedule a message for future delivery.
- As an API client, I can query the message and see overall status.
- As an API client, I can query each recipient delivery and its attempts.
- As a system worker, I can claim pending deliveries without double-processing
  them.
- As a system worker, I can retry temporary failures and stop after max
  attempts.
- As an API client, I can send an OTP to a phone number.
- As an API client, I can verify an OTP once before it expires.
- As a provider simulator, I can send duplicate or late callbacks without
  corrupting state.

## MVP Scope

### Message API

- `POST /messages`
- `GET /messages/{message_id}`
- `GET /messages/{message_id}/deliveries`
- `GET /messages/{message_id}/report`

### OTP API

- `POST /otp/send`
- `POST /otp/verify`

### Provider Callback API

- `POST /provider-callbacks/mock`

### Worker

- Claims due deliveries from PostgreSQL.
- Uses priority ordering: OTP, transactional, campaign.
- Calls a mock provider.
- Records every attempt.
- Updates delivery state according to the delivery lifecycle.
- Schedules retries for retryable failures.

## Key Domain Concepts

| Concept | Meaning |
| --- | --- |
| Message | The top-level notification request from a client. |
| Recipient | A target phone number or email address. |
| Delivery | The per-recipient delivery job created from a message. |
| Delivery Attempt | One provider send attempt for one delivery. |
| Provider | An adapter that simulates sending messages. |
| Provider Callback | A provider-originated status update. |
| OTP Code | A short-lived, one-time verification code. |

## Message Types

| Type | Priority | Notes |
| --- | --- | --- |
| `otp` | 100 | Highest priority; short TTL; one-time verification. |
| `transactional` | 50 | Important operational message. |
| `campaign` | 10 | Bulk or marketing-style message. |

## Delivery Statuses

The canonical delivery lifecycle is documented in
`docs/reference/delivery-lifecycle.md`.

MVP statuses:

- `queued`
- `processing`
- `sent`
- `delivered`
- `retry_scheduled`
- `failed`
- `expired`

## Required Fields

### Create Message Request

| Field | Required | Notes |
| --- | --- | --- |
| `type` | yes | `otp`, `transactional`, or `campaign`. |
| `channel` | yes | MVP starts with `sms`. |
| `sender_title` | yes | Originator/sender label. |
| `body` | yes | Message body. |
| `recipients` | yes | One or more recipients. |
| `scheduled_at` | no | If omitted, send as soon as possible. |
| `idempotency_key` | yes | Globally prevents duplicate message creation in the single-client MVP. |
| `report_label` | no | Client-provided reporting label. |
| `encoding` | no | `latin`, `turkish`, or `unicode`. |

## Functional Requirements

- Message creation must be idempotent by globally unique idempotency key in the
  single-client MVP. Introducing client identity requires changing this to a
  client-scoped key.
- A message with N recipients must create N delivery records.
- Worker claims must avoid duplicate processing when multiple workers run.
- Every provider send attempt must be recorded.
- Retryable failures must schedule `next_attempt_at`.
- Permanent failures must not be retried.
- OTP codes must be stored hashed, not plaintext.
- OTP codes must expire after a configured TTL.
- OTP verification must succeed only once.
- Provider callbacks must be idempotent.
- Late or duplicate callbacks must not regress a terminal delivery state.

## Technical Requirements

- Language/runtime: C# on .NET.
- Web framework: ASP.NET Core Minimal APIs.
- Database: PostgreSQL.
- Persistence: EF Core with Npgsql.
- Queue: PostgreSQL-backed queue for MVP.
- Migrations: EF Core migrations.
- Runtime: Docker Compose for PostgreSQL and `dotnet run` for local app
  development.
- Tests: xUnit unit and integration tests for the core delivery flow.

## MVP Success Criteria

- The service runs locally with documented commands.
- A message can be created and processed by the worker.
- Delivery attempts are visible through the API.
- Retry behavior is demonstrable with a configurable or flaky mock provider.
- OTP send and verify work with TTL and one-time use.
- Provider callback simulation updates delivery status safely.
- README explains the project, architecture, and demo flow.
- Tests cover delivery lifecycle, idempotency, OTP verification, and
  retry/backoff.

## Stretch Goals

- CSV recipient import.
- IP allowlist for API clients.
- Rate limit or quota per client.
- Basic segment count calculation for SMS encoding.
- Webhook delivery to client callback URLs.
- Rule-based message draft review endpoint.
- Redis or NATS queue adapter after the PostgreSQL-backed queue works.

## Demo Flow

1. Create a campaign message with three recipients.
2. Start the worker.
3. Mock provider delivers one recipient, temporarily fails one, and permanently
   fails one.
4. Query delivery report.
5. Wait for retry backoff.
6. Query attempt history.
7. Send an OTP.
8. Verify the OTP once.
9. Attempt to verify the same OTP again and receive a failure.

## Risks

- Scope can grow into a full messaging platform. Keep MVP focused on delivery
  reliability.
- Provider abstraction can become over-designed. Start with one mock provider
  and one interface.
- PostgreSQL-backed queue can be subtle. Keep worker claim size small and test
  concurrency behavior.
- OTP security can expand quickly. MVP covers TTL, hashing, one-time use, and
  attempt limit only.
