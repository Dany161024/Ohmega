// Program.cs — Ohmega for Windows: start, files given on the command line, and two development modes.
//
//   Ohmega.exe [files or folders…]          normal start (Explorer "Open with", dropping files on the icon)
//   Ohmega.exe --snapshot out.png [paths…] [--then-normalize] [--size 980x620]
//                                           opens the window with these files, scans (and normalizes + rescans),
//                                           saves the window as PNG and quits — for checking over SSH
//   Ohmega.exe --selftest <folder> <out.txt>
//                                           scan + normalize to −14 LUFS + rescan without a window, writes the results
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Ohmega
{
    static class Program
    {
        public const string Version = "1.3.1";
        public const string FfmpegVersion = "9.0.2";

        [STAThread]
        static int Main(string[] args)
        {
            int self = Array.IndexOf(args, "--selftest");
            if (self >= 0 && self + 2 < args.Length)
                return SelfTest(args[self + 1], args[self + 2]).GetAwaiter().GetResult();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm();
            var options = new HashSet<string> { "--snapshot", "--size" };
            var paths = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !options.Contains(args[i - 1]))).ToList();
            int snap = Array.IndexOf(args, "--snapshot");
            if (snap >= 0 && snap + 1 < args.Length)
                form.Shown += async (s, e) => await Snapshot(form, args[snap + 1], paths, args.Contains("--then-normalize"), Arg(args, "--size"));
            else if (paths.Count > 0)
                form.Shown += (s, e) => form.Lib.Add(paths);
            Application.Run(form);
            return 0;
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static async Task Snapshot(MainForm form, string output, List<string> paths, bool thenNormalize, string size)
        {
            if (size != null && size.Split('x') is var wh && wh.Length == 2)
                form.Size = new Size(int.Parse(wh[0], CultureInfo.InvariantCulture), int.Parse(wh[1], CultureInfo.InvariantCulture));
            if (paths.Count > 0)
            {
                form.Lib.Add(paths);
                await form.Lib.Scan();
                if (thenNormalize)                  // whole round trip: normalize, then scan again
                {
                    await form.Lib.Normalize();
                    await form.Lib.Scan();
                }
                if (form.Lib.Tracks.Count > 1) form.Lib.Tracks[1].Selected = true;
            }
            await Task.Delay(600);
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                bmp.Save(output, ImageFormat.Png);
            }
            form.Close();
        }

        static async Task<int> SelfTest(string folder, string output)
        {
            var log = new StringBuilder();
            int code = 0;
            var inv = CultureInfo.InvariantCulture;
            log.AppendLine("ffmpeg: " + (Engine.Ffmpeg ?? "MISSING"));
            var files = Engine.Collect(folder);
            log.AppendLine("files: " + files.Count);
            foreach (var file in files)
            {
                try
                {
                    double before = await Engine.Measure(file);
                    double gain = -14 - before;
                    if (Math.Abs(gain) >= Engine.SkipBelow)
                    {
                        Engine.Backup(file);
                        await Engine.Apply(gain, file);
                    }
                    double after = await Engine.Measure(file);
                    log.AppendLine(string.Format(inv, "{0}  {1:0.0} → {2:0.0} LUFS (gain {3:+0.0;-0.0} dB)", Path.GetFileName(file), before, after, gain));
                    if (Math.Abs(after + 14) > 0.5) code = 1;
                }
                catch (Exception e)
                {
                    log.AppendLine(Path.GetFileName(file) + "  ERROR: " + e.Message);
                    code = 2;
                }
            }
            try
            {
                log.AppendLine(string.Format(inv, "album (all files as one programme): {0:0.0} LUFS", await Engine.MeasureAlbum(files)));
            }
            catch (Exception e)
            {
                log.AppendLine("album ERROR: " + e.Message);
                code = 2;
            }
            File.WriteAllText(output, log.ToString(), new UTF8Encoding(false));
            return code;
        }
    }
}
