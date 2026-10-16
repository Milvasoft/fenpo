import FenpoApple
import UIKit
import UserNotifications

/// Logs why and how the app was launched; device test S4 reads this to see BLE relaunches.
final class FenpoAppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    private let log = FenpoLog("App")

    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        let centrals = launchOptions?[.bluetoothCentrals] as? [String] ?? []
        let reason = centrals.isEmpty ? "user or system" : "Bluetooth restoration \(centrals)"
        log.info("Launch \(LocalDevice.appVersion), reason: \(reason), app \(application.applicationState.logName)")
        UNUserNotificationCenter.current().delegate = self
        return true
    }

    /// Notifications show while Fenpo is open too: the PC's notifications, files that arrived, the hotspot hint.
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification) async -> UNNotificationPresentationOptions {
        [.banner, .list, .sound]
    }

    func applicationWillTerminate(_ application: UIApplication) {
        log.info("Will terminate")
    }
}
