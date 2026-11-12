// Library.swift — the list of tracks and what Ohmega does with them (scan, normalize, profiles).
import AppKit
import Foundation
import UserNotifications

struct Track: Identifiable, Hashable {
    let id = UUID()
    let url: URL
    /// Folder the track was added with — tracks of one folder share one album gain. nil = a loose track.
    let album: URL?
    var lufs: Double?
    var scanError: String?
    var outcome: Outcome?

    enum Outcome: Hashable {
        case applied(Double)
        case skipped
        case failed(String)
    }

    var name: String { url.lastPathComponent }
    var format: String { url.pathExtension.uppercased() }
    var isLossless: Bool { Engine.lossless.contains(url.pathExtension.lowercased()) }
}

struct Profile: Codable {
    var targetLabel: String
    var customLUFS: Double
    var backup: Bool

    enum CodingKeys: String, CodingKey {        // same JSON as the Linux app (~/.config/ohmega/profiles.json)
        case targetLabel = "target_label", customLUFS = "custom_lufs", backup
    }
}

@MainActor
final class Library: ObservableObject {
    enum Work: Equatable { case scanning, normalizing }

    @Published var tracks: [Track] = []
    @Published var selection = Set<Track.ID>()
    /// 0…2 = presets from Engine.targets, 3 = custom
    @Published var targetIndex = 0
    @Published var customLUFS = -16.0
    @Published var backup = true
    @Published private(set) var work: Work?
    @Published private(set) var done = 0
    @Published private(set) var total = 0
    @Published var status = "Drop files or folders — a folder becomes one album."
    @Published private(set) var profiles: [String: Profile] = [:]

    private let parallel = max(1, min(4, ProcessInfo.processInfo.activeProcessorCount / 2))

    init() { loadProfiles() }

    var target: Double { targetIndex < Engine.targets.count ? Engine.targets[targetIndex].lufs : customLUFS }
    var targetLabel: String {
        targetIndex < Engine.targets.count
            ? "\(Engine.targets[targetIndex].name) (\(Int(Engine.targets[targetIndex].lufs)) LUFS)" : "Custom"
    }
    var albumCount: Int { Set(tracks.compactMap(\.album)).count }
    var canScan: Bool { work == nil && !tracks.isEmpty }
    var canNormalize: Bool { work == nil && tracks.contains { $0.lufs != nil } }

    // MARK: adding and removing

    /// A folder becomes one album per sub-folder that holds music; a loose file is normalized on its own.
    func add(_ urls: [URL]) {
        guard work == nil, !urls.isEmpty else { return }
        var known = Set(tracks.map(\.url.standardizedFileURL))
        var added = 0
        for url in urls {
            var isFolder: ObjCBool = false
            FileManager.default.fileExists(atPath: url.path, isDirectory: &isFolder)
            for file in Engine.collect(url) where !known.contains(file.standardizedFileURL) {
                known.insert(file.standardizedFileURL)
                tracks.append(Track(url: file, album: isFolder.boolValue ? file.deletingLastPathComponent() : nil))
                added += 1
            }
        }
        if added == 0, !urls.isEmpty {
            status = "No supported audio files there."
        } else {
            status = summary
        }
    }

    var summary: String {
        let albums = albumCount
        return "\(tracks.count) file\(tracks.count == 1 ? "" : "s")" + (albums > 0 ? " · \(albums) album\(albums == 1 ? "" : "s")" : "")
    }

    func remove(_ ids: Set<Track.ID>) {
        guard work == nil else { return }
        tracks.removeAll { ids.contains($0.id) }
        selection.subtract(ids)
        status = tracks.isEmpty ? "Drop files or folders — a folder becomes one album." : summary
    }

    func clear() { remove(Set(tracks.map(\.id))) }

    private func index(of id: Track.ID) -> Int? { tracks.firstIndex { $0.id == id } }

    // MARK: scan

    func scan() {
        guard canScan else { return }
        for i in tracks.indices {      // the Gain column keeps the last result, so a re-scan proves it
            tracks[i].lufs = nil
            tracks[i].scanError = nil
        }
        let jobs = tracks.map { ($0.id, $0.url) }
        start(.scanning, total: jobs.count)
        Task {
            await forEach(jobs) { id, url in
                do {
                    let lufs = try await Engine.measure(url)
                    self.update(id) { $0.lufs = lufs }
                } catch {
                    self.update(id) { $0.scanError = error.localizedDescription }
                }
                self.step("Scanning \(url.lastPathComponent)")
            }
            let measured = tracks.filter { $0.lufs != nil }.count
            finish("Scanned \(measured) of \(tracks.count) — \(measured > 0 ? "ready to normalize" : "nothing could be measured").")
        }
    }

    // MARK: normalize

    func normalize() {
        guard canNormalize else { return }
        let target = target
        let backup = backup
        let albums = Dictionary(grouping: tracks.filter { $0.album != nil && $0.lufs != nil }, by: { $0.album! })
            .mapValues { $0.map { ($0.id, $0.url) } }
        let loose = tracks.filter { $0.album == nil && $0.lufs != nil }.map { ($0.id, $0.url, $0.lufs!) }
        for i in tracks.indices { tracks[i].outcome = nil }
        start(.normalizing, total: albums.values.reduce(0) { $0 + $1.count } + loose.count)
        Task {
            // albums: one gain for the whole folder, so the balance between its tracks stays
            for (folder, files) in albums.sorted(by: { $0.key.path < $1.key.path }) {
                status = "Measuring album \(folder.lastPathComponent)…"
                let gain: Double
                do {
                    gain = target - (try await Engine.measureAlbum(files.map(\.1)))
                } catch {
                    for (id, _) in files {
                        update(id) { $0.outcome = .failed(error.localizedDescription) }
                        step(nil)
                    }
                    continue
                }
                await forEach(files) { id, url in
                    await self.normalizeOne(id, url, gain: gain, backup: backup)
                }
            }
            await forEach(loose.map { ($0.0, ($0.1, $0.2)) }) { id, item in
                await self.normalizeOne(id, item.0, gain: target - item.1, backup: backup)
            }
            let applied = tracks.filter { if case .applied = $0.outcome { return true } else { return false } }.count
            let message = "Done — \(applied) file\(applied == 1 ? "" : "s") normalized to \(Self.lufsText(target))"
                + (backup ? ", originals in “\(Engine.backupDirName)”." : ".")
            finish(message)
            notify(message)
        }
    }

    private func normalizeOne(_ id: Track.ID, _ url: URL, gain: Double, backup: Bool) async {
        defer { step("Normalizing \(url.lastPathComponent)") }
        if abs(gain) < Engine.skipBelow {
            update(id) { $0.outcome = .skipped }
            return
        }
        do {
            if backup { try Engine.backup(url) }
            try await Engine.apply(gain: gain, to: url)
            update(id) { $0.outcome = .applied(gain) }
        } catch {
            update(id) { $0.outcome = .failed(error.localizedDescription) }
        }
    }

    // MARK: progress

    private func start(_ kind: Work, total: Int) {
        work = kind
        done = 0
        self.total = total
    }

    private func step(_ message: String?) {
        done += 1
        if let message { status = "\(message) (\(done)/\(total))" }
    }

    private func finish(_ message: String) {
        work = nil
        status = message
    }

    private func update(_ id: Track.ID, _ change: (inout Track) -> Void) {
        if let i = index(of: id) { change(&tracks[i]) }
    }

    /// Runs `body` for every item, a few at a time (ffmpeg is single-threaded per file).
    private func forEach<T>(_ items: [(Track.ID, T)], _ body: @escaping (Track.ID, T) async -> Void) async {
        var queue = items[...]
        await withTaskGroup(of: Void.self) { group in
            for _ in 0..<parallel {
                guard let next = queue.popFirst() else { break }
                group.addTask { await body(next.0, next.1) }
            }
            while await group.next() != nil {
                if let next = queue.popFirst() { group.addTask { await body(next.0, next.1) } }
            }
        }
    }

    private func notify(_ message: String) {
        guard !NSApp.isActive else { return }
        let center = UNUserNotificationCenter.current()
        center.requestAuthorization(options: [.alert, .sound]) { granted, _ in
            guard granted else { return }
            let content = UNMutableNotificationContent()
            content.title = "Ohmega"
            content.body = message
            center.add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
        }
    }

    static func lufsText(_ value: Double) -> String {
        String(format: "%.1f LUFS", value).replacingOccurrences(of: "-", with: "−")
    }

    // MARK: profiles

    private static var profilesURL: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Ohmega", isDirectory: true).appendingPathComponent("profiles.json")
    }

    private func loadProfiles() {
        guard let data = try? Data(contentsOf: Self.profilesURL),
              let saved = try? JSONDecoder().decode([String: Profile].self, from: data) else { return }
        profiles = saved
    }

    private func saveProfiles() {
        let url = Self.profilesURL
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try? encoder.encode(profiles).write(to: url, options: .atomic)
    }

    func saveProfile(named name: String) {
        profiles[name] = Profile(targetLabel: targetLabel, customLUFS: customLUFS, backup: backup)
        saveProfiles()
    }

    func deleteProfile(named name: String) {
        profiles[name] = nil
        saveProfiles()
    }

    func apply(profile name: String) {
        guard let p = profiles[name] else { return }
        if let i = Engine.targets.firstIndex(where: { p.targetLabel.hasPrefix($0.name) }) {
            targetIndex = i
        } else {
            targetIndex = Engine.targets.count
            customLUFS = p.customLUFS
        }
        backup = p.backup
    }
}
