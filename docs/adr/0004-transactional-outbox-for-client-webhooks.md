# Persist client webhooks with a transactional outbox

## Status

Accepted.

## Context

A Delivery transition and its client notification cannot be committed through
two independent operations without a crash window: the Delivery may change
without a Webhook Event, or an event may describe a transition that never
committed. Publishing directly to a broker would not remove that atomicity
problem and would add infrastructure to a project that already uses PostgreSQL
for durable work claiming.

## Decision

Create the Webhook Event in the same PostgreSQL transaction as each
client-visible Delivery transition. Dispatch persisted events through a
dedicated PostgreSQL-backed Webhook Worker using transactional claims and
`FOR UPDATE SKIP LOCKED`.

Provide at-least-once dispatch with stable event identifiers. Persist every
Webhook Attempt, retry transient failures for a bounded window, and retain Dead
Webhook Events for investigation and manual replay.

## Consequences

- A committed client-visible Delivery transition cannot lose its Webhook Event.
- Remote ambiguity can still produce duplicate requests, so API Clients must
  deduplicate by event identifier.
- The Webhook Queue and Delivery Queue remain operationally independent while
  sharing PostgreSQL.
- A future broker adapter may replace dispatch claiming, but PostgreSQL remains
  the source of truth for the outbox unless the atomicity model is revisited.
