# DigiByte Core Node
# Downloads pre-built binary from official GitHub releases
# Used for regtest (dev), testnet, mainnet (pruned) and the Railway full node (entrypoint-railway.sh)

FROM ubuntu:22.04

ARG DGB_VERSION=9.26.7

# Install dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    wget ca-certificates && \
    rm -rf /var/lib/apt/lists/*

# Download and install DigiByte Core. The tarball is checked against the SHA256SUMS
# file published with the same release; the build fails on a mismatch. A checksum
# only detects changed bytes, it is not a digital signature.
RUN set -eux; \
    base="https://github.com/DigiByte-Core/digibyte/releases/download/v${DGB_VERSION}"; \
    file="digibyte-${DGB_VERSION}-x86_64-linux-gnu.tar.gz"; \
    wget -q "${base}/${file}" -O "/tmp/${file}"; \
    wget -q "${base}/SHA256SUMS" -O /tmp/SHA256SUMS; \
    cd /tmp; \
    grep " ${file}\$" SHA256SUMS | sha256sum -c -; \
    tar -xzf "/tmp/${file}" -C /opt/; \
    ln -sf $(find /opt/ -name "digibyted" -type f | head -1) /usr/local/bin/digibyted; \
    ln -sf $(find /opt/ -name "digibyte-cli" -type f | head -1) /usr/local/bin/digibyte-cli; \
    rm -f "/tmp/${file}" /tmp/SHA256SUMS

# Store config templates outside the data volume
COPY digibyte.conf /etc/digibyte/digibyte.conf
COPY digibyte-testnet.conf /etc/digibyte/digibyte-testnet.conf
COPY digibyte-mainnet.conf /etc/digibyte/digibyte-mainnet.conf
COPY entrypoint.sh /entrypoint.sh
COPY entrypoint-testnet.sh /etc/digibyte/entrypoint-testnet.sh
COPY entrypoint-mainnet.sh /etc/digibyte/entrypoint-mainnet.sh
COPY digibyte-railway.conf /etc/digibyte/digibyte-railway.conf
COPY entrypoint-railway.sh /etc/digibyte/entrypoint-railway.sh
RUN chmod +x /entrypoint.sh /etc/digibyte/entrypoint-testnet.sh /etc/digibyte/entrypoint-mainnet.sh /etc/digibyte/entrypoint-railway.sh

# Expose all possible ports (regtest, testnet, mainnet P2P + RPC)
EXPOSE 18444 18443 12025 14023 12024 14022

# Blockchain data lives in /root/.digibyte. No VOLUME instruction: Railway rejects it
# (attach a Railway volume at that path), and the compose files mount named volumes there.

# Default entrypoint is regtest (override via docker-compose for testnet/mainnet)
ENTRYPOINT ["/entrypoint.sh"]
