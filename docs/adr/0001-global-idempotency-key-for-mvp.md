# Global idempotency key for the MVP

NotifyRail currently treats `messages.idempotency_key` as globally unique because the MVP has no client, tenant, account, or API key identity yet. This is a deliberate simplification so message creation can still prevent duplicate retries before authentication and client ownership exist.

When client identity is introduced, idempotency must become client-scoped by storing the client identity on messages and replacing the global uniqueness rule with a unique constraint over `(client_id, idempotency_key)`.
