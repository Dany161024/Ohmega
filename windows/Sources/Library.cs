// Library.cs — the list of tracks and what Ohmega does with them (scan, normalize, profiles).
// Everything here runs on the UI thread; only ffmpeg runs on worker threads (Engine.Run), and await brings
// the results back, so the window can redraw after each change.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Ohmega
{
    enum OutcomeKind { Applied, Skipped, Failed }

    sealed class Outcome
    {
        public OutcomeKind Kind;
        public double Gain;
        public string Message;
    }

    sealed class Track
    {
        public readonly string Path;
        /// Folder the track was added with — tracks of one folder share one album gain. null = a loose track.
        public readonly string Album;
        public double? Lufs;
        public string ScanError;
        public Outcome Outcome;
        public bool Selected;

        public Track(string path, string album) { Path = path; Album = album; }

        public string Name => System.IO.Path.GetFileName(Path);
        public string Format => Engine.Ext(Path).ToUpperInvariant();
        public bool IsLossless => Engine.IsLossless(Path);
        public string AlbumName => Album == null ? null : System.IO.Path.GetFileName(Album.TrimEnd('\\', '/'));
    }

    sealed class Profile
    {
        public string TargetLabel;
        public double CustomLufs;
        public bool Backup;
    }

    enum Work { None, Scanning, Normalizing }

    sealed class Library
    {
        public const string EmptyStatus = "Drop files or folders — a folder becomes one album.";

        public readonly List<Track> Tracks = new List<Track>();
        /// 0…2 = presets from Engine.Targets, 3 = custom
        public int TargetIndex;
        public double CustomLufs = -16;
        public bool Backup = true;
        public Work Work { get; private set; }
        public int Done { get; private set; }
        public int Total { get; private set; }
        public string Status { get; private set; } = EmptyStatus;
        public SortedDictionary<string, Profile> Profiles { get; private set; } = new SortedDictionary<string, Profile>(StringComparer.CurrentCultureIgnoreCase);

        /// Something changed — the window redraws (always raised on the UI thread).
        public event Action Changed;
        /// Normalizing finished — the window tells the user if it is in the background.
        public event Action<string> Finished;

        readonly int parallel = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));

        public Library() { LoadProfiles(); }

        void Notify() => Changed?.Invoke();

        public double Target => TargetIndex < Engine.Targets.Length ? Engine.Targets[TargetIndex].Lufs : CustomLufs;
        public string TargetLabel => TargetIndex < Engine.Targets.Length
            ? $"{Engine.Targets[TargetIndex].Name} ({(int)Engine.Targets[TargetIndex].Lufs} LUFS)" : "Custom";
        public int AlbumCount => Tracks.Where(t => t.Album != null).Select(t => t.Album).Distinct().Count();
        public bool CanScan => Work == Work.None && Tracks.Count > 0;
        public bool CanNormalize => Work == Work.None && Tracks.Any(t => t.Lufs != null);
        public bool CanEdit => Work == Work.None;

        public string Summary
        {
            get
            {
                int albums = AlbumCount;
                return $"{Tracks.Count} file{(Tracks.Count == 1 ? "" : "s")}" + (albums > 0 ? $" · {albums} album{(albums == 1 ? "" : "s")}" : "");
            }
        }

        // MARK: adding and removing

        /// A folder becomes one album per sub-folder that holds music; a loose file is normalized on its own.
        public void Add(IEnumerable<string> paths)
        {
            var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (Work != Work.None || list.Count == 0) return;
            var known = new HashSet<string>(Tracks.Select(t => Normalize(t.Path)), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var path in list)
            {
                bool folder = Directory.Exists(path);
                foreach (var file in Engine.Collect(path))
                {
                    if (!known.Add(Normalize(file))) continue;
                    Tracks.Add(new Track(file, folder ? System.IO.Path.GetDirectoryName(file) : null));
                    added++;
                }
            }
            Status = added == 0 ? "No supported audio files there." : Summary;
            Notify();
        }

        static string Normalize(string path)
        {
            try { return System.IO.Path.GetFullPath(path); } catch (Exception) { return path; }
        }

        public void Remove(IEnumerable<Track> tracks)
        {
            if (Work != Work.None) return;
            var gone = new HashSet<Track>(tracks);
            Tracks.RemoveAll(gone.Contains);
            Status = Tracks.Count == 0 ? EmptyStatus : Summary;
            Notify();
        }

        public void RemoveSelected() => Remove(Tracks.Where(t => t.Selected).ToList());
        public void Clear() => Remove(Tracks.ToList());

        // MARK: scan

        public async Task Scan()
        {
            if (!CanScan) return;
            foreach (var t in Tracks)          // the Gain column keeps the last result, so a re-scan proves it
            {
                t.Lufs = null;
                t.ScanError = null;
            }
            var jobs = Tracks.ToList();
            Start(Work.Scanning, jobs.Count);
            await ForEach(jobs, async track =>
            {
                try { track.Lufs = await Engine.Measure(track.Path); }
                catch (Exception e) { track.ScanError = e.Message; }
                Step("Scanning " + track.Name);
            });
            int measured = Tracks.Count(t => t.Lufs != null);
            Finish($"Scanned {measured} of {Tracks.Count} — {(measured > 0 ? "ready to normalize" : "nothing could be measured")}.");
        }

        // MARK: normalize

        public async Task Normalize()
        {
            if (!CanNormalize) return;
            double target = Target;
            bool backup = Backup;
            var albums = Tracks.Where(t => t.Album != null && t.Lufs != null).GroupBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
            var loose = Tracks.Where(t => t.Album == null && t.Lufs != null).ToList();
            foreach (var t in Tracks) t.Outcome = null;
            Start(Work.Normalizing, albums.Sum(g => g.Count()) + loose.Count);

            // albums: one gain for the whole folder, so the balance between its tracks stays
            foreach (var album in albums)
            {
                var files = album.ToList();
                Status = $"Measuring album {System.IO.Path.GetFileName(album.Key)}…";
                Notify();
                double gain;
                try { gain = target - await Engine.MeasureAlbum(files.Select(t => t.Path).ToList()); }
                catch (Exception e)
                {
                    foreach (var t in files)
                    {
                        t.Outcome = new Outcome { Kind = OutcomeKind.Failed, Message = e.Message };
                        Step(null);
                    }
                    continue;
                }
                await ForEach(files, t => NormalizeOne(t, gain, backup));
            }
            await ForEach(loose, t => NormalizeOne(t, target - t.Lufs.Value, backup));

            int applied = Tracks.Count(t => t.Outcome?.Kind == OutcomeKind.Applied);
            string message = $"Done — {applied} file{(applied == 1 ? "" : "s")} normalized to {Engine.LufsText(target)}"
                + (backup ? $", originals in “{Engine.BackupDirName}”." : ".");
            Finish(message);
            Finished?.Invoke(message);
        }

        async Task NormalizeOne(Track track, double gain, bool backup)
        {
            try
            {
                if (Math.Abs(gain) < Engine.SkipBelow)
                {
                    track.Outcome = new Outcome { Kind = OutcomeKind.Skipped };
                    return;
                }
                if (backup) await Task.Run(() => Engine.Backup(track.Path));
                await Engine.Apply(gain, track.Path);
                track.Outcome = new Outcome { Kind = OutcomeKind.Applied, Gain = gain };
            }
            catch (Exception e)
            {
                track.Outcome = new Outcome { Kind = OutcomeKind.Failed, Message = e.Message };
            }
            finally
            {
                Step("Normalizing " + track.Name);
            }
        }

        // MARK: progress

        void Start(Work kind, int total)
        {
            Work = kind;
            Done = 0;
            Total = total;
            Notify();
        }

        void Step(string message)
        {
            Done++;
            if (message != null) Status = $"{message} ({Done}/{Total})";
            Notify();
        }

        void Finish(string message)
        {
            Work = Work.None;
            Status = message;
            Notify();
        }

        /// Runs `body` for every item, a few at a time (ffmpeg is single-threaded per file).
        async Task ForEach<T>(IList<T> items, Func<T, Task> body)
        {
            using (var gate = new SemaphoreSlim(parallel))
            {
                var tasks = items.Select(async item =>
                {
                    await gate.WaitAsync();
                    try { await body(item); }
                    finally { gate.Release(); }
                }).ToList();
                await Task.WhenAll(tasks);
            }
        }

        // MARK: profiles — same JSON as the Linux app (~/.config/ohmega/profiles.json) and the Mac app

        static string ProfilesPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ohmega", "profiles.json");

        void LoadProfiles()
        {
            try
            {
                var raw = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(ProfilesPath, Encoding.UTF8)) as Dictionary<string, object>;
                if (raw == null) return;
                foreach (var pair in raw)
                {
                    if (!(pair.Value is Dictionary<string, object> p)) continue;
                    Profiles[pair.Key] = new Profile
                    {
                        TargetLabel = p.TryGetValue("target_label", out var label) ? label as string ?? "" : "",
                        CustomLufs = p.TryGetValue("custom_lufs", out var lufs) ? Convert.ToDouble(lufs, CultureInfo.InvariantCulture) : -16,
                        Backup = !p.TryGetValue("backup", out var b) || b is bool flag && flag,
                    };
                }
            }
            catch (Exception) { }          // no profiles yet, or a broken file — start empty
        }

        void SaveProfiles()
        {
            var inv = CultureInfo.InvariantCulture;
            var json = new JavaScriptSerializer();
            var sb = new StringBuilder("{\n");
            sb.Append(string.Join(",\n", Profiles.Select(p =>
                $"  {json.Serialize(p.Key)}: {{\n    \"backup\": {(p.Value.Backup ? "true" : "false")},\n" +
                $"    \"custom_lufs\": {p.Value.CustomLufs.ToString("0.0###", inv)},\n    \"target_label\": {json.Serialize(p.Value.TargetLabel)}\n  }}")));
            sb.Append("\n}\n");
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ProfilesPath));
                File.WriteAllText(ProfilesPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        public void SaveProfile(string name)
        {
            Profiles[name] = new Profile { TargetLabel = TargetLabel, CustomLufs = CustomLufs, Backup = Backup };
            SaveProfiles();
            Notify();
        }

        public void DeleteProfile(string name)
        {
            Profiles.Remove(name);
            SaveProfiles();
            Notify();
        }

        public void ApplyProfile(string name)
        {
            if (!Profiles.TryGetValue(name, out var p)) return;
            int i = Array.FindIndex(Engine.Targets, t => p.TargetLabel.StartsWith(t.Name, StringComparison.Ordinal));
            if (i >= 0) TargetIndex = i;
            else
            {
                TargetIndex = Engine.Targets.Length;
                CustomLufs = p.CustomLufs;
            }
            Backup = p.Backup;
            Notify();
        }
    }
}
