#!/usr/bin/env bash
# Takes the raw App Store material from a 6.9-inch iPhone simulator: every screen in specs.json in
# both themes and languages (raw/<lang>/<theme>/NN_name.png, 1320 x 2868), and the demo tour
# recorded for the app preview (recordings/<lang>.mov). Needs a Mac; build calls it with --capture.
# Usage: capture.sh <simulator udid> <path to Fenpo.app (Debug, simulator)> <design/store dir>
set -euo pipefail

udid="$1"
app="$2"
store="$3"
bundle_id="com.fenpo.app"

screens() {
    python3 -c 'import json, sys; [print(s["id"], s["scene"]) for s in json.load(open(sys.argv[1]))["screens"]]' "$store/specs.json"
}

launch() {
    local lang="$1" scene="$2"
    xcrun simctl terminate "$udid" "$bundle_id" >/dev/null 2>&1 || true
    xcrun simctl launch "$udid" "$bundle_id" -AppleLanguages "($lang)" -AppleLocale "$lang" -fenpo.screenshot "$scene" >/dev/null
}

xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl install "$udid" "$app"
xcrun simctl privacy "$udid" grant camera "$bundle_id"
# A clean status bar: 9:41, full battery without the charging bolt, full signal.
xcrun simctl status_bar "$udid" override --time "9:41" --dataNetwork wifi --wifiMode active --wifiBars 3 \
    --cellularMode active --cellularBars 4 --operatorName "" --batteryState discharging --batteryLevel 100

for theme in light dark; do
    xcrun simctl ui "$udid" appearance "$theme"
    for lang in tr en; do
        mkdir -p "$store/raw/$lang/$theme"
        while read -r id scene; do
            launch "$lang" "$scene"
            sleep 4
            xcrun simctl io "$udid" screenshot "$store/raw/$lang/$theme/$id.png" >/dev/null
        done < <(screens)
    done
done

# The app preview: the demo tour (Fenpo/App/ScreenshotScene.swift) in the preview's theme.
theme="$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1]))["preview"]["theme"])' "$store/specs.json")"
xcrun simctl ui "$udid" appearance "$theme"
mkdir -p "$store/recordings"
for lang in tr en; do
    xcrun simctl terminate "$udid" "$bundle_id" >/dev/null 2>&1 || true
    sleep 1
    log="$store/recordings/$lang.log"
    xcrun simctl io "$udid" recordVideo --codec=h264 --force "$store/recordings/$lang.mov" 2>"$log" &
    recorder=$!
    # The recorder can take seconds to start on a CI machine; launch once it says it has.
    for _ in $(seq 1 60); do
        grep -q "Recording started" "$log" && break
        sleep 0.5
    done
    sleep 1
    launch "$lang" demo
    # Home for 3 s + 3.5 s, then six steps of 3.5 s each, the last screen and a margin for a slow launch.
    sleep 34
    kill -INT "$recorder"
    wait "$recorder" || true
done

echo "Raw screens: $(find "$store/raw" -name '*.png' | wc -l | tr -d ' '), recordings: $(ls "$store/recordings"/*.mov | wc -l | tr -d ' ')"
