import AppKit
import CoreGraphics
import Darwin
import IOKit

typealias DisplayID = CGDirectDisplayID

struct Display {
    let id: DisplayID
    let name: String
    let bounds: CGRect
    let enabled: Bool
    let isMain: Bool
    let mirrorSource: DisplayID
    let mode: CGDisplayMode?

    var rotation: Int { Int(CGDisplayRotation(id)) }

    static func modeTitle(_ mode: CGDisplayMode) -> String {
        let scale = mode.pixelWidth > mode.width ? " HiDPI" : ""
        let rate = mode.refreshRate > 0 ? " · \(mode.refreshRate.formatted(.number.precision(.fractionLength(0...2)))) Hz" : ""
        return "\(mode.width) × \(mode.height)\(scale)\(rate)"
    }
}

enum DisplayError: LocalizedError {
    case system(String, CGError)
    case unavailable(String)
    case changed

    var errorDescription: String? {
        switch self {
        case let .system(operation, code):
            return "\(operation)失败（系统错误 \(code.rawValue)）。"
        case let .unavailable(message):
            return message
        case .changed:
            return "显示器连接或状态已变化，请重新选择。"
        }
    }
}

final class DisplayConfiguration {
    private typealias ListDisplays = @convention(c) (UInt32, UnsafeMutablePointer<DisplayID>?, UnsafeMutablePointer<UInt32>?) -> Int32
    private typealias SetEnabled = @convention(c) (CGDisplayConfigRef?, DisplayID, Bool) -> Int32

    // The public display list omits screens that WindowServer has disabled.
    private let listAll: ListDisplays? = {
        guard let symbol = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "CGSGetDisplayList") else { return nil }
        return unsafeBitCast(symbol, to: ListDisplays.self)
    }()

    private let setEnabled: SetEnabled? = {
        guard let symbol = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "CGSConfigureDisplayEnabled") else { return nil }
        return unsafeBitCast(symbol, to: SetEnabled.self)
    }()

    private var names: [DisplayID: String] = [:]
    private(set) var canSelectDisplays = false

    func displays() throws -> [Display] {
        for screen in NSScreen.screens {
            if let id = screen.displayID {
                names[id] = screen.localizedName
            }
        }

        let online = try displayIDs { CGGetOnlineDisplayList($0, $1, $2).rawValue }
        let available = listAll.flatMap { list in try? displayIDs(using: list) }
        let onlineIDs = Set(online)
        canSelectDisplays = setEnabled != nil && available.map { onlineIDs.isSubset(of: Set($0)) } == true
        let lidClosed = canSelectDisplays && isLidClosed()
        let offline = canSelectDisplays ? (available ?? []).filter { id in
            !onlineIDs.contains(id) &&
                !(lidClosed && CGDisplayIsBuiltin(id) != 0) &&
                (CGDisplayVendorNumber(id) != 0 || CGDisplayModelNumber(id) != 0)
        } : []
        let mainID = CGMainDisplayID()
        return (online + offline).map { id in
            let name = names[id] ?? (CGDisplayIsBuiltin(id) != 0 ? "内建显示器" : "显示器 \(id)")
            return Display(id: id, name: name, bounds: CGDisplayBounds(id),
                           enabled: CGDisplayIsActive(id) != 0 || CGDisplayIsInMirrorSet(id) != 0,
                           isMain: id == mainID, mirrorSource: CGDisplayMirrorsDisplay(id),
                           mode: CGDisplayCopyDisplayMode(id))
        }
    }

    private func isLidClosed() -> Bool {
        let root = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("IOPMrootDomain"))
        guard root != IO_OBJECT_NULL else { return false }
        defer { IOObjectRelease(root) }
        return IORegistryEntryCreateCFProperty(root, "AppleClamshellState" as CFString, kCFAllocatorDefault, 0)?
            .takeRetainedValue() as? Bool == true
    }

    private func displayIDs(using list: (UInt32, UnsafeMutablePointer<DisplayID>?, UnsafeMutablePointer<UInt32>) -> Int32) throws -> [DisplayID] {
        var count: UInt32 = 0
        guard list(0, nil, &count) == 0 else { throw DisplayError.changed }
        guard count > 0 else { return [] }
        var ids = [DisplayID](repeating: 0, count: Int(count))
        guard list(count, &ids, &count) == 0 else { throw DisplayError.changed }
        return Array(ids.prefix(Int(count))).filter { $0 != 0 }
    }

    func modes(for id: DisplayID) -> [CGDisplayMode] {
        let options = [kCGDisplayShowDuplicateLowResolutionModes: true] as CFDictionary
        let modes = CGDisplayCopyAllDisplayModes(id, options) as? [CGDisplayMode] ?? []
        let current = CGDisplayCopyDisplayMode(id)
        var titles = Set<String>()
        return ([current].compactMap { $0 } + modes.filter { $0.isUsableForDesktopGUI() })
            .filter { titles.insert(Display.modeTitle($0)).inserted }
            .sorted { ($0.width, $0.height, $0.refreshRate, $0.pixelWidth) >
                      ($1.width, $1.height, $1.refreshRate, $1.pixelWidth) }
    }

    func show(_ ids: Set<DisplayID>) throws {
        let current = try displays()
        guard !ids.isEmpty, ids.isSubset(of: Set(current.map(\.id))) else { throw DisplayError.changed }
        try setPower(on: true, for: current.filter { ids.contains($0.id) && !$0.enabled })
        let enabled = try displays().filter(\.enabled)
        guard ids.isSubset(of: Set(enabled.map(\.id))) else {
            throw DisplayError.unavailable("部分显示器未能点亮，原有显示器会保持开启。")
        }
        try configureMirrors(for: enabled) { _ in 0 }
        try setPower(on: false, for: try displays().filter { $0.enabled && !ids.contains($0.id) })

        let applied = try displays().filter(\.enabled)
        guard Set(applied.map(\.id)) == ids, applied.allSatisfy({ $0.mirrorSource == 0 }) else {
            throw DisplayError.unavailable("macOS 未能应用所选显示模式。")
        }
    }

    func arrange(_ positions: [DisplayID: CGPoint]) throws {
        let extended = try displays().filter { $0.enabled && $0.mirrorSource == 0 }
        guard Set(positions.keys).isSubset(of: Set(extended.map(\.id))) else { throw DisplayError.changed }
        try transaction { config in
            for display in extended {
                guard let origin = positions[display.id], origin != display.bounds.origin else { continue }
                try setOrigin(origin, of: display.id, in: config)
            }
        }
    }

    func changeMode(_ mode: CGDisplayMode, on id: DisplayID) throws {
        guard let display = try displays().first(where: { $0.id == id && $0.enabled && $0.mirrorSource == 0 }),
              let available = modes(for: id).first(where: { $0.ioDisplayModeID == mode.ioDisplayModeID }) else {
            throw DisplayError.changed
        }
        if display.mode?.ioDisplayModeID == available.ioDisplayModeID { return }
        try transaction { config in
            try check(CGConfigureDisplayWithDisplayMode(config, id, available, nil), "切换分辨率或刷新率")
        }
    }

    func restore(_ snapshot: [Display]) throws {
        let online = try displays()
        let onlineIDs = Set(online.map(\.id))
        let saved = snapshot.filter { onlineIDs.contains($0.id) }
        guard saved.contains(where: \.enabled) else { throw DisplayError.changed }
        let savedByID = Dictionary(uniqueKeysWithValues: saved.map { ($0.id, $0) })
        try setPower(on: true, for: online.filter { !$0.enabled && savedByID[$0.id]?.enabled == true })

        let current = try displays()
        let matching = current.filter { $0.enabled && savedByID[$0.id] != nil }
        try configureMirrors(for: matching) { display in
            guard let source = savedByID[display.id]?.mirrorSource, onlineIDs.contains(source) else { return 0 }
            return source
        }

        try setPower(on: false, for: try displays().filter { $0.enabled && savedByID[$0.id]?.enabled == false })
        let restored = try displays().filter { $0.enabled && $0.mirrorSource == 0 }
        if restored.contains(where: { display in
            guard let old = savedByID[display.id], old.enabled, old.mirrorSource == 0 else { return false }
            return old.mode?.ioDisplayModeID != display.mode?.ioDisplayModeID || old.bounds.origin != display.bounds.origin
        }) {
            try transaction { config in
                for display in restored {
                    guard let old = savedByID[display.id], old.enabled, old.mirrorSource == 0 else { continue }
                    if let mode = old.mode, mode.ioDisplayModeID != display.mode?.ioDisplayModeID {
                        try check(CGConfigureDisplayWithDisplayMode(config, display.id, mode, nil), "恢复分辨率")
                    }
                    if old.bounds.origin != display.bounds.origin {
                        try setOrigin(old.bounds.origin, of: display.id, in: config)
                    }
                }
            }
        }
        let final = try displays()
        guard snapshot.count == saved.count, saved.allSatisfy({ old in
            guard let display = final.first(where: { $0.id == old.id }), display.enabled == old.enabled else { return false }
            if !old.enabled { return true }
            guard display.mirrorSource == old.mirrorSource, display.isMain == old.isMain else { return false }
            return old.mirrorSource != 0 || (display.bounds.origin == old.bounds.origin &&
                display.mode?.ioDisplayModeID == old.mode?.ioDisplayModeID)
        }) else { throw DisplayError.unavailable("部分显示设置未能恢复，请在系统设置中检查显示器。") }
    }

    private func configureMirrors(for displays: [Display], source: (Display) -> DisplayID) throws {
        let changes = displays.filter { $0.mirrorSource != source($0) }
        guard !changes.isEmpty else { return }
        // Break old mirror groups first so any of their members can become the new source.
        let stale = changes.filter { $0.mirrorSource != 0 }
        if !stale.isEmpty {
            try transaction { config in
                for display in stale {
                    try check(CGConfigureDisplayMirrorOfDisplay(config, display.id, 0), "取消镜像")
                }
            }
        }

        let mirrors = changes.filter { source($0) != 0 }
        if !mirrors.isEmpty {
            try transaction { config in
                for display in mirrors {
                    try check(CGConfigureDisplayMirrorOfDisplay(config, display.id, source(display)), "开启镜像")
                }
            }
        }
    }

    private func setPower(on: Bool, for displays: [Display]) throws {
        guard !displays.isEmpty else { return }
        guard canSelectDisplays, let setEnabled else {
            throw DisplayError.unavailable("这台 Mac 无法通过应用单独开启或停用显示器。")
        }
        try transaction { config in
            for display in displays {
                let result = setEnabled(config, display.id, on)
                guard result == 0 else {
                    throw DisplayError.unavailable("无法\(on ? "开启" : "停用")「\(display.name)」（系统错误 \(result)）。")
                }
            }
        }
    }

    private func setOrigin(_ point: CGPoint, of id: DisplayID, in config: CGDisplayConfigRef) throws {
        try check(CGConfigureDisplayOrigin(config, id, Int32(point.x.rounded()), Int32(point.y.rounded())), "应用排列")
    }

    private func transaction(_ configure: (CGDisplayConfigRef) throws -> Void) throws {
        var config: CGDisplayConfigRef?
        try check(CGBeginDisplayConfiguration(&config), "开始显示设置")
        guard let config else { throw DisplayError.changed }
        do {
            try configure(config)
        } catch {
            CGCancelDisplayConfiguration(config)
            throw error
        }
        try check(CGCompleteDisplayConfiguration(config, .forSession), "应用显示设置")
    }

    private func check(_ result: CGError, _ operation: String) throws {
        guard result == .success else { throw DisplayError.system(operation, result) }
    }
}

extension NSScreen {
    var displayID: DisplayID? {
        (deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value
    }
}
