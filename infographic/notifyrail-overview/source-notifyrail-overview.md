# NotifyRail Overview Source

## Repository Summary

NotifyRail is a learning-focused C#/.NET backend that simulates reliable
notification delivery. It focuses on reliable delivery, retries, delivery
reports, OTP verification, provider abstraction, scheduled sending, and
callback handling.

## Current Implementation

- process liveness and PostgreSQL readiness endpoints
- atomic message creation with one delivery per recipient
- globally unique idempotency keys with replay and conflict handling
- PostgreSQL delivery claiming with scheduling, expiry, priority ordering, and
  `FOR UPDATE SKIP LOCKED`
- five-minute lease recovery for deliveries abandoned in `processing`
- a hosted background worker that sends claimed deliveries through an
  in-process, recipient-configurable mock provider
- atomic provider-result recording for `accepted`, `retryable_failure`, and
  `permanent_failure` outcomes, including `sent`, `retry_scheduled`, and
  `failed` transitions
- recipient-level delivery reads with ordered provider attempt history
- message summary reads with delivery status counts
- aggregate message reports with counts for every delivery status
- idempotent mock-provider callbacks that finalize sent deliveries as delivered
  or failed without regressing terminal states
- idempotent OTP send with hashed code persistence, delivery expiry, and a mock
  `debug_code`
- concurrency-safe one-time OTP verification with TTL and an attempt limit

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

## Implemented Endpoints

- `GET /healthz`
- `GET /readyz`
- `POST /messages`
- `GET /messages/{message_id}`
- `GET /messages/{message_id}/deliveries`
- `GET /messages/{message_id}/report`
- `POST /provider-callbacks/mock`
- `POST /otp/send`
- `POST /otp/verify`

## Delivery Statuses

- `queued`
- `processing`
- `sent`
- `delivered`
- `retry_scheduled`
- `failed`
- `expired`

## Validation

`dotnet test NotifyRail.slnx`

Result observed in the current workspace:

- Failed: 0
- Passed: 70
- Skipped: 0
- Total: 70
