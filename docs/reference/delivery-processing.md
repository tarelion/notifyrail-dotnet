# Delivery Processing Reference

## Purpose

This page defines the implemented background delivery-processing contract for
NotifyRail: how due deliveries and Webhook Events are claimed, how provider and
Webhook Endpoint adapters are called, and how their outcomes are recorded.

## Scope

- Runtime wiring: `src/NotifyRail.Api/Program.cs`
- Queue module: `src/NotifyRail.Api/Features/Deliveries/Queue`
- Worker module: `src/NotifyRail.Api/Features/Deliveries/Worker`
- Provider contract: `src/NotifyRail.Api/Features/Deliveries/Providers`
- Provider callback module:
  `src/NotifyRail.Api/Features/Deliveries/ProviderCallbacks/Mock`
- Webhook queue, dispatch, and worker modules:
  `src/NotifyRail.Api/Features/Webhooks/Queue`,
  `src/NotifyRail.Api/Features/Webhooks/Dispatch`, and
  `src/NotifyRail.Api/Features/Webhooks/Worker`
- Delivery persistence: `src/NotifyRail.Api/Features/Deliveries/Persistence`
- EF Core migrations:
  `src/NotifyRail.Api/Infrastructure/Persistence/Migrations`

## Runtime Wiring

`Program.cs` starts one ASP.NET Core process containing both:

- the HTTP API server
- a hosted Delivery Worker
- a separate hosted Webhook Worker

Both components use the configured `ConnectionStrings:Postgres`. The hosted
worker opens a DI scope for each batch so scoped dependencies such as
`NotifyRailDbContext` are not shared across polls.

### Configuration

| Setting | Source | Default | Contract |
| --- | --- | --- | --- |
| `ConnectionStrings:Postgres` | configuration | none | Required for persistence-backed modules to be registered. |
| `DeliveryWorker:WorkerId` | configuration / `DeliveryWorkerOptions.WorkerId` | `notifyrail-<guid>` | Trimmed non-empty worker identity. |
| `DeliveryWorker:BatchSize` | configuration / `DeliveryWorkerOptions.BatchSize` | `1` | `0` uses the default; negative values are invalid. |
| `DeliveryWorker:PollInterval` | configuration / `DeliveryWorkerOptions.PollInterval` | `500ms` | `00:00:00` uses the default; negative values are invalid. |
| `DeliveryQueue:BaseRetryDelay` | configuration / `DeliveryQueueOptions.BaseRetryDelay` | `1m` | Positive base retry delay. Attempt `N` waits `N * BaseRetryDelay`. |
| `WebhookWorker:WorkerId` | configuration / `WebhookWorkerOptions.WorkerId` | `notifyrail-webhook-<guid>` | Trimmed non-empty identity independent from the Delivery Worker. |
| `WebhookWorker:BatchSize` | configuration / `WebhookWorkerOptions.BatchSize` | `1` | Maximum events processed per poll. Events are claimed individually immediately before dispatch; `0` uses the default and negative values are invalid. |
| `WebhookWorker:PollInterval` | configuration / `WebhookWorkerOptions.PollInterval` | `500ms` | `00:00:00` uses the default; negative values are invalid. |
| `WebhookWorker:BaseRetryDelay` | configuration / `WebhookWorkerOptions.BaseRetryDelay` | `1m` | Exponential retry delay before jitter; it must not be less than the minimum delay. |
| `WebhookWorker:MinimumRetryDelay` | configuration / `WebhookWorkerOptions.MinimumRetryDelay` | `1s` | Positive lower safety bound for computed delays and valid `Retry-After` values. |
| `WebhookWorker:MaximumRetryDelay` | configuration / `WebhookWorkerOptions.MaximumRetryDelay` | `1h` | Upper safety bound not less than the base delay. |
| `WebhookWorker:JitterRatio` | configuration / `WebhookWorkerOptions.JitterRatio` | `0.2` | Random proportional offset in the inclusive range `0` to `1`; `0.2` gives a multiplier from `0.8` through `1.2`. |
| `WebhookWorker:RequestTimeout` | configuration / `WebhookWorkerOptions.RequestTimeout` | `100s` | Positive timeout applied to each outbound HTTP request. |
| `WebhookWorker:ClaimTimeout` | configuration / `WebhookWorkerOptions.ClaimTimeout` | `5m` | Positive lease duration after which an in-progress event is eligible for recovery; it must be greater than `RequestTimeout`. |
| `Webhooks:AllowLocalhostEndpoints` | configuration / `WebhookOptions.AllowLocalhostEndpoints` | `false` | Explicit development/test exception allowing HTTP or HTTPS `localhost` and loopback literal endpoints. |
| `MockProvider:Rules` | configuration / `MockProviderOptions.Rules` | empty | Recipient-specific mock outcome sequences. Unmatched recipients are accepted. |
| `MockProviderCallback:Secret` | configuration / `MockProviderCallbackOptions.Secret` | none | Required provider-specific HMAC secret for authenticating mock Provider Callbacks. It is separate from API Keys, Operator Credentials, and Webhook Secrets. |
| `MockProviderCallback:SignatureTolerance` | configuration / `MockProviderCallbackOptions.SignatureTolerance` | `00:05:00` | Maximum accepted clock difference in either direction for mock Provider Callback signatures. |
| `Otp:Secret` | configuration / `OtpOptions.Secret` | none | Required secret for mock OTP derivation and hashing. |
| `Otp:SenderTitle` | configuration / `OtpOptions.SenderTitle` | `NotifyRail` | Non-blank OTP sender title. |
| `Otp:Ttl` | configuration / `OtpOptions.Ttl` | `5m` | Positive OTP Challenge and Delivery lifetime. |
| `Otp:MaxAttempts` | configuration / `OtpOptions.MaxAttempts` | `5` | Positive incorrect-verification limit. |

## Provider Adapter Contract

Provider adapters implement:

```csharp
public interface IProviderSender
{
    string Name { get; }

    Task<ProviderResult> SendAsync(
        ProviderRequest request,
        CancellationToken cancellationToken);
}
```

`Name` is the stable provider identifier used when the worker must turn a
transient provider exception into a persisted attempt without receiving a
`ProviderResult`.

### Request Fields

| Field | Required | Description |
| --- | --- | --- |
| `IdempotencyKey` | yes | Unique key for one delivery attempt. `DeliveryQueue.ClaimDueAsync` formats it as `<delivery_id>-attempt-<attempt_number>`. |
| `Recipient` | yes | Recipient copied from the delivery. |
| `Channel` | yes | Message channel. Current intake accepts only `sms`. |
| `SenderTitle` | yes | Sender title copied from the message. |
| `Body` | yes | Message body copied from the message. |
| `AttemptNumber` | yes | One-based delivery attempt number used by configurable provider scenarios. |

### Result Fields

| Field | Required | Description |
| --- | --- | --- |
| `Outcome` | yes | One of `Accepted`, `RetryableFailure`, or `PermanentFailure`. Persisted values are `accepted`, `retryable_failure`, and `permanent_failure`. |
| `Provider` | yes for persisted attempts | Stable provider name, for example `mock`. |
| `ProviderMessageId` | no | Provider-side message identifier when available. |
| `ErrorCode` | no | Normalized provider error code for failed attempts. |
| `ErrorMessage` | no | Provider error detail for failed attempts. |

### Implemented Outcome Handling

| Outcome | Lifecycle behavior |
| --- | --- |
| `Accepted` | Records an attempt and moves the delivery to `sent`. |
| `RetryableFailure` | Records an attempt and moves the delivery to `retry_scheduled` when attempts remain. |
| `PermanentFailure` | Records an attempt and moves the delivery to `failed`. |

Retryable failures are capped at three attempts. When attempts remain,
`next_attempt_at` is set to
`attempted_at + attempt_number * DeliveryQueue:BaseRetryDelay`; a retryable
failure on the third attempt moves the delivery to `failed`.

Provider adapters must respect cancellation and return promptly when the
provided cancellation token is canceled.

## Mock Provider Behavior

`MockProvider` is the only production-wired provider adapter in the current
implementation.

| Input / condition | Behavior |
| --- | --- |
| Canceled token | Throws `OperationCanceledException`. |
| Blank `IdempotencyKey` after trimming | Throws `ArgumentException`. |
| Recipient has no configured rule | Returns `Accepted` with a deterministic provider message ID. |
| Configured outcome is `accepted` | Returns `Accepted` with a deterministic provider message ID. |
| Configured outcome is `retryable_failure` | Returns `RetryableFailure` with error code `mock_retryable_failure`. |
| Configured outcome is `permanent_failure` | Returns `PermanentFailure` with error code `mock_permanent_failure`. |

### Recipient Outcome Rules

Each rule matches one normalized recipient and defines the result for each
one-based attempt. When an attempt number is greater than the sequence length,
the final configured outcome is repeated. An empty rule list preserves the
default accept-all behavior.

```json
{
  "MockProvider": {
    "Rules": [
      {
        "Recipient": "+905552222222",
        "Outcomes": ["retryable_failure", "accepted"]
      },
      {
        "Recipient": "+905553333333",
        "Outcomes": ["permanent_failure"]
      }
    ]
  }
}
```

Supported outcome values are `accepted`, `retryable_failure`, and
`permanent_failure`. A configured rule must contain at least one outcome;
otherwise provider construction fails with `OptionsValidationException`.

The example makes `+905552222222` fail temporarily on its first attempt and
succeed on its second. `+905553333333` fails permanently on its first attempt.
Every other recipient is accepted.

The mock provider message ID is:

```text
mock_<sha256_hex(idempotency_key)>
```

The same idempotency key always produces the same provider message ID. Different
attempt numbers produce different keys and therefore different provider message
IDs.

## Mock Provider Callback Processing

`POST /provider-callbacks/mock` first passes the timestamp header, signature
header, and exact request body to the provider callback verifier boundary. The
mock implementation validates a versioned HMAC-SHA256 signature with the
separate `MockProviderCallback:Secret` and rejects timestamps outside
`MockProviderCallback:SignatureTolerance`. Only an authenticated body is parsed
and passed as a normalized provider message ID and terminal status to
`MockProviderCallbackHandler`. A future provider adapter can replace the
verifier without changing Delivery transition behavior.

The handler performs a conditional PostgreSQL update:

1. Match the unique `deliveries.provider_message_id`.
2. Update only when the current delivery status is `sent`.
3. Move the delivery to `delivered` or `failed` and set `updated_at` to the
   server callback-processing time.
4. Read and return the persisted delivery state.

The conditional update makes callback processing state-idempotent without a
separate callback event table. Duplicate callbacks do not update `updated_at`.
A callback received after `delivered`, `failed`, or `expired` returns the
existing state without changing it. For conflicting final callbacks, the first
terminal transition wins.

An unknown provider message ID returns `404 Not Found`. Callback processing
does not insert a delivery attempt because a provider callback finalizes a
previous send; it is not another provider send attempt.

## Worker Processing Flow

`DeliveryWorkerBackgroundService` repeats this loop until the host stops:

1. Open a DI scope.
2. Resolve `DeliveryWorker`.
3. Call `DeliveryWorker.ProcessBatchAsync(now, stoppingToken)`.
4. Wait `DeliveryWorker:PollInterval`, then poll again.

`DeliveryWorker.ProcessBatchAsync` performs one batch:

1. Call `DeliveryQueue.ClaimDueAsync(workerId, batchSize, now, token)`.
2. For each returned job, send the provider request returned by the queue
   module.
3. Record the provider result using
   `DeliveryQueue.RecordProviderResultAsync`.

`HttpRequestException` and `TimeoutException` from a provider send are recorded
as retryable failures with error code `provider_exception`; they do not escape
the batch. Host cancellation continues to propagate normally.

If another queue, provider, or provider-result recording error escapes a batch,
the hosted service logs it, waits for the configured poll interval, and starts
a new batch in a new dependency-injection scope. One unexpected batch failure
therefore does not terminate the hosted worker or the ASP.NET Core host.

`attempted_at` is captured after the provider call returns, immediately before
the provider result is recorded.

## Queue Claim Behavior

`DeliveryQueue.ClaimDueAsync` performs three persistence actions:

1. Recover stale `processing` claims whose `claimed_at` is older than the
   five-minute claim lease.
2. Expire due deliveries in `queued`, `retry_scheduled`, or `sent` when
   `expires_at <= claim_time`.
3. Select due `queued` or `retry_scheduled` deliveries with
   `FOR UPDATE SKIP LOCKED`, move them to `processing`, and return the provider
   request plus an opaque claim used for result recording.

### Stale Claim Recovery

The claim lease is currently fixed at five minutes in `DeliveryQueue`.

| Processing row condition | Recovered state | `next_attempt_at` |
| --- | --- | --- |
| `attempt_count = 0` | `queued` | `NULL` |
| `attempt_count > 0` | `retry_scheduled` | recovery time |

Recovery clears `claimed_at` and `claimed_by`.

## Provider Result Recording

`DeliveryQueue.RecordProviderResultAsync` atomically inserts one
`delivery_attempts` row and moves the currently claimed delivery to the matching
lifecycle state.

### Preconditions

- The claim must come from `DeliveryQueue.ClaimDueAsync`.
- The provider outcome must be `Accepted`, `RetryableFailure`, or
  `PermanentFailure`.
- Provider name must be non-empty after trimming.
- Attempted time must be non-zero.
- The delivery must still be in `processing`.
- The delivery must still be claimed by the same worker.
- The delivery `attempt_count` must equal the claim attempt number minus one.

If the delivery is no longer claimed by the worker or the attempt number is not
current, `RecordProviderResultAsync` throws `InvalidOperationException` with
`Delivery claim is stale.`.

### Postconditions

After any successful provider-result recording:

- one row exists in `delivery_attempts`
- `delivery_attempts.outcome` matches the provider result
- `deliveries.attempt_count` equals the recorded attempt number
- `deliveries.claimed_at` and `deliveries.claimed_by` are cleared
- `deliveries.updated_at = attempted_at`

Outcome-specific delivery transitions:

| Outcome | Delivery status | `next_attempt_at` | `provider_message_id` |
| --- | --- | --- | --- |
| `Accepted` | `sent` | `NULL` | Provider message ID when provided. |
| `RetryableFailure` with attempts remaining | `retry_scheduled` | `attempted_at + attempt_number * DeliveryQueue:BaseRetryDelay` | `NULL` |
| `RetryableFailure` on max attempt | `failed` | `NULL` | `NULL` |
| `PermanentFailure` | `failed` | `NULL` | `NULL` |

## Webhook Dispatch

Client-visible Delivery transitions create pending `delivery.sent`,
`delivery.delivered`, `delivery.failed`, or `delivery.expired` Webhook Events
inside the same transaction as the transition when the Message owner has an
active Webhook Endpoint. Retryable Delivery provider failures with attempts
remaining do not create events. This contract is shared by campaign,
transactional, and OTP Deliveries. `WebhookWorkerBackgroundService` polls the
persisted events independently from `DeliveryWorkerBackgroundService`.

`WebhookQueue.ClaimDueAsync` claims pending events, due `retry_scheduled`
events, and in-progress events whose configurable claim lease has expired
through one `FOR UPDATE SKIP LOCKED` statement. A retry is not claimable before
`next_attempt_at`. Locked stale events are skipped instead of blocking claims
for unrelated Deliveries. A candidate is skipped while a lower-sequence event
for the same Delivery has not reached a terminal dispatch state. Unrelated
Deliveries can still be claimed in the same batch or by another worker. The
claim transaction commits before any HTTP request is made. Each request is a
`POST` whose body is the exact JSON text persisted on the Webhook Event.
The claim lease must exceed the outbound request timeout so a live request
cannot become eligible for concurrent recovery.
Immediately before opening a connection, NotifyRail resolves the endpoint again
and requires every IPv4 or IPv6 answer to satisfy the same public-address policy
used during configuration. Mixed safe and unsafe answers fail closed. The socket
is opened directly to one of the validated answers, which prevents a second
implicit DNS lookup from introducing a rebinding window. Environment HTTP
proxies and automatic redirects are disabled. The development/test localhost
exception applies at connection time only when explicitly configured.

The worker claims one event immediately before each send, up to the configured
batch limit, so later batch items do not consume their claim lease while an
earlier endpoint is responding.

The outbound headers are:

| Header | Value |
| --- | --- |
| `X-NotifyRail-Event-Id` | Stable Webhook Event UUID. |
| `X-NotifyRail-Timestamp` | Unix timestamp in seconds. |
| `X-NotifyRail-Signature` | `v1=<lowercase hex HMAC-SHA256>`. |

The v1 signature input is the UTF-8 encoding of
`<timestamp>.<exact_body>`. The HMAC key is the owning API Client's unprotected
Webhook Secret. The timestamp is captured for each request immediately before
dispatch.

Dispatch outcomes are normalized as follows:

| Remote outcome | Webhook Attempt outcome | Webhook Event state |
| --- | --- | --- |
| HTTP 2xx | `succeeded` | `succeeded` |
| Network error, timeout, HTTP 408, HTTP 429, or HTTP 5xx | `retryable_failure` | `retry_scheduled` |
| Redirect or any other HTTP 3xx/4xx | `permanent_failure` | `failed` |
| Endpoint resolves to any prohibited address | `permanent_failure` | `failed` |

Attempt `N` uses `BaseRetryDelay * 2^(N-1)` and a jitter multiplier uniformly
bounded by `1 - JitterRatio` and `1 + JitterRatio`. The result is clamped to the
configured minimum and maximum delay. The clock and jitter source are injected,
so exact schedules can be reproduced in tests. A valid delta-seconds or HTTP-date
`Retry-After` value replaces the computed backoff and is clamped to the same
safety bounds; an invalid value falls back to exponential backoff.

Every request records one Webhook Attempt. Attempts retain status code, timing,
latency, normalized outcome, and error diagnostics bounded to 100 characters
for codes and 500 characters for messages. Response bodies are not persisted.
Network and timeout diagnostics are normalized rather than persisting raw
exception or endpoint details. Prohibited dispatch destinations record the
bounded code `unsafe_endpoint` and a fixed message without URL, hostname, query,
or resolved-address details.
Webhook dispatch state never changes Delivery status.

A timeout or other ambiguous response records a retryable Webhook Attempt. The
next dispatch reuses the same Webhook Event identifier and exact persisted JSON
payload, including its version, occurrence time, Delivery sequence, and data.
The receiver may therefore observe the logical event more than once and must
deduplicate by event identifier.

The canonical table, relationship, constraint, and index definitions are in
the [persistence model reference](persistence-model.md).

## Current Limits

- The 24-hour automatic retry deadline and Dead Webhook Event transition are
  delivered by the later dead-event slice; until then, transient outcomes keep
  scheduling retries.
