import AppKit
import Carbon
import SwiftUI

@MainActor
final class DisplayPanelModel: ObservableObject {
    enum Status {
        case message(String)
        case screenCapturePermissionRequired

        var text: String {
            switch self {
            case let .message(text): return text
            case .screenCapturePermissionRequired:
                return "请在系统设置中允许「屏幕切换」录制屏幕，然后退出并重新打开应用，再试一次投屏。"
            }
        }
    }

    private let configuration = DisplayConfiguration()
    private let preview = ScreenPreview()
    private let defaults = UserDefaults.standard
    private var rollback: [Display]?
    private var timer: Timer?
    private var confirmationDeadline: Date?

    @Published private(set) var displays: [Display] = []
    @Published private(set) var panelDisplayID = NSScreen.main?.displayID
    @Published private(set) var isCasting = false
    @Published var selection: Set<DisplayID> = []
    @Published private(set) var status: Status?
    @Published var pending: Int?
    @Published var ballVisible: Bool {
        didSet {
            defaults.set(ballVisible, forKey: "ballVisible")
            onBallVisibilityChanged?(ballVisible)
        }
    }
    @Published var shortcutKey: UInt32? {
        didSet {
            defaults.set(shortcutKey.map(Int.init) ?? -1, forKey: "shortcutKey")
            onShortcutChanged?()
        }
    }
    @Published var shortcutModifiers: UInt32 {
        didSet {
            defaults.set(Int(shortcutModifiers), forKey: "shortcutModifiers")
            onShortcutChanged?()
        }
    }
    @Published var shortcutUnavailable = false

    var onBallVisibilityChanged: ((Bool) -> Void)?
    var onShortcutChanged: (() -> Void)?
    var onPendingChange: (() -> Void)?
    var onShowPanel: (() -> Void)?

    init() {
        let savedKey = defaults.object(forKey: "shortcutKey") as? Int ?? kVK_ANSI_E
        shortcutKey = UInt32(exactly: savedKey)
        shortcutModifiers = UInt32(exactly: defaults.object(forKey: "shortcutModifiers") as? Int ?? (controlKey | optionKey))
            ?? UInt32(controlKey | optionKey)
        ballVisible = defaults.object(forKey: "ballVisible") as? Bool ?? true
        preview.onClose = { [weak self] in self?.isCasting = false }
        preview.onError = { [weak self] message in
            self?.status = .message(message)
            self?.onShowPanel?()
        }
        preview.onPermissionRequired = { [weak self] in
            self?.status = .screenCapturePermissionRequired
            self?.onShowPanel?()
        }
    }

    var canSelectDisplays: Bool { configuration.canSelectDisplays }
    var extended: [Display] { displays.filter { $0.enabled && $0.mirrorSource == 0 } }
    var castSources: [Display] {
        guard let panelDisplayID else { return [] }
        return extended.filter { $0.id != panelDisplayID }
    }
    var enabledIDs: Set<DisplayID> { Set(displays.filter(\.enabled).map(\.id)) }
    var shortcutTitle: String {
        guard let shortcutKey else { return "已关闭" }
        let symbols = [(controlKey, "⌃"), (optionKey, "⌥"), (shiftKey, "⇧"), (cmdKey, "⌘")]
            .compactMap { shortcutModifiers & UInt32($0.0) != 0 ? $0.1 : nil }.joined()
        return symbols + (Self.shortcutKeys.first { $0.1 == shortcutKey }?.0 ?? "")
    }

    static let shortcutKeys: [(String, UInt32)] = [
        ("A", UInt32(kVK_ANSI_A)), ("B", UInt32(kVK_ANSI_B)), ("C", UInt32(kVK_ANSI_C)),
        ("D", UInt32(kVK_ANSI_D)), ("E", UInt32(kVK_ANSI_E)), ("F", UInt32(kVK_ANSI_F)),
        ("G", UInt32(kVK_ANSI_G)), ("H", UInt32(kVK_ANSI_H)), ("I", UInt32(kVK_ANSI_I)),
        ("J", UInt32(kVK_ANSI_J)), ("K", UInt32(kVK_ANSI_K)), ("L", UInt32(kVK_ANSI_L)),
        ("M", UInt32(kVK_ANSI_M)), ("N", UInt32(kVK_ANSI_N)), ("O", UInt32(kVK_ANSI_O)),
        ("P", UInt32(kVK_ANSI_P)), ("Q", UInt32(kVK_ANSI_Q)), ("R", UInt32(kVK_ANSI_R)),
        ("S", UInt32(kVK_ANSI_S)), ("T", UInt32(kVK_ANSI_T)), ("U", UInt32(kVK_ANSI_U)),
        ("V", UInt32(kVK_ANSI_V)), ("W", UInt32(kVK_ANSI_W)), ("X", UInt32(kVK_ANSI_X)),
        ("Y", UInt32(kVK_ANSI_Y)), ("Z", UInt32(kVK_ANSI_Z))
    ]

    func reload(resetSelection: Bool = false) {
        do {
            let previous = enabledIDs
            let wasMirroring = displays.contains { $0.mirrorSource != 0 }
            displays = try configuration.displays()
            if resetSelection || previous != enabledIDs || wasMirroring != displays.contains(where: { $0.mirrorSource != 0 }) {
                selection = enabledIDs
            } else {
                selection.formIntersection(displays.map(\.id))
            }
            preview.update(available: extended)
        } catch {
            status = .message(error.localizedDescription)
        }
    }

    func showAll() {
        apply { try configuration.show(Set(displays.map(\.id))) }
    }

    func showSelected() {
        apply { try configuration.show(selection) }
    }

    func showOnly(_ display: Display) {
        apply { try configuration.show([display.id]) }
    }

    func modes(for display: Display) -> [CGDisplayMode] {
        configuration.modes(for: display.id)
    }

    func changeMode(_ mode: CGDisplayMode, on display: Display) {
        apply { try configuration.changeMode(mode, on: display.id) }
    }

    func arrange(_ positions: [DisplayID: CGPoint]) {
        apply { try configuration.arrange(positions) }
    }

    func setPanelDisplay(_ id: DisplayID?) {
        guard panelDisplayID != id else { return }
        panelDisplayID = id
    }

    func showPreview(_ display: Display) {
        guard let destination = NSScreen.screens.first(where: { $0.displayID == panelDisplayID }),
              let source = castSources.first(where: { $0.id == display.id }) else {
            reload()
            status = .message(DisplayError.changed.localizedDescription)
            return
        }
        status = nil
        if preview.show(source, on: destination, available: extended) {
            isCasting = true
        }
    }

    func stopPreview() {
        preview.close()
    }

    func keep() {
        finish()
        status = nil
        reload(resetSelection: true)
    }

    func revert() {
        guard rollback != nil else { return }
        do {
            try restorePending()
            status = .message("已恢复之前的显示设置。")
        } catch {
            status = .message(error.localizedDescription)
        }
        reload(resetSelection: true)
    }

    private func apply(_ change: () throws -> Void) {
        guard rollback == nil else { return }
        status = nil
        do {
            rollback = try configuration.displays()
            try change()
            reload(resetSelection: true)
            confirmationDeadline = Date().addingTimeInterval(15)
            pending = 15
            let timer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.tick() }
            }
            self.timer = timer
            RunLoop.main.add(timer, forMode: .common)
            onPendingChange?()
        } catch {
            var message = error.localizedDescription
            do {
                try restorePending()
            } catch {
                message += " 恢复失败，\(error.localizedDescription)"
            }
            reload(resetSelection: true)
            status = .message(message)
        }
    }

    private func restorePending() throws {
        guard let rollback else { return }
        finish()
        try configuration.restore(rollback)
    }

    private func tick() {
        guard let confirmationDeadline else { return }
        let remaining = confirmationDeadline.timeIntervalSinceNow
        if remaining <= 0 {
            revert()
        } else {
            pending = Int(ceil(remaining))
        }
    }

    private func finish() {
        timer?.invalidate()
        timer = nil
        confirmationDeadline = nil
        pending = nil
        rollback = nil
    }
}
