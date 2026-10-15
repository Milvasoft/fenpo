import FenpoApple
import FenpoCore
import SwiftUI

/// System, Light or Dark. The choice applies at once and is shared with the share extension.
struct AppearancePickerView: View {
    @Environment(AppearanceStore.self) private var appearance

    var body: some View {
        ScrollView {
            CardRows(separatorInset: 16) {
                ForEach(AppearancePreference.allCases, id: \.self) { option in
                    Button {
                        appearance.preference = option
                    } label: {
                        HStack {
                            Text(option.title)
                                .foregroundStyle(.textPrimary)
                            Spacer()
                            if appearance.preference == option {
                                Image(systemName: "checkmark")
                                    .font(.body.weight(.semibold))
                                    .foregroundStyle(.accent)
                            }
                        }
                        .padding(.horizontal, 16)
                        .frame(minHeight: 50)
                        .contentShape(.rect)
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(appearance.preference == option ? .isSelected : [])
                }
            }
            .padding(16)
        }
        .fenpoScreenBackground()
        .navigationTitle("settings.appearance")
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview("Light · Türkçe") {
    NavigationStack { AppearancePickerView() }.sampleModel().preview(.light, "tr")
}

#Preview("Dark · English") {
    NavigationStack { AppearancePickerView() }.sampleModel().preview(.dark, "en")
}
