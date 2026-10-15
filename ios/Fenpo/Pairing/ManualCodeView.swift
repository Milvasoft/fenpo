import SwiftUI

/// Entry for the 6 digit code shown under the QR code on the PC.
struct ManualCodeView: View {
    let onSubmit: (String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var code = ""
    @FocusState private var isFocused: Bool

    private static let length = 6

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 16) {
                Text("pairing.code.message")
                    .foregroundStyle(.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)

                TextField("pairing.code.placeholder", text: $code)
                    .keyboardType(.numberPad)
                    .textContentType(.oneTimeCode)
                    .font(.system(.largeTitle, design: .monospaced).weight(.semibold))
                    .multilineTextAlignment(.center)
                    .foregroundStyle(.textPrimary)
                    .padding(.vertical, 14)
                    .fenpoCard(cornerRadius: 18)
                    .focused($isFocused)
                    .onChange(of: code) { _, newValue in
                        let digits = String(newValue.filter(\.isNumber).prefix(Self.length))
                        if digits != newValue {
                            code = digits
                        }
                    }

                Button("pairing.code.connect") {
                    onSubmit(code)
                }
                .buttonStyle(PrimaryPillButtonStyle())
                .disabled(code.count != Self.length)

                Spacer(minLength: 0)
            }
            .padding(20)
            .fenpoScreenBackground()
            .navigationTitle("pairing.manualEntry")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("common.cancel") {
                        dismiss()
                    }
                }
            }
            .onAppear {
                isFocused = true
            }
        }
        .presentationDetents([.medium, .large])
    }
}

#Preview("Light · Türkçe") {
    ManualCodeView { _ in }.preview(.light, "tr")
}

#Preview("Dark · English") {
    ManualCodeView { _ in }.preview(.dark, "en")
}
