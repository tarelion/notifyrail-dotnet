# NotifyRail Domain Language

NotifyRail models asynchronous notification delivery and short-lived OTP
verification without integrating with a real provider.

## Messaging

**API Client**:
An independent external application that uses NotifyRail and owns the Messages
it submits and the outbound notifications about them.
_Avoid_: User, account, tenant

**Message**:
A top-level notification request accepted from an API Client.
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

## Client Webhooks

**Webhook Endpoint**:
The registered network destination where NotifyRail sends an API Client's
Webhook Events.
_Avoid_: Provider Callback endpoint, per-message callback URL

**Webhook Event**:
An immutable notification that a client-visible Delivery state occurred and
must be communicated to the owning API Client.
_Avoid_: Delivery, Provider Callback

**Webhook Attempt**:
One attempt to send one Webhook Event to an API Client's registered endpoint.
_Avoid_: Delivery Attempt, Delivery

**Webhook Secret**:
A secret shared with an API Client for proving that a Webhook Event came from
NotifyRail and was not altered.
_Avoid_: API key, OTP Code

**Dead Webhook Event**:
A Webhook Event whose automatic attempts have ended without success and which
remains available for investigation or manual replay.
_Avoid_: Failed Delivery, discarded event

## OTP Verification

**OTP Challenge**:
A short-lived opportunity to prove possession of a recipient address using one
OTP Code. It can be verified successfully only once.
_Avoid_: OTP message, OTP record

**OTP Code**:
A short numeric secret associated with one OTP Challenge.
_Avoid_: Password, PIN

**Debug OTP Code**:
The OTP Code exposed by the mock send response so a local API Client can perform
verification without receiving a real SMS.
_Avoid_: Stored code, production OTP response
