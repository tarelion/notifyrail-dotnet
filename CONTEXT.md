# NotifyRail Domain Language

NotifyRail models asynchronous notification delivery and short-lived OTP
verification without integrating with a real provider.

## Messaging

**Message**:
A top-level notification request accepted from an API client.
_Avoid_: Notification job, batch

**Delivery**:
The per-recipient work and lifecycle created from a Message.
_Avoid_: Message, send

**Delivery Attempt**:
One provider send attempt for one Delivery.
_Avoid_: Retry, delivery

**Provider Callback**:
A provider-originated final status update for a previously accepted Delivery.
_Avoid_: Delivery attempt, client webhook

## OTP Verification

**OTP Challenge**:
A short-lived opportunity to prove possession of a recipient address using one
OTP Code. It can be verified successfully only once.
_Avoid_: OTP message, OTP record

**OTP Code**:
A short numeric secret associated with one OTP Challenge.
_Avoid_: Password, PIN

**Debug OTP Code**:
The OTP Code exposed by the mock send response so a local API client can perform
verification without receiving a real SMS.
_Avoid_: Stored code, production OTP response
