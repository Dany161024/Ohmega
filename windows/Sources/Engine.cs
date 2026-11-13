// Engine.cs — loudness measurement and gain application (the Windows port of ohmega_core.py).
// Same ffmpeg commands as the Linux and Mac apps, so all three give identical results.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ohmega
{
    sealed class Target
    {
        public readonly string Name;
        public readonly double Lufs;
        public Target(string name, double lufs) { Name = name; Lufs = lufs; }
    }

    sealed class Failure : Exception
    {
        public Failure(string message) : base(message) { }
    }

    static class Engine
    {
        public static readonly Target[] Targets =
        {
            new Target("Streaming", -14),
            new Target("ReplayGain 2.0", -18),
            new Target("Broadcast / Film", -23),
        };

        /// Encoder settings per extension — identical to CODEC_MAP in ohmega_core.py.
        public static readonly Dictionary<string, string[]> Codecs = new Dictionary<string, string[]>
        {
            // lossless — audio data re-encoded losslessly
            ["flac"] = new[] { "-c:a", "flac", "-compression_level", "8" },
            ["wav"] = new[] { "-c:a", "pcm_s24le" },
            ["aiff"] = new[] { "-c:a", "pcm_s24be" },
            ["aif"] = new[] { "-c:a", "pcm_s24be" },
            ["ape"] = new[] { "-c:a", "ape" },
            ["wv"] = new[] { "-c:a", "wavpack" },
            // lossy — re-encoded at the highest quality
            ["mp3"] = new[] { "-c:a", "libmp3lame", "-q:a", "0" },
            ["ogg"] = new[] { "-c:a", "libvorbis", "-q:a", "10" },
            ["opus"] = new[] { "-c:a", "libopus", "-b:a", "256k" },   // 256k = libopus maximum for mono files
            ["m4a"] = new[] { "-c:a", "aac", "-b:a", "320k" },
            ["aac"] = new[] { "-c:a", "aac", "-b:a", "320k" },
            ["wma"] = new[] { "-c:a", "wmav2", "-b:a", "320k" },
            ["mpc"] = new[] { "-c:a", "libmp3lame", "-q:a", "0" },
        };
        public static readonly HashSet<string> Lossless = new HashSet<string> { "flac", "wav", "aiff", "aif", "ape", "wv" };
        public const string BackupDirName = "Ohmega Backup";
        /// Gains smaller than this are not worth a re-encode.
        public const double SkipBelow = 0.5;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Ext(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        public static bool IsSupported(string path) => Codecs.ContainsKey(Ext(path));
        public static bool IsLossless(string path) => Lossless.Contains(Ext(path));

        // MARK: ffmpeg

        /// ffmpeg.exe next to Ohmega.exe (the installer puts it there); one on PATH for development builds.
        public static readonly string Ffmpeg = FindFfmpeg();

        static string FindFfmpeg()
        {
            var here = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(here)) return here;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        /// One argument quoted the way CommandLineToArgvW and the C runtime split them again.
        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                sb.Append(c);
                slashes = 0;
            }
            sb.Append('\\', slashes * 2);
            return sb.Append('"').ToString();
        }

        static readonly HashSet<Process> running = new HashSet<Process>();
        static readonly HashSet<string> temporaries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// Closing the window: stop every ffmpeg (Windows would let them run on) and remove their half-written files.
        public static void StopAll()
        {
            lock (running)
                foreach (var p in running)
                    try { if (!p.HasExited) { p.Kill(); p.WaitForExit(2000); } } catch (Exception) { }
            lock (temporaries)
                foreach (var t in temporaries) TryDelete(t);
        }

        static string CommandLine(IEnumerable<string> args) =>
            string.Join(" ", new[] { "-hide_banner", "-nostdin" }.Concat(args).Select(Quote));

        /// Runs ffmpeg on a worker thread → (exit code, stderr). No console window ever shows up.
        public static Task<(int Status, string Log)> Run(IEnumerable<string> args) => Task.Run(() =>
        {
            if (Ffmpeg == null) throw new Failure("ffmpeg.exe is missing next to Ohmega.exe — please reinstall Ohmega.");
            var info = new ProcessStartInfo(Ffmpeg, CommandLine(args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using (var process = new Process { StartInfo = info })
            {
                try { process.Start(); }
                catch (Exception e) { throw new Failure("Could not start ffmpeg: " + e.Message); }
                lock (running) running.Add(process);
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }   // keep the PC responsive
                var output = process.StandardOutput.ReadToEndAsync();      // drained so a full pipe never blocks ffmpeg
                string log = process.StandardError.ReadToEnd();
                output.Wait();
                process.WaitForExit();
                lock (running) running.Remove(process);
                return (process.ExitCode, log);
            }
        });

        /// "I: -14.2 LUFS" from the ebur128 summary at the end of the log.
        public static double? IntegratedLufs(string log)
        {
            foreach (var line in log.Split('\n').Reverse())
            {
                if (!line.Contains("I:") || !line.Contains("LUFS")) continue;
                var parts = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                int i = Array.IndexOf(parts, "I:");
                if (i >= 0 && i + 1 < parts.Length && double.TryParse(parts[i + 1], NumberStyles.Float, Inv, out var value))
                    return value;
            }
            return null;
        }

        /// The line that says why ffmpeg failed (not the generic "Conversion failed!" at the end).
        static string LastLines(string log)
        {
            var lines = log.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var keys = new[] { "rror", "nsupported", "nvalid", "No such file", "ermission denied" };
            var reasons = lines.Where(l => keys.Any(l.Contains) && !l.Contains("Task finished") && !l.Contains("Terminating thread")).ToList();
            var chosen = reasons.Count == 0 ? lines.Skip(Math.Max(0, lines.Count - 2)) : reasons.Take(2);
            return string.Join(" — ", chosen.Select(l => Regex.Replace(l, @"^\[[^\]]*\]\s*", "")));
        }

        // MARK: measuring and normalizing

        public static async Task<double> Measure(string file)
        {
            var (_, log) = await Run(new[] { "-i", file, "-af", "ebur128=peak=true:framelog=verbose", "-f", "null", "-" });
            return IntegratedLufs(log) ?? throw new Failure("Could not measure loudness: " + LastLines(log));
        }

        /// Integrated loudness of a whole folder measured as one continuous programme (all tracks concatenated) —
        /// the basis for one shared album gain.
        public static async Task<double> MeasureAlbum(IList<string> files)
        {
            if (files.Count == 1) return await Measure(files[0]);
            var args = new List<string>();
            var chains = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                args.Add("-i");
                args.Add(files[i]);
                chains.Add($"[{i}:a]aformat=sample_rates=48000:channel_layouts=stereo[a{i}]");   // mixed formats in one folder
            }
            string inputs = string.Concat(Enumerable.Range(0, files.Count).Select(i => $"[a{i}]"));
            string graph = string.Join(";", chains) + $";{inputs}concat=n={files.Count}:v=0:a=1[c];[c]ebur128=peak=true:framelog=verbose[out]";
            args.AddRange(new[] { "-filter_complex", graph, "-map", "[out]", "-f", "null", "-" });
            string log;
            if (CommandLine(args).Length + (Ffmpeg?.Length ?? 0) + 4 < 32000)
            {
                (_, log) = await Run(args);
            }
            else
            {
                // a huge album does not fit on a Windows command line (32 767 characters): concat demuxer from a list
                var list = Path.Combine(Path.GetTempPath(), $"ohmega-album-{Guid.NewGuid():N}.txt");
                File.WriteAllLines(list, files.Select(f => "file '" + f.Replace("'", @"'\''") + "'"), new UTF8Encoding(false));
                try
                {
                    (_, log) = await Run(new[] { "-f", "concat", "-safe", "0", "-i", list,
                        "-af", "aformat=sample_rates=48000:channel_layouts=stereo,ebur128=peak=true:framelog=verbose", "-f", "null", "-" });
                }
                finally { try { File.Delete(list); } catch (IOException) { } }
            }
            return IntegratedLufs(log) ?? throw new Failure("Could not measure album loudness: " + LastLines(log));
        }

        /// Writes the gain into the audio itself: re-encode next to the original, then swap it in one step.
        public static async Task Apply(double gain, string file)
        {
            string ext = Ext(file);
            if (!Codecs.TryGetValue(ext, out var codec)) throw new Failure($"Unsupported format: .{ext}");
            if (ext == "ape") throw new Failure("FFmpeg can read APE but cannot write it — convert it to FLAC first.");
            string temporary = Path.Combine(Path.GetDirectoryName(file),
                Path.GetFileNameWithoutExtension(file) + ".ohmega_tmp" + Path.GetExtension(file));
            var args = new List<string> { "-y", "-i", file, "-af", string.Format(Inv, "volume={0:F4}dB", gain), "-map_metadata", "0" };
            args.AddRange(codec);
            args.Add(temporary);
            lock (temporaries) temporaries.Add(temporary);
            try
            {
                var (status, log) = await Run(args);
                if (status != 0)
                {
                    TryDelete(temporary);
                    throw new Failure(LastLines(log));
                }
                if (!MoveFileEx(temporary, file, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
                {
                    int error = Marshal.GetLastWin32Error();
                    TryDelete(temporary);
                    throw new Failure("Could not replace the original: " + new System.ComponentModel.Win32Exception(error).Message);
                }
            }
            finally
            {
                lock (temporaries) temporaries.Remove(temporary);
            }
        }

        static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (Exception) { }
        }

        /// Copies the original into "Ohmega Backup" next to it (only the first time — the backup stays the original).
        public static void Backup(string file)
        {
            string folder = Path.Combine(Path.GetDirectoryName(file), BackupDirName);
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, Path.GetFileName(file));
            if (!File.Exists(destination)) File.Copy(file, destination);
        }

        /// Supported audio files in a folder (recursively, skipping our backups and hidden folders) or the file itself.
        public static List<string> Collect(string path)
        {
            if (File.Exists(path)) return IsSupported(path) ? new List<string> { path } : new List<string>();
            var found = new List<string>();
            if (!Directory.Exists(path)) return found;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                try
                {
                    found.AddRange(Directory.EnumerateFiles(dir).Where(IsSupported));
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        var name = Path.GetFileName(sub);
                        if (name == BackupDirName || (File.GetAttributes(sub) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        pending.Push(sub);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            found.Sort(NaturalCompare);      // like Explorer: "2 song" before "10 song"
            return found;
        }

        public static int NaturalCompare(string a, string b) => StrCmpLogicalW(a, b);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);

        const int MOVEFILE_REPLACE_EXISTING = 0x1, MOVEFILE_WRITE_THROUGH = 0x8;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileEx(string existing, string replacement, int flags);

        public static string LufsText(double value) =>
            value.ToString("0.0", Inv).Replace('-', '−') + " LUFS";

        public static string GainText(double gain) =>
            (gain >= 0 ? "+" : "−") + Math.Abs(gain).ToString("0.0", Inv) + " dB";
    }
}
