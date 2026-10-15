import SwiftUI

/// Settings row from the design: 30 pt icon badge, title, trailing accessory.
struct SettingsRowLayout<Accessory: View>: View {
    let icon: FenpoIcon
    let color: Color
    let title: LocalizedStringKey
    var titleColor: Color = .textPrimary
    @ViewBuilder let accessory: Accessory

    var body: some View {
        HStack(spacing: 12) {
            IconBadge(icon: icon, color: color)
            Text(title)
                .foregroundStyle(titleColor)
                .frame(maxWidth: .infinity, alignment: .leading)
            accessory
        }
        .padding(.horizontal, 16)
        .frame(minHeight: 50)
    }
}

/// A row that shows a value and a chevron and opens something when tapped.
struct DisclosureRow: View {
    let icon: FenpoIcon
    let color: Color
    let title: LocalizedStringKey
    let value: Text
    var valueColor: Color = .textSecondary
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            SettingsRowLayout(icon: icon, color: color, title: title) {
                value
                    .foregroundStyle(valueColor)
                Image(systemName: "chevron.right")
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(.textTertiary)
            }
            .contentShape(.rect)
        }
        .buttonStyle(.plain)
    }
}

/// A row with a value and no action.
struct ValueRow: View {
    let icon: FenpoIcon
    let color: Color
    let title: LocalizedStringKey
    let value: Text
    var valueColor: Color = .textSecondary

    var body: some View {
        SettingsRowLayout(icon: icon, color: color, title: title) {
            value
                .foregroundStyle(valueColor)
        }
    }
}

/// A row with a switch.
struct ToggleRow: View {
    let icon: FenpoIcon
    let color: Color
    let title: LocalizedStringKey
    @Binding var isOn: Bool

    @Environment(\.isEnabled) private var isEnabled

    var body: some View {
        SettingsRowLayout(icon: icon, color: color, title: title) {
            Toggle(title, isOn: $isOn)
                .labelsHidden()
        }
        .opacity(isEnabled ? 1 : 0.5)
    }
}
