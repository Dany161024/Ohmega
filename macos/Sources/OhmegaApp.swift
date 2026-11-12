// OhmegaApp.swift — app entry: window, menus, files opened from Finder, About.
//
// Extra modes for development (no window):
//   Ohmega --selftest <folder>       scan + normalize to −14 LUFS + rescan, prints the results
//   Ohmega --snapshot <png> [paths] [--then-normalize]  opens the window with these files, scans
//                                    (and normalizes + rescans), saves a screenshot, quits
import AppKit
import SwiftUI

@main
enum Main {
    static func main() {
        let arguments = CommandLine.arguments
        if let i = arguments.firstIndex(of: "--selftest"), i + 1 < arguments.count {
            exit(SelfTest.run(folder: URL(fileURLWithPath: arguments[i + 1])))
        }
        OhmegaApp.main()
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    var library: Library?
    private var pending: [URL] = []

    func application(_ application: NSApplication, open urls: [URL]) {
        if let library { library.add(urls) } else { pending += urls }
        showWindow()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Launched to open files (Finder "Open With", paths on the command line): SwiftUI then opens no window.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { self.showWindow() }
    }

    func showWindow() {
        if !NSApp.windows.contains(where: { $0.isVisible && $0.canBecomeMain }) { WindowOpener.open?() }
        NSApp.activate(ignoringOtherApps: true)
    }

    func attach(_ library: Library) {
        self.library = library
        if !pending.isEmpty { library.add(pending); pending = [] }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

struct OhmegaApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var library = Library()

    var body: some Scene {
        Window("Ohmega", id: "main") {
            ContentView()
                .environmentObject(library)
                .onAppear {
                    delegate.attach(library)
                    Snapshot.startIfRequested(library)
                }
        }
        .defaultSize(width: 980, height: 620)
        .commands { OhmegaCommands(library: library) }
    }
}

/// Lets the app delegate open the main window (SwiftUI only hands out `openWindow` inside views and commands).
enum WindowOpener {
    static var open: (() -> Void)?
    static func remember(_ action: @escaping () -> Void) { open = action }
}

struct OhmegaCommands: Commands {
    @Environment(\.openWindow) private var openWindow
    @ObservedObject var library: Library

    var body: some Commands {
        let _ = WindowOpener.remember { openWindow(id: "main") }
        CommandGroup(replacing: .appInfo) {
            Button("About Ohmega") { About.show() }
        }
        CommandGroup(replacing: .newItem) {
            Button("Add Files…") { Panels.openFiles(library) }
                .keyboardShortcut("o")
            Button("Add Folder…") { Panels.openFolders(library) }
                .keyboardShortcut("o", modifiers: [.command, .shift])
        }
        CommandMenu("Loudness") {
            Button("Scan") { library.scan() }
                .keyboardShortcut("r")
                .disabled(!library.canScan)
            Button("Normalize") { library.normalize() }
                .keyboardShortcut(.return)
                .disabled(!library.canNormalize)
            Divider()
            Button("Clear List") { library.clear() }
                .keyboardShortcut(.delete, modifiers: [.command, .shift])
                .disabled(library.tracks.isEmpty || library.work != nil)
        }
    }
}

enum About {
    static func show() {
        let credits = NSMutableAttributedString(
            string: "Normalizes loudness straight into your audio files (EBU R128). "
                + "No tags, no player settings — it plays right everywhere.\n\n",
            attributes: [.font: NSFont.systemFont(ofSize: 11), .foregroundColor: NSColor.labelColor])
        let small: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 10), .foregroundColor: NSColor.secondaryLabelColor]
        credits.append(NSAttributedString(string: "Free software under the GPL-3.0 · ", attributes: small))
        credits.append(NSAttributedString(string: "ohmega.is-my.app", attributes: small.merging(
            [.link: URL(string: "https://ohmega.is-my.app")!]) { $1 }))
        credits.append(NSAttributedString(string: "\n\nIncludes FFmpeg 9.0.2 (GPL-3.0, ffmpeg.org) — static build by evermeet.cx; "
            + "its source code is at ffmpeg.org/download.html.", attributes: small))
        NSApp.orderFrontStandardAboutPanel(options: [.credits: credits])
        NSApp.activate(ignoringOtherApps: true)
    }
}

// MARK: development helpers

enum Snapshot {
    /// --snapshot <png> [files or folders…]: add, scan, then save the window as PNG and quit.
    @MainActor
    static func startIfRequested(_ library: Library) {
        let arguments = CommandLine.arguments
        guard let i = arguments.firstIndex(of: "--snapshot"), i + 1 < arguments.count else { return }
        let output = URL(fileURLWithPath: arguments[i + 1])
        let inputs = arguments.dropFirst(i + 2).filter { !$0.hasPrefix("--") }.map { URL(fileURLWithPath: $0) }
        library.add(Array(inputs))
        if !inputs.isEmpty { library.scan() }
        Task { @MainActor in
            var waited = 0
            while library.work != nil && waited < 120 {
                try? await Task.sleep(nanoseconds: 250_000_000)
                waited += 1
            }
            if arguments.contains("--then-normalize") {     // whole round trip: normalize, then scan again
                library.normalize()
                while library.work != nil && waited < 480 { try? await Task.sleep(nanoseconds: 250_000_000); waited += 1 }
                library.scan()
                while library.work != nil && waited < 600 { try? await Task.sleep(nanoseconds: 250_000_000); waited += 1 }
            }
            try? await Task.sleep(nanoseconds: 800_000_000)
            if let window = NSApp.windows.first(where: { $0.isVisible }), let frame = window.contentView?.superview {
                if let rep = frame.bitmapImageRepForCachingDisplay(in: frame.bounds) {
                    frame.cacheDisplay(in: frame.bounds, to: rep)
                    try? rep.representation(using: .png, properties: [:])?.write(to: output)
                }
            }
            NSApp.terminate(nil)
        }
    }
}

enum SelfTest {
    /// Runs the engine without a window and prints what it did — returns the exit code.
    static func run(folder: URL) -> Int32 {
        var code: Int32 = 0
        let done = DispatchSemaphore(value: 0)
        Task {
            print("ffmpeg: \(Engine.ffmpeg?.path ?? "MISSING")")
            let files = Engine.collect(folder)
            print("files: \(files.count)")
            for file in files {
                do {
                    let before = try await Engine.measure(file)
                    let gain = -14 - before
                    if abs(gain) >= Engine.skipBelow {
                        try Engine.backup(file)
                        try await Engine.apply(gain: gain, to: file)
                    }
                    let after = try await Engine.measure(file)
                    print(String(format: "%@  %.1f → %.1f LUFS (gain %+.1f dB)", file.lastPathComponent, before, after, gain))
                    if abs(after + 14) > 0.5 { code = 1 }
                } catch {
                    print("\(file.lastPathComponent)  ERROR: \(error.localizedDescription)")
                    code = 2
                }
            }
            do {
                let album = try await Engine.measureAlbum(files)
                print(String(format: "album (all files as one programme): %.1f LUFS", album))
            } catch {
                print("album ERROR: \(error.localizedDescription)")
                code = 2
            }
            done.signal()
        }
        done.wait()
        return code
    }
}
