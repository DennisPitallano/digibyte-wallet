#!/bin/bash
mkdir -p /root/.digibyte
cp /etc/digibyte/digibyte-testnet.conf /root/.digibyte/digibyte.conf
exec digibyted -testnet -printtoconsole "$@"
