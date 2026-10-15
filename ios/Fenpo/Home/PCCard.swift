import FenpoCore
import SwiftUI

/// The paired PC with its connection paths. Top of the home screen and the PC tab.
struct PCCard: View {
    let pc: PairedPC
    let isConnected: Bool
    let proximity: Proximity
    let links: ChannelLinks

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 14) {
                LaptopIllustration()
                VStack(alignment: .leading, spacing: 0) {
                    Text(isConnected ? "pc.status.connected" : "pc.status.disconnected")
                        .font(.footnote)
                        .foregroundStyle(.textSecondary)
                    Text(verbatim: pc.name)
                        .font(.title3.weight(.semibold))
                        .foregroundStyle(.textPrimary)
                    placeLine
                        .font(.footnote)
                        .foregroundStyle(.textSecondary)
                }
            }
            HStack(spacing: 6) {
                LinkPill(title: "link.bluetooth", isUp: links.bluetooth)
                LinkPill(title: "link.wifi", isUp: links.wifi)
                LinkPill(title: "link.usb", isUp: links.usb)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(alignment: .topTrailing) {
            Circle()
                .fill(RadialGradient(colors: [Color.bloom.opacity(0.28), Color.bloom.opacity(0)], center: .center, startRadius: 0, endRadius: 120))
                .frame(width: 240, height: 240)
                .offset(x: 60, y: -80)
        }
        .fenpoCard()
        .clipShape(.rect(cornerRadius: 26, style: .continuous))
        .accessibilityElement(children: .combine)
    }

    private var placeLine: Text {
        switch proximity {
        case .near:
            Text("pc.place.near \(pc.systemName)")
        case .away:
            Text("pc.place.away \(pc.systemName)")
        case .unknown:
            Text(verbatim: pc.systemName)
        }
    }
}

/// One connection path with a status dot.
struct LinkPill: View {
    let title: LocalizedStringKey
    let isUp: Bool

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(isUp ? Color.leaf : Color.textTertiary)
                .frame(width: 7, height: 7)
            Text(title)
                .lineLimit(1)
        }
        .font(.caption)
        .foregroundStyle(.textSecondary)
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.canvas, in: .rect(cornerRadius: 14, style: .continuous))
    }
}

/// Small laptop drawing from the design: glass-to-blush screen, dark bezel and base.
struct LaptopIllustration: View {
    var body: some View {
        RoundedRectangle(cornerRadius: 6, style: .continuous)
            .fill(LinearGradient(colors: [.haze, .blush], startPoint: .topLeading, endPoint: .bottomTrailing))
            .overlay {
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .strokeBorder(.laptopFrame, lineWidth: 3)
            }
            .frame(width: 64, height: 44)
            .overlay(alignment: .bottom) {
                UnevenRoundedRectangle(bottomLeadingRadius: 5, bottomTrailingRadius: 5)
                    .fill(.laptopFrame)
                    .frame(width: 82, height: 5)
                    .offset(y: 9)
            }
            .padding(.horizontal, 9)
            .padding(.bottom, 9)
            .accessibilityHidden(true)
    }
}

#Preview("Light · Türkçe") {
    PCCard(pc: SampleSession.pairedSnapshot().pc!, isConnected: true, proximity: .near, links: SampleSession.pairedSnapshot().links)
        .padding()
        .fenpoScreenBackground()
        .preview(.light, "tr")
}

#Preview("Dark · English") {
    PCCard(pc: SampleSession.pairedSnapshot().pc!, isConnected: false, proximity: .away, links: .none)
        .padding()
        .fenpoScreenBackground()
        .preview(.dark, "en")
}
