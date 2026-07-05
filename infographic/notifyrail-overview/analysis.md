---
title: "NotifyRail Backend Delivery System"
topic: "technical backend portfolio project"
data_type: "system overview / architecture / process"
complexity: "moderate"
point_count: 7
source_language: "en"
user_language: "tr"
---

## Main Topic
NotifyRail is a C#/.NET backend that demonstrates reliable notification
delivery using PostgreSQL persistence, a hosted worker, retry/backoff behavior,
provider callback handling, and OTP verification.

## Learning Objectives
After viewing this infographic, the viewer should understand:
1. What backend capabilities NotifyRail demonstrates.
2. How message intake, PostgreSQL queueing, worker processing, mock provider
   results, and callbacks connect.
3. Which MVP behaviors are implemented and verified by tests.

## Target Audience
- **Knowledge Level**: Intermediate backend engineers, internship reviewers,
  and portfolio visitors.
- **Context**: They are quickly evaluating whether the repository demonstrates
  meaningful backend engineering beyond CRUD.
- **Expectations**: They need to see system flow, lifecycle states, reliability
  behavior, OTP behavior, API surface, and test evidence.

## Content Type Analysis
- **Data Structure**: Multiple related backend modules and behaviors arranged
  around one central delivery lifecycle.
- **Key Relationships**: API requests create messages and deliveries;
  PostgreSQL stores and coordinates state; the hosted worker claims due
  deliveries; the mock provider produces accepted, retryable failure, or
  permanent failure outcomes; callbacks safely finalize sent deliveries; OTP
  challenges enforce TTL and one-time verification.
- **Visual Opportunities**: Use compact modules, a central pipeline strip,
  lifecycle chips, endpoint inventory, retry/callback safety callouts, and a
  test validation badge.

## Key Data Points (Verbatim)
- "NotifyRail is a learning-focused C#/.NET backend that simulates reliable notification delivery."
- "atomic message creation with one delivery per recipient"
- "globally unique idempotency keys with replay and conflict handling"
- "PostgreSQL delivery claiming with scheduling, expiry, priority ordering, and `FOR UPDATE SKIP LOCKED`"
- "five-minute lease recovery for deliveries abandoned in `processing`"
- "a hosted background worker that sends claimed deliveries through an in-process, recipient-configurable mock provider"
- "atomic provider-result recording for `accepted`, `retryable_failure`, and `permanent_failure` outcomes, including `sent`, `retry_scheduled`, and `failed` transitions"
- "idempotent mock-provider callbacks that finalize sent deliveries as delivered or failed without regressing terminal states"
- "concurrency-safe one-time OTP verification with TTL and an attempt limit"
- "`GET /healthz`"
- "`GET /readyz`"
- "`POST /messages`"
- "`GET /messages/{message_id}`"
- "`GET /messages/{message_id}/deliveries`"
- "`GET /messages/{message_id}/report`"
- "`POST /provider-callbacks/mock`"
- "`POST /otp/send`"
- "`POST /otp/verify`"
- "Failed: 0"
- "Passed: 70"
- "Skipped: 0"
- "Total: 70"

## Layout × Style Signals
- Content type: system overview and backend reliability guide -> suggests
  dense-modules or structural-breakdown.
- Tone: technical, portfolio-facing, precise -> suggests pop-laboratory or
  technical-schematic.
- Audience: backend reviewers and internship context -> suggests a serious
  engineering visual rather than playful illustration.
- Complexity: moderate with 7 modules -> suggests dense but organized modules.

## Design Instructions (from user input)
The user rejected Mermaid-style diagrams and wants a modern raster infographic
using the Baoyu workflow and a GPT image generation backend.

## Recommended Combinations
1. **dense-modules + pop-laboratory** (Recommended): Shows many backend proof
   points in one high-impact, technical portfolio visual.
2. **structural-breakdown + technical-schematic**: Strong for architecture
   anatomy and component relationships.
3. **bento-grid + corporate-memphis**: Cleaner and more readable for a README
   header, but less distinctive.
