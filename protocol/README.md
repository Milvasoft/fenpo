# Fenpo Protocol

Single source of truth for every message exchanged between Fenpo apps. Windows (C#), iPhone (Swift) and Android (Kotlin) code is generated from these files; never hand-write message types.

| File | Contents |
|---|---|
| `fenpo/v1/common.proto` | `DevicePlatform`, `Capability` |
| `fenpo/v1/hello.proto` | `Hello`, `Ping`, `Pong`, `Error` |
| `fenpo/v1/pairing.proto` | QR format, `PairingRequest`, `PairingResult` |
| `fenpo/v1/content.proto` | Clipboard, open URL |
| `fenpo/v1/transfer.proto` | File transfer negotiation and progress |
| `fenpo/v1/device.proto` | Device state, PC status, ring, lock |
| `fenpo/v1/media.proto` | Now playing and media control |
| `fenpo/v1/messages.proto` | SMS over the Fenpo channel for Android phones without MAP |
| `fenpo/v1/pc_input.proto` | The phone as the PC's touchpad, keyboard and presentation remote |
| `fenpo/v1/video.proto` | Media connections: formats, `MediaStreamOpen`, `StreamPacket` |
| `fenpo/v1/camera.proto` | The phone as a webcam |
| `fenpo/v1/screen.proto` | The phone's screen on the PC, with control (Android) |
| `fenpo/v1/notification.proto` | Notification mirroring from Android phones |
| `fenpo/v1/pc_notification.proto` | The PC's notifications on the phone |
| `fenpo/v1/files.proto` | The PC's shared folders on the phone (Files app, Android file picker) |
| `fenpo/v1/photos.proto` | The phone's photo library browsed from the PC |
| `fenpo/v1/hotspot.proto` | Joining the phone's hotspot from the PC |
| `fenpo/v1/envelope.proto` | `Envelope`, field ranges per feature group |

## Generation

| Platform | Tool | Output |
|---|---|---|
| Windows | `Grpc.Tools` (messages only, no services) in `Fenpo.Protocol.csproj` | `obj/` at build time |
| iPhone | `protoc` + `protoc-gen-swift` built from the swift-protobuf version `FenpoKit` resolves, run by `ios/scripts/generate-proto.sh` (SwiftPM build plugins cannot read files outside the package) | before building, into `ios/FenpoKit/Sources/FenpoProtocol/Generated/` (ignored by git) |
| Android | protobuf Gradle plugin, Kotlin lite (or Wire, decided in Android phase A) | build time |

Include path is this `protocol/` folder, so imports are written as `fenpo/v1/<file>.proto`.

## Fenpo BLE channel

The PC is the GATT server, the phone the client. These UUIDs are fixed for every PC and every app version; the iPhone app declares the service UUID in `NSAccessorySetupBluetoothServices`.

| Item | UUID | Properties |
|---|---|---|
| Fenpo service | `1888047E-C8C5-45CC-AAEA-333A8859E79F` | Primary, advertised |
| `rx` (phone → PC) | `ECC8D0D1-990A-4B02-A4FC-49FA647E7254` | Write without response, encryption required |
| `tx` (PC → phone) | `E9EC01E0-8C02-4B8E-902C-4F113E25DFC8` | Notify, encryption required |

Requiring encryption on both characteristics makes the first access trigger iOS pairing (bonding), which the PC then uses to discover ANCS.

### Sealing

Every Envelope is sealed before it is split; the Bluetooth bond alone is not trusted.

- AES-256-GCM with `PairingResult.ble_channel_key`, delivered over TLS during pairing.
- Nonce, 12 bytes: `[1 byte sender: 0x01 phone, 0x02 PC][3 zero bytes][8 byte counter, big-endian]`.
- Additional authenticated data: the ASCII bytes `fenpo-ble-v1`.
- Sealed message: `nonce (12) || ciphertext || tag (16)`, the same layout as CryptoKit's `combined`. The receiver reads the counter from the nonce.
- Each sender keeps its counter persistent and increasing. The receiver rejects a counter less than or equal to the last one it accepted from that sender.
- A sealed message is at most 4096 bytes. Anything larger goes over Wi‑Fi; the BLE channel only tells the other side to switch.

### Chunks

A sealed message is split to fit the ATT MTU. Each write (`rx`) or notification (`tx`) is `[1 byte flags][2 byte sequence, big-endian][payload]`.

- Flags: bit 0 first chunk, bit 1 last chunk (a one-chunk message sets both).
- The sequence starts at 0 for every message.
- Payload is at most ATT MTU − 3 (ATT header) − 3 (chunk header) bytes.

### Session

The phone sends `Hello` as soon as it subscribes to `tx`, which means the encrypted bond is up. The PC answers with its own `Hello`, then `PcStatus`. `Ping`/`Pong` work on both channels.

### Wi‑Fi on demand

A phone in the background often keeps only the BLE channel (iOS suspends the app and its sockets; Android may lose its socket in Doze). When the PC has something that needs Wi‑Fi (a file, a clipboard item too large for BLE, photos, folders) and the phone has no control connection over Wi‑Fi, the PC sends `WifiWanted` on BLE and waits up to 10 seconds for the phone's Wi‑Fi `Hello` before giving up. The phone opens its control connection at once, also from the background (on iOS the BLE notification wakes the app; it holds a background task while the work lasts), and may close it again a while after the last message. A phone that does not know `WifiWanted` ignores it, and the PC gives up after the wait.

## File transfer

- The phone opens every data connection; the PC never connects to the phone and the phone never listens. A server
  socket on the phone runs into Android's local network permission, NAT and iOS background limits.
- Data connections go to the PC's control server port with the same mutual, pinned TLS 1.3 as the control connection.
  The first frame tells the two apart: `Hello` or `PairingRequest` starts a control connection, `TransferStreamHeader`
  starts a data connection.
- `TransferAccept.data_port` is set only by the PC; 0 means the control server port. The phone always sends 0.
- Phone to PC: the phone sends `TransferOffer` on the control connection, the PC answers `TransferAccept`, the phone
  opens a data connection, writes `[uint32 length][TransferStreamHeader]` and then the bytes of each file in offer order.
- PC to phone: the PC sends `TransferOffer`, the phone answers `TransferAccept`, the phone opens a data connection and
  writes `[uint32 length][TransferStreamHeader]`, then the PC writes the bytes. The direction comes from which side
  offered `transfer_id`.
- The PC accepts a data connection only for an accepted transfer that has not started yet and that belongs to the
  phone whose certificate opened the connection; anything else is closed. One data connection per transfer.
- The phone opens the data connection within 30 seconds of the accept, otherwise both sides treat the transfer as
  failed. The receiver reports `TransferProgress` on the control connection and `TRANSFER_STATE_COMPLETED` once every
  byte arrived and every given `sha256` matched, counting by the sizes in the offer, without waiting for the end of
  the stream.
- The phone reads its data connection to the end and closes it only after the PC's `COMPLETED` or `FAILED` (or a
  timeout). Closing a TLS socket that still holds unread bytes, such as TLS 1.3 session tickets, sends a TCP reset,
  which can throw away the last bytes still on their way to the PC. The PC closes after its last byte or its report.

## Notification mirroring (Android)

Only Android phones send their notifications over the Fenpo channel (`CAPABILITY_NOTIFICATION_MIRROR`); the iPhone's come through ANCS.

- After `Hello`, on that channel, the phone sends a snapshot: `PhoneNotification` with `pre_existing` for every current notification, then `NotificationSnapshotDone`. The PC removes that phone's keys it did not receive since the snapshot started and does not pop up `pre_existing` ones. The snapshot is idempotent; the phone may skip it on BLE while Wi‑Fi is up.
- Then the phone sends one `PhoneNotification` for each new or updated notification and `NotificationRemoved` when one is gone, on its best open channel. The `key` identifies a notification. `seq` grows with every such message across both channels; the PC ignores a message for a key whose `seq` is not newer than the last it saw and remembers removed keys for a few minutes, so a late update cannot bring a removed notification back.
- `category` comes from Android's `Notification.category`: `call` is `INCOMING_CALL` (the PC shows it in the Island with its actions), `missed_call` `MISSED_CALL`, `msg` and `social` `SOCIAL`, `email` `EMAIL`, `event`, `reminder` and `alarm` `SCHEDULE`, `navigation` `LOCATION`, anything else `UNSPECIFIED`.
- The PC answers with `DismissNotification` (only for `dismissible` ones) or `RunNotificationAction` (`reply_text` for actions with `accepts_reply`). A notification or action that no longer exists is answered with `Error` `INVALID_REQUEST`.
- `content_hidden` means Android withheld the content (one-time codes, lock screen privacy); the PC says so instead of showing an empty notification.
- App icons are not repeated in every notification: the PC sends `AppIconRequest` for an `app_id` it has not cached and the phone answers with `AppIcon` (64x64 PNG).
- On the BLE channel a `PhoneNotification` must fit the 4096-byte sealed limit: the phone drops `large_icon_png` first, then shortens `text` and `messages`. An `AppIcon` that does not fit waits for Wi‑Fi.

## Media (phones and PC)

- `CAPABILITY_MEDIA`: each side may report its own `MediaState` and control the other with `MediaCommand`. The iPhone's media still comes through AMS: an iPhone that offers `CAPABILITY_MEDIA` only receives the PC's `MediaState` and sends `MediaCommand` (the PC tab's player), and never sends a `MediaState` of its own.
- `MediaState` goes after `Hello` and on every change of track, play state or volume, on the best open channel. Position is not streamed; the receiver advances `position_ms` from `position_at` while `playing`.
- `artwork_jpeg` is left out on BLE; the same state is sent again with artwork once Wi‑Fi is up.
- A `MediaCommand` with nothing to control is answered with `Error` `UNSUPPORTED`.

## Media connections (camera and screen)

- Video and audio never travel on the control connection. The phone opens a separate TLS connection to the PC's control server port, with the same pinned certificates, when it reports `STARTING` with a `stream_id`.
- First frame: `[uint32 length][Envelope]` whose body is `media_stream_open` (so the PC tells it from `Hello` and from `TransferStreamHeader`). Then every frame is `[uint32 length][StreamPacket]` until the phone closes the connection.
- The PC lists what it can decode in `accepted` (`CameraStart`, `ScreenStart`), in its order of preference; the phone takes the first it can encode. H.264 (Annex-B, Baseline or Main, no B-frames, so presentation and decode order match) is the default; HEVC only when listed, because many PCs lack its decoder. For audio the PC prefers `PCM_S16LE` 48 kHz on a LAN, `AAC` (raw access units, config in `codec_config`) otherwise.
- Key frames about every 2 seconds, and at once when the PC sends `RequestKeyFrame` (after a decode error or a resize). Camera 1080p30 aims at 6 Mbps with a 1.5 Mbps floor; the screen 4 to 8 Mbps, at most 1920 px on the long side. The phone lowers the bitrate when the connection falls behind.
- `codec_config` comes before the first key frame and again before every later key frame, so the PC can join after a loss. When the connection falls behind, the phone drops video frames instead of queueing them; audio is never dropped.
- Timestamps are the phone's monotonic microseconds; the PC uses them only relative to each other.

## Camera

- The PC sends `CameraStart` when an app opens Fenpo Kamera (`consumer` is shown on the phone, e.g. "Teams'te canlı").
- Android opens the camera only after a tap on the phone: the phone answers `CameraStatus` `WAITING_FOR_TAP` and shows a "Start camera" notification, then reports `STARTING` with `stream_id`, opens the media connection and reports `STREAMING`.
- The camera's microphone needs a virtual microphone on the PC, which Windows offers only through an audio driver; until the PC has one it does not ask for audio in `accepted`.
- `CameraSettingsChange` goes both ways for facing, zoom, microphone and center stage; the phone confirms what it applied with `CameraStatus`. Either side may send `CameraStop`; the phone then reports `STOPPED` and closes the media connection.

## Screen mirroring (Android)

- The PC sends `ScreenStart`. Android asks for consent on the phone every time: `ScreenStatus` `WAITING_FOR_CONSENT`, then `STREAMING` with `stream_id` and the media connection. A rotation changes `video` and starts with a new key frame on the same connection.
- With `audio`, the phone also sends its own sound (Android's playback capture; apps that forbid capture stay silent), played on the PC.
- When the phone locks, Android ends the capture: `PAUSED_LOCKED`. Mirroring resumes only with a new consent on the phone.
- `ScreenInput` points are fractions of the streamed frame (0..1 from the top left). Control needs Fenpo's accessibility service on the phone; `control_available` says whether it is on, and input without it is answered with `Error` `UNSUPPORTED`. Windows that protect their content (`FLAG_SECURE`, banking apps) arrive black.

## Messages (Android without MAP)

- With MAP on the phone the PC reads and sends messages over classic Bluetooth and does not use these. `CAPABILITY_MESSAGES` is for phones whose Bluetooth has no message access.
- The PC asks with `SmsSyncRequest` (conversations, newest first) and `SmsMessagesRequest` (one conversation, paged by `before`). The phone pushes every new incoming or outgoing message as `SmsMessage`, and the status changes of sent ones (`PENDING`, then `SENT` or `FAILED`).
- `SmsSend` is answered with `SmsSendResult`; the stored message follows as `SmsMessage`.
- Android does not let an app that is not the default SMS app mark messages read, so there is no "mark read". MMS content and RCS are not available to other apps; the PC says so in the conversation.
- Contact names come from the PC's phone book (PBAP); the phone sends numbers only.

## PC input (touchpad, keyboard, presentation remote)

- `CAPABILITY_PC_INPUT`, phone → PC only. The PC injects `PcInput` into the current Windows session; it ignores input while the session is locked (the phone can still send `LockPc`).
- Pointer moves are relative, in the PC's pixels after the phone's speed setting, summed on the phone and sent at most 60 times a second. A drag is `PRESS`, moves, `RELEASE`. Scrolls are in wheel notches and may be fractional.
- Pointer moves are in the phone's pixels after its speed setting; the PC adds its own acceleration.
- Text goes as Unicode, so it does not depend on the PC's keyboard layout; `PcKey` is for keys that are not text, with modifiers, a phase (tap, down, up) and optionally a Windows virtual-key code.
- The presentation remote sends `presenter` (next, previous, start, end, black; the PC maps them to Right, Left, F5, Esc, B), `laser` (a dot the PC draws at a point of its primary screen) and `keep_awake` (the PC's screen stays on until it is turned off or the phone disconnects).
- Only the paired phone in use is accepted. Input does not reach elevated windows or the secure desktop (Windows UIPI).
- Input goes on the best open channel in order; over Bluetooth only, the phone may send fewer moves.

## PC notifications on the phone

- `CAPABILITY_PC_NOTIFICATIONS`, PC → phone. The PC reads its own notifications through the Windows notification
  listener, with its user's permission, and sends only those of the apps its user picked in Fenpo.
- `PcNotification` goes on the best open channel when it appears, `PcNotificationRemoved` when it is gone on the PC.
  The phone shows a local notification and removes it again. On BLE the message must fit the sealed limit: no
  `app_icon_png`, shortened `text`. The phone caches icons per `app_id`.
- On iOS the BLE channel is what wakes Fenpo in the background, so a notification arrives while the app is not open.

## The PC's shared folders on the phone

- `CAPABILITY_FILE_PROVIDER`. The iPhone shows them in the Files app (File Provider extension), Android in the system
  file picker (DocumentsProvider). The PC shares only the folders its user picked; nothing else is reachable.
- Paths are relative to the shared folders with `/` between parts; `""` lists the shared folders. The PC answers an
  absolute path, `..`, a path through a link or one that leaves a shared folder with `Error` `INVALID_REQUEST`, and a
  missing item the same way.
- `FolderListRequest` → `FolderListing`, `FileThumbnailRequest` → `FileThumbnail`, `FileChange` → `FileChangeResult`,
  all with the request's `reply_to`. A deleted item goes to the PC's Recycle Bin.
- Contents are ordinary transfers. Download: the phone sends `FileFetch` with its own `transfer_id`; the PC answers with
  a `TransferOffer` carrying that id, the phone accepts it without asking and opens the data connection as for any PC
  to phone transfer. Upload: the phone's `TransferOffer` sets `destination_path`; a name that exists gets " (2)".
- `FolderChanged` tells the phone that a folder it listed changed on the PC, so it lists it again.

## Photo library from the PC

- `CAPABILITY_PHOTOS`, while Fenpo on the phone runs. Lists and thumbnails go only over Wi‑Fi; on BLE the phone answers
  `Error` `BUSY`.
- `PhotoListRequest` pages the library newest first (`before`, at most 200 per page) → `PhotoList` with `access`
  (`DENIED` has no items; the PC tells its user to allow photo access on the phone).
- `PhotoThumbnailRequest` (at most 60 ids) → one `PhotoThumbnail` per id. `PhotoFetch` with the PC's `transfer_id` →
  the phone offers the originals in one `TransferOffer` with that id and the PC accepts it without asking.

## Hotspot

- `CAPABILITY_HOTSPOT`. Apps cannot read the hotspot's name and password or turn it on, on iOS or Android, so the
  phone's user types them into Fenpo once. The phone sends `HotspotInfo` after `Hello` and on every change (`cleared`
  when removed); the PC keeps them with DPAPI and adds the Windows Wi‑Fi profile itself.
- "Connect to hotspot" on the PC sends `HotspotRequest`. The phone answers `HotspotStatus`: `ASKED_USER` after showing
  a notification that opens its hotspot settings (Android) or says where it is (iOS), `ON` when it knows the hotspot is
  on, `NOT_SET_UP` without saved details. The PC joins as soon as the network is in range, for up to a minute.

## Screen mirroring from the iPhone

- Only the iPhone's user can start a ReplayKit broadcast. A `ScreenStart` is answered with `WAITING_FOR_CONSENT` and the
  iPhone shows how to start it; once it runs, Fenpo's broadcast extension opens the media connection and the iPhone
  reports `STREAMING` without a `ScreenStart`. There is no control from the PC: `control_available` is false and
  `ScreenInput` is answered with `Error` `UNSUPPORTED`.

## Proximity

- An Android phone does not advertise by itself, so the PC cannot measure it the way it measures an iPhone (DECISIONS #29). The phone reads the RSSI of its BLE link to the PC and sends it as `DeviceState.ble_rssi_dbm`, at most every 5 seconds and only when it moved by 4 dB or more, or every 30 seconds.

## Rules

- This is a draft of version 1. Until the first public release, fields may change; after that, only additive changes.
- Never reuse or renumber a field. Remove with `reserved`.
- New features get a `Capability` value and stay inside their `Envelope` field range.
- A protocol change is its own commit (`protocol: ...`) and updates `docs/ARCHITECTURE.md` in the same commit.
