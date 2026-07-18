# Delivery Lifecycle Reference

## Purpose

This page defines the canonical delivery states and transitions for NotifyRail.
Code, tests, and API responses should use this lifecycle as the source of truth.

This is the target lifecycle contract, not an implementation-status page. A
state or transition listed here may be planned but not yet wired into the
running application. See the root [README](../../README.md) for current
capabilities.

## States

| State | Meaning | Terminal |
| --- | --- | --- |
| `queued` | Delivery is ready or scheduled for future processing. | no |
| `processing` | A worker has claimed the delivery. | no |
| `sent` | Provider accepted the send request, but final delivery is not confirmed. | no |
| `delivered` | Provider reported successful delivery. | yes |
| `retry_scheduled` | A retryable failure happened and a future attempt is scheduled. | no |
| `failed` | Delivery cannot continue or max attempts were reached. | yes |
| `expired` | Delivery was not completed before its TTL or expiry window. | yes |

## Allowed Transitions

| From | Event | To | Notes |
| --- | --- | --- | --- |
| `queued` | worker claims due delivery | `processing` | Claim must be transactional. |
| `retry_scheduled` | retry becomes due | `processing` | Worker may claim when `next_attempt_at <= now()`. |
| `processing` | provider accepts send | `sent` | Store provider message ID when available. |
| `processing` | retryable provider failure | `retry_scheduled` | Increment attempt count and set `next_attempt_at`. |
| `processing` | permanent provider failure | `failed` | Do not retry. |
| `processing` | max attempts reached | `failed` | Do not schedule another retry. |
| `processing` | stale claim recovered before first attempt is recorded | `queued` | Clear claim fields; delivery may be claimed again. |
| `processing` | stale claim recovered after at least one attempt is recorded | `retry_scheduled` | Clear claim fields and set `next_attempt_at` to the recovery time. |
| `sent` | delivered callback | `delivered` | Callback must be idempotent. |
| `sent` | failed callback | `failed` | Only if callback represents final failure. |
| `queued` | expiry reached | `expired` | Applies to scheduled or TTL-bound deliveries. |
| `retry_scheduled` | expiry reached | `expired` | Applies before another attempt is claimed. |
| `sent` | expiry reached | `expired` | Applies when final provider status never arrives. |

## Forbidden Transitions

- `delivered` must not transition to any other state.
- `failed` must not transition to any other state.
- `expired` must not transition to any other state.
- `sent` must not transition back to `queued`.
- `processing` must not be visible indefinitely after worker failure; recovery
  logic must return stale claims to `queued` or `retry_scheduled`.

## Worker Claim Invariants

- A worker must claim deliveries inside a transaction.
- A claimed delivery must move to `processing` before provider send is
  attempted.
- Multiple workers must not process the same delivery at the same time.
- MVP implementation should use PostgreSQL row locking such as
  `FOR UPDATE SKIP LOCKED`.
- Stale `processing` claims must be recovered before due work is claimed.
- A recovered first attempt returns to `queued`; a recovered later attempt
  returns to `retry_scheduled`.

## Attempt Invariants

- Accepted provider sends must record a delivery attempt.
- Retryable failures must record a delivery attempt.
- Permanent failures must record a delivery attempt.
- `attempt_count` must equal the number of recorded attempts for the delivery.

## Retry Invariants

- Retryable failures must schedule `next_attempt_at`.
- `next_attempt_at` is required when state is `retry_scheduled`.
- `next_attempt_at` must be null for terminal states.
- Max attempts must be enforced before scheduling another retry.

## Callback Invariants

- Callback processing must be idempotent.
- Duplicate callbacks for the same provider message ID must not create duplicate
  final effects.
- Unknown provider message IDs must be rejected or recorded separately for
  investigation.
- Callbacks must not move terminal states backward.
- The implemented mock callback uses first-terminal-result-wins semantics:
  once a delivery is `delivered`, `failed`, or `expired`, later callbacks are
  no-ops and return the persisted terminal state.

## Client Webhook Invariants

- Client-visible `sent`, `delivered`, `failed`, and `expired` transitions create
  `delivery.sent`, `delivery.delivered`, `delivery.failed`, and
  `delivery.expired` Webhook Events when the owning API Client has an active
  Webhook Endpoint.
- Internal `queued`, `processing`, and `retry_scheduled` transitions do not
  create Webhook Events.
- Webhook Event sequence values are positive and monotonically increasing
  within each Delivery.
- A later event for one Delivery is not dispatched while an earlier event is
  pending or processing. Events for different Deliveries remain independently
  claimable.
- Delivery truth and Webhook Event dispatch state are independent after the
  transition transaction commits.
- Duplicate or conflicting Provider Callbacks preserve the first terminal
  Delivery state and do not create another logical terminal Webhook Event.

## OTP-Specific Rules

- OTP deliveries use the same delivery lifecycle.
- OTP Challenge and Delivery expiry use the same configured TTL.
- OTP verification is separate from delivery success.
- A delivered OTP can still fail verification if expired, already used, or
  incorrect.
- OTP verification succeeds once; concurrent correct requests are serialized by
  a database row lock.
- Incorrect codes consume the configured attempt limit. Expired, verified, and
  locked challenges do not consume more attempts.
