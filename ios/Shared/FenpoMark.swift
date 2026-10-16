import SwiftUI

/// The Çekirdek logo (design/logo/svg/1-cekirdek.svg, 64 x 64 view box) drawn at any size.
struct FenpoMark: View {
    var body: some View {
        GeometryReader { proxy in
            let side = min(proxy.size.width, proxy.size.height)
            let scale = side / 64
            ZStack {
                RoundedRectangle(cornerRadius: 19 * scale, style: .continuous)
                    .fill(Color.bloom)
                    .frame(width: 58 * scale, height: 58 * scale)
                Circle()
                    .fill(Color.kernel)
                    .frame(width: 42 * scale, height: 42 * scale)
                SeedsShape()
                    .fill(Color.markSeed)
            }
            .frame(width: side, height: side)
        }
        .aspectRatio(1, contentMode: .fit)
        .accessibilityHidden(true)
    }
}

/// The five seeds around the center.
private struct SeedsShape: Shape {
    func path(in rect: CGRect) -> Path {
        let scale = min(rect.width, rect.height) / 64
        func point(_ x: CGFloat, _ y: CGFloat) -> CGPoint {
            CGPoint(x: x, y: y)
        }

        var seed = Path()
        seed.move(to: point(32, 15.5))
        seed.addCurve(to: point(32, 27.1), control1: point(35.4, 19.7), control2: point(35.7, 24.5))
        seed.addCurve(to: point(32, 15.5), control1: point(28.3, 24.5), control2: point(28.6, 19.7))
        seed.closeSubpath()

        var path = Path()
        for index in 0..<5 {
            let angle = CGFloat(index) * 72 * .pi / 180
            let rotation = CGAffineTransform(translationX: 32, y: 32)
                .rotated(by: angle)
                .translatedBy(x: -32, y: -32)
            path.addPath(seed, transform: rotation)
        }
        let origin = CGPoint(x: rect.midX - 32 * scale, y: rect.midY - 32 * scale)
        return path.applying(CGAffineTransform(a: scale, b: 0, c: 0, d: scale, tx: origin.x, ty: origin.y))
    }
}
