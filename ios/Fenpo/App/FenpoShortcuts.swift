import AppIntents

/// Siri and Shortcuts phrases; the controls and the Action button use the same intents.
struct FenpoShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: SendClipboardToPCIntent(),
            phrases: [
                "Send clipboard to PC with \(.applicationName)",
                "Send my clipboard with \(.applicationName)",
            ],
            shortTitle: "intent.sendClipboard.short",
            systemImageName: "doc.on.clipboard"
        )
        AppShortcut(
            intent: PasteFromPCIntent(),
            phrases: [
                "Paste from PC with \(.applicationName)",
            ],
            shortTitle: "intent.pasteFromPC.short",
            systemImageName: "arrow.down.doc"
        )
        AppShortcut(
            intent: LockPCIntent(),
            phrases: [
                "Lock my PC with \(.applicationName)",
            ],
            shortTitle: "intent.lockPC.short",
            systemImageName: "lock.display"
        )
    }
}
