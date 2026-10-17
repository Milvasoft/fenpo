#!/usr/bin/env bash
# Prints the UDID of a 6.9-inch iPhone simulator (1320 x 2868, the size App Store Connect requires),
# creating one on the newest iOS runtime when none exists.
set -euo pipefail

xcrun simctl list devices available --json | python3 -c '
import json, subprocess, sys
wanted = ["iPhone 17 Pro Max", "iPhone 16 Pro Max"]
devices = json.load(sys.stdin)["devices"]
def version(runtime):
    tail = runtime.rsplit("iOS-", 1)[-1]
    return tuple(int(p) for p in tail.split("-") if p.isdigit())
runtimes = sorted((r for r in devices if "iOS" in r), key=version, reverse=True)
for name in wanted:
    for runtime in runtimes:
        for device in devices[runtime]:
            if device["name"] == name:
                print(device["udid"])
                sys.exit(0)
types = json.loads(subprocess.run(["xcrun", "simctl", "list", "devicetypes", "--json"], capture_output=True, text=True).stdout)["devicetypes"]
for name in wanted:
    match = next((t for t in types if t["name"] == name), None)
    if match and runtimes:
        udid = subprocess.run(["xcrun", "simctl", "create", "Fenpo Store", match["identifier"], runtimes[0]], capture_output=True, text=True, check=True).stdout.strip()
        print(udid)
        sys.exit(0)
sys.exit("No 6.9-inch iPhone simulator available")
'
