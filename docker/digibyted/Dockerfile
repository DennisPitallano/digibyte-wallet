# DigiByte Core Regtest Node (instant mining for development)
# Downloads pre-built binary from official GitHub releases

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

# Store config template outside the data volume
COPY digibyte.conf /etc/digibyte/digibyte.conf
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Regtest P2P + RPC ports
EXPOSE 18444 18443

# Persist blockchain data
VOLUME ["/root/.digibyte"]

# Health check
HEALTHCHECK --interval=10s --timeout=5s --retries=5 --start-period=10s \
    CMD digibyte-cli -regtest -rpcuser=dgbrpc -rpcpassword=changeme getblockcount || exit 1

ENTRYPOINT ["/entrypoint.sh"]
