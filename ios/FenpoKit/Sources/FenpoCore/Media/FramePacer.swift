/// Picks which frames of a source go to the encoder for a steady output rate. ReplayKit delivers
/// screen frames unevenly, anywhere between 40 and 60 a second while things move; skipping every
/// frame that comes "too soon" after the last one sent halves such a source to 20 to 26 a second.
/// The pacer keeps a schedule instead: a frame up to 0.4 of an interval early still counts, and a
/// slower source passes whole.
public struct FramePacer: Sendable {
    /// Seconds between output frames.
    public let interval: Double
    private var nextDue: Double?

    /// Creates a pacer.
    /// - Parameter fps: Output frames per second.
    public init(fps: Double) {
        interval = 1 / fps
    }

    /// Whether the frame at `time` goes out.
    /// - Parameter time: Presentation time in seconds.
    public mutating func accept(_ time: Double) -> Bool {
        guard let due = nextDue else {
            nextDue = time + interval
            return true
        }
        guard time >= due - interval * 0.4 else {
            return false
        }
        // Behind schedule (a slow or paused source): the schedule restarts from this frame.
        nextDue = max(due + interval, time + interval * 3 / 4)
        return true
    }
}
