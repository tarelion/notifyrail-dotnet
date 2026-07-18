# HTTP API Reference

## Purpose

This page defines the HTTP routes currently registered by NotifyRail. Planned
routes belong in the [PRD](../prd-notifyrail.md) until they are implemented.

## Scope

- Runtime wiring: `src/NotifyRail.Api/Program.cs`
- Health endpoints: `src/NotifyRail.Api/Features/Health`
- Management API Client endpoints: `src/NotifyRail.Api/Features/ApiClients`
- Management Webhook Endpoint operations:
  `src/NotifyRail.Api/Features/Webhooks/RegisterWebhookEndpoint`,
  `src/NotifyRail.Api/Features/Webhooks/InspectWebhookEndpoint`, and
  `src/NotifyRail.Api/Features/Webhooks/DisableWebhookEndpoint`
- Message endpoint: `src/NotifyRail.Api/Features/Messages/CreateMessage`
- Message intake rules: `MessageIntake` and `CreateMessageRequestNormalizer`
- Message summary endpoint:
  `src/NotifyRail.Api/Features/Messages/GetMessage`
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

## Authentication

NotifyRail registers separate replaceable authentication schemes and policies:

| Identity | Authorization header | Policy | Current routes |
| --- | --- | --- | --- |
| Operator | `Authorization: Operator <credential>` | `Operator` | `/management/*` |
| API Client | `Authorization: ApiKey nrk_<lookup_id>_<secret>` | `ApiClient` | `GET /api-client`, all Message routes, `POST /otp/send`, and `POST /otp/verify`. |

The Operator credential is configured at
`Authentication:Operator:Credential` and must be non-blank at application
startup. API Client credentials cannot satisfy the Operator policy. OTP routes
assign and authorize ownership through the authenticated API Client. Health
routes remain public, and Provider Callback authentication remains
provider-specific.

## `GET /api-client`

Returns the API Client represented by the supplied API Key and provides an HTTP
boundary for validating a credential during rotation.

Success response:

- Status: `200 OK`

```json
{
  "api_client_id": "177b08d9-1ae3-4590-b7c6-c01c23776c8f",
  "name": "Shipping Service"
}
```

Successful authentication updates the credential's `last_used_at`. Missing,
malformed, unknown, expired, revoked, or disabled-client credentials return
`401 Unauthorized` and do not update it. An Operator credential cannot satisfy
the `ApiClient` policy.

## `POST /management/api-clients`

Creates an enabled API Client and its initial API Key atomically. Requires the
`Operator` policy.

Request:

```json
{"name":"Shipping Service"}
```

Success response:

- Status: `201 Created`
- Location: `/management/api-clients/{api_client_id}`

```json
{
  "api_client_id": "177b08d9-1ae3-4590-b7c6-c01c23776c8f",
  "name": "Shipping Service",
  "api_key_id": "0241b3df-32ce-424f-a6a7-32baeb929bcb",
  "api_key": "nrk_<lookup_id>_<secret>",
  "display_prefix": "nrk_<prefix>",
  "created_at": "2026-07-17T12:00:00Z"
}
```

`api_key` is returned only by this creation operation. `api_key_id` identifies
the credential independently, and `display_prefix` is safe to show when the
secret is no longer available. NotifyRail persists its lookup identifier,
SHA-256 verification value, and display-safe prefix, not the plaintext
credential.

| Status | Condition |
| --- | --- |
| `400 Bad Request` | `name` is blank. |
| `401 Unauthorized` | The Operator credential is missing or invalid. |

## `POST /management/api-clients/{api_client_id}/disable`

Disables an API Client and records `disabled_at`. Repeating the operation is
idempotent. Requires the `Operator` policy.

| Status | Condition |
| --- | --- |
| `204 No Content` | The API Client exists and is disabled. |
| `401 Unauthorized` | The Operator credential is missing or invalid. |
| `404 Not Found` | The API Client does not exist. |

## `POST /management/api-clients/{api_client_id}/api-keys`

Creates an additional API Key for an API Client. Requires the `Operator`
policy. More than one non-expired, non-revoked key may be active at once so a
client can rotate credentials without downtime.

Request:

```json
{"expires_at":"2026-08-17T12:00:00Z"}
```

`expires_at` is optional and may be `null`.

Success response:

- Status: `201 Created`
- Location:
  `/management/api-clients/{api_client_id}/api-keys/{api_key_id}`

```json
{
  "api_key_id": "0241b3df-32ce-424f-a6a7-32baeb929bcb",
  "api_key": "nrk_<lookup_id>_<secret>",
  "display_prefix": "nrk_<prefix>",
  "created_at": "2026-07-17T12:00:00Z",
  "expires_at": "2026-08-17T12:00:00Z"
}
```

The full `api_key` appears only in this response. Later reads expose the stable
`api_key_id` and display-safe metadata.

| Status | Condition |
| --- | --- |
| `401 Unauthorized` | The Operator credential is missing or invalid, including when an API Key is supplied instead. |
| `404 Not Found` | The API Client does not exist. |

## `GET /management/api-clients/{api_client_id}/api-keys`

Lists API Key metadata for an API Client. Requires the `Operator` policy and
never returns a full credential or verification hash.

```json
{
  "api_keys": [
    {
      "api_key_id": "0241b3df-32ce-424f-a6a7-32baeb929bcb",
      "display_prefix": "nrk_<prefix>",
      "created_at": "2026-07-17T12:00:00Z",
      "last_used_at": "2026-07-17T12:05:00Z",
      "expires_at": "2026-08-17T12:00:00Z",
      "revoked_at": null
    }
  ]
}
```

Keys are ordered by `created_at` and then `api_key_id`.

| Status | Condition |
| --- | --- |
| `200 OK` | The API Client exists. |
| `401 Unauthorized` | The Operator credential is missing or invalid. |
| `404 Not Found` | The API Client does not exist. |

## `POST /management/api-clients/{api_client_id}/api-keys/{api_key_id}/revoke`

Permanently revokes one API Key without affecting other keys for the same API
Client. Repeating the operation is idempotent and preserves the original
`revoked_at` value. Requires the `Operator` policy.

| Status | Condition |
| --- | --- |
| `204 No Content` | The API Key exists under the API Client and is revoked. |
| `401 Unauthorized` | The Operator credential is missing or invalid. |
| `404 Not Found` | The API Client/API Key combination does not exist. |

An API Key fails authentication when its expiry time has been reached or it has
been revoked. Failed authentication does not update `last_used_at`.

## `PUT /management/api-clients/{api_client_id}/webhook-endpoint`

Registers the first active Webhook Endpoint or explicitly replaces the active
endpoint with a new resource. Requires the `Operator` policy. An API Client may
exist without calling this operation and continues to use polling when it has
no active endpoint.

Request:

```json
{"url":"https://client.example.com/notifyrail-events"}
```

The first registration returns `201 Created` and issues the API Client's
initial Webhook Secret:

```json
{
  "webhook_endpoint_id": "6c932de0-8a5c-4be8-b41e-c9ca33554bea",
  "api_client_id": "177b08d9-1ae3-4590-b7c6-c01c23776c8f",
  "url": "https://client.example.com/notifyrail-events",
  "is_enabled": true,
  "created_at": "2026-07-18T12:00:00Z",
  "updated_at": "2026-07-18T12:00:00Z",
  "webhook_secret": "nrs_<secret>"
}
```

`webhook_secret` is high entropy and appears only when the first secret is
created. NotifyRail protects it before persistence; later inspection and
replacement responses never return it. Replacing an active endpoint returns
`200 OK`, disables the previous Webhook Endpoint, creates a new active resource,
and omits `webhook_secret` from the response.

Repeating `PUT` with the active endpoint's normalized URL is idempotent: it
returns `200 OK`, preserves the existing resource identifier and timestamps,
and does not return the Webhook Secret again.

| Status | Condition |
| --- | --- |
| `201 Created` | The API Client's first endpoint and initial secret are created. |
| `200 OK` | A new endpoint replaces a previous active or disabled endpoint; the existing secret is not returned. |
| `400 Bad Request` | `url` is not an absolute HTTP(S) URL, contains user information or a fragment, uses public HTTP, or violates the localhost policy. |
| `401 Unauthorized` | The Operator credential is missing or invalid. |
| `404 Not Found` | The API Client does not exist. |

Public endpoint URLs require HTTPS. Localhost names and loopback IP addresses,
including equivalent trailing-dot and IPv4-mapped IPv6 forms, are rejected unless
`Webhooks:AllowLocalhostEndpoints` is explicitly `true`; with that setting,
HTTP or HTTPS loopback URLs are accepted for development and tests. Complete
address and DNS validation at dispatch time is not part of this configuration
operation yet.

Registering or replacing an endpoint does not create Webhook Events for
historical Delivery transitions.

## `GET /management/api-clients/{api_client_id}/webhook-endpoint`

Returns the most recently configured Webhook Endpoint, including its enabled
state and `disabled_at` metadata. Requires the `Operator` policy and never
returns `webhook_secret` or protected secret material.

```json
{
  "webhook_endpoint_id": "6c932de0-8a5c-4be8-b41e-c9ca33554bea",
  "api_client_id": "177b08d9-1ae3-4590-b7c6-c01c23776c8f",
  "url": "https://client.example.com/notifyrail-events",
  "is_enabled": false,
  "created_at": "2026-07-18T12:00:00Z",
  "updated_at": "2026-07-18T13:00:00Z",
  "disabled_at": "2026-07-18T13:00:00Z"
}
```

| Status | Condition |
| --- | --- |
| `200 OK` | The API Client has configured an endpoint, whether enabled or disabled. |
| `401 Unauthorized` | The Operator credential is missing or invalid. |
| `404 Not Found` | The API Client does not exist or has never configured an endpoint. |

## `POST /management/api-clients/{api_client_id}/webhook-endpoint/disable`

Disables the active Webhook Endpoint without disabling its API Client.
Repeating the operation is idempotent, including when the API Client has no
active endpoint. The API Client remains able to authenticate and poll existing
read endpoints.

| Status | Condition |
| --- | --- |
| `204 No Content` | The API Client exists; its active endpoint, if any, is disabled. |
| `401 Unauthorized` | The Operator credential is missing or invalid. |
| `404 Not Found` | The API Client does not exist. |

## Webhook Secret protection configuration

`Webhooks:DataProtectionKeyRingPath` selects the filesystem directory used by
.NET Data Protection for Webhook Secret encryption keys. The key ring is not
stored in PostgreSQL. Local Compose services mount the shared,
persistent `data_protection_keys` volume at
`/var/lib/notifyrail/data-protection-keys`.

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
Requires the `ApiClient` policy, and the new Message belongs to the
authenticated API Client.

### Request Body

| Field | JSON type | Required | Contract |
| --- | --- | --- | --- |
| `type` | string | yes | One of `otp`, `transactional`, or `campaign`. |
| `channel` | string | yes | Must be `sms`. |
| `sender_title` | string | yes | Trimmed before storage; must not be empty. |
| `body` | string | yes | Must contain at least one non-whitespace character. |
| `recipients` | array of strings | yes | Must contain at least one non-empty, unique recipient. Values are trimmed before storage. |
| `idempotency_key` | string | yes | Trimmed before storage; unique within the authenticated API Client. |
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

Idempotency is scoped to the authenticated API Client. Different API Clients
may use the same key without conflicting.

- Repeating the same normalized request with the same key returns
  `202 Accepted` and the original receipt; it creates no new rows.
- Recipient order does not affect replay equality.
- Reusing the key with different message content, options, or recipients
  returns `409 Conflict`.

The database enforces the same boundary with the unique constraint over
`(api_client_id, idempotency_key)`.

### Error Responses

Validation and idempotency errors use this shape:

```json
{"error":"description"}
```

| Status | Body contract | Condition |
| --- | --- | --- |
| `401 Unauthorized` | No application-level body contract. | The API Key is missing, malformed, unknown, expired, or revoked. |
| `400 Bad Request` | `{"error":"description"}` | Invalid JSON body or invalid normalized input. |
| `409 Conflict` | `{"error":"description"}` | The idempotency key already belongs to a different normalized request. |
| `500 Internal Server Error` | No application-level contract. | An unexpected internal or persistence failure escaped the handler. ASP.NET Core produces the response. |

### Example Request

```sh
curl --request POST http://localhost:5012/messages \
  --header 'Authorization: ApiKey nrk_<lookup_id>_<secret>' \
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

## `GET /messages/{message_id}`

Returns message metadata together with aggregate delivery status counts.
`message_id` must be a UUID. The endpoint does not return individual
recipient deliveries or provider attempt history; use
`GET /messages/{message_id}/deliveries` for that detail.
Requires the `ApiClient` policy and returns only a Message owned by the
authenticated API Client.

### Success Response

- Status: `200 OK`

```json
{
  "message_id": "177b08d9-1ae3-4590-b7c6-c01c23776c8f",
  "type": "transactional",
  "channel": "sms",
  "sender_title": "NotifyRail",
  "body": "Your order is ready.",
  "scheduled_at": null,
  "report_label": "orders",
  "encoding": "unicode",
  "created_at": "2026-06-30T12:00:00Z",
  "updated_at": "2026-06-30T12:00:00Z",
  "deliveries": {
    "total": 2,
    "queued": 1,
    "processing": 0,
    "sent": 1,
    "delivered": 0,
    "retry_scheduled": 0,
    "failed": 0,
    "expired": 0
  }
}
```

### Message Fields

| Field | JSON type | Nullable | Contract |
| --- | --- | --- | --- |
| `message_id` | string UUID | no | Identifies the Message. |
| `type` | string | no | One of `otp`, `transactional`, or `campaign`. |
| `channel` | string | no | Current MVP value is `sms`. |
| `sender_title` | string | no | Normalized sender label stored at creation. |
| `body` | string | no | Message body stored at creation. |
| `scheduled_at` | RFC 3339 timestamp | yes | Earliest delivery claim time when scheduled. |
| `report_label` | string | yes | Optional client-provided reporting label. |
| `encoding` | string | yes | Optional encoding value stored at creation. |
| `created_at` | RFC 3339 timestamp | no | Message creation instant. |
| `updated_at` | RFC 3339 timestamp | no | Latest message metadata update instant. |
| `deliveries` | object | no | Aggregate status counts for recipient deliveries. |

### Delivery Summary Fields

| Field | JSON type | Contract |
| --- | --- | --- |
| `total` | integer | Number of all recipient deliveries. |
| `queued` | integer | Deliveries waiting to be claimed. |
| `processing` | integer | Deliveries currently claimed by a worker. |
| `sent` | integer | Deliveries accepted by the provider without a final callback. |
| `delivered` | integer | Deliveries confirmed as delivered. |
| `retry_scheduled` | integer | Deliveries waiting for their next attempt. |
| `failed` | integer | Deliveries in terminal failure. |
| `expired` | integer | Deliveries in terminal expiry. |

All delivery summary fields are present even when their count is zero. `total`
equals the sum of `queued`, `processing`, `sent`, `delivered`,
`retry_scheduled`, `failed`, and `expired`.

### Error Response

Missing or invalid API Keys return `401 Unauthorized`. If no owned Message has
the requested UUID, including when another API Client owns it:

- Status: `404 Not Found`

```json
{"error":"message not found"}
```

## `GET /messages/{message_id}/deliveries`

Returns every recipient delivery for a message together with its provider
attempt history. `message_id` must be a UUID. Requires the `ApiClient` policy
and returns Deliveries only when the authenticated API Client owns the Message.

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

Missing or invalid API Keys return `401 Unauthorized`. If no owned Message has
the requested UUID, including when another API Client owns it:

- Status: `404 Not Found`

```json
{"error":"message not found"}
```

## `POST /otp/send`

Creates one OTP Challenge, one `otp` Message, and one recipient Delivery in a
single transaction. The Message belongs to the authenticated API Client, and
the linked OTP Challenge inherits that ownership.

### Request Body

| Field | JSON type | Required | Contract |
| --- | --- | --- | --- |
| `recipient` | string | yes | Trimmed non-blank SMS recipient. |
| `idempotency_key` | string | yes | Trimmed Message idempotency key, unique within the authenticated API Client. |

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
different recipient under the same API Client returns `409 Conflict`. Different
API Clients may reuse the same key independently.

### Error Responses

| Status | Body | Condition |
| --- | --- | --- |
| `401 Unauthorized` | empty | The API Key is missing or invalid. |
| `400 Bad Request` | `{"error":"recipient is required"}` | Recipient is null, empty, or whitespace. |
| `400 Bad Request` | `{"error":"idempotency_key is required"}` | Idempotency key is null, empty, or whitespace. |
| `409 Conflict` | `{"error":"idempotency key is already used with different content"}` | Key belongs to another normalized request. |

## `POST /otp/verify`

Verifies one active OTP Challenge owned by the authenticated API Client using
its six-digit code.

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
| `401 Unauthorized` | empty | The API Key is missing or invalid. |
| `400 Bad Request` | `{"error":"otp_id is required"}` | OTP ID is absent or empty. |
| `400 Bad Request` | `{"error":"code must contain exactly 6 digits"}` | Code shape is invalid. |
| `400 Bad Request` | `{"error":"invalid OTP code","attempts_remaining":4}` | Code is incorrect and attempts remain. |
| `404 Not Found` | `{"error":"OTP challenge not found"}` | OTP ID is unknown or belongs to another API Client. |
| `409 Conflict` | `{"error":"OTP challenge is already verified"}` | Challenge was already used successfully. |
| `410 Gone` | `{"error":"OTP challenge has expired"}` | Current time is at or after expiry. |
| `429 Too Many Requests` | `{"error":"OTP attempt limit exceeded","attempts_remaining":0}` | Incorrect-attempt limit is reached. |

Verification is serialized with a PostgreSQL row lock. Concurrent correct
requests therefore produce one `200 OK`; later requests observe the verified
state and return `409 Conflict`.

## `POST /provider-callbacks/mock`

Applies a final mock-provider status to the delivery identified by
`provider_message_id`. The endpoint accepts only callbacks authenticated with
the mock provider callback secret; API Keys, Operator Credentials, and Webhook
Secrets are not valid callback credentials.

### Authentication Headers

| Header | Required | Contract |
| --- | --- | --- |
| `X-Mock-Provider-Timestamp` | yes | Callback creation time as Unix seconds. The default acceptance window is five minutes before or after server time. |
| `X-Mock-Provider-Signature` | yes | `v1=<lowercase-or-uppercase-hex-hmac>` where the HMAC is SHA-256 using the mock provider callback secret. |

The signed bytes are the UTF-8 timestamp text, one period (`.`), and the exact
request body bytes, with no canonicalization:

```text
HMAC-SHA256(secret, timestamp + "." + exact_request_body)
```

Missing, malformed, incorrect, tampered, future-dated, or stale authentication
is rejected before JSON parsing or Delivery lookup. Authentication failures use
the same generic response and do not disclose the configured secret, supplied
signature, or timestamp.

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
| `401 Unauthorized` | `{"error":"invalid provider callback authentication"}` | Either authentication header is missing or malformed, the signature does not match the exact body, or the timestamp falls outside the configured freshness window. |
| `400 Bad Request` | `{"error":"provider_message_id is required"}` | Provider message ID is null, empty, or whitespace. |
| `400 Bad Request` | `{"error":"status must be one of: delivered, failed"}` | Status is absent or unsupported. |
| `404 Not Found` | `{"error":"provider message not found"}` | No delivery has the provider message ID. |

Malformed JSON and missing request bodies are rejected by ASP.NET Core and do
not currently have an application-level JSON error contract.

## `GET /messages/{message_id}/report`

Returns aggregate delivery counts for a message. `message_id` must be a UUID.
The endpoint calculates counts in PostgreSQL and does not return individual
delivery or attempt records. Requires the `ApiClient` policy and reports only
when the authenticated API Client owns the Message.

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

Missing or invalid API Keys return `401 Unauthorized`. If no owned Message has
the requested UUID, including when another API Client owns it:

- Status: `404 Not Found`

```json
{"error":"message not found"}
```
