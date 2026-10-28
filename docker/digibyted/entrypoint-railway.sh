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
#   DGB_DBCACHE_MB     database cache in MB (default 512; raise for the first sync)
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
    echo "dbcache=${DGB_DBCACHE_MB:-512}"
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
exec digibyted -printtoconsole "$@"
