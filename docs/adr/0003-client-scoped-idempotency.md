# Scope idempotency keys to API Clients

## Status

Accepted. Supersedes ADR-0001 for v2.

## Context

The MVP treats idempotency keys as globally unique because it has no caller
identity. V2 introduces API Clients as ownership and isolation boundaries. A
global key would make unrelated clients conflict and would not represent the
multi-client behavior the project intends to demonstrate.

## Decision

Every Message belongs to exactly one API Client. Authenticate data-plane calls
with an API Key and enforce idempotency with a unique constraint over
`(client_id, idempotency_key)`. Queries and OTP operations use the authenticated
API Client as an implicit scope and return not found for resources owned by a
different client.

Preserve existing rows during migration by assigning them to a legacy API
Client before making ownership mandatory and replacing the global constraint.

## Consequences

- Different API Clients may safely reuse the same idempotency key.
- Authentication becomes part of every Message and OTP data-plane contract.
- Existing clients must receive API Keys before using v2 data-plane endpoints.
- Ownership checks must be applied consistently to writes and reads.
