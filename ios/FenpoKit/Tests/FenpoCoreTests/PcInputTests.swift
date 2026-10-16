import FenpoCore
import FenpoProtocol
import Testing

struct PcInputCoalescerTests {
    @Test
    func sumsMovesIntoOneFrame() {
        var coalescer = PcInputCoalescer()
        coalescer.move(dx: 3, dy: -1)
        coalescer.move(dx: 2, dy: 4)
        #expect(coalescer.flush() == [.move(dx: 5, dy: 3)])
        #expect(coalescer.flush().isEmpty)
    }

    @Test
    func sendsPendingMoveBeforeAClick() {
        var coalescer = PcInputCoalescer()
        coalescer.move(dx: 10, dy: 0)
        coalescer.scroll(dx: 0, dy: 0.5)
        #expect(coalescer.then(.click()) == [.move(dx: 10, dy: 0), .scroll(dx: 0, dy: 0.5), .click(.left, .click)])
        #expect(coalescer.then(.click(.right)) == [.click(.right, .click)])
    }

    @Test
    func laserIsClampedToTheScreen() {
        let input = Fenpo_V1_PcInput.laser(active: true, x: 1.4, y: -0.2)
        #expect(input.laser.x == 1)
        #expect(input.laser.y == 0)
        #expect(input.laser.active)
    }
}

struct TypedTextDiffTests {
    @Test
    func typingSendsOnlyTheNewCharacters() {
        #expect(TypedTextDiff.inputs(from: "mer", to: "merhaba") == [.text("haba")])
    }

    @Test
    func deletingSendsBackspaces() {
        #expect(TypedTextDiff.inputs(from: "merhaba", to: "merh") == [.key(.backspace), .key(.backspace), .key(.backspace)])
    }

    @Test
    func autocorrectReplacesTheChangedPart() {
        #expect(TypedTextDiff.inputs(from: "teh", to: "the ") == [.key(.backspace), .key(.backspace), .text("he ")])
    }

    @Test
    func turkishCharactersCountAsOne() {
        #expect(TypedTextDiff.inputs(from: "ş", to: "şğ") == [.text("ğ")])
    }
}
