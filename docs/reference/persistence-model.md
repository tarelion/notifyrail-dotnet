# Persistence Model Reference

## Purpose

This page defines the PostgreSQL schema currently mapped by EF Core for
messages, deliveries, and delivery attempts. Migrations remain the executable
schema history; this page is the canonical human- and agent-readable contract.

## Scope

- DbContext: `src/NotifyRail.Api/Infrastructure/Persistence/NotifyRailDbContext.cs`
- Entity mappings: `MessageConfiguration`, `DeliveryConfiguration`, and
  `DeliveryAttemptConfiguration`
- Migrations: `src/NotifyRail.Api/Infrastructure/Persistence/Migrations`

## Relationships

- One message has one delivery per normalized recipient.
- Each delivery references one message through `deliveries.message_id`.
- Each delivery attempt references one delivery through
  `delivery_attempts.delivery_id`.
- Both foreign keys use `NO ACTION` deletion behavior; deleting a parent does
  not cascade to its children.

## `messages`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key; defaults to `gen_random_uuid()`. |
| `type` | `text` | yes | One of `otp`, `transactional`, or `campaign`. |
| `channel` | `text` | yes | Must be `sms`. |
| `sender_title` | `text` | yes | Must not be blank after trimming. |
| `body` | `text` | yes | Must not be blank after trimming. |
| `idempotency_key` | `text` | yes | Must not be blank after trimming; globally unique for the MVP. |
| `report_label` | `text` | no | Client-provided reporting label. |
| `encoding` | `text` | no | When present, one of `latin`, `turkish`, or `unicode`. |
| `scheduled_at` | `timestamp with time zone` | no | Earliest time at which queued deliveries may be claimed. |
| `created_at` | `timestamp with time zone` | yes | Defaults to `now()`. |
| `updated_at` | `timestamp with time zone` | yes | Defaults to `now()`. |

Indexes and uniqueness:

- Primary key on `id`.
- Unique constraint `messages_idempotency_key_key` on `idempotency_key`.

## `deliveries`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key; defaults to `gen_random_uuid()`. |
| `message_id` | `uuid` | yes | References `messages(id)`. |
| `recipient` | `text` | yes | Must not be blank after trimming. |
| `status` | `text` | yes | One of the states in the [delivery lifecycle](delivery-lifecycle.md); defaults to `queued`. |
| `attempt_count` | `integer` | yes | Must be non-negative; defaults to `0`. |
| `next_attempt_at` | `timestamp with time zone` | no | Required only while status is `retry_scheduled`. |
| `claimed_at` | `timestamp with time zone` | no | Required only while status is `processing`. |
| `claimed_by` | `text` | no | Non-blank worker identity required only while status is `processing`. |
| `provider_message_id` | `text` | no | Must not be blank when present; unique across non-null values. |
| `expires_at` | `timestamp with time zone` | no | Delivery expires when this time is reached. Current message intake leaves it null. |
| `created_at` | `timestamp with time zone` | yes | Defaults to `now()`. |
| `updated_at` | `timestamp with time zone` | yes | Defaults to `now()`. |

Indexes and uniqueness:

- Primary key on `id`.
- Unique constraint `deliveries_message_id_recipient_key` on
  `(message_id, recipient)`.
- Partial unique index `deliveries_provider_message_id_idx` on
  `provider_message_id` where the value is not null.
- Partial due-work index `deliveries_due_idx` on
  `(status, next_attempt_at, created_at)` for `queued` and `retry_scheduled`
  rows.

State-dependent constraints:

- `retry_scheduled` requires a non-null `next_attempt_at`; every other status
  requires it to be null.
- `processing` requires non-null `claimed_at` and non-blank `claimed_by`; every
  other status requires both fields to be null.

## `delivery_attempts`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key; defaults to `gen_random_uuid()`. |
| `delivery_id` | `uuid` | yes | References `deliveries(id)`. |
| `attempt_number` | `integer` | yes | Must be greater than zero. |
| `provider` | `text` | yes | Must not be blank after trimming. |
| `outcome` | `text` | yes | One of `accepted`, `retryable_failure`, or `permanent_failure`. |
| `provider_message_id` | `text` | no | Must not be blank when present. |
| `error_code` | `text` | no | Must not be blank when present. |
| `error_message` | `text` | no | Must not be blank when present. |
| `attempted_at` | `timestamp with time zone` | yes | Defaults to `now()`; the worker supplies the provider-return time. |

Indexes and uniqueness:

- Primary key on `id`.
- Unique constraint `delivery_attempts_delivery_id_attempt_number_key` on
  `(delivery_id, attempt_number)`.

## Cross-Table Invariants

- Message creation inserts one message and all of its deliveries in one
  transaction.
- A message cannot contain duplicate normalized recipients because
  `(message_id, recipient)` is unique.
- Provider-result recording inserts an attempt and updates its delivery in one
  transaction.
- `deliveries.attempt_count` equals the number of recorded attempts for that
  delivery.
- Schema changes require an EF Core migration; entity mappings and this
  reference must be updated with the migration.
