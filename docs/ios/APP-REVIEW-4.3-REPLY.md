# Fenpo Link: reply to App Review (4.3 Spam / 4.2.6)

Draft reply to the 2026-10-18 rejection (submission 7ad4376a-9c90-408c-bd9d-aa78aabadb01, build 1.0.0 (89)).
Apple asked: if the app already meets 4.3(a), 4.3(b) and 4.2.6, do not resubmit, reply and answer all nine questions.
Confirmed by Buğra on 2026-10-26; the text between the --- lines is what goes to Resolution Center.

---

Hello,

Thank you for the review. We believe Fenpo meets guidelines 4.3(a), 4.3(b) and 4.2.6, so we are answering the questions instead of resubmitting.

1. What the app does
Fenpo connects an iPhone to the user's own Windows 11 PC, together with its companion Fenpo for Windows. It provides:
- two-way clipboard (Control Center, Lock Screen widget, Shortcuts)
- file and photo transfer over Wi-Fi, with a Live Activity
- the PC's shared folders in the Files app (File Provider extension)
- the iPhone as the PC's webcam
- screen mirroring to the PC (ReplayKit broadcast)
- touchpad, keyboard and presentation remote for the PC
- media control, PC lock, "ring my iPhone", PC notifications and Focus sync
Pairing uses AccessorySetupKit and a QR code. After that, a Bluetooth LE link plus TLS 1.3 with pinned certificates on the local network is used. There is no server and no account. Data never leaves the user's two devices.

2. Intended users
People who use an iPhone and a Windows PC at the same desk every day: students, office workers, developers. They still move files and text by emailing themselves or through cloud drives and messengers.

3. The gap
Microsoft Phone Link supports iPhone only for notifications, messages and calls. It has no clipboard, no Files integration, no webcam and no remote. Intel Unison covered files and photos, but it was discontinued in June 2025. Other webcam and remote apps each do one thing, usually through a cloud relay or an account. Fenpo combines these features with no server and no account. It uses AccessorySetupKit, File Provider, ReplayKit, Live Activities, Focus filters and App Intents. It is open source (MIT): https://github.com/Milvasoft/fenpo

4. Beta testing
We tested internally in TestFlight on real iPhones paired with real Windows PCs. Issues found there and fixed in the production build:
- An iPhone at the edge of Bluetooth range was treated as a broken pairing. The PC now waits and reconnects.
- The accessory picker hid PCs that had been renamed. It no longer filters by name.
- With two PCs paired, a transfer could go to the wrong PC. This was fixed before 1.0.0.

5. Standalone or suite
Fenpo is a standalone product. Its only companion is Fenpo for Windows on the Microsoft Store (https://apps.microsoft.com/detail/9MTTKJ8BV1LG). Our other apps (a travel journal, an expense tracker, a period tracker, a home app, an invoice maker) are unrelated, and none of them connects to a PC.

6. In-app purchase in another app?
No. None of our other apps works with a PC, Bluetooth accessories, file providers or camera streaming. Fenpo needs its own extensions (File Provider, broadcast upload, Share, widgets) and its own entitlements (AccessorySetupKit, background Bluetooth).

7. Code shared with our other apps
None, beyond standard Apple frameworks.

8. Code shared with third-party apps
None. The file transfer protocol can interoperate with the open LocalSend protocol, but the implementation is our own. All of Fenpo's code is public.

9. Client, template or app generation service
No. Fenpo is our own product, designed and built by our team. It is not built from a template or a generation service, and it is published under our own account.

Testing without a Windows PC
The app needs Fenpo for Windows on a Windows 11 PC with Bluetooth. This video shows the full flow (pairing, clipboard, files, Files app folders, webcam, remote): https://youtu.be/_b9qKZQZxUc

We are happy to provide anything else you need. Thank you.
