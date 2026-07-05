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
| Valid request | Returns `ProviderOutcome.Accepted`, `Provider: "mock"`, and a deterministic provider message ID. |

The mock provider message ID is:

```text
mock_<sha256_hex(idempotency_key)>
```

The same idempotency key always produces the same provider message ID. Different
attempt numbers produce different keys and therefore different provider message
IDs.

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

- The production-wired mock provider accepts every valid send, so retryable and
  permanent transitions require tests or another provider adapter to exercise.
- Provider callbacks are not wired yet.
- The retry policy is not configurable.
- The stale claim lease is not configurable.
