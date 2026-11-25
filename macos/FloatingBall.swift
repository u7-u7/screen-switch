import AppKit

@MainActor
final class FloatingBall {
    private static let diameter: CGFloat = 44
    private static let tabWidth = diameter / 2

    private let window: NSPanel
    private let control: BallView
    private let defaults = UserDefaults.standard
    private var screenID: DisplayID?
    private var rightEdge: Bool
    private var height: CGFloat
    private var expanded = false
    private var dragStart: CGPoint?
    private var dragOrigin = CGPoint.zero

    init(onOpen: @escaping (CGRect) -> Void) {
        window = NSPanel(contentRect: CGRect(x: 0, y: 0, width: Self.tabWidth, height: Self.diameter),
                         styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        control = BallView(onOpen: onOpen)
        let savedID = defaults.object(forKey: "ballDisplay") as? Int
        screenID = savedID.flatMap { DisplayID(exactly: $0) }
        rightEdge = defaults.object(forKey: "ballRightEdge") as? Bool ?? true
        height = min(1, max(0, defaults.object(forKey: "ballHeight") as? Double ?? 0.55))

        window.contentView = control
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.level = .floating
        window.hidesOnDeactivate = false
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        control.onHover = { [weak self] inside in self?.hover(inside) }
        control.onDrag = { [weak self] in self?.drag() }
        control.onRelease = { [weak self] dragged in self?.release(dragged) }
        relocate()
    }

    func setVisible(_ visible: Bool) {
        if visible {
            relocate()
            window.orderFrontRegardless()
        } else {
            window.orderOut(nil)
        }
    }

    func relocate() {
        guard let screen = NSScreen.screens.first(where: { $0.displayID == screenID }) ?? NSScreen.main else { return }
        dock(in: screen)
    }

    private func dock(in screen: NSScreen) {
        let area = screen.visibleFrame
        let width = expanded ? Self.diameter : Self.tabWidth
        let x = rightEdge ? area.maxX - width : area.minX
        let y = area.minY + max(0, area.height - Self.diameter) * height
        control.rightEdge = rightEdge
        control.expanded = expanded
        window.setFrame(CGRect(x: x, y: y, width: width, height: Self.diameter), display: true)
    }

    private func hover(_ inside: Bool) {
        guard dragStart == nil, expanded != inside else { return }
        expanded = inside
        relocate()
    }

    private func drag() {
        let pointer = NSEvent.mouseLocation
        if dragStart == nil {
            dragStart = pointer
            dragOrigin = window.frame.origin
        }
        guard let start = dragStart else { return }
        window.setFrameOrigin(CGPoint(x: dragOrigin.x + pointer.x - start.x,
                                      y: dragOrigin.y + pointer.y - start.y))
    }

    private func release(_ dragged: Bool) {
        dragStart = nil
        guard dragged else { return }
        let pointer = NSEvent.mouseLocation
        guard let screen = NSScreen.screens.first(where: { $0.frame.contains(pointer) }) ?? NSScreen.main else { return }
        let area = screen.visibleFrame
        screenID = screen.displayID
        rightEdge = pointer.x >= area.midX
        height = min(1, max(0, (pointer.y - Self.diameter / 2 - area.minY) / max(1, area.height - Self.diameter)))
        defaults.set(screenID.map { Int($0) }, forKey: "ballDisplay")
        defaults.set(rightEdge, forKey: "ballRightEdge")
        defaults.set(height, forKey: "ballHeight")
        expanded = false
        dock(in: screen)
    }
}

private final class BallView: NSView {
    private let onOpen: (CGRect) -> Void
    private let logo = NSImage(named: "ScreenSwitchLogo")
    private var tracking: NSTrackingArea?
    private var dragged = false
    var onHover: ((Bool) -> Void)?
    var onDrag: (() -> Void)?
    var onRelease: ((Bool) -> Void)?
    var expanded = false { didSet { needsDisplay = true } }
    var rightEdge = true { didSet { needsDisplay = true } }

    init(onOpen: @escaping (CGRect) -> Void) {
        self.onOpen = onOpen
        super.init(frame: .zero)
        setAccessibilityRole(.button)
        setAccessibilityLabel("屏幕切换")
        toolTip = "点按打开屏幕切换，拖动可以换边"
    }

    required init?(coder: NSCoder) { nil }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .inVisibleRect, .activeAlways],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        tracking = area
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseEntered(with event: NSEvent) { onHover?(true) }
    override func mouseExited(with event: NSEvent) { onHover?(false) }
    override func mouseDown(with event: NSEvent) { dragged = false }
    override func mouseDragged(with event: NSEvent) {
        dragged = true
        onDrag?()
    }
    override func mouseUp(with event: NSEvent) {
        onRelease?(dragged)
        if !dragged { _ = accessibilityPerformPress() }
    }

    override func accessibilityPerformPress() -> Bool {
        guard let frame = window?.frame else { return false }
        onOpen(frame)
        return true
    }

    override func draw(_ dirtyRect: NSRect) {
        if expanded, let logo {
            logo.draw(in: bounds)
            return
        }

        let diameter = bounds.height
        let x = rightEdge ? bounds.minX : bounds.maxX - diameter
        let circle = NSBezierPath(ovalIn: CGRect(x: x, y: bounds.minY, width: diameter, height: diameter))
        let light = NSColor(srgbRed: 67 / 255, green: 140 / 255, blue: 247 / 255, alpha: 1)
        let dark = NSColor(srgbRed: 36 / 255, green: 83 / 255, blue: 196 / 255, alpha: 1)
        NSGradient(starting: light, ending: dark)?.draw(in: circle, angle: -65)

        let direction: CGFloat = rightEdge ? -1 : 1
        let center = CGPoint(x: bounds.midX, y: bounds.midY)
        let chevron = NSBezierPath()
        chevron.move(to: CGPoint(x: center.x - 2 * direction, y: center.y - 4))
        chevron.line(to: CGPoint(x: center.x + 2 * direction, y: center.y))
        chevron.line(to: CGPoint(x: center.x - 2 * direction, y: center.y + 4))
        chevron.lineWidth = 2
        chevron.lineCapStyle = .round
        chevron.lineJoinStyle = .round
        NSColor.white.setStroke()
        chevron.stroke()
    }
}
