# Global idempotency key for the MVP

## Status

Accepted.

## Context

The MVP has no client, tenant, account, or API-key identity. Idempotency still
needs a stable scope so retrying message creation cannot create duplicate
messages and deliveries.

## Decision

Treat `messages.idempotency_key` as globally unique for the MVP. A replay with
the same normalized content returns the original receipt; reuse with different
content is an idempotency conflict.

Enforce the scope in PostgreSQL with a unique constraint on
`messages.idempotency_key`, rather than relying only on application checks.

## Consequences

- Two otherwise unrelated callers cannot reuse the same idempotency key.
- Concurrent requests are resolved by the database uniqueness constraint.
- Introducing client identity requires storing that identity on messages and
  replacing the global constraint with a unique constraint over
  `(client_id, idempotency_key)`.
- The database constraint and the HTTP idempotency contract must change in the
  same migration.
