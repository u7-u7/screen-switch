import AppKit
import Carbon
import SwiftUI

// Use the property wrapper so the app also builds with the standalone Command Line Tools.
private typealias LocalState<Value> = SwiftUI.State<Value>

struct DisplayPanel: View {
    @ObservedObject var model: DisplayPanelModel
    @LocalState private var showPreferences = false

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            if showPreferences {
                preferences
            } else {
                if let remaining = model.pending {
                    confirmation(remaining).padding([.top, .horizontal], 16)
                }
                if let status = model.status {
                    VStack(alignment: .leading, spacing: 6) {
                        Text(status.text)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                        if case .screenCapturePermissionRequired = status,
                           let settings = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
                            Link("打开屏幕录制设置", destination: settings)
                        }
                    }
                        .font(.callout)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding([.top, .horizontal], 16)
                }
                ScrollView {
                    VStack(alignment: .leading, spacing: 16) {
                        if model.displays.isEmpty {
                            Text("暂未读取到显示器。").foregroundStyle(.secondary)
                        } else {
                            controls.disabled(model.pending != nil)
                        }
                    }
                    .padding(16)
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
        .frame(width: 370, height: 510)
    }

    private var header: some View {
        HStack(spacing: 8) {
            Image(systemName: "rectangle.on.rectangle")
                .imageScale(.large)
                .foregroundStyle(Color.accentColor)
                .accessibilityHidden(true)
            Text(showPreferences ? "偏好设置" : "屏幕切换").font(.headline)
            Spacer()
            Button {
                showPreferences.toggle()
            } label: {
                Image(systemName: showPreferences ? "arrow.uturn.backward" : "gearshape")
            }
            .buttonStyle(.borderless)
            .help(showPreferences ? "返回显示面板" : "偏好设置")
            .accessibilityLabel(showPreferences ? "返回显示面板" : "偏好设置")
            .disabled(model.pending != nil)
        }
        .padding(14)
        .onChange(of: model.pending != nil) { confirming in
            if confirming { showPreferences = false }
        }
    }

    private var controls: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Button("扩展全部", action: model.showAll)
                    .disabled(!model.canSelectDisplays && model.displays.contains(where: { !$0.enabled }))
                Menu {
                    ForEach(model.castSources, id: \.id) { display in
                        Button(display.name) { model.showPreview(display) }
                    }
                    if model.isCasting {
                        if !model.castSources.isEmpty { Divider() }
                        Button("停止投屏", action: model.stopPreview)
                    }
                } label: {
                    Label(model.isCasting ? "投屏中" : "投屏", systemImage: "pip")
                }
                .fixedSize()
                .disabled(model.castSources.isEmpty && !model.isCasting)
                .help(model.castSources.isEmpty && !model.isCasting
                      ? "请先开启另一块扩展屏幕" : "在当前屏幕的小窗中显示另一块屏幕")
            }

            VStack(alignment: .leading, spacing: 8) {
                Text("显示器").font(.headline)
                ForEach(model.displays, id: \.id) { display in
                    displayRow(display)
                }
                if model.canSelectDisplays && model.displays.count > 1 {
                    Button("仅点亮勾选的显示器", action: model.showSelected)
                        .disabled(model.selection.isEmpty || (model.selection == model.enabledIDs &&
                            !model.displays.contains(where: { $0.mirrorSource != 0 })))
                } else if !model.canSelectDisplays && model.displays.count > 1 {
                    Text("当前系统不支持单独开关显示器。").font(.caption).foregroundStyle(.secondary)
                }
            }

            if model.extended.count > 1 {
                DisplayArrangement(displays: model.extended, apply: model.arrange)
            }

            Button("打开系统显示器设置") {
                if let url = URL(string: "x-apple.systempreferences:com.apple.Displays-Settings.extension") {
                    NSWorkspace.shared.open(url)
                }
            }
            .buttonStyle(.link)
        }
    }

    private func displayRow(_ display: Display) -> some View {
        HStack(spacing: 10) {
            if model.canSelectDisplays && model.displays.count > 1 {
                Toggle(isOn: selection(for: display.id)) { Text(display.name) }
                    .labelsHidden()
            }
            VStack(alignment: .leading, spacing: 3) {
                Text(display.name).fontWeight(.medium).lineLimit(1)
                Text(displayDetails(display)).font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 4)
            Menu {
                if model.canSelectDisplays && model.displays.count > 1 {
                    Button("仅点亮这块屏幕") { model.showOnly(display) }
                }
                if display.enabled && display.mirrorSource == 0 {
                    Menu("分辨率与刷新率") {
                        ForEach(model.modes(for: display), id: \.ioDisplayModeID) { mode in
                            Button {
                                model.changeMode(mode, on: display)
                            } label: {
                                if display.mode?.ioDisplayModeID == mode.ioDisplayModeID {
                                    Label(Display.modeTitle(mode), systemImage: "checkmark")
                                } else {
                                    Text(Display.modeTitle(mode))
                                }
                            }
                        }
                    }
                }
            } label: {
                Image(systemName: "ellipsis.circle").accessibilityLabel("设置「\(display.name)」")
            }
            .menuStyle(.borderlessButton)
            .fixedSize()
            .disabled((!display.enabled || display.mirrorSource != 0) &&
                (!model.canSelectDisplays || model.displays.count < 2))
        }
        .padding(9)
        .background(.quaternary.opacity(0.45), in: RoundedRectangle(cornerRadius: 8))
    }

    private func displayDetails(_ display: Display) -> String {
        if !display.enabled { return "已停用" }
        if display.mirrorSource != 0 { return "系统镜像" }
        return [
            display.isMain ? "主屏幕" : "扩展屏幕",
            display.mode.map(Display.modeTitle),
            display.rotation == 0 ? nil : "\(display.rotation)°"
        ].compactMap { $0 }.joined(separator: " · ")
    }

    private func selection(for id: DisplayID) -> Binding<Bool> {
        Binding {
            model.selection.contains(id)
        } set: { checked in
            if checked { model.selection.insert(id) } else { model.selection.remove(id) }
        }
    }

    private func confirmation(_ remaining: Int) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("\(remaining) 秒后恢复之前的显示设置").fontWeight(.semibold)
            HStack {
                Button("保留更改", action: model.keep).buttonStyle(.borderedProminent)
                Button("立即恢复", action: model.revert)
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.accentColor.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))
    }

    private var preferences: some View {
        Form {
            Toggle("显示桌面悬浮球", isOn: $model.ballVisible)
            Picker("全局快捷键", selection: $model.shortcutKey) {
                Text("关闭").tag(UInt32?.none)
                ForEach(DisplayPanelModel.shortcutKeys, id: \.1) { name, key in
                    Text(name).tag(Optional(key))
                }
            }
            Picker("修饰键", selection: $model.shortcutModifiers) {
                Text("Control + Option").tag(UInt32(controlKey | optionKey))
                Text("Command + Option").tag(UInt32(cmdKey | optionKey))
                Text("Control + Shift").tag(UInt32(controlKey | shiftKey))
                Text("Command + Control").tag(UInt32(cmdKey | controlKey))
            }
            .disabled(model.shortcutKey == nil)
            if model.shortcutUnavailable {
                Text("快捷键不可用，请选择其他组合。").foregroundStyle(.secondary)
            }
            Button("退出屏幕切换") { NSApp.terminate(nil) }
        }
        .padding(16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

private struct DisplayArrangement: View {
    let displays: [Display]
    let apply: ([DisplayID: CGPoint]) -> Void
    @LocalState private var draft: [DisplayID: CGPoint] = [:]
    @GestureState private var drag: Drag?

    private struct Drag {
        let id: DisplayID
        let translation: CGSize
    }

    private var actual: [DisplayID: CGRect] {
        Dictionary(uniqueKeysWithValues: displays.map { ($0.id, $0.bounds) })
    }

    private var layout: [DisplayID: CGRect] {
        actual.reduce(into: [:]) { result, entry in
            result[entry.key] = CGRect(origin: draft[entry.key] ?? entry.value.origin, size: entry.value.size)
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            Text("显示器排列").font(.headline)
            Text("拖动屏幕卡片，调整鼠标跨屏的方向。").font(.caption).foregroundStyle(.secondary)
            GeometryReader { proxy in
                let bounds = actual.values.reduce(CGRect.null) { $0.union($1) }
                let scale = min((proxy.size.width - 24) / bounds.width, (proxy.size.height - 24) / bounds.height) * 0.88
                let origin = CGPoint(x: (proxy.size.width - bounds.width * scale) / 2 - bounds.minX * scale,
                                     y: (proxy.size.height - bounds.height * scale) / 2 - bounds.minY * scale)
                ForEach(displays, id: \.id) { display in
                    if let rectangle = layout[display.id] {
                        tile(display, rectangle: rectangle, scale: scale, origin: origin)
                    }
                }
            }
            .frame(height: 130)
            .background(.quaternary.opacity(0.4), in: RoundedRectangle(cornerRadius: 8))
            if !draft.isEmpty {
                HStack {
                    Button("应用排列") { apply(draft) }
                    Button("重置草稿") { draft.removeAll() }
                }
            }
        }
        .onChange(of: actual) { _ in draft.removeAll() }
    }

    private func tile(_ display: Display, rectangle: CGRect, scale: CGFloat, origin: CGPoint) -> some View {
        let moved = drag?.id == display.id ? drag?.translation ?? .zero : .zero
        return Text(display.name)
            .font(.caption2)
            .lineLimit(1)
            .padding(.horizontal, 3)
            .frame(width: rectangle.width * scale, height: rectangle.height * scale)
            .background(display.isMain ? Color.accentColor.opacity(0.28) : Color.secondary.opacity(0.18),
                        in: RoundedRectangle(cornerRadius: 5))
            .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(.secondary.opacity(0.32)))
            .position(x: rectangle.midX * scale + origin.x + moved.width,
                      y: rectangle.midY * scale + origin.y + moved.height)
            .zIndex(drag?.id == display.id ? 1 : 0)
            .gesture(DragGesture(coordinateSpace: .global)
                .updating($drag) { value, state, _ in
                    if !display.isMain { state = Drag(id: display.id, translation: value.translation) }
                }
                .onEnded { value in
                    guard !display.isMain else { return }
                    let moved = rectangle.offsetBy(dx: value.translation.width / scale,
                                                   dy: value.translation.height / scale)
                    if let position = snap(moved, excluding: display.id) {
                        draft[display.id] = position == display.bounds.origin ? nil : position
                    }
                })
            .help(display.isMain ? "\(display.name)（主屏幕）" : display.name)
    }

    private func snap(_ moved: CGRect, excluding id: DisplayID) -> CGPoint? {
        let others = layout.filter { $0.key != id }.map(\.value)
        let edgeOverlap: CGFloat = 16
        let candidates = others.flatMap { target in
            let x = min(max(moved.minX, target.minX - moved.width + edgeOverlap), target.maxX - edgeOverlap)
            let y = min(max(moved.minY, target.minY - moved.height + edgeOverlap), target.maxY - edgeOverlap)
            return [
                CGPoint(x: target.minX - moved.width, y: y), CGPoint(x: target.maxX, y: y),
                CGPoint(x: x, y: target.minY - moved.height), CGPoint(x: x, y: target.maxY)
            ]
        }
        return candidates.filter { point in
            let candidate = CGRect(origin: point, size: moved.size)
            return others.allSatisfy { candidate.intersection($0).isEmpty }
        }.min { first, second in
            hypot(first.x - moved.minX, first.y - moved.minY) < hypot(second.x - moved.minX, second.y - moved.minY)
        }
    }
}
