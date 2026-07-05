# HTTP API Reference

## Purpose

This page defines the HTTP routes currently registered by NotifyRail. Planned
routes belong in the [PRD](../prd-notifyrail.md) until they are implemented.

## Scope

- Runtime wiring: `src/NotifyRail.Api/Program.cs`
- Health endpoints: `src/NotifyRail.Api/Features/Health`
- Message endpoint: `src/NotifyRail.Api/Features/Messages/CreateMessage`
- Message intake rules: `MessageIntake` and `CreateMessageRequestNormalizer`
- Message delivery endpoint:
  `src/NotifyRail.Api/Features/Messages/GetMessageDeliveries`
- Message report endpoint:
  `src/NotifyRail.Api/Features/Messages/GetMessageReport`
- Mock provider callback endpoint:
  `src/NotifyRail.Api/Features/Deliveries/ProviderCallbacks/Mock`
- OTP endpoints: `src/NotifyRail.Api/Features/Otp/SendOtp` and
  `src/NotifyRail.Api/Features/Otp/VerifyOtp`

Responses explicitly produced by the registered handlers use JSON payloads.
Unhandled failures are delegated to ASP.NET Core and do not currently have an
application-level JSON contract.

## `GET /healthz`

Reports process liveness. It does not inspect PostgreSQL or schema state.

### Success Response

- Status: `200 OK`

```json
{"status":"ok"}
```

## `GET /readyz`

Checks whether PostgreSQL responds to `SELECT 1` within the configured
readiness check. It does not verify that migrations are current.

### Responses

| Status | Body | Condition |
| --- | --- | --- |
| `200 OK` | `{"status":"ready"}` | PostgreSQL ping succeeds. |
| `503 Service Unavailable` | `{"status":"unavailable"}` | PostgreSQL ping fails or times out. |

## `POST /messages`

Atomically creates one message and one delivery for each recipient.

### Request Body

| Field | JSON type | Required | Contract |
| --- | --- | --- | --- |
| `type` | string | yes | One of `otp`, `transactional`, or `campaign`. |
| `channel` | string | yes | Must be `sms`. |
| `sender_title` | string | yes | Trimmed before storage; must not be empty. |
| `body` | string | yes | Must contain at least one non-whitespace character. |
| `recipients` | array of strings | yes | Must contain at least one non-empty, unique recipient. Values are trimmed before storage. |
| `idempotency_key` | string | yes | Trimmed before storage; globally unique for the MVP. |
| `scheduled_at` | RFC 3339 timestamp or `null` | no | Normalized to UTC before storage. A due-delivery claim will not select it before this instant. |
| `report_label` | string or `null` | no | Trimmed before storage. |
| `encoding` | string or `null` | no | When present, one of `latin`, `turkish`, or `unicode`. |

Recipient format is not otherwise validated in the current implementation. The
request body must be at most 1 MiB, must contain exactly one JSON object, and
must not include unknown fields.

### Success Response

- Status: `202 Accepted`
- Header: `Location: /messages/{message_id}`

```json
{
  "message_id": "177b08d9-1ae3-4590-b7c6-c01c23776c8f",
  "delivery_count": 2,
  "created_at": "2026-06-30T12:00:00Z"
}
```

The message and all deliveries are committed in one database transaction. A
partial set of deliveries must not be persisted.

### Idempotency

`idempotency_key` is globally unique because the MVP has no client or tenant
identity.

- Repeating the same normalized request with the same key returns
  `202 Accepted` and the original receipt; it creates no new rows.
- Recipient order does not affect replay equality.
- Reusing the key with different message content, options, or recipients
  returns `409 Conflict`.

If client identity is introduced later, both the database uniqueness constraint
and this contract must change together before idempotency becomes client-scoped.

### Error Responses

Validation and idempotency errors use this shape:

```json
{"error":"description"}
```

| Status | Body contract | Condition |
| --- | --- | --- |
| `400 Bad Request` | `{"error":"description"}` | Invalid JSON body or invalid normalized input. |
| `409 Conflict` | `{"error":"description"}` | The idempotency key already belongs to a different normalized request. |
| `500 Internal Server Error` | No application-level contract. | An unexpected internal or persistence failure escaped the handler. ASP.NET Core produces the response. |

### Example Request

```sh
curl --request POST http://localhost:5012/messages \
  --header 'Content-Type: application/json' \
  --data '{
    "type": "transactional",
    "channel": "sms",
    "sender_title": "NotifyRail",
    "body": "Your order is ready.",
    "recipients": ["+905551111111", "+905552222222"],
    "idempotency_key": "order-42-ready"
  }'
```

## `GET /messages/{message_id}/deliveries`

Returns every recipient delivery for a message together with its provider
attempt history. `message_id` must be a UUID.

Deliveries are ordered by `created_at` and then `delivery_id`. Attempts within
each delivery are ordered by `attempt_number`.

### Success Response

- Status: `200 OK`

```json
{
  "message_id": "177b08d9-1ae3-4590-b7c6-c01c23776c8f",
  "deliveries": [
    {
      "delivery_id": "0241b3df-32ce-424f-a6a7-32baeb929bcb",
      "recipient": "+905551111111",
      "status": "sent",
      "attempt_count": 1,
      "next_attempt_at": null,
      "provider_message_id": "mock_e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "expires_at": null,
      "created_at": "2026-06-30T12:00:00Z",
      "updated_at": "2026-06-30T12:00:01Z",
      "attempts": [
        {
          "attempt_number": 1,
          "provider": "mock",
          "outcome": "accepted",
          "provider_message_id": "mock_e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
          "error_code": null,
          "error_message": null,
          "attempted_at": "2026-06-30T12:00:01Z"
        }
      ]
    }
  ]
}
```

### Delivery Fields

| Field | JSON type | Nullable | Contract |
| --- | --- | --- | --- |
| `delivery_id` | string UUID | no | Identifies the recipient delivery. |
| `recipient` | string | no | Normalized recipient stored at message creation. |
| `status` | string | no | Current state from the delivery lifecycle. |
| `attempt_count` | integer | no | Number of recorded provider attempts. |
| `next_attempt_at` | RFC 3339 timestamp | yes | Present only when a retry is scheduled. |
| `provider_message_id` | string | yes | Provider identifier recorded for an accepted send. |
| `expires_at` | RFC 3339 timestamp | yes | Delivery expiry instant when configured. |
| `created_at` | RFC 3339 timestamp | no | Delivery creation instant. |
| `updated_at` | RFC 3339 timestamp | no | Instant of the latest delivery state change. |
| `attempts` | array | no | Provider attempts in ascending attempt-number order. |

### Attempt Fields

| Field | JSON type | Nullable | Contract |
| --- | --- | --- | --- |
| `attempt_number` | integer | no | One-based attempt sequence for the delivery. |
| `provider` | string | no | Stable provider name. |
| `outcome` | string | no | `accepted`, `retryable_failure`, or `permanent_failure`. |
| `provider_message_id` | string | yes | Provider identifier returned by that attempt. |
| `error_code` | string | yes | Normalized provider error code. |
| `error_message` | string | yes | Provider error detail. |
| `attempted_at` | RFC 3339 timestamp | no | Instant the provider result was recorded. |

### Error Response

If no message has the requested UUID:

- Status: `404 Not Found`

```json
{"error":"message not found"}
```

## `POST /otp/send`

Creates one OTP Challenge, one `otp` Message, and one recipient Delivery in a
single transaction.

### Request Body

| Field | JSON type | Required | Contract |
| --- | --- | --- | --- |
| `recipient` | string | yes | Trimmed non-blank SMS recipient. |
| `idempotency_key` | string | yes | Trimmed globally unique Message idempotency key. |

```json
{
  "recipient": "+905551111111",
  "idempotency_key": "login-42"
}
```

### Success Response

- Status: `202 Accepted`

```json
{
  "otp_id": "ffeff488-5c94-45d1-a972-8d38d51eb135",
  "message_id": "177b08d9-1ae3-4590-b7c6-c01c23776c8f",
  "expires_at": "2026-07-05T12:05:00Z",
  "debug_code": "482193"
}
```

`debug_code` is a sensitive, simulation-only field. Its plaintext value is not
stored in PostgreSQL or in the Message body.

Repeating the same normalized request with the same idempotency key returns the
original response, including the same `debug_code`. Reusing the key with a
different recipient returns `409 Conflict`.

### Error Responses

| Status | Body | Condition |
| --- | --- | --- |
| `400 Bad Request` | `{"error":"recipient is required"}` | Recipient is null, empty, or whitespace. |
| `400 Bad Request` | `{"error":"idempotency_key is required"}` | Idempotency key is null, empty, or whitespace. |
| `409 Conflict` | `{"error":"idempotency key is already used with different content"}` | Key belongs to another normalized request. |

## `POST /otp/verify`

Verifies one active OTP Challenge using its six-digit code.

### Request Body

```json
{
  "otp_id": "ffeff488-5c94-45d1-a972-8d38d51eb135",
  "code": "482193"
}
```

`otp_id` must be a non-empty UUID. `code` must contain exactly six ASCII digits.

### Success Response

- Status: `200 OK`

```json
{
  "otp_id": "ffeff488-5c94-45d1-a972-8d38d51eb135",
  "status": "verified",
  "verified_at": "2026-07-05T12:01:00Z"
}
```

### Error Responses

| Status | Body | Condition |
| --- | --- | --- |
| `400 Bad Request` | `{"error":"otp_id is required"}` | OTP ID is absent or empty. |
| `400 Bad Request` | `{"error":"code must contain exactly 6 digits"}` | Code shape is invalid. |
| `400 Bad Request` | `{"error":"invalid OTP code","attempts_remaining":4}` | Code is incorrect and attempts remain. |
| `404 Not Found` | `{"error":"OTP challenge not found"}` | OTP ID is unknown. |
| `409 Conflict` | `{"error":"OTP challenge is already verified"}` | Challenge was already used successfully. |
| `410 Gone` | `{"error":"OTP challenge has expired"}` | Current time is at or after expiry. |
| `429 Too Many Requests` | `{"error":"OTP attempt limit exceeded","attempts_remaining":0}` | Incorrect-attempt limit is reached. |

Verification is serialized with a PostgreSQL row lock. Concurrent correct
requests therefore produce one `200 OK`; later requests observe the verified
state and return `409 Conflict`.

## `POST /provider-callbacks/mock`

Applies a final mock-provider status to the delivery identified by
`provider_message_id`.

### Request Body

| Field | JSON type | Required | Contract |
| --- | --- | --- | --- |
| `provider_message_id` | string | yes | Non-blank provider message ID returned by an accepted mock send. Leading and trailing whitespace is removed. |
| `status` | string | yes | One of `delivered` or `failed`. Leading and trailing whitespace is removed. |

```json
{
  "provider_message_id": "mock_e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  "status": "delivered"
}
```

### Success Response

- Status: `200 OK`

```json
{
  "delivery_id": "0241b3df-32ce-424f-a6a7-32baeb929bcb",
  "provider_message_id": "mock_e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  "status": "delivered",
  "updated_at": "2026-06-30T12:05:00Z"
}
```

The response reports the persisted delivery state. A callback updates only a
delivery currently in `sent`:

| Persisted status | Callback status | Result |
| --- | --- | --- |
| `sent` | `delivered` | Delivery moves to `delivered`; `updated_at` changes. |
| `sent` | `failed` | Delivery moves to `failed`; `updated_at` changes. |
| `delivered`, `failed`, or `expired` | either value | No state or timestamp change; response returns the existing terminal state. |

Duplicate callbacks therefore return the same delivery state and timestamp.
When conflicting callbacks race, the first terminal transition wins and later
callbacks are no-ops.

### Error Responses

Errors explicitly produced by the endpoint use this shape:

```json
{"error":"description"}
```

| Status | Body | Condition |
| --- | --- | --- |
| `400 Bad Request` | `{"error":"provider_message_id is required"}` | Provider message ID is null, empty, or whitespace. |
| `400 Bad Request` | `{"error":"status must be one of: delivered, failed"}` | Status is absent or unsupported. |
| `404 Not Found` | `{"error":"provider message not found"}` | No delivery has the provider message ID. |

Malformed JSON and missing request bodies are rejected by ASP.NET Core and do
not currently have an application-level JSON error contract.

## `GET /messages/{message_id}/report`

Returns aggregate delivery counts for a message. `message_id` must be a UUID.
The endpoint calculates counts in PostgreSQL and does not return individual
delivery or attempt records.

### Success Response

- Status: `200 OK`

```json
{
  "message_id": "177b08d9-1ae3-4590-b7c6-c01c23776c8f",
  "total": 100,
  "queued": 5,
  "processing": 2,
  "sent": 20,
  "delivered": 65,
  "retry_scheduled": 3,
  "failed": 4,
  "expired": 1
}
```

All status fields are present even when their count is zero. `total` equals the
sum of `queued`, `processing`, `sent`, `delivered`, `retry_scheduled`, `failed`,
and `expired`.

### Response Fields

| Field | JSON type | Contract |
| --- | --- | --- |
| `message_id` | string UUID | Identifies the reported message. |
| `total` | integer | Number of all recipient deliveries. |
| `queued` | integer | Deliveries waiting to be claimed. |
| `processing` | integer | Deliveries currently claimed by a worker. |
| `sent` | integer | Deliveries accepted by the provider without a final callback. |
| `delivered` | integer | Deliveries confirmed as delivered. |
| `retry_scheduled` | integer | Deliveries waiting for their next attempt. |
| `failed` | integer | Deliveries in terminal failure. |
| `expired` | integer | Deliveries in terminal expiry. |

### Error Response

If no message has the requested UUID:

- Status: `404 Not Found`

```json
{"error":"message not found"}
```
