# NotifyRail Documentation

This directory contains product direction and durable technical contracts for
contributors and coding agents. Use the root [README](../README.md) for the
current implementation and local workflow.

## Product Direction

- [NotifyRail PRD](prd-notifyrail.md): completed and frozen MVP goals,
  boundaries, user stories, and success criteria. It is a historical baseline,
  not an implementation-status page or roadmap.
- [NotifyRail v2 PRD](prd-notifyrail-v2.md): planned API Client isolation,
  reliable client webhooks, security, and observability milestone.

## Domain Language

- [NotifyRail domain language](../CONTEXT.md): canonical meanings for Message,
  Delivery, provider callback, and OTP verification terms.

## Reference

- [HTTP API](reference/http-api.md): implemented routes, payloads, validation,
  responses, and idempotency behavior.
- [Delivery lifecycle](reference/delivery-lifecycle.md): canonical delivery
  states, transitions, invariants, and forbidden transitions for the target
  system.
- [Delivery processing](reference/delivery-processing.md): worker runtime,
  provider adapter contract, mock provider behavior, and result recording.
- [Persistence model](reference/persistence-model.md): implemented PostgreSQL
  tables, relationships, constraints, and indexes.
- [OTP verification](reference/otp-verification.md): challenge lifecycle,
  configuration, hashing, idempotency, and verification rules.
- [Observability](reference/observability.md): OpenTelemetry activation,
  asynchronous trace links, structured correlation fields, and privacy
  invariants.

## Architecture Decisions

- [Global idempotency key for the MVP](adr/0001-global-idempotency-key-for-mvp.md)
- [Expose the OTP code in the mock send response](adr/0002-expose-otp-code-in-mock-send-response.md)
- [Scope idempotency keys to API Clients](adr/0003-client-scoped-idempotency.md)
- [Persist client webhooks with a transactional outbox](adr/0004-transactional-outbox-for-client-webhooks.md)

## Source-of-Truth Rules

- Current capabilities and known gaps belong in the root `README.md`.
- The completed MVP scope and non-goals are frozen in
  `docs/prd-notifyrail.md`.
- Future work belongs in GitHub issues or a new versioned PRD.
- Stable API and lifecycle contracts belong in `docs/reference/`.
- Architecture decisions belong in `docs/adr/`.
- Task plans and temporary progress notes must not redefine these contracts.
- A change to implemented behavior must update its canonical reference in the
  same change.

## Guidance for Coding Agents

Before changing behavior:

1. Read the root `AGENTS.md` and `README.md`.
2. Check the PRD for scope boundaries.
3. Read only the reference documents relevant to the change.
4. Treat a PRD requirement or lifecycle rule as a target, not proof that the
   feature is already wired into the running application.
