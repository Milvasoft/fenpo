import Foundation

/// What plays on the PC, for the PC tab's "Now playing" (the PC's `MediaState`).
public struct PCMedia: Equatable, Sendable {
    public var appName: String
    public var title: String
    public var artist: String
    public var isPlaying: Bool
    /// Track length; 0 when unknown (live streams).
    public var durationMs: UInt64
    /// Position at `positionAt`; it moves on from there while playing.
    public var positionMs: UInt64
    public var positionAt: Date
    /// The PC's media volume, 0-100.
    public var volumePercent: Int
    public var canPrevious: Bool
    public var canNext: Bool
    /// Cover art; only over Wi-Fi.
    public var artwork: Data?

    /// Creates a state.
    public init(
        appName: String,
        title: String,
        artist: String,
        isPlaying: Bool,
        durationMs: UInt64 = 0,
        positionMs: UInt64 = 0,
        positionAt: Date = .now,
        volumePercent: Int,
        canPrevious: Bool,
        canNext: Bool,
        artwork: Data? = nil
    ) {
        self.appName = appName
        self.title = title
        self.artist = artist
        self.isPlaying = isPlaying
        self.durationMs = durationMs
        self.positionMs = positionMs
        self.positionAt = positionAt
        self.volumePercent = volumePercent
        self.canPrevious = canPrevious
        self.canNext = canNext
        self.artwork = artwork
    }

    /// The position now: moves on from `positionAt` while playing, never past the end.
    /// - Parameter now: The current time.
    public func position(at now: Date) -> UInt64 {
        guard isPlaying else {
            return positionMs
        }
        let moved = UInt64(max(0, now.timeIntervalSince(positionAt)) * 1000)
        let position = positionMs + moved
        return durationMs > 0 ? min(position, durationMs) : position
    }

    /// The volume one step (10) up or down, kept within 0-100.
    /// - Parameter up: Louder or quieter.
    public func volumeStep(up: Bool) -> Int {
        min(100, max(0, volumePercent + (up ? 10 : -10)))
    }

    /// The title, or the app's name when the player gives none.
    public var displayTitle: String {
        title.isEmpty ? appName : title
    }
}

/// What the PC tab's media buttons ask of the PC.
public enum PCMediaCommand: Equatable, Sendable {
    case toggle
    case next
    case previous
    case volume(Int)
}
