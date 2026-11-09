#!/bin/sh
# Writes /usr/share/nginx/html/config.json when the web container starts (nginx runs every executable
# script in /docker-entrypoint.d/ before it starts). The wallet reads this file at startup, so the own-node
# settings change by changing environment variables and restarting the service: no rebuild.
# Contract: specs/005-wallet-own-node-source/contracts/runtime-config.md
#
#   WALLET_NODE_ENABLED            true | false              (default: not written, node use stays off)
#   WALLET_NODE_API_URL            https://node.example.org  (must start with http:// or https://)
#   WALLET_NODE_CHECK_SECONDS      whole number
#   WALLET_NODE_STABLE_SECONDS     whole number
#   WALLET_NODE_HEIGHT_TOLERANCE   whole number
#   WALLET_DEV_VIEW                true | false
#   WALLET_DD_VIEW                 true | false   DigiDollar:ViewEnabled   (false hides every DigiDollar section)
#   WALLET_DD_SEND                 true | false   DigiDollar:SendEnabled
#   WALLET_DD_ORACLE               true | false   DigiDollar:OracleEnabled
#
# Nothing here is secret: everything in config.json is readable by any visitor's browser.
# Values that do not look right are left out (the wallet then uses its defaults) instead of producing broken JSON.
# WALLET_CONFIG_PATH only exists so the script can be tested away from the container.
set -eu

OUT="${WALLET_CONFIG_PATH:-/usr/share/nginx/html/config.json}"

json_escape() {
    # backslash and double quote are the only characters that need escaping in what we accept (no control characters)
    printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

is_bool() { [ "$1" = "true" ] || [ "$1" = "false" ]; }
is_number() { case "$1" in ''|*[!0-9]*) return 1 ;; *) return 0 ;; esac; }
is_url() { case "$1" in http://*|https://*) ;; *) return 1 ;; esac; case "$1" in *[[:cntrl:]]*|*' '*) return 1 ;; esac; return 0; }

pairs=""
add() {
    # $1 = json key, $2 = already formatted json value
    if [ -n "$pairs" ]; then pairs="$pairs,"; fi
    pairs="$pairs\"$1\":$2"
}

if [ -n "${WALLET_NODE_ENABLED:-}" ] && is_bool "$WALLET_NODE_ENABLED"; then add Enabled "$WALLET_NODE_ENABLED"; fi
if [ -n "${WALLET_NODE_API_URL:-}" ] && is_url "$WALLET_NODE_API_URL"; then add ApiUrl "\"$(json_escape "$WALLET_NODE_API_URL")\""; fi
if [ -n "${WALLET_NODE_CHECK_SECONDS:-}" ] && is_number "$WALLET_NODE_CHECK_SECONDS"; then add CheckIntervalSeconds "$WALLET_NODE_CHECK_SECONDS"; fi
if [ -n "${WALLET_NODE_STABLE_SECONDS:-}" ] && is_number "$WALLET_NODE_STABLE_SECONDS"; then add StablePeriodSeconds "$WALLET_NODE_STABLE_SECONDS"; fi
if [ -n "${WALLET_NODE_HEIGHT_TOLERANCE:-}" ] && is_number "$WALLET_NODE_HEIGHT_TOLERANCE"; then add HeightTolerance "$WALLET_NODE_HEIGHT_TOLERANCE"; fi
if [ -n "${WALLET_DEV_VIEW:-}" ] && is_bool "$WALLET_DEV_VIEW"; then add DevView "$WALLET_DEV_VIEW"; fi

dd=""
add_dd() {
    if [ -n "$dd" ]; then dd="$dd,"; fi
    dd="$dd\"$1\":$2"
}
if [ -n "${WALLET_DD_VIEW:-}" ] && is_bool "$WALLET_DD_VIEW"; then add_dd ViewEnabled "$WALLET_DD_VIEW"; fi
if [ -n "${WALLET_DD_SEND:-}" ] && is_bool "$WALLET_DD_SEND"; then add_dd SendEnabled "$WALLET_DD_SEND"; fi
if [ -n "${WALLET_DD_ORACLE:-}" ] && is_bool "$WALLET_DD_ORACLE"; then add_dd OracleEnabled "$WALLET_DD_ORACLE"; fi

out_json=""
if [ -n "$pairs" ]; then out_json="\"OwnNode\":{$pairs}"; fi
if [ -n "$dd" ]; then
    if [ -n "$out_json" ]; then out_json="$out_json,"; fi
    out_json="$out_json\"DigiDollar\":{$dd}"
fi
printf '{%s}\n' "$out_json" > "$OUT"
echo "40-write-config: wrote $OUT"
