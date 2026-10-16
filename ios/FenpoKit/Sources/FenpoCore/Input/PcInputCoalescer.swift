import FenpoProtocol

/// Orders the touchpad's input for the PC (protocol/README.md, "PC input"): pointer moves and
/// scrolls are summed and leave once per frame (at most 60 a second); every other input leaves at
/// once, after the sums before it, so a click never lands before the move that led to it.
public struct PcInputCoalescer: Sendable {
    private var moveX: Float = 0
    private var moveY: Float = 0
    private var scrollX: Float = 0
    private var scrollY: Float = 0

    /// Creates an empty coalescer.
    public init() {
    }

    /// Adds to the pending pointer move.
    /// - Parameters:
    ///   - dx: Horizontal move in the PC's pixels.
    ///   - dy: Vertical move.
    public mutating func move(dx: Float, dy: Float) {
        moveX += dx
        moveY += dy
    }

    /// Adds to the pending scroll.
    /// - Parameters:
    ///   - dx: Horizontal wheel notches.
    ///   - dy: Vertical wheel notches, positive scrolls the content down.
    public mutating func scroll(dx: Float, dy: Float) {
        scrollX += dx
        scrollY += dy
    }

    /// What a frame sends: the summed move and scroll, if any, and resets them.
    public mutating func flush() -> [Fenpo_V1_PcInput] {
        var out: [Fenpo_V1_PcInput] = []
        if moveX != 0 || moveY != 0 {
            out.append(.move(dx: moveX, dy: moveY))
            moveX = 0
            moveY = 0
        }
        if scrollX != 0 || scrollY != 0 {
            out.append(.scroll(dx: scrollX, dy: scrollY))
            scrollX = 0
            scrollY = 0
        }
        return out
    }

    /// The pending sums, then `input`.
    /// - Parameter input: An input that is not a move or a scroll.
    public mutating func then(_ input: Fenpo_V1_PcInput) -> [Fenpo_V1_PcInput] {
        flush() + [input]
    }
}

extension Fenpo_V1_PcInput {
    /// A relative pointer move.
    public static func move(dx: Float, dy: Float) -> Self {
        var move = Fenpo_V1_PcPointerMove()
        move.dx = dx
        move.dy = dy
        var input = Self()
        input.move = move
        return input
    }

    /// A wheel scroll in notches.
    public static func scroll(dx: Float, dy: Float) -> Self {
        var scroll = Fenpo_V1_PcScroll()
        scroll.dx = dx
        scroll.dy = dy
        var input = Self()
        input.scroll = scroll
        return input
    }

    /// A mouse button.
    public static func click(_ button: Fenpo_V1_PcButton = .left, _ kind: Fenpo_V1_PcClickKind = .click) -> Self {
        var click = Fenpo_V1_PcClick()
        click.button = button
        click.kind = kind
        var input = Self()
        input.click = click
        return input
    }

    /// A key that is not text, pressed and released.
    public static func key(_ code: Fenpo_V1_PcKeyCode) -> Self {
        var key = Fenpo_V1_PcKey()
        key.code = code
        key.phase = .tap
        var input = Self()
        input.key = key
        return input
    }

    /// Text typed as Unicode.
    public static func text(_ text: String) -> Self {
        var input = Self()
        input.text = text
        return input
    }

    /// A presentation remote action.
    public static func presenter(_ action: Fenpo_V1_PcPresenterAction) -> Self {
        var input = Self()
        input.presenter = action
        return input
    }

    /// The laser dot, at a point of the PC's primary screen (0...1 from the top left).
    public static func laser(active: Bool, x: Float = 0, y: Float = 0) -> Self {
        var laser = Fenpo_V1_PcLaser()
        laser.active = active
        laser.x = min(max(x, 0), 1)
        laser.y = min(max(y, 0), 1)
        var input = Self()
        input.laser = laser
        return input
    }

    /// Keeps the PC's screen on, or lets it sleep again.
    public static func keepAwake(_ isOn: Bool) -> Self {
        var input = Self()
        input.keepAwake = isOn
        return input
    }
}

/// What the keyboard field sends when its text changes: the new characters as text, or backspaces
/// for removed ones. Autocorrect replacing a word becomes backspaces and the new word.
public enum TypedTextDiff {
    /// Inputs that turn `old` on the PC into `new`.
    /// - Parameters:
    ///   - old: Text the field had.
    ///   - new: Text it has now.
    public static func inputs(from old: String, to new: String) -> [Fenpo_V1_PcInput] {
        let common = zip(old, new).prefix { $0 == $1 }.count
        let removed = old.count - common
        let added = String(new.dropFirst(common))
        var out = [Fenpo_V1_PcInput](repeating: .key(.backspace), count: removed)
        if !added.isEmpty {
            out.append(.text(added))
        }
        return out
    }
}
