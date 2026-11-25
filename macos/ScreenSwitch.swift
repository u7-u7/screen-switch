import AppKit
import Carbon
import SwiftUI

@main
enum ScreenSwitch {
    static func main() {
        let app = NSApplication.shared
        let controller = ScreenSwitchController()
        app.setActivationPolicy(.accessory)
        app.delegate = controller
        withExtendedLifetime(controller) { app.run() }
    }
}

@MainActor
final class ScreenSwitchController: NSObject, NSApplicationDelegate {
    private let model = DisplayPanelModel()
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let panel = NSPanel(contentRect: CGRect(x: 0, y: 0, width: 370, height: 510),
                                styleMask: [.titled, .closable, .utilityWindow],
                                backing: .buffered, defer: false)
    private var ball: FloatingBall?
    private var shortcut: GlobalShortcut?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let icon = NSImage(systemSymbolName: "rectangle.on.rectangle", accessibilityDescription: "屏幕切换")
        statusItem.button?.image = icon
        statusItem.button?.toolTip = "屏幕切换  ⌃⌥E"
        statusItem.button?.target = self
        statusItem.button?.action = #selector(togglePanel)
        statusItem.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])

        panel.title = "屏幕切换"
        panel.contentView = NSHostingView(rootView: DisplayPanel(model: model))
        panel.isReleasedWhenClosed = false
        panel.isMovableByWindowBackground = true
        panel.hidesOnDeactivate = true
        panel.level = .floating

        ball = FloatingBall { [weak self] frame in self?.showBallPanel(nextTo: frame) }
        ball?.setVisible(model.ballVisible)
        model.onBallVisibilityChanged = { [weak self] visible in self?.ball?.setVisible(visible) }
        model.onPendingChange = { [weak self] in self?.showConfirmation() }
        model.onShowPanel = { [weak self] in self?.showPanel() }
        registerShortcut()
        model.onShortcutChanged = { [weak self] in self?.registerShortcut() }
        model.reload()

        NotificationCenter.default.addObserver(self, selector: #selector(panelChangedScreen),
            name: NSWindow.didChangeScreenNotification, object: panel)
        NotificationCenter.default.addObserver(self, selector: #selector(screensChanged),
            name: NSApplication.didChangeScreenParametersNotification, object: nil)
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showPanel()
        return true
    }

    func applicationWillTerminate(_ notification: Notification) {
        model.revert()
    }

    @objc private func togglePanel() {
        if NSApp.currentEvent?.type == .rightMouseUp {
            panel.orderOut(nil)
            let menu = NSMenu()
            menu.addItem(withTitle: "显示面板", action: #selector(showPanel), keyEquivalent: "").target = self
            menu.addItem(withTitle: model.ballVisible ? "隐藏悬浮球" : "显示悬浮球",
                         action: #selector(toggleBall), keyEquivalent: "").target = self
            menu.addItem(.separator())
            menu.addItem(withTitle: "退出屏幕切换", action: #selector(NSApplication.terminate(_:)),
                         keyEquivalent: "q").target = NSApp
            statusItem.menu = menu
            statusItem.button?.performClick(nil)
            statusItem.menu = nil
            return
        }
        toggleDisplayPanel()
    }

    private func toggleDisplayPanel() {
        if panel.isVisible {
            panel.orderOut(nil)
        } else {
            showPanel()
        }
    }

    @objc private func showPanel() {
        guard let screen = screenAtPointer() else { return }
        let area = screen.visibleFrame.insetBy(dx: 8, dy: 8)
        let origin = CGPoint(x: area.maxX - panel.frame.width, y: area.maxY - panel.frame.height)
        presentPanel(at: origin, in: screen)
    }

    @objc private func toggleBall() {
        model.ballVisible.toggle()
    }

    private func showBallPanel(nextTo frame: CGRect) {
        if panel.isVisible {
            panel.orderOut(nil)
            return
        }
        guard let screen = NSScreen.screens.first(where: { $0.frame.intersects(frame) }) ?? screenAtPointer() else { return }
        let x = frame.midX > screen.visibleFrame.midX ? frame.minX - panel.frame.width - 8 : frame.maxX + 8
        let y = frame.midY - panel.frame.height / 2
        presentPanel(at: CGPoint(x: x, y: y), in: screen)
    }

    private func presentPanel(at origin: CGPoint, in screen: NSScreen) {
        if model.pending == nil { model.reload() }
        let area = screen.visibleFrame.insetBy(dx: 8, dy: 8)
        let size = panel.frame.size
        panel.setFrameOrigin(CGPoint(x: max(area.minX, min(origin.x, area.maxX - size.width)),
                                     y: max(area.minY, min(origin.y, area.maxY - size.height))))
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        model.setPanelDisplay(panel.screen?.displayID ?? screen.displayID)
    }

    @objc private func panelChangedScreen() {
        model.setPanelDisplay(panel.screen?.displayID)
    }

    private func screenAtPointer() -> NSScreen? {
        let pointer = NSEvent.mouseLocation
        return NSScreen.screens.first(where: { $0.frame.contains(pointer) }) ?? NSScreen.main
    }

    private func showConfirmation() {
        // The display hosting the panel may have just been turned off.
        if panel.isVisible && NSScreen.screens.contains(where: { $0.visibleFrame.contains(panel.frame) }) { return }
        showPanel()
    }

    private func registerShortcut() {
        shortcut = nil
        shortcut = GlobalShortcut(key: model.shortcutKey, modifiers: model.shortcutModifiers) { [weak self] in
            self?.toggleDisplayPanel()
        }
        statusItem.button?.toolTip = "屏幕切换  \(model.shortcutTitle)"
        model.shortcutUnavailable = model.shortcutKey != nil && shortcut == nil
    }

    @objc private func screensChanged() {
        model.reload()
        ball?.relocate()
        if model.pending != nil { showConfirmation() }
    }
}

private final class GlobalShortcut {
    private var handler: EventHandlerRef?
    private var registration: EventHotKeyRef?
    private let action: () -> Void

    init?(key: UInt32?, modifiers: UInt32, action: @escaping () -> Void) {
        guard let key else { return nil }
        self.action = action
        let target = GetApplicationEventTarget()
        var event = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let installed = InstallEventHandler(target, { _, _, context in
            guard let context else { return OSStatus(eventNotHandledErr) }
            Unmanaged<GlobalShortcut>.fromOpaque(context).takeUnretainedValue().action()
            return noErr
        }, 1, &event, Unmanaged.passUnretained(self).toOpaque(), &handler)
        guard installed == noErr else { return nil }
        let id = EventHotKeyID(signature: 0x53575348, id: 1)
        guard RegisterEventHotKey(key, modifiers, id, target, 0, &registration) == noErr else { return nil }
    }

    deinit {
        if let registration { UnregisterEventHotKey(registration) }
        if let handler { RemoveEventHandler(handler) }
    }
}
