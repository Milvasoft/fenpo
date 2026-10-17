#!/usr/bin/env bash
# Takes simulator screenshots of every screen in both themes and both languages.
# Usage: screenshots.sh <simulator udid> <path to Fenpo.app> <output dir>
# Screens are opened with the DEBUG-only -fenpo.screenshot launch argument (Fenpo/App/ScreenshotScene.swift).
set -euo pipefail

udid="$1"
app="$2"
out="$3"
bundle_id="com.fenpo.app"
scenes=(home home-unpaired transfers pc settings developer scan steps ringing camera touchpad presenter transfer-detail camera-ready)

mkdir -p "$out"
xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl install "$udid" "$app"
# The scanner asks for the camera; a pending system alert would cover the following screenshots.
xcrun simctl privacy "$udid" grant camera "$bundle_id"
xcrun simctl status_bar "$udid" override --time "18:51" --batteryState charged --batteryLevel 100 --wifiBars 3 --cellularBars 4 || true

for appearance in light dark; do
    xcrun simctl ui "$udid" appearance "$appearance"
    for lang in tr en; do
        for scene in "${scenes[@]}"; do
            xcrun simctl terminate "$udid" "$bundle_id" >/dev/null 2>&1 || true
            xcrun simctl launch "$udid" "$bundle_id" -AppleLanguages "($lang)" -AppleLocale "$lang" -fenpo.screenshot "$scene" >/dev/null
            sleep 4
            xcrun simctl io "$udid" screenshot "$out/$scene-$appearance-$lang.png" >/dev/null
        done
    done
done

# System alerts use the simulator's language, so list the localized Info.plist strings instead.
for strings in "$app"/*.lproj/InfoPlist.strings; do
    echo "== $strings"
    plutil -p "$strings"
done

echo "Saved $(ls "$out" | wc -l | tr -d ' ') screenshots to $out"
