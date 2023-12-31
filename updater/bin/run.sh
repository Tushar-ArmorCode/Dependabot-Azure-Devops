#!/bin/bash
set -e

command="$1"
if [ -z "$command" ]; then
  echo "usage: run [update_script|fetch_files|update_files]"
  exit 1
fi

# Tell hex to use the system-wide CA bundle
export HEX_CACERTS_PATH=/etc/ssl/certs/ca-certificates.crt

# Tell python to use the system-wide CA bundle
export REQUESTS_CA_BUNDLE=/etc/ssl/certs/ca-certificates.crt

bundle exec ruby "bin/${command}.rb"
