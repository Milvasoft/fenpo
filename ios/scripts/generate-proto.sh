#!/usr/bin/env bash
# Generates Swift types from protocol/fenpo/v1/*.proto into FenpoKit/Sources/FenpoProtocol/Generated.
#
# protoc-gen-swift is built from the swift-protobuf version FenpoKit resolves, so generated
# code and runtime always match. Needs protoc on PATH:
#   macOS:   brew install protobuf
#   Windows: winget install --id Google.Protobuf -e   (run this script from Git Bash)
set -euo pipefail

scripts="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ios="$(dirname "$scripts")"
repo="$(dirname "$ios")"
kit="$ios/FenpoKit"
out="$kit/Sources/FenpoProtocol/Generated"

# protoc.exe on Windows needs Windows paths.
native_path() {
    if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi
}

if ! command -v protoc >/dev/null 2>&1; then
    echo "protoc not found. Install it first (see the top of this script)." >&2
    exit 1
fi

swift package --package-path "$kit" resolve
checkout="$kit/.build/checkouts/swift-protobuf"
swift build --package-path "$checkout" -c release --product protoc-gen-swift
bin="$(swift build --package-path "$checkout" -c release --show-bin-path)"
plugin="$bin/protoc-gen-swift"
[[ -f "$plugin" ]] || plugin="$plugin.exe"

rm -rf "$out"
mkdir -p "$out"

protos=()
for file in "$repo"/protocol/fenpo/v1/*.proto; do
    protos+=("$(native_path "$file")")
done

protoc \
    --plugin=protoc-gen-swift="$(native_path "$plugin")" \
    --swift_opt=Visibility=Public \
    --swift_opt=FileNaming=PathToUnderscores \
    --swift_out="$(native_path "$out")" \
    --proto_path="$(native_path "$repo/protocol")" \
    "${protos[@]}"

echo "Generated $(ls "$out" | wc -l | tr -d ' ') files in $out"
