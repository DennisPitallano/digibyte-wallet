#!/bin/bash
mkdir -p /root/.digibyte
cp /etc/digibyte/digibyte-mainnet.conf /root/.digibyte/digibyte.conf
exec digibyted -printtoconsole "$@"
