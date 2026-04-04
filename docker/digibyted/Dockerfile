# DigiByte Core Node
# Downloads pre-built binary from official GitHub releases
# Used for regtest (dev), testnet, and mainnet (pruned prod)

FROM ubuntu:22.04

ARG DGB_VERSION=8.26.2

# Install dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    wget ca-certificates && \
    rm -rf /var/lib/apt/lists/*

# Download and install DigiByte Core
RUN wget "https://github.com/DigiByte-Core/digibyte/releases/download/v${DGB_VERSION}/digibyte-${DGB_VERSION}-x86_64-linux-gnu.tar.gz" \
    -O /tmp/digibyte.tar.gz && \
    tar -xzf /tmp/digibyte.tar.gz -C /opt/ && \
    ln -sf $(find /opt/ -name "digibyted" -type f | head -1) /usr/local/bin/digibyted && \
    ln -sf $(find /opt/ -name "digibyte-cli" -type f | head -1) /usr/local/bin/digibyte-cli && \
    rm /tmp/digibyte.tar.gz

# Store config templates outside the data volume
COPY digibyte.conf /etc/digibyte/digibyte.conf
COPY digibyte-testnet.conf /etc/digibyte/digibyte-testnet.conf
COPY digibyte-mainnet.conf /etc/digibyte/digibyte-mainnet.conf
COPY entrypoint.sh /entrypoint.sh
COPY entrypoint-testnet.sh /etc/digibyte/entrypoint-testnet.sh
COPY entrypoint-mainnet.sh /etc/digibyte/entrypoint-mainnet.sh
RUN chmod +x /entrypoint.sh /etc/digibyte/entrypoint-testnet.sh /etc/digibyte/entrypoint-mainnet.sh

# Expose all possible ports (regtest, testnet, mainnet P2P + RPC)
EXPOSE 18444 18443 12025 14023 12024 14022

# Persist blockchain data
VOLUME ["/root/.digibyte"]

# Default entrypoint is regtest (override via docker-compose for testnet/mainnet)
ENTRYPOINT ["/entrypoint.sh"]
