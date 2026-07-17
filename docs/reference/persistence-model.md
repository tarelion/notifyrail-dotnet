# Persistence Model Reference

## Purpose

This page defines the PostgreSQL schema currently mapped by EF Core for
API Clients, API Keys, messages, deliveries, delivery attempts, and OTP
challenges. Migrations remain
the executable schema history; this page is the canonical human- and
agent-readable contract.

## Scope

- DbContext: `src/NotifyRail.Api/Infrastructure/Persistence/NotifyRailDbContext.cs`
- Entity mappings: `ApiClientConfiguration`, `ApiKeyConfiguration`,
  `MessageConfiguration`, `DeliveryConfiguration`,
  `DeliveryAttemptConfiguration`, and `OtpChallengeConfiguration`
- Migrations: `src/NotifyRail.Api/Infrastructure/Persistence/Migrations`

## Relationships

- One API Client has zero or more API Keys and Messages.
- One message has one delivery per normalized recipient.
- Each delivery references one message through `deliveries.message_id`.
- Each delivery attempt references one delivery through
  `delivery_attempts.delivery_id`.
- Each OTP challenge references one Message through
  `otp_challenges.message_id` and inherits that Message's API Client ownership.
- All foreign keys use `NO ACTION` deletion behavior; deleting a parent does
  not cascade to its children.

## `api_clients`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key; defaults to `gen_random_uuid()`. |
| `name` | `text` | yes | Display name; must not be blank after trimming. |
| `is_enabled` | `boolean` | yes | Whether credentials may authenticate; defaults to `true`. |
| `created_at` | `timestamp with time zone` | yes | Creation instant. |
| `updated_at` | `timestamp with time zone` | yes | Latest state-change instant. |
| `disabled_at` | `timestamp with time zone` | no | Required exactly when `is_enabled` is `false`. |

The fixed API Client ID `00000000-0000-0000-0000-000000000001` identifies
the legacy owner used while data-plane endpoints are migrated. The ownership
migration creates it and does not create an API Key for it.

## `api_keys`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key; defaults to `gen_random_uuid()`. |
| `api_client_id` | `uuid` | yes | References `api_clients(id)`. |
| `lookup_id` | `text` | yes | Unique, non-secret identifier embedded in the credential. |
| `verification_hash` | `bytea` | yes | 32-byte SHA-256 verification value; never plaintext. |
| `display_prefix` | `text` | yes | Non-secret prefix safe for operator display. |
| `created_at` | `timestamp with time zone` | yes | Credential creation instant. |
| `last_used_at` | `timestamp with time zone` | no | Most recent successful authenticated use. Failed authentication does not update it. |
| `expires_at` | `timestamp with time zone` | no | Optional expiry instant. |
| `revoked_at` | `timestamp with time zone` | no | Irreversible first revocation instant; repeated revocation preserves it. |

Full API Keys have the recognizable `nrk_<lookup_id>_<secret>` form. Only the
creation response exposes the full value; no plaintext column exists.
Expired and revoked keys cannot authenticate. Multiple other active keys may
remain usable for the same enabled API Client.

Indexes and uniqueness:

- Primary key on `id`.
- Unique constraint `api_keys_lookup_id_key` on `lookup_id`.
- Index on `api_client_id` for credential lookup by owner.

## `messages`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key; defaults to `gen_random_uuid()`. |
| `api_client_id` | `uuid` | yes | References the owning `api_clients(id)`. |
| `type` | `text` | yes | One of `otp`, `transactional`, or `campaign`. |
| `channel` | `text` | yes | Must be `sms`. |
| `sender_title` | `text` | yes | Must not be blank after trimming. |
| `body` | `text` | yes | Must not be blank after trimming. |
| `idempotency_key` | `text` | yes | Must not be blank after trimming; unique within one API Client. |
| `report_label` | `text` | no | Client-provided reporting label. |
| `encoding` | `text` | no | When present, one of `latin`, `turkish`, or `unicode`. |
| `scheduled_at` | `timestamp with time zone` | no | UTC-normalized earliest instant at which queued deliveries may be claimed. |
| `created_at` | `timestamp with time zone` | yes | Defaults to `now()`. |
| `updated_at` | `timestamp with time zone` | yes | Defaults to `now()`. |

Indexes and uniqueness:

- Primary key on `id`.
- Unique constraint `messages_api_client_id_idempotency_key_key` on
  `(api_client_id, idempotency_key)`.

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
| `expires_at` | `timestamp with time zone` | no | Delivery expires when this time is reached. Generic message intake leaves it null; OTP send sets it to the challenge expiry. |
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

## `otp_challenges`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key and public `otp_id`. |
| `message_id` | `uuid` | yes | Unique reference to the challenge's `otp` Message. |
| `recipient` | `text` | yes | Non-blank normalized recipient. |
| `code_hash` | `bytea` | yes | Exactly 32 bytes; plaintext OTP Codes are not persisted. |
| `expires_at` | `timestamp with time zone` | yes | Must be later than `created_at`. |
| `verified_at` | `timestamp with time zone` | no | First successful verification instant. |
| `failed_attempt_count` | `integer` | yes | Between zero and `max_attempts`; defaults to zero. |
| `max_attempts` | `integer` | yes | Positive verification-attempt limit captured at creation. |
| `created_at` | `timestamp with time zone` | yes | Challenge creation instant. |
| `updated_at` | `timestamp with time zone` | yes | Latest verification-state change. |

Indexes and uniqueness:

- Primary key on `id`.
- Unique index `otp_challenges_message_id_key` on `message_id`.
- Index `otp_challenges_recipient_expiry_idx` on `(recipient, expires_at)`.

Cross-table OTP invariants:

- OTP send inserts the Message, Delivery, and OTP Challenge in one transaction.
- The Delivery and OTP Challenge use the same recipient and expiry instant.
- Verification changes only the OTP Challenge; it does not create a Delivery
  Attempt or change Delivery status.

## Cross-Table Invariants

- Generic Message creation inserts one Message and all of its Deliveries in one
  transaction and assigns the authenticated API Client as owner.
- OTP send assigns the authenticated API Client to its Message; OTP verification
  scopes challenge lookup through that Message owner.
- A Message cannot contain duplicate normalized recipients because
  `(message_id, recipient)` is unique.
- Provider-result recording inserts an Attempt and updates its Delivery in one
  transaction.
- `deliveries.attempt_count` equals the number of recorded Attempts for that
  Delivery.
- Schema changes require an EF Core migration; entity mappings and this
  reference must be updated with the migration.
