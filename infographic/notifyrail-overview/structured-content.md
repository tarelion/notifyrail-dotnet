# NotifyRail: Reliable Notification Delivery Backend

## Overview
NotifyRail is a backend-focused notification delivery simulator built with
C#/.NET, PostgreSQL, EF Core, Docker, and xUnit. It demonstrates message intake,
idempotency, PostgreSQL-backed delivery claiming, worker processing, retries,
provider callbacks, delivery reports, and OTP verification.

## Learning Objectives
The viewer will understand:
1. What backend capabilities NotifyRail demonstrates.
2. How the delivery pipeline moves from API request to stored delivery state.
3. Which behaviors are verified by tests.

---

## Section 1: Core Identity

**Key Concept**: NotifyRail is a learning-focused backend project that simulates
reliable notification delivery.

**Content**:
- "NotifyRail is a learning-focused C#/.NET backend that simulates reliable notification delivery."
- "The project focuses on domain-relevant backend concepts: reliable delivery, retries, delivery reports, OTP verification, provider abstraction, scheduled sending, and callback handling."

**Visual Element**:
- Type: central system nameplate
- Subject: NotifyRail as a backend control panel or lab specimen
- Treatment: large title, coordinate label A-01, technical metadata strip

**Text Labels**:
- Headline: "NotifyRail"
- Subhead: "Reliable Notification Delivery Backend"
- Labels: ".NET", "PostgreSQL", "EF Core", "xUnit", "Docker"

---

## Section 2: Delivery Pipeline

**Key Concept**: A message request creates per-recipient delivery jobs that a
hosted worker claims and processes through a mock provider.

**Content**:
- "atomic message creation with one delivery per recipient"
- "PostgreSQL delivery claiming with scheduling, expiry, priority ordering, and `FOR UPDATE SKIP LOCKED`"
- "a hosted background worker that sends claimed deliveries through an in-process, recipient-configurable mock provider"

**Visual Element**:
- Type: horizontal pipeline
- Subject: API Client -> Minimal API -> PostgreSQL Queue -> Hosted Worker -> Mock Provider
- Treatment: connected technical blocks with arrows and small state markers

**Text Labels**:
- Headline: "Delivery Pipeline"
- Labels: "API Client", "Minimal API", "PostgreSQL Queue", "Hosted Worker", "Mock Provider"

---

## Section 3: Reliability Mechanics

**Key Concept**: NotifyRail models idempotency, safe queue claiming, retry
state, and lease recovery.

**Content**:
- "globally unique idempotency keys with replay and conflict handling"
- "five-minute lease recovery for deliveries abandoned in `processing`"
- "atomic provider-result recording for `accepted`, `retryable_failure`, and `permanent_failure` outcomes, including `sent`, `retry_scheduled`, and `failed` transitions"

**Visual Element**:
- Type: warning and guarantee module
- Subject: duplicate prevention, lock safety, retry scheduling, stale claim recovery
- Treatment: compact checklist with highlighted safety guarantees

**Text Labels**:
- Headline: "Reliability Mechanics"
- Labels: "Idempotency", "FOR UPDATE SKIP LOCKED", "Retry Backoff", "Lease Recovery"

---

## Section 4: Delivery Lifecycle

**Key Concept**: The delivery lifecycle is visible through status states and
attempt history.

**Content**:
- "`queued`"
- "`processing`"
- "`sent`"
- "`delivered`"
- "`retry_scheduled`"
- "`failed`"
- "`expired`"
- "recipient-level delivery reads with ordered provider attempt history"

**Visual Element**:
- Type: state machine strip
- Subject: queued -> processing -> sent -> delivered, with branches to retry_scheduled, failed, expired
- Treatment: colored status chips, small arrows, terminal-state markers

**Text Labels**:
- Headline: "Delivery Lifecycle"
- Labels: "queued", "processing", "sent", "delivered", "retry_scheduled", "failed", "expired"

---

## Section 5: OTP and Callback Safety

**Key Concept**: OTP verification and provider callbacks are guarded against
unsafe repeated state changes.

**Content**:
- "idempotent mock-provider callbacks that finalize sent deliveries as delivered or failed without regressing terminal states"
- "idempotent OTP send with hashed code persistence, delivery expiry, and a mock `debug_code`"
- "concurrency-safe one-time OTP verification with TTL and an attempt limit"

**Visual Element**:
- Type: split safety module
- Subject: callback idempotency on the left, OTP one-time verification on the right
- Treatment: shield-like technical callouts, lock iconography, terminal-state stamp

**Text Labels**:
- Headline: "Safe Finalization"
- Labels: "Callback Idempotency", "Hashed OTP", "TTL", "One-Time Verify", "Attempt Limit"

---

## Section 6: API Surface

**Key Concept**: The current MVP exposes health, message, delivery report,
provider callback, and OTP endpoints.

**Content**:
- "`GET /healthz`"
- "`GET /readyz`"
- "`POST /messages`"
- "`GET /messages/{message_id}`"
- "`GET /messages/{message_id}/deliveries`"
- "`GET /messages/{message_id}/report`"
- "`POST /provider-callbacks/mock`"
- "`POST /otp/send`"
- "`POST /otp/verify`"

**Visual Element**:
- Type: endpoint inventory table
- Subject: grouped HTTP routes
- Treatment: compact terminal-like table with method badges

**Text Labels**:
- Headline: "Implemented Endpoints"
- Labels: "Health", "Messages", "Deliveries", "Reports", "Callbacks", "OTP"

---

## Section 7: Verification Evidence

**Key Concept**: The MVP behavior is covered by integration and unit tests.

**Content**:
- "`dotnet test NotifyRail.slnx`"
- "Failed: 0"
- "Passed: 70"
- "Skipped: 0"
- "Total: 70"

**Visual Element**:
- Type: test result badge
- Subject: passing test suite with command and counts
- Treatment: large "70 passed" badge, small terminal strip, no decorative-only elements

**Text Labels**:
- Headline: "Validation"
- Labels: "dotnet test NotifyRail.slnx", "70 passed", "0 failed"

---

## Data Points (Verbatim)

### Statistics
- "Failed: 0"
- "Passed: 70"
- "Skipped: 0"
- "Total: 70"

### Quotes
- "NotifyRail is a learning-focused C#/.NET backend that simulates reliable notification delivery."

### Key Terms
- **Message**: "The top-level notification request from a client."
- **Delivery**: "The per-recipient delivery job created from a message."
- **Delivery Attempt**: "One provider send attempt for one delivery."
- **Provider Callback**: "A provider-originated status update."
- **OTP Code**: "A short-lived, one-time verification code."

---

## Design Instructions

### Style Preferences
- Use `pop-laboratory`: lab manual precision, pop art color impact, coordinate
  systems, technical diagrams, fluorescent accents on blueprint grid.
- Avoid Mermaid-style plain diagram aesthetics.
- Avoid cute, cartoonish, generic stock icon visuals.

### Layout Preferences
- Use `dense-modules`.
- Use landscape 16:9.
- Use English text.

### Other Requirements
- Keep labels short and readable.
- Prioritize visual proof of backend behavior over decorative illustration.
- Do not invent features beyond the source content.
