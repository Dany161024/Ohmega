// Views.swift — the Ohmega window: drop zone, track table, toolbar and status bar.
import AppKit
import SwiftUI
import UniformTypeIdentifiers

let ohmegaOrange = Color(red: 0.91, green: 0.41, blue: 0.16)

struct ContentView: View {
    @EnvironmentObject var library: Library
    @State private var dropTargeted = false
    @State private var savingProfile = false
    @State private var profileName = ""

    var body: some View {
        VStack(spacing: 0) {
            if library.tracks.isEmpty {
                EmptyDropZone(highlighted: dropTargeted)
            } else {
                TrackTable()
                    .overlay {
                        if dropTargeted {
                            RoundedRectangle(cornerRadius: 10)
                                .strokeBorder(ohmegaOrange, style: StrokeStyle(lineWidth: 3, dash: [9, 6]))
                                .background(ohmegaOrange.opacity(0.06))
                                .padding(6)
                                .allowsHitTesting(false)
                        }
                    }
            }
            Divider()
            StatusBar()
        }
        .frame(minWidth: 820, minHeight: 480)
        .onDrop(of: [.fileURL], isTargeted: $dropTargeted) { providers in
            loadURLs(from: providers) { library.add($0) }
            return true
        }
        .toolbar { toolbar }
        .sheet(isPresented: $savingProfile) { saveProfileSheet }
        .tint(ohmegaOrange)
    }

    @ToolbarContentBuilder
    private var toolbar: some ToolbarContent {
        ToolbarItemGroup(placement: .navigation) {
            Button { Panels.openFiles(library) } label: { Label("Add Files", systemImage: "doc.badge.plus") }
                .help("Add audio files — each is normalized on its own")
            Button { Panels.openFolders(library) } label: { Label("Add Folder", systemImage: "folder.badge.plus") }
                .help("Add a folder — it becomes one album with a shared gain")
            Button { library.clear() } label: { Label("Clear", systemImage: "xmark.circle") }
                .help("Remove all tracks from the list")
                .disabled(library.tracks.isEmpty || library.work != nil)
        }
        ToolbarItemGroup(placement: .primaryAction) {
            Picker("Target", selection: $library.targetIndex) {
                ForEach(Array(Engine.targets.enumerated()), id: \.offset) { i, t in
                    Text("\(t.name)  \(Library.lufsText(t.lufs).replacingOccurrences(of: ".0", with: ""))").tag(i)
                }
                Divider()
                Text("Custom").tag(Engine.targets.count)
            }
            .pickerStyle(.menu)
            .frame(width: 210)
            .help("Loudness target")
            if library.targetIndex == Engine.targets.count {
                HStack(spacing: 4) {
                    TextField("LUFS", value: $library.customLUFS, format: .number.precision(.fractionLength(1)))
                        .frame(width: 52)
                        .multilineTextAlignment(.trailing)
                    Text("LUFS").foregroundStyle(.secondary)
                }
            }
            Toggle("Back up originals", isOn: $library.backup)
                .toggleStyle(.checkbox)
                .help("Copy originals to “\(Engine.backupDirName)” next to them before changing anything")
            Menu {
                if library.profiles.isEmpty {
                    Text("No saved profiles")
                } else {
                    ForEach(library.profiles.keys.sorted(), id: \.self) { name in
                        Button(name) { library.apply(profile: name) }
                    }
                    Divider()
                    Menu("Delete Profile") {
                        ForEach(library.profiles.keys.sorted(), id: \.self) { name in
                            Button(name, role: .destructive) { library.deleteProfile(named: name) }
                        }
                    }
                }
                Divider()
                Button("Save Current Settings…") { profileName = ""; savingProfile = true }
            } label: {
                Label("Profiles", systemImage: "slider.horizontal.3")
            }
            .help("Saved target and backup settings")
        }
    }

    private var saveProfileSheet: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Save profile").font(.headline)
            Text("\(library.targetLabel == "Custom" ? Library.lufsText(library.customLUFS) : library.targetLabel)"
                 + (library.backup ? ", with backups" : ", without backups"))
                .foregroundStyle(.secondary)
            TextField("Profile name", text: $profileName)
                .frame(width: 280)
                .onSubmit(saveProfile)
            HStack {
                Spacer()
                Button("Cancel") { savingProfile = false }.keyboardShortcut(.cancelAction)
                Button("Save", action: saveProfile)
                    .keyboardShortcut(.defaultAction)
                    .disabled(profileName.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(20)
    }

    private func saveProfile() {
        let name = profileName.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return }
        library.saveProfile(named: name)
        savingProfile = false
    }
}

/// Open panels for adding music (also used by the File menu).
@MainActor
enum Panels {
    static func openFiles(_ library: Library) {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.allowedContentTypes = Engine.codecs.keys.compactMap { UTType(filenameExtension: $0) }
        panel.message = "Each file is normalized on its own."
        if panel.runModal() == .OK { library.add(panel.urls) }
    }

    static func openFolders(_ library: Library) {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.message = "Every folder becomes one album with a shared gain."
        if panel.runModal() == .OK { library.add(panel.urls) }
    }
}

func loadURLs(from providers: [NSItemProvider], then handle: @escaping ([URL]) -> Void) {
    let group = DispatchGroup()
    var urls: [URL] = []
    let lock = NSLock()
    for provider in providers {
        group.enter()
        _ = provider.loadObject(ofClass: URL.self) { url, _ in
            if let url {
                lock.lock(); urls.append(url); lock.unlock()
            }
            group.leave()
        }
    }
    group.notify(queue: .main) { handle(urls.sorted { $0.path < $1.path }) }
}

// MARK: empty state

struct EmptyDropZone: View {
    let highlighted: Bool

    var body: some View {
        VStack(spacing: 18) {
            Image(nsImage: NSApp.applicationIconImage)
                .resizable()
                .frame(width: 96, height: 96)
                .shadow(color: .black.opacity(0.18), radius: 8, y: 4)
            Text("Drop your music here")
                .font(.system(size: 26, weight: .semibold))
            Text("Files are normalized one by one. A folder becomes an album — one shared gain,\nso its quiet intro stays quiet and the drop stays big.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)
            Text("FLAC · WAV · AIFF · WavPack · MP3 · AAC · M4A · OGG · OPUS · WMA")
                .font(.callout.monospaced())
                .foregroundStyle(.tertiary)
        }
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background {
            RoundedRectangle(cornerRadius: 18)
                .strokeBorder(highlighted ? ohmegaOrange : Color.secondary.opacity(0.35),
                              style: StrokeStyle(lineWidth: highlighted ? 3 : 2, dash: [10, 7]))
                .background(RoundedRectangle(cornerRadius: 18).fill(highlighted ? ohmegaOrange.opacity(0.07) : .clear))
                .padding(18)
        }
    }
}

// MARK: table

struct TrackTable: View {
    @EnvironmentObject var library: Library

    var body: some View {
        Table(library.tracks, selection: $library.selection) {
            TableColumn("File") { track in
                HStack(spacing: 8) {
                    Text(track.name).lineLimit(1).truncationMode(.middle)
                    if let album = track.album {
                        Text(album.lastPathComponent)
                            .font(.caption)
                            .lineLimit(1)
                            .padding(.horizontal, 6)
                            .padding(.vertical, 1)
                            .background(Capsule().fill(ohmegaOrange.opacity(0.14)))
                            .foregroundStyle(ohmegaOrange)
                            .help("Album: one shared gain for this folder")
                    }
                }
            }
            TableColumn("Format") { track in
                Text(track.format)
                    .font(.callout.monospaced())
                    .foregroundStyle(track.isLossless ? Color.secondary : Color.orange)
                    .help(track.isLossless ? "Lossless — re-encoded without quality loss" : "Lossy — re-encoded at the highest quality")
            }
            .width(64)
            TableColumn("Loudness") { track in
                LoudnessCell(track: track, target: library.target)
            }
            .width(min: 190, ideal: 230)
            TableColumn("Gain") { track in
                OutcomeCell(track: track)
            }
            .width(min: 120, ideal: 150)
        }
        .onDeleteCommand { library.remove(library.selection) }
        .contextMenu(forSelectionType: Track.ID.self) { ids in
            Button("Show in Finder") {
                NSWorkspace.shared.activateFileViewerSelecting(library.tracks.filter { ids.contains($0.id) }.map(\.url))
            }
            Button("Remove from List") { library.remove(ids) }
        }
    }
}

/// LUFS value plus a small meter from −30 to −4 LUFS with a tick at the target.
struct LoudnessCell: View {
    let track: Track
    let target: Double

    private var color: Color {
        guard let lufs = track.lufs else { return .secondary }
        let off = lufs - target
        if abs(off) < 1 { return .green }
        return off > 6 ? .red : .orange
    }

    var body: some View {
        if let error = track.scanError {
            Label("can't read", systemImage: "exclamationmark.triangle")
                .foregroundStyle(.red)
                .help(error)
        } else if let lufs = track.lufs {
            HStack(spacing: 10) {
                Text(Library.lufsText(lufs))
                    .monospacedDigit()
                    .foregroundStyle(color)
                    .frame(width: 84, alignment: .trailing)
                GeometryReader { geo in
                    let w = geo.size.width
                    let position = { (v: Double) in CGFloat((min(max(v, -30), -4) + 30) / 26) * w }
                    ZStack(alignment: .leading) {
                        Capsule().fill(Color.secondary.opacity(0.15))
                        Capsule().fill(color.opacity(0.85)).frame(width: max(position(lufs), 4))
                        Rectangle().fill(Color.primary.opacity(0.7)).frame(width: 2, height: 12).offset(x: position(target) - 1)
                    }
                    .frame(height: 6)
                    .frame(maxHeight: .infinity)
                }
                .frame(height: 16)
            }
        } else {
            Text("—").foregroundStyle(.tertiary)
        }
    }
}

struct OutcomeCell: View {
    let track: Track

    var body: some View {
        switch track.outcome {
        case .applied(let gain):
            Label(String(format: "%+.1f dB", gain).replacingOccurrences(of: "-", with: "−")
                  + (track.album != nil ? " album" : ""), systemImage: "checkmark")
                .foregroundStyle(.green)
                .monospacedDigit()
        case .skipped:
            Label("already right", systemImage: "checkmark").foregroundStyle(.secondary)
        case .failed(let message):
            Label("failed", systemImage: "xmark.octagon").foregroundStyle(.red).help(message)
        case nil:
            Text("—").foregroundStyle(.tertiary)
        }
    }
}

// MARK: status bar

struct StatusBar: View {
    @EnvironmentObject var library: Library

    var body: some View {
        HStack(spacing: 12) {
            if library.work != nil {
                ProgressView(value: Double(library.done), total: Double(max(library.total, 1)))
                    .frame(width: 160)
            }
            Text(library.status)
                .lineLimit(1)
                .truncationMode(.middle)
                .foregroundStyle(.secondary)
            Spacer()
            if !library.tracks.isEmpty {
                Text(library.summary).foregroundStyle(.tertiary)
            }
            Button { library.scan() } label: { Label("Scan", systemImage: "waveform") }
                .controlSize(.large)
                .help("Measure the loudness of every track (⌘R)")
                .disabled(!library.canScan)
            Button { library.normalize() } label: { Label("Normalize", systemImage: "dial.medium") }
                .controlSize(.large)
                .buttonStyle(.borderedProminent)
                .help("Write the gain into the audio files (⌘↩)")
                .disabled(!library.canNormalize)
        }
        .font(.callout)
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}
