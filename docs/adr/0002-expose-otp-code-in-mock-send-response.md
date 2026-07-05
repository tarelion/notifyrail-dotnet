# Expose the OTP code in the mock send response

## Status

Accepted.

## Context

NotifyRail does not send real SMS messages, but its demo flow must let an API
client obtain the generated OTP Code and exercise verification. Logging a code
or using a global fixed code would make the HTTP demo indirect or weaken the
one-challenge-one-code model.

## Decision

Return a `debug_code` from the mock `POST /otp/send` response. Never persist the
plaintext code in PostgreSQL or in `messages.body`; persist only its verification
hash. Treat `debug_code` as a simulation-only contract that a real provider-backed
deployment must remove or protect.

## Consequences

- The local demo can send and verify an OTP using only HTTP calls.
- API clients must treat the response as sensitive because it contains the code.
- The mock response deliberately differs from a production OTP-send response.
