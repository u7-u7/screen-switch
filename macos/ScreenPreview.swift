import AppKit
import CoreGraphics
import CoreMedia
import CoreVideo
import IOSurface
import OSLog
import ScreenCaptureKit

@MainActor
final class ScreenPreview: NSObject, NSWindowDelegate, NSMenuDelegate {
    private var window: NSWindow?
    private var view: PreviewView?
    private var stream: SCStream?
    private var opening: Task<Void, Never>?
    private var firstFrameTimeout: Task<Void, Never>?
    private var output: PreviewOutput?
    private var source: DisplayID?
    private var sources: [Display] = []
    private var sourcePicker: NSPopUpButton?
    private var fullScreenButton: NSButton?
    private let logger = Logger(subsystem: "io.github.u7-u7.screen-switch", category: "Preview")
    var onClose: (() -> Void)?
    var onError: ((String) -> Void)?
    var onPermissionRequired: (() -> Void)?

    func show(_ display: Display, on destination: NSScreen, available: [Display]) -> Bool {
        if window == nil && !CGPreflightScreenCaptureAccess() && !CGRequestScreenCaptureAccess() {
            onPermissionRequired?()
            return false
        }

        update(available: available)
        let existing = window
        let window = existing ?? makeWindow(for: display)
        let moveToDestination = existing == nil || window.screen?.displayID != destination.displayID
        if moveToDestination || source != display.id {
            fitWindow(to: display, on: destination, resetPosition: moveToDestination)
        }
        if source != display.id { changeSource(display) }
        if window.isMiniaturized { window.deminiaturize(nil) }
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(view)
        view?.refreshControls()
        return true
    }

    func close() {
        window?.close()
    }

    func update(available: [Display]) {
        sources = available
        refreshSourcePicker()
        if let source, !available.contains(where: { $0.id == source }) {
            window?.close()
            onError?("来源屏幕已停用或断开，投屏已停止。")
        }
    }

    func windowWillClose(_ notification: Notification) {
        stopCapture()
        window = nil
        view = nil
        source = nil
        sourcePicker = nil
        fullScreenButton = nil
        onClose?()
    }

    func windowDidChangeScreen(_ notification: Notification) {
        refreshSourcePicker()
    }

    func windowDidEnterFullScreen(_ notification: Notification) {
        updateFullScreenStyle()
    }

    func windowDidExitFullScreen(_ notification: Notification) {
        updateFullScreenStyle()
        if let screen = window?.screen, let display = sources.first(where: { $0.id == source }) {
            fitWindow(to: display, on: screen, resetPosition: false)
        }
    }

    func menuWillOpen(_ menu: NSMenu) {
        view?.sourceMenuOpen = true
    }

    func menuDidClose(_ menu: NSMenu) {
        view?.sourceMenuOpen = false
    }

    private func makeWindow(for display: Display) -> NSWindow {
        let size = previewSize(for: display, longSide: 560)
        let window = NSWindow(contentRect: CGRect(origin: .zero, size: size),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                              backing: .buffered, defer: false)
        window.title = "投屏小窗"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = true
        window.titlebarSeparatorStyle = .none
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = true
        window.isMovableByWindowBackground = true
        window.contentAspectRatio = display.bounds.size
        window.contentMinSize = CGSize(width: 190, height: 120)
        window.isReleasedWhenClosed = false
        window.collectionBehavior = [.fullScreenPrimary]
        window.delegate = self
        for button: NSWindow.ButtonType in [.closeButton, .miniaturizeButton, .zoomButton] {
            window.standardWindowButton(button)?.isHidden = true
        }

        let view = PreviewView(frame: CGRect(origin: .zero, size: size))
        window.contentView = view
        self.view = view
        self.window = window
        addControls(to: view)
        return window
    }

    private func fitWindow(to display: Display, on screen: NSScreen, resetPosition: Bool) {
        guard let window, !window.styleMask.contains(.fullScreen) else { return }
        window.contentAspectRatio = display.bounds.size
        let area = screen.visibleFrame.insetBy(dx: 16, dy: 16)
        let frame = window.frame
        let ideal = previewSize(for: display, longSide: max(frame.width, frame.height))
        let scale = min(1, area.width / ideal.width, area.height / ideal.height)
        let size = CGSize(width: ideal.width * scale, height: ideal.height * scale)
        let x = resetPosition ? area.minX : max(area.minX, min(frame.minX, area.maxX - size.width))
        let top = resetPosition ? area.maxY : min(area.maxY, max(frame.maxY, area.minY + size.height))
        window.setFrame(CGRect(x: x, y: top - size.height, width: size.width, height: size.height), display: true)
        window.invalidateShadow()
    }

    private func previewSize(for display: Display, longSide: CGFloat) -> CGSize {
        let width = max(1, display.bounds.width)
        let height = max(1, display.bounds.height)
        let scale = longSide / max(width, height)
        return CGSize(width: width * scale, height: height * scale)
    }

    private func addControls(to view: PreviewView) {
        let picker = NSPopUpButton(frame: .zero, pullsDown: false)
        picker.target = self
        picker.action = #selector(pickSource(_:))
        picker.menu?.delegate = self
        picker.font = .systemFont(ofSize: 12, weight: .medium)
        picker.isBordered = false
        picker.lineBreakMode = .byTruncatingTail
        picker.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        picker.setAccessibilityLabel("投屏来源")
        picker.toolTip = "切换投屏来源"

        let pin = controlButton("pin", title: "置顶", action: #selector(togglePin(_:)))
        pin.setButtonType(.pushOnPushOff)
        let fullScreen = controlButton("arrow.up.left.and.arrow.down.right", title: "全屏", action: #selector(toggleFullScreen))
        let close = controlButton("xmark", title: "关闭投屏", action: #selector(closeFromControls))
        view.setControls([picker, pin, fullScreen, close])
        sourcePicker = picker
        fullScreenButton = fullScreen
    }

    private func controlButton(_ symbol: String, title: String, action: Selector) -> NSButton {
        let button = NSButton(title: "", target: self, action: action)
        button.image = controlIcon(symbol)
        button.imagePosition = .imageOnly
        button.isBordered = false
        button.toolTip = title
        button.setAccessibilityLabel(title)
        button.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            button.widthAnchor.constraint(equalToConstant: 24),
            button.heightAnchor.constraint(equalToConstant: 26)
        ])
        return button
    }

    private func controlIcon(_ symbol: String) -> NSImage? {
        NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?
            .withSymbolConfiguration(.init(pointSize: 12, weight: .semibold))
    }

    private func updateFullScreenStyle() {
        guard let window else { return }
        let isFullScreen = window.styleMask.contains(.fullScreen)
        view?.setFullScreen(isFullScreen)
        fullScreenButton?.image = controlIcon(isFullScreen
            ? "arrow.down.right.and.arrow.up.left" : "arrow.up.left.and.arrow.down.right")
        let title = isFullScreen ? "退出全屏" : "全屏"
        fullScreenButton?.toolTip = title
        fullScreenButton?.setAccessibilityLabel(title)
        window.invalidateShadow()
    }

    private func refreshSourcePicker() {
        guard let picker = sourcePicker else { return }
        let destination = window?.screen?.displayID
        picker.removeAllItems()
        for (offset, display) in sources.enumerated() where display.id != destination || display.id == source {
            picker.addItem(withTitle: "\(offset + 1) · \(display.name)")
            picker.lastItem?.representedObject = NSNumber(value: display.id)
        }
        if let source {
            let index = picker.indexOfItem(withRepresentedObject: NSNumber(value: source))
            if index >= 0 { picker.selectItem(at: index) }
        }
        picker.isEnabled = picker.numberOfItems > 1
    }

    @objc private func pickSource(_ sender: NSPopUpButton) {
        guard let id = (sender.selectedItem?.representedObject as? NSNumber)?.uint32Value,
              let display = sources.first(where: { $0.id == id }), id != source else { return }
        if let screen = window?.screen { fitWindow(to: display, on: screen, resetPosition: false) }
        changeSource(display)
    }

    @objc private func togglePin(_ sender: NSButton) {
        let isPinned = sender.state == .on
        window?.level = isPinned ? .floating : .normal
        sender.image = controlIcon(isPinned ? "pin.fill" : "pin")
        let title = isPinned ? "取消置顶" : "置顶"
        sender.toolTip = title
        sender.setAccessibilityLabel(title)
    }

    @objc private func toggleFullScreen() {
        window?.toggleFullScreen(nil)
    }

    @objc private func closeFromControls() {
        close()
    }

    private func changeSource(_ display: Display) {
        stopCapture()
        source = display.id
        refreshSourcePicker()
        view?.prepareForCapture()

        let receiver = PreviewOutput(onFrame: { [weak self] stream, surface in
            self?.showFrame(surface, from: stream)
        }, onStop: { [weak self] failed, message in
            DispatchQueue.main.async {
                if self?.stream === failed { self?.fail(message) }
            }
        })
        output = receiver
        awaitFirstFrame(from: display)
        opening = Task { @MainActor [weak self] in
            do {
                let stream = try await Self.makeStream(for: display, receiver: receiver)
                try Task.checkCancellation()
                guard let self else { return }
                self.stream = stream
                try await stream.startCapture()
                if Task.isCancelled { try? await stream.stopCapture() }
            } catch {
                guard !Task.isCancelled else { return }
                if (error as? SCStreamError)?.code == .userDeclined {
                    self?.close()
                    self?.onPermissionRequired?()
                } else {
                    self?.fail("无法预览「\(display.name)」：\(error.localizedDescription)")
                }
            }
        }
    }

    private func showFrame(_ surface: IOSurface, from stream: SCStream) {
        guard self.stream === stream else { return }
        if let firstFrameTimeout {
            firstFrameTimeout.cancel()
            self.firstFrameTimeout = nil
            logger.info("Displaying the first BGRA screen frame")
        }
        view?.showFrame(surface)
    }

    private func awaitFirstFrame(from display: Display) {
        firstFrameTimeout = Task { @MainActor [weak self] in
            do {
                try await Task.sleep(for: .seconds(8))
            } catch {
                return
            }
            guard !Task.isCancelled, let self else { return }
            self.logger.error("Screen capture timed out before the first displayable BGRA frame")
            self.fail("没有收到「\(display.name)」的画面。请确认来源屏幕已唤醒，再试一次。")
        }
    }

    private static func makeStream(for display: Display, receiver: PreviewOutput) async throws -> SCStream {
        let shareable = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard let target = shareable.displays.first(where: { $0.displayID == display.id }) else {
            throw DisplayError.changed
        }
        let excluded = shareable.applications.filter { $0.processID == ProcessInfo.processInfo.processIdentifier }
        let filter = SCContentFilter(display: target, excludingApplications: excluded, exceptingWindows: [])
        let scale = min(1, 1600.0 / Double(max(1, target.width, target.height)))
        let configuration = SCStreamConfiguration()
        configuration.width = max(1, Int(Double(target.width) * scale))
        configuration.height = max(1, Int(Double(target.height) * scale))
        configuration.pixelFormat = kCVPixelFormatType_32BGRA
        configuration.colorSpaceName = CGColorSpace.sRGB
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: 30)
        configuration.queueDepth = 3
        configuration.showsCursor = true
        let stream = SCStream(filter: filter, configuration: configuration, delegate: receiver)
        try stream.addStreamOutput(receiver, type: .screen, sampleHandlerQueue: .main)
        return stream
    }

    private func stopCapture() {
        opening?.cancel()
        opening = nil
        firstFrameTimeout?.cancel()
        firstFrameTimeout = nil
        let receiver = output
        output = nil
        if let stream {
            self.stream = nil
            Task {
                try? await stream.stopCapture()
                if let receiver { try? stream.removeStreamOutput(receiver, type: .screen) }
            }
        }
    }

    private func fail(_ message: String) {
        window?.close()
        onError?(message)
    }
}

private final class PreviewView: NSView {
    private let video = CALayer()
    private let controls = NSVisualEffectView()
    private let connecting = NSTextField(labelWithString: "正在连接投屏…")
    private var tracking: NSTrackingArea?
    var sourceMenuOpen = false { didSet { refreshControls() } }

    override init(frame: CGRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor
        layer?.masksToBounds = true
        layer?.cornerCurve = .continuous
        setFullScreen(false)
        video.frame = bounds
        video.contentsGravity = .resizeAspect
        video.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
        layer?.insertSublayer(video, at: 0)

        connecting.font = .systemFont(ofSize: 13, weight: .medium)
        connecting.textColor = .secondaryLabelColor
        connecting.appearance = NSAppearance(named: .darkAqua)
        connecting.translatesAutoresizingMaskIntoConstraints = false
        addSubview(connecting)
        NSLayoutConstraint.activate([
            connecting.centerXAnchor.constraint(equalTo: centerXAnchor),
            connecting.centerYAnchor.constraint(equalTo: centerYAnchor)
        ])

        controls.material = .hudWindow
        controls.blendingMode = .withinWindow
        controls.state = .active
        controls.appearance = NSAppearance(named: .darkAqua)
        controls.wantsLayer = true
        controls.layer?.cornerRadius = 14
        controls.layer?.masksToBounds = true
        controls.isHidden = true
        controls.translatesAutoresizingMaskIntoConstraints = false
        addSubview(controls)
        NSLayoutConstraint.activate([
            controls.centerXAnchor.constraint(equalTo: centerXAnchor),
            controls.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -12),
            controls.leadingAnchor.constraint(greaterThanOrEqualTo: leadingAnchor, constant: 12),
            controls.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -12)
        ])
    }

    required init?(coder: NSCoder) { nil }

    override var acceptsFirstResponder: Bool { true }
    override var mouseDownCanMoveWindow: Bool { true }

    func setControls(_ buttons: [NSView]) {
        let stack = NSStackView(views: buttons)
        stack.alignment = .centerY
        stack.spacing = 4
        stack.detachesHiddenViews = false
        stack.translatesAutoresizingMaskIntoConstraints = false
        controls.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: controls.leadingAnchor, constant: 8),
            stack.trailingAnchor.constraint(equalTo: controls.trailingAnchor, constant: -8),
            stack.topAnchor.constraint(equalTo: controls.topAnchor, constant: 4),
            stack.bottomAnchor.constraint(equalTo: controls.bottomAnchor, constant: -4)
        ])
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: .zero,
                                  options: [.mouseEnteredAndExited, .inVisibleRect, .activeAlways],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseEntered(with event: NSEvent) { controls.isHidden = false }
    override func mouseExited(with event: NSEvent) { controls.isHidden = !sourceMenuOpen }

    override func cancelOperation(_ sender: Any?) {
        if window?.styleMask.contains(.fullScreen) == true {
            window?.toggleFullScreen(nil)
        } else {
            window?.close()
        }
    }

    override func insertTab(_ sender: Any?) {
        controls.isHidden = false
        window?.selectNextKeyView(self)
    }

    func refreshControls() {
        guard let window else { return }
        let pointer = convert(window.mouseLocationOutsideOfEventStream, from: nil)
        controls.isHidden = !sourceMenuOpen && !bounds.contains(pointer)
    }

    func setFullScreen(_ isFullScreen: Bool) {
        layer?.cornerRadius = isFullScreen ? 0 : 24
        layer?.borderWidth = isFullScreen ? 0 : 1
        layer?.borderColor = NSColor.white.withAlphaComponent(0.18).cgColor
    }

    func prepareForCapture() {
        video.contents = nil
        connecting.isHidden = false
    }

    func showFrame(_ surface: IOSurface) {
        // The stream already supplies a display-ready BGRA surface.
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        video.contents = surface
        CATransaction.commit()
        connecting.isHidden = true
    }
}

private final class PreviewOutput: NSObject, SCStreamOutput, SCStreamDelegate {
    private let onFrame: @MainActor (SCStream, IOSurface) -> Void
    private let onStop: (SCStream, String) -> Void

    init(onFrame: @escaping @MainActor (SCStream, IOSurface) -> Void,
         onStop: @escaping (SCStream, String) -> Void) {
        self.onFrame = onFrame
        self.onStop = onStop
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer buffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, let surface = Self.surface(from: buffer) else { return }
        MainActor.assumeIsolated { onFrame(stream, surface) }
    }

    private static func surface(from buffer: CMSampleBuffer) -> IOSurface? {
        guard let image = CMSampleBufferGetImageBuffer(buffer),
              let metadata = CMSampleBufferGetSampleAttachmentsArray(buffer, createIfNecessary: false)
                  as? [[SCStreamFrameInfo: Any]],
              let status = metadata.first?[.status] as? Int,
              status == SCFrameStatus.complete.rawValue || status == SCFrameStatus.started.rawValue else { return nil }
        return CVPixelBufferGetIOSurface(image)?.takeUnretainedValue() as? IOSurface
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        onStop(stream, "投屏已停止：\(error.localizedDescription)")
    }
}
