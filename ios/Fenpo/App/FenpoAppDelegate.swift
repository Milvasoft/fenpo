import FenpoApple
import UIKit

/// Logs why and how the app was launched; device test S4 reads this to see BLE relaunches.
final class FenpoAppDelegate: NSObject, UIApplicationDelegate {
    private let log = FenpoLog("App")

    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        let centrals = launchOptions?[.bluetoothCentrals] as? [String] ?? []
        let reason = centrals.isEmpty ? "user or system" : "Bluetooth restoration \(centrals)"
        log.info("Launch \(LocalDevice.appVersion), reason: \(reason), app \(application.applicationState.logName)")
        return true
    }

    func applicationWillTerminate(_ application: UIApplication) {
        log.info("Will terminate")
    }
}
