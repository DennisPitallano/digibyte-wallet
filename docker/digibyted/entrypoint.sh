#!/bin/bash
# Entrypoint: copy config into the data dir (which may be a volume), then start
mkdir -p /root/.digibyte
cp /etc/digibyte/digibyte.conf /root/.digibyte/digibyte.conf
exec digibyted -regtest -printtoconsole "$@"
