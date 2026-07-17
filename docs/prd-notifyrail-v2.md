# NotifyRail v2: Reliable Client Webhooks

> Status: Planned. This specification was agreed on 2026-07-17 and is the
> product and engineering baseline for the next milestone. The completed MVP
> remains documented in `docs/prd-notifyrail.md`.
>
> Tracking issue: [#1](https://github.com/tarelion/notifyrail-dotnet/issues/1)

## Problem Statement

NotifyRail already demonstrates reliable asynchronous notification processing,
but it models only one anonymous caller and requires that caller to poll for
results. Global idempotency keys cause unrelated callers to conflict, existing
read endpoints do not express an ownership boundary, and a client cannot learn
about a Delivery transition automatically. The project also lacks an
end-to-end operational view of queueing, retries, and failure recovery.

As a backend portfolio project, NotifyRail needs a coherent next milestone that
demonstrates multi-client isolation, secure machine-to-machine authentication,
transactional event publication, at-least-once HTTP dispatch, failure recovery,
and observability without expanding into a full authentication product or
adding infrastructure for its own sake.

## Solution

Introduce API Clients as the ownership boundary for Messages, Deliveries, and
OTP Challenges. Protect data-plane calls with rotatable API Keys and expose a
separately protected Management API for client, credential, Webhook Endpoint,
secret, and replay operations.

Notify each API Client about client-visible Delivery transitions through a
registered Webhook Endpoint. Persist each Webhook Event atomically with the
Delivery transition, dispatch it with a dedicated PostgreSQL-backed worker,
record every Webhook Attempt, retry transient failures for up to 24 hours, and
retain exhausted events as Dead Webhook Events for investigation and manual
replay. Sign outbound webhooks, authenticate inbound provider callbacks, block
unsafe destinations, and expose the complete flow through OpenTelemetry and a
standalone Aspire Dashboard.

## User Stories

1. As an operator, I want to create an API Client, so that an external application has an explicit ownership boundary in NotifyRail.
2. As an operator, I want a newly created API Key to be shown only once, so that plaintext credentials are not recoverable later.
3. As an operator, I want to create more than one active API Key for an API Client, so that I can rotate credentials without downtime.
4. As an operator, I want to revoke an API Key permanently, so that a retired or compromised credential can no longer call NotifyRail.
5. As an operator, I want to see API Key creation, last-use, expiry, and revocation metadata, so that I can manage credential lifecycle safely.
6. As an API Client, I want to authenticate with an API Key, so that NotifyRail can associate each request with the correct owner.
7. As an API Client, I want to reuse an idempotency key that another API Client has used, so that unrelated integrations do not conflict.
8. As an API Client, I want repeated equivalent requests under my own idempotency key to replay safely, so that network retries do not create duplicate Messages or Deliveries.
9. As an API Client, I want conflicting content under my own idempotency key to be rejected, so that accidental key reuse remains visible.
10. As an API Client, I want to read only my own Messages, Deliveries, reports, and OTP Challenges, so that another client cannot access my data.
11. As an API Client, I want cross-client resource identifiers to return not found, so that resource existence is not leaked.
12. As an operator, I want existing MVP data assigned to a legacy API Client during migration, so that the v2 schema change does not discard persisted work.
13. As an operator, I want to register one active Webhook Endpoint for an API Client, so that NotifyRail knows where to send client-visible events.
14. As an operator, I want the Webhook Endpoint to be a separate resource from the API Client, so that multiple endpoints and subscriptions can be added later without redesigning client identity.
15. As an operator, I want to disable or replace a Webhook Endpoint explicitly, so that outbound traffic never silently follows a changed destination.
16. As an API Client, I want a `delivery.sent` Webhook Event, so that I know the provider accepted the send request.
17. As an API Client, I want `delivery.delivered`, `delivery.failed`, and `delivery.expired` Webhook Events, so that I learn the final outcome without polling.
18. As an API Client, I want internal `queued`, `processing`, and `retry_scheduled` transitions omitted, so that implementation details do not create webhook noise.
19. As an API Client, I want each Webhook Event to include a stable event identifier, event type, schema version, occurrence time, per-Delivery sequence, Message identifier, Delivery identifier, recipient, and status, so that I can process it deterministically.
20. As an API Client, I want Message bodies, OTP Codes, API Keys, Webhook Secrets, and raw provider responses excluded from payloads, so that unnecessary secrets and content are not propagated.
21. As an API Client, I want published v1 event meanings to remain compatible, so that additive evolution does not break my consumer.
22. As an API Client, I want breaking event contract changes to use a new version, so that I can migrate deliberately.
23. As an API Client, I want a Delivery transition and its Webhook Event to be committed atomically, so that a committed transition cannot lose its notification.
24. As an API Client, I want at-least-once webhook dispatch, so that a lost HTTP response cannot silently lose an event.
25. As an API Client, I want duplicate attempts to reuse the same event identifier, so that I can make consumption idempotent.
26. As an API Client, I want Webhook Events for one Delivery sent in sequence order, so that `delivered` is not normally observed before `sent`.
27. As an API Client, I want a monotonic per-Delivery sequence in every event, so that duplicate, delayed, or manually replayed events cannot regress my view.
28. As an API Client, I want different Deliveries dispatched concurrently, so that ordering for one recipient does not limit total throughput.
29. As an operator, I want network errors, timeouts, HTTP 408, HTTP 429, and HTTP 5xx responses retried with exponential backoff and jitter, so that transient client failures recover automatically.
30. As an operator, I want HTTP 2xx responses to complete an event and other HTTP 3xx/4xx responses to fail permanently, so that configuration and authentication errors do not retry pointlessly.
31. As an operator, I want redirects disabled, so that endpoint changes and redirect-based SSRF attempts are not followed implicitly.
32. As an operator, I want every outbound try recorded as a Webhook Attempt, so that response status, latency, timing, and failure category can be investigated.
33. As an operator, I want automatic attempts to stop after a configurable 24-hour window, so that permanently failing endpoints do not consume resources forever.
34. As an operator, I want exhausted events retained as Dead Webhook Events, so that failures remain visible rather than being discarded.
35. As an operator, I want to replay a Dead Webhook Event through the Management API, so that delivery can resume after the client fixes its endpoint.
36. As an API Client, I want webhook requests signed with my Webhook Secret, so that I can verify origin and integrity.
37. As an API Client, I want webhook headers to carry event ID, timestamp, and versioned HMAC-SHA256 signature, so that I can reject replays and deduplicate requests before parsing business data.
38. As an operator, I want Webhook Secrets encrypted at rest with keys held outside the application database, so that a database-only disclosure does not expose signing secrets.
39. As an operator, I want Webhook Secret rotation to support a bounded overlap, so that clients can switch verifiers without downtime while new requests use only the new secret.
40. As an operator, I want production-like configurations to accept only public HTTPS Webhook Endpoints, so that API Clients cannot use NotifyRail to reach internal networks or metadata services.
41. As a developer, I want an explicit development/test option for localhost Webhook Endpoints, so that integration tests and the local demo remain practical without weakening the default policy.
42. As an operator, I want DNS destinations revalidated at connection time and private, loopback, link-local, and metadata addresses rejected, so that DNS rebinding cannot bypass URL validation.
43. As a provider adapter, I want mock Provider Callbacks authenticated with a provider-specific HMAC secret and timestamp, so that unauthorized callers cannot finalize Deliveries.
44. As a future provider adapter, I want callback verification behind a provider-specific boundary, so that a real provider's verification protocol can replace the mock scheme without changing Delivery behavior.
45. As an operator, I want the Management API protected by an Operator Credential separate from API Keys, so that control-plane authority cannot be confused with client data-plane access.
46. As a future operator, I want operator authentication behind a standard authentication policy, so that OIDC can replace the deployment secret without changing management behavior.
47. As an operator, I want queue depth, claim duration, attempt counts, dead-event counts, success rate, and webhook latency metrics, so that degradation is visible.
48. As an operator, I want traces to correlate Message intake, Delivery processing, Provider Callback handling, Webhook Event creation, and Webhook Attempts, so that I can follow a request across asynchronous boundaries.
49. As an operator, I want structured logs correlated by client, Message, Delivery, Webhook Event, and attempt identifiers, so that incidents can be investigated without parsing prose.
50. As a security reviewer, I want recipients masked in telemetry and all credentials, OTP Codes, and Message bodies omitted, so that observability does not become a data leak.
51. As a developer, I want a single standalone Aspire Dashboard container to display local logs, traces, and metrics, so that observability is demonstrable without a production monitoring stack.
52. As a reviewer, I want a Docker demo that shows client creation, authenticated Message intake, provider completion, webhook failure, retry, dead-event inspection, replay, and telemetry, so that the reliability claims are visible end to end.

## Implementation Decisions

- Keep NotifyRail as one ASP.NET Core application for this milestone. Separate the data-plane endpoints, Management API policies, Delivery Worker, and Webhook Worker as behavioral modules inside the process.
- Model API Client independently from credentials. Every Message belongs to exactly one API Client; related Deliveries and OTP Challenges inherit that ownership through their Message.
- Authenticate Message and OTP endpoints with API Keys. Health endpoints remain public. Management endpoints accept only an Operator Credential. Provider Callback endpoints use provider-specific authentication rather than either credential type.
- Represent API Keys as high-entropy, recognizable credentials containing a lookup identifier and secret. Return the full credential only on creation, persist a non-reversible verification value and display-safe prefix, and support multiple simultaneously active keys for rotation.
- Track API Key creation, last use, optional expiry, and revocation. Revocation is irreversible.
- Keep authentication replaceable through standard ASP.NET Core authentication schemes and authorization policies named for API Client and Operator access. Endpoint behavior consumes an authenticated identity rather than parsing credentials directly.
- Add required Message ownership and replace global idempotency uniqueness with uniqueness over API Client and idempotency key.
- Use a data-preserving migration that creates a legacy API Client, backfills existing Messages, makes ownership required, and replaces the global unique constraint. Do not generate an active legacy credential automatically.
- Return not found when an authenticated API Client requests a resource it does not own. Missing or invalid credentials return an authentication failure without revealing client metadata.
- Expose a panel-free Management API for API Client creation and disabling, API Key creation and revocation, Webhook Endpoint configuration, Webhook Secret rotation, Dead Webhook Event inspection, and replay. Its initial Operator Credential is supplied as a deployment secret; user accounts, sessions, and role management are not introduced.
- Model Webhook Endpoint as a separate resource owned by an API Client. Support at most one active endpoint per client in this milestone while keeping the relationship extensible to multiple endpoints later.
- Permit an API Client to operate without an active Webhook Endpoint and continue polling. Create client Webhook Events only when an active endpoint exists at the time of the client-visible Delivery transition; enabling an endpoint does not backfill historical transitions.
- Produce Webhook Events for `delivery.sent`, `delivery.delivered`, `delivery.failed`, and `delivery.expired`. Do not expose `queued`, `processing`, or `retry_scheduled` as client events.
- Keep Delivery state independent from webhook state. A Delivery may be `delivered` while its Webhook Event is pending, retrying, or dead.
- Use a versioned JSON envelope with `event_id`, `type`, `version`, `occurred_at`, and a data object containing `message_id`, `delivery_id`, `sequence`, `status`, and the full recipient submitted by the owning API Client.
- Treat v1 fields and meanings as a published contract. Additive optional fields may remain v1; removals, renames, type changes, or semantic changes require a new event version.
- Allocate a monotonic sequence for client-visible transitions within each Delivery. Keep normal dispatch ordered by Delivery and endpoint; allow different Deliveries to dispatch concurrently.
- Create each Webhook Event in the same PostgreSQL transaction as its Delivery transition. The outbox write occurs for transitions caused by provider results, provider callbacks, expiry, and any other path that reaches a client-visible state.
- Use a dedicated PostgreSQL-backed Webhook Queue and Webhook Worker. Claim due work transactionally with `FOR UPDATE SKIP LOCKED`; do not share claims or worker state with the Delivery Queue.
- Do not hold a database transaction open during the outbound HTTP request. Commit the claim, perform the request, and then record the result. If the remote side handles the request but NotifyRail cannot record the response, retry the same event ID under the at-least-once contract.
- Persist one Webhook Attempt for every outbound request with attempt time, completion time or latency, HTTP status when available, normalized outcome, and a bounded diagnostic error. Never persist response bodies by default.
- Treat any HTTP 2xx response as success. Retry network failures, timeouts, HTTP 408, HTTP 429, and HTTP 5xx. Treat redirects and other HTTP 3xx/4xx responses as permanent failure.
- Retry transient failures with configurable exponential backoff and jitter until a configurable 24-hour deadline. After the deadline, mark the Webhook Event dead and stop automatic attempts.
- A dead earlier event unblocks later events for the same Delivery. Manual replay preserves the original event ID, occurrence time, version, and sequence so consumers can deduplicate and reject stale state.
- Provide Management API replay for Dead Webhook Events. Replaying changes dispatch state but does not mutate the Delivery or create a new domain occurrence.
- Sign outbound requests with a per-client Webhook Secret using HMAC-SHA256 over the timestamp and exact raw body. Send event ID, timestamp, and versioned signature in dedicated headers.
- Generate Webhook Secrets with high entropy, reveal them only when created or rotated, and encrypt them at rest through a purpose-specific protector. Keep protection keys outside the application database. Use a persisted shared .NET Data Protection key ring for local containers and preserve a replacement boundary for KMS or a secret manager.
- During Webhook Secret rotation, use the new secret for all new signatures and expose enough bounded overlap metadata for clients to accept both secrets during migration. Retire the old secret after the overlap deadline.
- Validate Webhook Endpoints on configuration and again when connecting. Default to public HTTPS; reject loopback, private, link-local, multicast, unspecified, and cloud metadata destinations; disable redirects; and defend against DNS rebinding. Allow localhost only through an explicit development/test setting.
- Authenticate mock Provider Callbacks with a separate provider-specific secret, timestamp, and HMAC-SHA256 signature. Preserve the existing first-terminal-result-wins and duplicate callback behavior.
- Add OpenTelemetry traces, metrics, and logs across HTTP intake, PostgreSQL operations, both workers, provider callbacks, event creation, and Webhook Attempts. Propagate correlation across persisted asynchronous work using identifiers and trace links where parent context is no longer live.
- Mask recipients in telemetry and never record Message bodies, OTP Codes, API Keys, Operator Credentials, Webhook Secrets, signature material, or unbounded remote content.
- Add one standalone Aspire Dashboard container under an optional Docker Compose observability profile. Export via OTLP so a future deployment can replace the developer dashboard with production backends without changing instrumentation.
- Deliver the work in five independently validated slices: client foundation; webhook model and outbox; reliable dispatch; security; observability and demo. Each slice includes migrations, behavior tests, and documentation updates before the next begins.

## Testing Decisions

- Treat externally observable behavior as the contract. Prefer integration tests through real HTTP endpoints, real PostgreSQL migrations and constraints, running hosted workers, and a controllable fake Webhook Endpoint. Avoid tests that assert private helper structure or EF implementation details.
- Use the existing `WebApplicationFactory` pattern as the primary application seam and the existing PostgreSQL-backed integration style as prior art. Continue using the existing hosted-service removal helper when a test must drive a worker deterministically.
- Use a controllable HTTP webhook receiver as the external-client seam. It must record headers, exact bodies, arrival order, and attempt count and be able to return scripted 2xx, 3xx, 4xx, 408, 429, 5xx, delayed, disconnected, and response-lost outcomes.
- Preserve `TimeProvider` as the clock seam for retry deadlines, backoff, signature age, credential expiry, secret overlap, and OTP behavior. Inject deterministic jitter for schedule assertions without exposing internal methods as a test seam.
- Test API Client authentication and isolation through HTTP: missing, malformed, unknown, expired, and revoked keys; same idempotency key across two clients; conflicting reuse within one client; owned reads; and cross-client not-found responses.
- Test Management API authorization through HTTP and prove that API Keys cannot invoke operator actions and the Operator Credential cannot silently become a data-plane owner.
- Test the migration against a database at the current MVP schema with representative Messages, Deliveries, attempts, and OTP Challenges. Verify legacy ownership, preservation of identifiers and relationships, and the new composite uniqueness constraint.
- Test the atomic outbox behavior by observing that every committed client-visible transition has exactly one logical Webhook Event and rolled-back transitions have none. Exercise transitions produced by provider acceptance, permanent failure, max attempts, callback completion, and expiry.
- Test at-least-once ambiguity by allowing the fake receiver to record an event and then drop the response. Verify a later attempt has the same event ID, version, sequence, occurrence time, and body semantics and that two Webhook Attempts are recorded.
- Test concurrency with multiple Webhook Workers against the same PostgreSQL database. Verify one in-flight claim per event, no concurrent sends of the same event, parallel progress for different Deliveries, stale-claim recovery, and no starvation behind unrelated ordered streams.
- Test ordering by making `delivery.sent` retry while `delivery.delivered` already exists. Verify normal arrival order, later-event blocking, unblocking after success or death, and monotonic sequence values.
- Test response classification for all agreed HTTP categories and verify backoff, jitter bounds, `Retry-After` handling when valid, the 24-hour deadline, Dead Webhook Event retention, and absence of automatic attempts after death.
- Test manual replay through the Management API. Verify the Delivery does not change, the original event identity and sequence remain stable, a new Webhook Attempt is recorded, and stale replay cannot regress a sequence-aware consumer.
- Test outbound signature verification at the fake receiver using the exact received body and headers. Cover tampering, timestamp expiry, wrong client secret, secret rotation overlap, and absence of signing material from logs.
- Test Provider Callback authentication through the public callback endpoint. Cover valid signature, tampered body, stale timestamp, wrong provider secret, duplicate callback, racing conflicting callbacks, and terminal-state non-regression.
- Test SSRF protection at the Webhook Endpoint configuration and dispatch seams. Cover non-HTTPS URLs, redirects, localhost, private IPv4 and IPv6, link-local and metadata ranges, mixed DNS answers, DNS rebinding, and the explicit development/test localhost exception.
- Test telemetry through an in-memory or test OTLP exporter rather than the dashboard UI. Verify critical spans, links, metrics, and correlation attributes exist and assert that credentials, OTP Codes, Message bodies, full recipients, and remote bodies do not appear.
- Keep focused public module tests for queue-claim races and recovery only where the full HTTP seam cannot deterministically expose the invariant. Existing Delivery Queue and Delivery Worker integration tests are the prior art; do not introduce private-method unit tests.
- Run the complete containerized suite against an isolated PostgreSQL database. The milestone is not complete until the Docker demo exercises authenticated intake, provider completion, webhook failure, retry or death, replay, and visible telemetry.

## Out of Scope

- A frontend management panel.
- User registration, password login, sessions, JWT refresh tokens, and an in-house OAuth or OIDC provider.
- OIDC-based operator login in this milestone; the authentication boundary must allow it later.
- Rate limits, quotas, billing, and usage plans.
- A real SMS, email, or OTP provider integration.
- RabbitMQ, Kafka, NATS, Redis, or another external queue.
- More than one active Webhook Endpoint per API Client and event-type subscription filters.
- Historical webhook backfill when an endpoint is first enabled.
- Exactly-once remote delivery; the contract is explicitly at-least-once.
- A production observability backend. Prometheus, Grafana, Tempo, Loki, and commercial backends may consume the same OpenTelemetry output later.
- Message content, OTP Codes, provider raw responses, or credentials in Webhook Event payloads or telemetry.
- Changing the existing Delivery lifecycle beyond emitting events for its client-visible states.

## Further Notes

- The canonical domain terms are API Client, Message, Delivery, Delivery Attempt, Provider Callback, Webhook Endpoint, Webhook Event, Webhook Attempt, Webhook Secret, and Dead Webhook Event. Do not use Delivery for webhook dispatch.
- ADR-0003 supersedes the MVP's global idempotency scope when v2 is implemented.
- ADR-0004 records the PostgreSQL transactional outbox and dedicated Webhook Worker choice.
- Delivery truth is never gated by client webhook availability. A Delivery remains `delivered` even while its Webhook Event retries or is dead.
- The API Client owns the full recipient value already submitted to NotifyRail, so the value is included in its webhook payload. Telemetry uses a masked representation.
- The first active implementation task is the client foundation slice. Later slices must not bypass its ownership or authentication boundaries.
