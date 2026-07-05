# OTP Verification Reference

## Purpose

This page defines the implemented OTP Challenge lifecycle, mock-code security
contract, configuration, and transaction boundaries.

## Scope

- OTP options and code hashing: `src/NotifyRail.Api/Features/Otp`
- Send module: `src/NotifyRail.Api/Features/Otp/SendOtp`
- Verify module: `src/NotifyRail.Api/Features/Otp/VerifyOtp`
- Persistence: `src/NotifyRail.Api/Features/Otp/Persistence`
- HTTP payloads and responses: [HTTP API](http-api.md)
- Table and constraint definitions: [Persistence model](persistence-model.md)

## Configuration

| Setting | Default | Contract |
| --- | --- | --- |
| `Otp:Secret` | none | Required server-side secret used for mock code derivation and HMAC hashing. |
| `Otp:SenderTitle` | `NotifyRail` | Non-blank sender title for the OTP Message. |
| `Otp:Ttl` | `5 minutes` | Must be greater than zero. Applied to both the OTP Challenge and Delivery. |
| `Otp:MaxAttempts` | `5` | Must be greater than zero. Counts incorrect verification attempts. |

Invalid OTP configuration fails application startup when PostgreSQL-backed
modules are registered. The development settings contain a local-only secret;
non-development environments must supply their own.

## Send Contract

`POST /otp/send` atomically creates:

- one `otp` Message
- one recipient Delivery whose `expires_at` matches the challenge TTL
- one OTP Challenge linked to the Message

The endpoint uses the global Message idempotency key. A replay with the same
normalized recipient returns the original `otp_id`, `message_id`, `expires_at`,
and `debug_code`. Reuse with another recipient returns an idempotency conflict.
The PostgreSQL Message uniqueness constraint arbitrates concurrent sends.

The persisted Message body is `Your verification code is ready.` and never
contains the OTP Code.

## Code Security

The mock OTP Code is a six-digit value deterministically derived from the random
challenge UUID and `Otp:Secret`. Deterministic derivation allows an idempotent
send replay to return the same code without storing plaintext.

Verification persists and compares a 32-byte HMAC-SHA-256 hash. Comparison uses
constant-time equality. The plaintext code exists only in process memory and in
the simulation-only `debug_code` response described by
[ADR 0002](../adr/0002-expose-otp-code-in-mock-send-response.md).

## Verification Contract

`POST /otp/verify` locks the challenge row with PostgreSQL `FOR UPDATE` before
checking or changing it. Concurrent verification requests are serialized, so
only one correct request can set `verified_at`.

Checks are applied in this order:

1. Reject an unknown challenge.
2. Reject an already verified challenge.
3. Reject a challenge at or after `expires_at`.
4. Reject a challenge that exhausted `max_attempts`.
5. Compare the supplied code in constant time.
6. Increment `failed_attempt_count` for an incorrect code, or set `verified_at`
   for a correct code.

The incorrect attempt that reaches `max_attempts` returns the attempt-limit
response. Expired, already verified, and already locked requests do not consume
another attempt.

OTP verification is independent of provider delivery status. A Delivery tracks
whether the notification was processed; an OTP Challenge tracks whether the
client proved possession of the code.

## Current Limits

- Multiple active challenges may exist for the same recipient; callers verify a
  specific challenge by `otp_id`.
- `debug_code` is intentionally exposed for the mock MVP and is not a production
  OTP-send contract.
- `Otp:Secret` must remain stable for at least the maximum challenge lifetime;
  changing it invalidates outstanding codes and changes idempotent replay codes.
