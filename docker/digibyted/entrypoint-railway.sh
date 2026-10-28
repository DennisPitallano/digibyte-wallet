#!/bin/bash
# Railway entrypoint for the mainnet full node.
#
# Required variables (set in Railway, never committed):
#   DGB_RPC_USER       RPC user name
#   DGB_RPC_PASSWORD   RPC password (long and random; "changeme" is refused)
#   DGB_RPC_ALLOW_IP   address range allowed to call RPC, e.g. the project's private
#                      network range. Comma separated for several ranges.
# Optional:
#   DGB_RPC_BIND       address RPC listens on (default 0.0.0.0; use :: if the private
#                      network is IPv6 only)
#   DGB_DBCACHE_MB     database cache in MB (default 2048, sized for the first sync;
#                      a bigger cache means fewer disk flushes and a faster, so
#                      cheaper, sync. Once synced set it to 450 to cut the RAM bill)
#   DGB_LISTEN         1 to accept inbound peers (default 0: serving peers is billed
#                      as egress and nothing here needs it)
#   DGB_MAX_UPLOAD_MB  daily upload cap in MB when DGB_LISTEN=1 (default 1000)
set -euo pipefail

fail() {
    echo "entrypoint-railway: $1" >&2
    exit 1
}

[ -n "${DGB_RPC_USER:-}" ] || fail "DGB_RPC_USER is not set"
[ -n "${DGB_RPC_PASSWORD:-}" ] || fail "DGB_RPC_PASSWORD is not set"
[ -n "${DGB_RPC_ALLOW_IP:-}" ] || fail "DGB_RPC_ALLOW_IP is not set (refusing to start with an open RPC range)"
[ "${DGB_RPC_PASSWORD}" != "changeme" ] || fail "DGB_RPC_PASSWORD must not be the default"
case "${DGB_RPC_ALLOW_IP}" in
    0.0.0.0/0|::/0) fail "DGB_RPC_ALLOW_IP must not allow every address" ;;
esac

DATA_DIR=/root/.digibyte
CONF="${DATA_DIR}/digibyte.conf"
mkdir -p "${DATA_DIR}"

# The data directory is a volume: rewrite the config on every start so a rotated
# password or range takes effect after a redeploy. Keep it readable by root only.
umask 077
cp /etc/digibyte/digibyte-railway.conf "${CONF}"

{
    echo "dbcache=${DGB_DBCACHE_MB:-2048}"
    echo "listen=${DGB_LISTEN:-0}"
    echo "maxuploadtarget=${DGB_MAX_UPLOAD_MB:-1000}"
    echo "rpcuser=${DGB_RPC_USER}"
    echo "rpcpassword=${DGB_RPC_PASSWORD}"
    echo "rpcbind=${DGB_RPC_BIND:-0.0.0.0}"
    IFS=',' read -ra RANGES <<< "${DGB_RPC_ALLOW_IP}"
    for range in "${RANGES[@]}"; do
        range="${range// /}"
        [ -n "${range}" ] && echo "rpcallowip=${range}"
    done
} >> "${CONF}"

# dbcache and the rpc* options belong to the [main] section, which the template
# ends with, so appending keeps them in the right section.

# During sync the node prints several difficulty-adjustment debug lines for every block,
# thousands a second. Railway rate-limits log output and drops lines, which hides real
# warnings and errors. Drop only those line types. The process substitution keeps the
# node as this shell's own process (exec), so stop signals still reach it directly.
exec > >(grep --line-buffered -v -E 'GetNextWorkRequired|nTargetTimespan = |Before: +[0-9a-f]{8} |After: +[0-9a-f]{8} |Block [0-9]+: version=') 2>&1

exec digibyted -printtoconsole "$@"
