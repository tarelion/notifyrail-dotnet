#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5012}"
DEMO_FLOW_DELAY="${DEMO_FLOW_DELAY:-0}"

require() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require curl
require jq

step() {
  printf "\n\033[1;36m%s\033[0m\n" "$1"
  pause
}

pause() {
  if [[ "$DEMO_FLOW_DELAY" != "0" && -n "$DEMO_FLOW_DELAY" ]]; then
    sleep "$DEMO_FLOW_DELAY"
  fi
}

request() {
  local method="$1"
  local path="$2"
  local data="${3:-}"
  local tmp
  tmp="$(mktemp)"

  if [[ -n "$data" ]]; then
    HTTP_STATUS="$(
      curl -sS -o "$tmp" -w "%{http_code}" \
        --request "$method" "$BASE_URL$path" \
        --header "Content-Type: application/json" \
        --data "$data"
    )"
  else
    HTTP_STATUS="$(
      curl -sS -o "$tmp" -w "%{http_code}" \
        --request "$method" "$BASE_URL$path"
    )"
  fi

  HTTP_BODY="$(<"$tmp")"
  rm -f "$tmp"
}

get_report() {
  request GET "/messages/$MESSAGE_ID/report"
  if [[ "$HTTP_STATUS" != "200" ]]; then
    echo "Expected report HTTP 200, got $HTTP_STATUS" >&2
    echo "$HTTP_BODY" >&2
    exit 1
  fi
  REPORT="$HTTP_BODY"
}

print_report() {
  jq '{total, queued, processing, sent, delivered, retry_scheduled, failed, expired}' <<<"$REPORT"
}

wait_for_report() {
  local expected_sent="$1"
  local expected_retry="$2"
  local expected_failed="$3"
  local expected_delivered="${4:-0}"
  local label="$5"

  for _ in {1..40}; do
    get_report

    local sent retry failed delivered
    sent="$(jq -r '.sent' <<<"$REPORT")"
    retry="$(jq -r '.retry_scheduled' <<<"$REPORT")"
    failed="$(jq -r '.failed' <<<"$REPORT")"
    delivered="$(jq -r '.delivered' <<<"$REPORT")"

    if [[ "$sent" == "$expected_sent" \
      && "$retry" == "$expected_retry" \
      && "$failed" == "$expected_failed" \
      && "$delivered" == "$expected_delivered" ]]; then
      echo "$label"
      print_report
      pause
      return
    fi

    sleep 0.25
  done

  echo "Timed out waiting for report state: sent=$expected_sent retry=$expected_retry failed=$expected_failed delivered=$expected_delivered" >&2
  print_report >&2
  exit 1
}

wait_for_ready() {
  for _ in {1..40}; do
    if curl -fsS "$BASE_URL/readyz" >/dev/null 2>&1; then
      return
    fi
    sleep 0.25
  done

  echo "NotifyRail API is not ready at $BASE_URL" >&2
  echo "Start it with: ./scripts/run-demo-api.sh" >&2
  exit 1
}

print_deliveries() {
  request GET "/messages/$MESSAGE_ID/deliveries"
  if [[ "$HTTP_STATUS" != "200" ]]; then
    echo "Expected deliveries HTTP 200, got $HTTP_STATUS" >&2
    echo "$HTTP_BODY" >&2
    exit 1
  fi

  jq -r '
    ["recipient", "status", "attempts", "outcomes"],
    (.deliveries[] |
      [
        .recipient,
        .status,
        (.attempt_count | tostring),
        ([.attempts[].outcome] | join(" -> "))
      ])
    | @tsv
  ' <<<"$HTTP_BODY" | awk -F '\t' '{
    printf "%-16s  %-16s  %-8s  %s\n", $1, $2, $3, $4
  }'
  pause
}

printf "\033[1mNotifyRail demo flow\033[0m\n"
echo "API: $BASE_URL"
wait_for_ready

DEMO_ID="$(date +%s)"
MESSAGE_KEY="demo-flow-message-$DEMO_ID"
OTP_KEY="demo-flow-otp-$DEMO_ID"

step "1. Create a campaign message"
CREATE_MESSAGE_BODY="$(
  jq -n --arg key "$MESSAGE_KEY" '{
    type: "campaign",
    channel: "sms",
    sender_title: "NotifyRail",
    body: "Demo campaign update.",
    recipients: ["+905551111111", "+905552222222", "+905553333333"],
    idempotency_key: $key,
    report_label: "demo-flow"
  }'
)"
request POST "/messages" "$CREATE_MESSAGE_BODY"
if [[ "$HTTP_STATUS" != "202" ]]; then
  echo "Expected create message HTTP 202, got $HTTP_STATUS" >&2
  echo "$HTTP_BODY" >&2
  exit 1
fi
MESSAGE_ID="$(jq -r '.message_id' <<<"$HTTP_BODY")"
jq '{message_id, delivery_count}' <<<"$HTTP_BODY"
pause

step "2. Worker processes first attempts"
wait_for_report 1 1 1 0 "One sent, one retry scheduled, one failed:"

step "3. Retry becomes due and succeeds"
wait_for_report 2 0 1 0 "Retry recipient moved to sent:"

step "4. Delivery attempt history"
print_deliveries

step "5. Provider callback marks one sent delivery as delivered"
PROVIDER_MESSAGE_ID="$(
  jq -r '.deliveries[] | select(.recipient == "+905551111111") | .provider_message_id' <<<"$HTTP_BODY"
)"
CALLBACK_BODY="$(
  jq -n --arg provider_message_id "$PROVIDER_MESSAGE_ID" '{
    provider_message_id: $provider_message_id,
    status: "delivered"
  }'
)"
request POST "/provider-callbacks/mock" "$CALLBACK_BODY"
if [[ "$HTTP_STATUS" != "200" ]]; then
  echo "Expected callback HTTP 200, got $HTTP_STATUS" >&2
  echo "$HTTP_BODY" >&2
  exit 1
fi
jq '{delivery_id, status, provider_message_id}' <<<"$HTTP_BODY"
pause
wait_for_report 1 0 1 1 "Report after callback:"

step "6. Send and verify an OTP"
OTP_SEND_BODY="$(
  jq -n --arg key "$OTP_KEY" '{
    recipient: "+905559999999",
    idempotency_key: $key
  }'
)"
request POST "/otp/send" "$OTP_SEND_BODY"
if [[ "$HTTP_STATUS" != "202" ]]; then
  echo "Expected OTP send HTTP 202, got $HTTP_STATUS" >&2
  echo "$HTTP_BODY" >&2
  exit 1
fi
OTP_ID="$(jq -r '.otp_id' <<<"$HTTP_BODY")"
OTP_CODE="$(jq -r '.debug_code' <<<"$HTTP_BODY")"
jq '{otp_id, expires_at, debug_code}' <<<"$HTTP_BODY"
pause

OTP_VERIFY_BODY="$(
  jq -n --arg otp_id "$OTP_ID" --arg code "$OTP_CODE" '{
    otp_id: $otp_id,
    code: $code
  }'
)"
request POST "/otp/verify" "$OTP_VERIFY_BODY"
if [[ "$HTTP_STATUS" != "200" ]]; then
  echo "Expected OTP verify HTTP 200, got $HTTP_STATUS" >&2
  echo "$HTTP_BODY" >&2
  exit 1
fi
jq '{otp_id, status, verified_at}' <<<"$HTTP_BODY"
pause

step "7. Reusing the OTP is rejected"
request POST "/otp/verify" "$OTP_VERIFY_BODY"
echo "HTTP $HTTP_STATUS"
jq . <<<"$HTTP_BODY"
pause

printf "\n\033[1;32mDemo complete.\033[0m\n"
