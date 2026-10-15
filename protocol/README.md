# Fenpo Protocol

Single source of truth for every message exchanged between Fenpo apps. Windows (C#), iPhone (Swift) and Android (Kotlin) code is generated from these files; never hand-write message types.

| File | Contents |
|---|---|
| `fenpo/v1/common.proto` | `DevicePlatform`, `Capability` |
| `fenpo/v1/hello.proto` | `Hello`, `Ping`, `Pong`, `Error` |
| `fenpo/v1/pairing.proto` | QR format, `PairingRequest`, `PairingResult` |
| `fenpo/v1/content.proto` | Clipboard, open URL |
| `fenpo/v1/transfer.proto` | File transfer negotiation and progress |
| `fenpo/v1/device.proto` | Device state, ring, lock |
| `fenpo/v1/envelope.proto` | `Envelope`, field ranges per feature group |

## Generation

| Platform | Tool | Output |
|---|---|---|
| Windows | `Grpc.Tools` (messages only, no services) in `Fenpo.Protocol.csproj` | `obj/` at build time |
| iPhone | `protoc` + `protoc-gen-swift` (swift-protobuf) via the SwiftPM build plugin in `FenpoKit` | build time |
| Android | protobuf Gradle plugin, Kotlin lite (or Wire, decided in Android phase A) | build time |

Include path is this `protocol/` folder, so imports are written as `fenpo/v1/<file>.proto`.

## Rules

- This is a draft of version 1. Until the first public release, fields may change; after that, only additive changes.
- Never reuse or renumber a field. Remove with `reserved`.
- New features get a `Capability` value and stay inside their `Envelope` field range.
- A protocol change is its own commit (`protocol: ...`) and updates `docs/ARCHITECTURE.md` in the same commit.
