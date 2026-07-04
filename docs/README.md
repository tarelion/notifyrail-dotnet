# NotifyRail Documentation

This directory contains product direction and durable technical contracts for
contributors and coding agents. Use the root [README](../README.md) for the
current implementation and local workflow.

## Product Direction

- [NotifyRail PRD](prd-notifyrail.md): target MVP goals, boundaries, user
  stories, and success criteria. It is not an implementation-status page.

## Reference

- [HTTP API](reference/http-api.md): implemented routes, payloads, validation,
  responses, and idempotency behavior.
- [Delivery lifecycle](reference/delivery-lifecycle.md): canonical delivery
  states, transitions, invariants, and forbidden transitions for the target
  system.
- [Delivery processing](reference/delivery-processing.md): worker runtime,
  provider adapter contract, mock provider behavior, and delivery attempt
  persistence.

## Architecture Decisions

- [Global idempotency key for the MVP](adr/0001-global-idempotency-key-for-mvp.md)

## Source-of-Truth Rules

- Current capabilities and known gaps belong in the root `README.md`.
- Product scope and non-goals belong in `docs/prd-notifyrail.md`.
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
