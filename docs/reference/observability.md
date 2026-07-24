# Observability Reference

## Purpose

This page defines NotifyRail's implemented OpenTelemetry trace and structured
log contract for the asynchronous Message-to-webhook path.

## Scope

- Runtime registration: `src/NotifyRail.Api/Telemetry/TelemetryExtensions.cs`
- Activity names, attributes, trace-link helpers, and recipient masking:
  `src/NotifyRail.Api/Telemetry/NotifyRailTelemetry.cs`
- Test exporter coverage:
  `tests/NotifyRail.Api.Tests/Features/Telemetry/TelemetryIntegrationTests.cs`

Metrics and the local Aspire Dashboard are not part of this contract.

## Activation

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to an absolute OTLP/gRPC base URI. NotifyRail
then registers:

- the `NotifyRail.Api` custom `ActivitySource`
- ASP.NET Core request instrumentation
- `HttpClient` instrumentation
- OTLP trace and structured-log exporters
- the OpenTelemetry resource service name `notifyrail-api`

`ILogger` scopes and parsed structured state values are included. Formatted
messages are not exported as a separate field.

When `OTEL_EXPORTER_OTLP_ENDPOINT` is absent or blank, NotifyRail registers no
OpenTelemetry provider or exporter. Health endpoints, the Delivery Worker, and
the Webhook Worker keep their existing behavior. A non-blank value that is not
an absolute URI prevents startup with
`OTEL_EXPORTER_OTLP_ENDPOINT must be an absolute URI.`.

## Activities

| Activity | Kind | Parent or link contract |
| --- | --- | --- |
| `notifyrail.message.intake` | `Producer` | Child of the current HTTP request when present. Its W3C trace parent is persisted on each created Delivery. OTP send uses the same activity name. |
| `notifyrail.delivery.process` | `Consumer` | Starts a new trace and links to `deliveries.source_trace_parent` when valid. |
| `notifyrail.provider_callback.handle` | `Consumer` | Starts a new trace and links to the matched Delivery's persisted source context. |
| `notifyrail.webhook_event.create` | `Producer` | Child of the live Delivery-processing or Provider Callback activity. Its W3C context is persisted on the Webhook Event. |
| `notifyrail.webhook.dispatch` | `Consumer` | Starts a new trace and links to `webhook_events.source_trace_parent`. Every retry repeats this link and receives its own span. |
| `notifyrail.webhook.replay` | `Producer` | Starts a new trace linked to the Webhook Event's current source context. A successful replay replaces that context so the next dispatch links to the replay. |

Rows created before the correlation migrations can have a null source trace
parent. Their worker activities still carry stable identifiers but contain no
synthetic or guessed trace link.

## Attributes

| Attribute | Value |
| --- | --- |
| `notifyrail.api_client.id` | API Client UUID. |
| `notifyrail.message.id` | Message UUID. |
| `notifyrail.delivery.id` | Delivery UUID. |
| `notifyrail.webhook_event.id` | Stable Webhook Event UUID. |
| `notifyrail.webhook_attempt.id` | Persisted Webhook Attempt UUID after result recording. |
| `notifyrail.delivery.count` | Number of Deliveries created by intake. |
| `notifyrail.delivery_attempt.number` | One-based provider attempt number. |
| `notifyrail.webhook_attempt.number` | One-based Webhook Attempt number. |
| `notifyrail.webhook_event.type` | Client-visible event type. |
| `notifyrail.webhook_event.dispatch_status` | Resulting event state: `retry_scheduled`, `dead`, or `succeeded`. |
| `notifyrail.outcome` | Bounded domain outcome or transition result. |
| `notifyrail.recipient.masked` | Trimmed recipient with only the first two and last two characters visible; recipients of four characters or fewer become `***`. |

Identifiers are high-cardinality correlation attributes. They belong on traces
and logs, not on metric dimensions.

## Structured Logs

Successful state changes emit structured information-level logs at these
boundaries:

- Message and OTP Message acceptance
- Delivery provider-result recording
- Provider Callback application
- Webhook Event creation
- Webhook Attempt result recording
- Dead Webhook Event replay

Each record includes the identifiers relevant at that boundary. Webhook
Attempt records include the API Client, Message, Delivery, Webhook Event, and
Webhook Attempt identifiers together. Recipient-bearing records use only
`notifyrail.recipient.masked`.

## Privacy Invariants

Telemetry must never contain:

- a full recipient
- Message bodies
- OTP Codes, including Debug OTP Codes
- API Keys
- Operator Credentials
- plaintext or protected Webhook Secrets
- callback or webhook signatures
- remote response bodies

ASP.NET Core and `HttpClient` instrumentation use their default header and body
capture behavior; NotifyRail does not add request bodies, response bodies, or
authorization/signature headers as attributes. The Webhook Dispatcher does not
read remote response bodies.

## Verification

`TelemetryIntegrationTests` uses the OpenTelemetry in-memory trace and log
exporters. It verifies activity names, stable identifiers, persisted
`ActivityLink` relationships, retry/death/replay correlation, structured log
attributes, recipient masking, and the privacy invariants above. Tests assert
exported telemetry rather than a dashboard or collector UI.
