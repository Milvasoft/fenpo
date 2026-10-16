#!/usr/bin/env bash
# Archives a Release build and uploads it to TestFlight. Run on a Mac whose Xcode is signed in to an
# Apple account in the Milvasoft team (docs/ios/TESTING.md, "TestFlight").
#
# The build number is the commit count of the checked out commit, so every upload is unique and
# traceable: `git rev-list --count HEAD`.
set -euo pipefail

scripts="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ios="$(dirname "$scripts")"
repo="$(dirname "$ios")"

if [[ -n "$(git -C "$repo" status --porcelain -- ios protocol)" ]]; then
    echo "ios/ or protocol/ has uncommitted changes; upload a committed state only." >&2
    exit 1
fi

build="$(git -C "$repo" rev-list --count HEAD)"
commit="$(git -C "$repo" rev-parse --short HEAD)"
out="$ios/build"
rm -rf "$out"

"$scripts/generate-proto.sh"
(cd "$ios" && xcodegen generate)

xcodebuild archive \
    -project "$ios/Fenpo.xcodeproj" \
    -scheme Fenpo \
    -configuration Release \
    -destination "generic/platform=iOS" \
    -archivePath "$out/Fenpo.xcarchive" \
    -allowProvisioningUpdates \
    CURRENT_PROJECT_VERSION="$build"

xcodebuild -exportArchive \
    -archivePath "$out/Fenpo.xcarchive" \
    -exportOptionsPlist "$ios/Config/ExportOptions.plist" \
    -exportPath "$out/export" \
    -allowProvisioningUpdates

echo "Uploaded Fenpo build $build from commit $commit. It shows up in TestFlight after processing."
