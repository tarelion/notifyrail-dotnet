# Delivery Processing Reference

## Purpose

This page defines the implemented background delivery-processing contract for
NotifyRail: how due deliveries are claimed, how provider adapters are called,
how the mock provider behaves, and how provider outcomes are recorded.

## Scope

- Runtime wiring: `src/NotifyRail.Api/Program.cs`
- Queue module: `src/NotifyRail.Api/Features/Deliveries/Queue`
- Worker module: `src/NotifyRail.Api/Features/Deliveries/Worker`
- Provider contract: `src/NotifyRail.Api/Features/Deliveries/Providers`
- Provider callback module:
  `src/NotifyRail.Api/Features/Deliveries/ProviderCallbacks/Mock`
- Delivery persistence: `src/NotifyRail.Api/Features/Deliveries/Persistence`
- EF Core migrations:
  `src/NotifyRail.Api/Infrastructure/Persistence/Migrations`

## Runtime Wiring

`Program.cs` starts one ASP.NET Core process containing both:

- the HTTP API server
- a hosted delivery worker

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
| `MockProvider:Rules` | configuration / `MockProviderOptions.Rules` | empty | Recipient-specific mock outcome sequences. Unmatched recipients are accepted. |

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
`next_attempt_at` is set to `attempted_at + attempt_number * 1 minute`; a
retryable failure on the third attempt moves the delivery to `failed`.

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

`POST /provider-callbacks/mock` passes a normalized provider message ID and
terminal status to `MockProviderCallbackHandler`.

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
| `RetryableFailure` with attempts remaining | `retry_scheduled` | `attempted_at + attempt_number * 1 minute` | `NULL` |
| `RetryableFailure` on max attempt | `failed` | `NULL` | `NULL` |
| `PermanentFailure` | `failed` | `NULL` | `NULL` |

The canonical table, relationship, constraint, and index definitions are in
the [persistence model reference](persistence-model.md).

## Current Limits

- The retry policy is not configurable.
- The stale claim lease is not configurable.
