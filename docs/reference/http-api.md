# HTTP API Reference

## Purpose

This page defines the HTTP routes currently registered by NotifyRail. Planned
routes belong in the [PRD](../prd-notifyrail.md) until they are implemented.

## Scope

- Runtime wiring: `src/NotifyRail.Api/Program.cs`
- Health endpoints: `src/NotifyRail.Api/Features/Health`
- Message endpoint: `src/NotifyRail.Api/Features/Messages/CreateMessage`
- Message intake rules: `MessageIntake` and `CreateMessageRequestNormalizer`

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
| `scheduled_at` | RFC 3339 timestamp or `null` | no | Stored with the message. A due-delivery claim will not select it before this time. |
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
