import SwiftUI

/// An icon from SF Symbols, or one drawn from the design when SF Symbols has no match.
enum FenpoIcon: Equatable {
    case symbol(String)
    case bluetooth

    static let home = FenpoIcon.symbol("house")
    static let transfers = FenpoIcon.symbol("arrow.up.arrow.down")
    static let pc = FenpoIcon.symbol("display")
    static let settings = FenpoIcon.symbol("gearshape")
    static let clipboard = FenpoIcon.symbol("doc.on.clipboard")
    static let paste = FenpoIcon.symbol("arrow.down.doc")
    static let photo = FenpoIcon.symbol("photo")
    static let camera = FenpoIcon.symbol("camera")
    static let lock = FenpoIcon.symbol("lock")
    static let sun = FenpoIcon.symbol("sun.max")
    static let globe = FenpoIcon.symbol("globe")
    static let bell = FenpoIcon.symbol("bell")
    static let wifi = FenpoIcon.symbol("wifi")
    static let hand = FenpoIcon.symbol("hand.tap")
    static let moon = FenpoIcon.symbol("moon")
    static let usb = FenpoIcon.symbol("cable.connector")
}

/// Draws a `FenpoIcon` at the current font size.
struct FenpoIconView: View {
    let icon: FenpoIcon

    var body: some View {
        switch icon {
        case .symbol(let name):
            Image(systemName: name)
        case .bluetooth:
            BluetoothGlyph()
                .stroke(style: StrokeStyle(lineWidth: 1.8, lineCap: .round, lineJoin: .round))
                .aspectRatio(1, contentMode: .fit)
                .frame(height: 18)
                .accessibilityHidden(true)
        }
    }
}

/// The Bluetooth rune from design/mobile/mobile.html (`i-bt`, 24 x 24 view box).
struct BluetoothGlyph: Shape {
    func path(in rect: CGRect) -> Path {
        let scale = min(rect.width, rect.height) / 24
        let origin = CGPoint(x: rect.midX - 12 * scale, y: rect.midY - 12 * scale)
        func point(_ x: CGFloat, _ y: CGFloat) -> CGPoint {
            CGPoint(x: origin.x + x * scale, y: origin.y + y * scale)
        }

        var path = Path()
        path.move(to: point(7, 7))
        path.addLine(to: point(17, 17))
        path.addLine(to: point(12, 21))
        path.addLine(to: point(12, 3))
        path.addLine(to: point(17, 7))
        path.addLine(to: point(7, 17))
        return path
    }
}

/// A colored rounded square with a white icon, used in tiles and list rows.
struct IconBadge: View {
    let icon: FenpoIcon
    let color: Color
    var size: CGFloat = 30
    var cornerRadius: CGFloat = 8

    var body: some View {
        FenpoIconView(icon: icon)
            .font(.system(size: size * 0.55, weight: .medium))
            .foregroundStyle(.onAccent)
            .frame(width: size, height: size)
            .background(color, in: .rect(cornerRadius: cornerRadius, style: .continuous))
    }
}
