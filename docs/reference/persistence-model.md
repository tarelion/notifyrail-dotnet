# Persistence Model Reference

## Purpose

This page defines the PostgreSQL schema currently mapped by EF Core for
API Clients, API Keys, Webhook Endpoints, Webhook Secrets, Webhook Events,
messages, deliveries, delivery attempts, and OTP challenges. Migrations remain
the executable schema history; this page is the canonical human- and
agent-readable contract.

## Scope

- DbContext: `src/NotifyRail.Api/Infrastructure/Persistence/NotifyRailDbContext.cs`
- Entity mappings: `ApiClientConfiguration`, `ApiKeyConfiguration`,
  `WebhookEndpointConfiguration`, `WebhookSecretConfiguration`,
  `WebhookEventConfiguration`, `WebhookAttemptConfiguration`,
  `MessageConfiguration`, `DeliveryConfiguration`, `DeliveryAttemptConfiguration`,
  and `OtpChallengeConfiguration`
- Migrations: `src/NotifyRail.Api/Infrastructure/Persistence/Migrations`

## Relationships

- One API Client has zero or more API Keys and Messages.
- One API Client has zero or more historical Webhook Endpoints and at most one
  active Webhook Endpoint.
- One API Client has zero or one initial Webhook Secret. Secret rotation may
  extend that relationship in a later schema migration.
- One message has one delivery per normalized recipient.
- Each delivery references one message through `deliveries.message_id`.
- Each delivery attempt references one delivery through
  `delivery_attempts.delivery_id`.
- Each Webhook Attempt references one Webhook Event through
  `webhook_attempts.webhook_event_id`.
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

## `webhook_endpoints`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key; defaults to `gen_random_uuid()`. Replacement creates a new identifier. |
| `api_client_id` | `uuid` | yes | References the owning `api_clients(id)`. |
| `url` | `text` | yes | Non-blank normalized absolute endpoint URL. Management API validation defines the accepted URL policy. |
| `is_enabled` | `boolean` | yes | Whether new Webhook Events may target the endpoint; defaults to `true`. |
| `created_at` | `timestamp with time zone` | yes | Resource creation instant. |
| `updated_at` | `timestamp with time zone` | yes | Latest state-change instant. |
| `disabled_at` | `timestamp with time zone` | no | Required exactly when `is_enabled` is `false`. |

Indexes and uniqueness:

- Primary key on `id`.
- Partial unique index `webhook_endpoints_active_api_client_id_key` on
  `api_client_id` where `is_enabled` is true. PostgreSQL therefore enforces at
  most one active Webhook Endpoint for each API Client.
- Index `webhook_endpoints_api_client_created_at_idx` on
  `(api_client_id, created_at)` for current and historical inspection.

Replacing an active endpoint disables the old row before inserting the new
row in one transaction. Disabling an endpoint does not disable its API Client.

## `webhook_secrets`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key; defaults to `gen_random_uuid()`. |
| `api_client_id` | `uuid` | yes | Unique reference to the owning `api_clients(id)`. |
| `protected_value` | `bytea` | yes | Non-empty .NET Data Protection ciphertext; plaintext is never persisted. |
| `created_at` | `timestamp with time zone` | yes | Secret creation instant. |

Indexes and uniqueness:

- Primary key on `id`.
- Unique index `webhook_secrets_api_client_id_key` on `api_client_id`.

The `IWebhookSecretProtector` boundary uses the purpose
`NotifyRail.Webhooks.Secrets.v1`. Data Protection keys live in the configured
filesystem key ring rather than the application database. The initial
`nrs_<secret>` credential is returned only on creation; later persistence and
Management API reads expose no plaintext or recoverable display value.

## `webhook_events`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Stable event identifier reused by every dispatch attempt. |
| `api_client_id` | `uuid` | yes | Owning API Client. |
| `webhook_endpoint_id` | `uuid` | yes | Endpoint selected when the event was created. |
| `message_id` | `uuid` | yes | Message whose Delivery changed. |
| `delivery_id` | `uuid` | yes | Delivery whose client-visible transition occurred. |
| `type` | `text` | yes | Versioned event type: `delivery.sent`, `delivery.delivered`, `delivery.failed`, or `delivery.expired`. |
| `version` | `integer` | yes | Positive payload contract version; initially `1`. |
| `sequence` | `integer` | yes | Positive, monotonic sequence within one Delivery. |
| `occurred_at` | `timestamp with time zone` | yes | Delivery transition instant. |
| `payload` | `text` | yes | Exact serialized JSON body used for signing and dispatch. |
| `status` | `text` | yes | One of `pending`, `processing`, `succeeded`, or `failed`. |
| `attempt_count` | `integer` | yes | Number of recorded Webhook Attempts; defaults to zero. |
| `claimed_at` | `timestamp with time zone` | no | Claim instant, present only while processing. |
| `claimed_by` | `text` | no | Non-blank worker identity, present only while processing. |
| `succeeded_at` | `timestamp with time zone` | no | Present exactly when status is `succeeded`. |
| `created_at` | `timestamp with time zone` | yes | Durable outbox creation instant. |
| `updated_at` | `timestamp with time zone` | yes | Latest dispatch state-change instant. |

`(delivery_id, sequence)` is unique. The due-work index on
`(status, created_at)` supports the dedicated Webhook Queue. A lower-sequence
event in any nonterminal dispatch state prevents a later event for the same
Delivery from being claimed without blocking other Deliveries. The payload is
stored as text so the bytes signed by NotifyRail are the bytes sent over HTTP.

## `webhook_attempts`

| Column | PostgreSQL type | Required | Contract |
| --- | --- | --- | --- |
| `id` | `uuid` | yes | Primary key; defaults to `gen_random_uuid()`. |
| `webhook_event_id` | `uuid` | yes | Webhook Event dispatched by this attempt. |
| `attempt_number` | `integer` | yes | Positive, one-based attempt number within the event. |
| `outcome` | `text` | yes | `succeeded` or `failed`. |
| `http_status_code` | `integer` | no | HTTP status between 100 and 599 when a response exists. |
| `error_code` | `text` | no | Normalized diagnostic bounded to 100 characters. |
| `error_message` | `text` | no | Diagnostic bounded to 500 characters; response bodies are excluded. |
| `attempted_at` | `timestamp with time zone` | yes | Claim and request timestamp. |
| `completed_at` | `timestamp with time zone` | yes | Result-recording timestamp, not earlier than `attempted_at`. |
| `latency_milliseconds` | `bigint` | yes | Non-negative measured HTTP latency. |

`(webhook_event_id, attempt_number)` is unique. Attempt insertion and Webhook
Event status update occur in one transaction after the HTTP request completes.

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
- A client-visible Delivery transition creates the corresponding
  `delivery.sent`, `delivery.delivered`, `delivery.failed`, or
  `delivery.expired` event in that same transaction when the Message owner has
  an active Webhook Endpoint. A rollback leaves no Webhook Event, and endpoint
  registration does not backfill earlier transitions.
- Internal `queued`, `processing`, and `retry_scheduled` transitions create no
  Webhook Events. Duplicate or conflicting Provider Callbacks create no
  duplicate logical terminal event.
- `deliveries.attempt_count` equals the number of recorded Attempts for that
  Delivery.
- Schema changes require an EF Core migration; entity mappings and this
  reference must be updated with the migration.
- Enabling or replacing a Webhook Endpoint does not backfill historical
  Delivery transitions.
