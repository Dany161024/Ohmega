// Engine.swift — loudness measurement and gain application (the macOS port of ohmega_core.py).
// Same ffmpeg commands as the Linux app, so both give identical results.
import Foundation

enum Engine {
    struct Target: Hashable {
        let name: String
        let lufs: Double
    }

    static let targets: [Target] = [
        Target(name: "Streaming", lufs: -14),
        Target(name: "ReplayGain 2.0", lufs: -18),
        Target(name: "Broadcast / Film", lufs: -23),
    ]

    /// Encoder settings per extension — identical to CODEC_MAP in ohmega_core.py.
    static let codecs: [String: [String]] = [
        // lossless — audio data re-encoded losslessly
        "flac": ["-c:a", "flac", "-compression_level", "8"],
        "wav": ["-c:a", "pcm_s24le"],
        "aiff": ["-c:a", "pcm_s24be"],
        "aif": ["-c:a", "pcm_s24be"],
        "ape": ["-c:a", "ape"],
        "wv": ["-c:a", "wavpack"],
        // lossy — re-encoded at the highest quality
        "mp3": ["-c:a", "libmp3lame", "-q:a", "0"],
        "ogg": ["-c:a", "libvorbis", "-q:a", "10"],
        "opus": ["-c:a", "libopus", "-b:a", "256k"],   // 256k = libopus maximum for mono files
        "m4a": ["-c:a", "aac", "-b:a", "320k"],
        "aac": ["-c:a", "aac", "-b:a", "320k"],
        "wma": ["-c:a", "wmav2", "-b:a", "320k"],
        "mpc": ["-c:a", "libmp3lame", "-q:a", "0"],
    ]
    static let lossless: Set<String> = ["flac", "wav", "aiff", "aif", "ape", "wv"]
    static let backupDirName = "Ohmega Backup"
    /// Gains smaller than this are not worth a re-encode.
    static let skipBelow = 0.5

    static func isSupported(_ url: URL) -> Bool { codecs[url.pathExtension.lowercased()] != nil }

    struct Failure: LocalizedError {
        let message: String
        var errorDescription: String? { message }
    }

    // MARK: ffmpeg

    /// The ffmpeg bundled in Ohmega.app; a Homebrew one when running a development build.
    static let ffmpeg: URL? = {
        if let bundled = Bundle.main.url(forResource: "ffmpeg", withExtension: nil) { return bundled }
        for path in ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"]
            where FileManager.default.isExecutableFile(atPath: path) {
            return URL(fileURLWithPath: path)
        }
        return nil
    }()

    /// Runs ffmpeg off the main thread → (exit status, stderr).
    static func run(_ arguments: [String]) async throws -> (status: Int32, log: String) {
        guard let ffmpeg else { throw Failure(message: "ffmpeg is missing from Ohmega.app.") }
        return try await withCheckedThrowingContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                let process = Process()
                process.executableURL = ffmpeg
                process.arguments = ["-hide_banner", "-nostdin"] + arguments
                let stderr = Pipe()
                process.standardError = stderr
                process.standardOutput = FileHandle.nullDevice
                process.standardInput = FileHandle.nullDevice
                do {
                    try process.run()
                } catch {
                    let code = (error as NSError).code
                    if code == 86 {   // EBADARCH: Intel ffmpeg on Apple Silicon without Rosetta
                        continuation.resume(throwing: Failure(message:
                            "FFmpeg needs Rosetta on Apple Silicon. Install it in Terminal with: softwareupdate --install-rosetta"))
                    } else {
                        continuation.resume(throwing: Failure(message: "Could not start ffmpeg: \(error.localizedDescription)"))
                    }
                    return
                }
                let data = stderr.fileHandleForReading.readDataToEndOfFile()
                process.waitUntilExit()
                continuation.resume(returning: (process.terminationStatus, String(decoding: data, as: UTF8.self)))
            }
        }
    }

    /// "I: -14.2 LUFS" from the ebur128 summary at the end of the log.
    static func integratedLUFS(in log: String) -> Double? {
        for line in log.split(separator: "\n").reversed() where line.contains("I:") && line.contains("LUFS") {
            let parts = line.split(separator: " ")
            if let i = parts.firstIndex(of: "I:"), i + 1 < parts.count, let value = Double(parts[i + 1]) {
                return value
            }
        }
        return nil
    }

    /// The line that says why ffmpeg failed (not the generic "Conversion failed!" at the end).
    private static func lastLines(_ log: String) -> String {
        let lines = log.split(separator: "\n").map { $0.trimmingCharacters(in: .whitespaces) }
        let reasons = lines.filter { line in
            ["rror", "nsupported", "nvalid", "No such file", "ermission denied"].contains { line.contains($0) }
                && !line.contains("Task finished") && !line.contains("Terminating thread")
        }
        let chosen = reasons.isEmpty ? Array(lines.suffix(2)) : Array(reasons.prefix(2))
        return chosen.map { $0.replacingOccurrences(of: #"^\[[^\]]*\]\s*"#, with: "", options: .regularExpression) }
            .joined(separator: " — ")
    }

    // MARK: measuring and normalizing

    static func measure(_ file: URL) async throws -> Double {
        let (_, log) = try await run(["-i", file.path, "-af", "ebur128=peak=true:framelog=verbose", "-f", "null", "-"])
        guard let lufs = integratedLUFS(in: log) else {
            throw Failure(message: "Could not measure loudness: \(lastLines(log))")
        }
        return lufs
    }

    /// Integrated loudness of a whole folder measured as one continuous programme (all tracks concatenated) —
    /// the basis for one shared album gain.
    static func measureAlbum(_ files: [URL]) async throws -> Double {
        if files.count == 1 { return try await measure(files[0]) }
        var arguments: [String] = []
        var chains: [String] = []
        for (i, file) in files.enumerated() {
            arguments += ["-i", file.path]
            chains.append("[\(i):a]aformat=sample_rates=48000:channel_layouts=stereo[a\(i)]")   // mixed formats in one folder
        }
        let inputs = (0..<files.count).map { "[a\($0)]" }.joined()
        let graph = chains.joined(separator: ";")
            + ";\(inputs)concat=n=\(files.count):v=0:a=1[c];[c]ebur128=peak=true:framelog=verbose[out]"
        let (_, log) = try await run(arguments + ["-filter_complex", graph, "-map", "[out]", "-f", "null", "-"])
        guard let lufs = integratedLUFS(in: log) else {
            throw Failure(message: "Could not measure album loudness: \(lastLines(log))")
        }
        return lufs
    }

    /// Writes the gain into the audio itself: re-encode next to the original, then swap atomically.
    static func apply(gain: Double, to file: URL) async throws {
        let ext = file.pathExtension.lowercased()
        guard let codec = codecs[ext] else { throw Failure(message: "Unsupported format: .\(ext)") }
        if ext == "ape" { throw Failure(message: "FFmpeg can read APE but cannot write it — convert it to FLAC first.") }
        let temporary = file.deletingPathExtension().appendingPathExtension("ohmega_tmp.\(file.pathExtension)")
        let (status, log) = try await run(["-y", "-i", file.path, "-af", String(format: "volume=%.4fdB", gain),
                                           "-map_metadata", "0"] + codec + [temporary.path])
        guard status == 0 else {
            try? FileManager.default.removeItem(at: temporary)
            throw Failure(message: lastLines(log))
        }
        guard rename(temporary.path, file.path) == 0 else {
            try? FileManager.default.removeItem(at: temporary)
            throw Failure(message: "Could not replace the original: \(String(cString: strerror(errno)))")
        }
    }

    /// Copies the original into "Ohmega Backup" next to it (only the first time — the backup stays the original).
    static func backup(_ file: URL) throws {
        let folder = file.deletingLastPathComponent().appendingPathComponent(backupDirName, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let destination = folder.appendingPathComponent(file.lastPathComponent)
        if !FileManager.default.fileExists(atPath: destination.path) {
            try FileManager.default.copyItem(at: file, to: destination)
        }
    }

    /// Supported audio files in a folder (recursively, skipping our backups) or the file itself.
    static func collect(_ url: URL) -> [URL] {
        var isFolder: ObjCBool = false
        guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isFolder) else { return [] }
        if !isFolder.boolValue { return isSupported(url) ? [url] : [] }
        var found: [URL] = []
        let walker = FileManager.default.enumerator(at: url, includingPropertiesForKeys: [.isDirectoryKey],
                                                    options: [.skipsHiddenFiles])
        while let item = walker?.nextObject() as? URL {
            if item.lastPathComponent == backupDirName { walker?.skipDescendants(); continue }
            if isSupported(item) { found.append(item) }
        }
        return found.sorted { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
    }
}
