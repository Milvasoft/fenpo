#!/usr/bin/env bash
# Prints the UDID of an available iPhone simulator on the newest installed iOS runtime.
set -euo pipefail

xcrun simctl list devices available --json | python3 -c '
import json, sys
devices = json.load(sys.stdin)["devices"]
def version(runtime):
    tail = runtime.rsplit("iOS-", 1)[-1]
    return tuple(int(p) for p in tail.split("-") if p.isdigit())
runtimes = sorted((r for r in devices if "iOS" in r), key=version, reverse=True)
for runtime in runtimes:
    for device in devices[runtime]:
        if device["name"].startswith("iPhone"):
            print(device["udid"])
            sys.exit(0)
sys.exit("No iPhone simulator found")
'
