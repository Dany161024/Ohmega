// Dialogs.cs — About, the profile name prompt, the modern folder picker and the "done" notification.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ohmega
{
    static class AboutBox
    {
        public static void Show(IWin32Window owner)
        {
            using (var form = new Form())
            {
                float s = form.DeviceDpi / 96f;
                form.Text = "About Ohmega";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = form.MinimizeBox = form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;
                form.Font = SystemFonts.MessageBoxFont;
                form.BackColor = Ui.Window;
                form.AutoScaleMode = AutoScaleMode.None;
                form.ClientSize = new Size((int)(440 * s), (int)(330 * s));
                int x = (int)(24 * s), w = form.ClientSize.Width - 2 * x;

                var icon = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(x, (int)(24 * s), (int)(64 * s), (int)(64 * s)) };
                using (var ico = Ui.AppIcon(256)) icon.Image = ico.ToBitmap();
                var name = new Label { Text = "Ohmega", Font = new Font(form.Font.FontFamily, 16f), AutoSize = true, ForeColor = Ui.Text,
                    Location = new Point(x + (int)(80 * s), (int)(30 * s)) };
                var version = new Label { Text = "Version " + Program.Version + " for Windows", AutoSize = true, ForeColor = Ui.Secondary,
                    Location = new Point(x + (int)(82 * s), (int)(62 * s)) };
                var text = new Label { Text = "Normalizes loudness straight into your audio files (EBU R128). No tags, no player settings — it plays right everywhere.",
                    ForeColor = Ui.Text, Bounds = new Rectangle(x, (int)(108 * s), w, (int)(40 * s)) };
                var link = new LinkLabel { Text = "ohmega.is-my.app", AutoSize = true, Location = new Point(x, (int)(156 * s)),
                    LinkColor = Ui.OrangePressed, ActiveLinkColor = Ui.Orange };
                link.LinkClicked += (o, e) => { try { Process.Start("https://ohmega.is-my.app"); } catch (Exception) { } };
                var legal = new Label
                {
                    Text = "Free software under the GPL-3.0.\n\nIncludes FFmpeg " + Program.FfmpegVersion + " (GPL-3.0, ffmpeg.org) — Windows build by gyan.dev; "
                         + "its source code is at ffmpeg.org/download.html.",
                    ForeColor = Ui.Secondary, Bounds = new Rectangle(x, (int)(186 * s), w, (int)(76 * s)),
                };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.System,
                    Size = new Size((int)(88 * s), (int)(28 * s)) };
                ok.Location = new Point(form.ClientSize.Width - x - ok.Width, form.ClientSize.Height - (int)(20 * s) - ok.Height);
                form.Controls.AddRange(new Control[] { icon, name, version, text, link, legal, ok });
                form.AcceptButton = form.CancelButton = ok;
                form.ShowDialog(owner);
            }
        }
    }

    static class InputBox
    {
        /// A small "name it" dialog → the text, or null when cancelled.
        public static string Ask(IWin32Window owner, string title, string detail, string placeholder)
        {
            using (var form = new Form())
            {
                float s = form.DeviceDpi / 96f;
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = form.MinimizeBox = form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;
                form.Font = SystemFonts.MessageBoxFont;
                form.BackColor = Ui.Window;
                form.AutoScaleMode = AutoScaleMode.None;
                form.ClientSize = new Size((int)(380 * s), (int)(150 * s));
                int x = (int)(20 * s), w = form.ClientSize.Width - 2 * x;
                var info = new Label { Text = detail, ForeColor = Ui.Secondary, AutoEllipsis = true, Bounds = new Rectangle(x, (int)(18 * s), w, (int)(20 * s)) };
                var box = new TextBox { Bounds = new Rectangle(x, (int)(46 * s), w, (int)(24 * s)) };
                SetCue(box, placeholder);
                var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.System, Size = new Size((int)(88 * s), (int)(28 * s)), Enabled = false };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.System, Size = ok.Size };
                int y = form.ClientSize.Height - (int)(18 * s) - ok.Height;
                ok.Location = new Point(form.ClientSize.Width - x - ok.Width, y);
                cancel.Location = new Point(ok.Left - (int)(8 * s) - cancel.Width, y);
                box.TextChanged += (o, e) => ok.Enabled = box.Text.Trim().Length > 0;
                form.Controls.AddRange(new Control[] { info, box, cancel, ok });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }

        const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

        static void SetCue(TextBox box, string text) => box.HandleCreated += (o, e) => SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
    }

    /// The Windows Vista+ folder picker (IFileOpenDialog with FOS_PICKFOLDERS) — the old FolderBrowserDialog
    /// is a tiny tree and cannot pick more than one folder.
    static class FolderPicker
    {
        public static List<string> Pick(IWin32Window owner, string title)
        {
            var result = new List<string>();
            IFileOpenDialog dialog = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialogCom();
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_ALLOWMULTISELECT | FOS_PATHMUSTEXIST);
                dialog.SetTitle(title);
                dialog.SetOkButtonLabel("Add");
                if (dialog.Show(owner?.Handle ?? IntPtr.Zero) != 0) return result;      // cancelled
                dialog.GetResults(out IShellItemArray items);
                items.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    items.GetItemAt(i, out IShellItem item);
                    item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                    if (!string.IsNullOrEmpty(path)) result.Add(path);
                }
            }
            catch (Exception)
            {
                using (var fallback = new FolderBrowserDialog { Description = title, ShowNewFolderButton = false })   // without COM (e.g. Wine)
                    if (fallback.ShowDialog(owner) == DialogResult.OK) result.Add(fallback.SelectedPath);
            }
            finally
            {
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
            return result;
        }

        const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_ALLOWMULTISELECT = 0x200, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCom { }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int placement);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hresult);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IShellItemArray items);
            void GetSelectedItems(out IShellItemArray items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint form, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemArray
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetPropertyStore(int flags, ref Guid iid, out IntPtr result);
            void GetPropertyDescriptionList(IntPtr keyType, ref Guid iid, out IntPtr result);
            void GetAttributes(int flags, uint mask, out uint attributes);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
            void EnumItems(out IntPtr items);
        }
    }

    /// When normalizing finishes in the background: flash the taskbar button and show a Windows notification.
    static class Notifier
    {
        public static void Done(Form window, string message)
        {
            if (Form.ActiveForm == window) return;
            var info = new FLASHWINFO { hwnd = window.Handle, dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG, uCount = 3 };
            info.cbSize = (uint)Marshal.SizeOf(info);
            FlashWindowEx(ref info);
            var icon = new NotifyIcon { Icon = window.Icon, Text = "Ohmega", Visible = true };
            icon.BalloonTipClicked += (s, e) => { window.Activate(); icon.Dispose(); };
            icon.ShowBalloonTip(8000, "Ohmega", message, ToolTipIcon.None);
            var timer = new Timer { Interval = 15000 };
            timer.Tick += (s, e) => { timer.Dispose(); icon.Dispose(); };
            timer.Start();
        }

        const uint FLASHW_TRAY = 2, FLASHW_TIMERNOFG = 12;

        [StructLayout(LayoutKind.Sequential)]
        struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        static extern bool FlashWindowEx(ref FLASHWINFO info);
    }
}
