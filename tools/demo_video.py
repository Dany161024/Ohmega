"""Demo video for the website: the real app (offscreen) on generated sample tracks, with a drawn cursor,
a camera that zooms to where the action is, and captions
→ docs/media/demo.mp4 + demo.webm + demo-poster.jpg + og.png + docs/screenshots/hero.png.

Nothing personal ends up in the video: tracks are synthesized by ffmpeg in ~/.cache/ohmega-demo,
profiles go to a temp file and the desktop notification is suppressed.

    ~/.venvs/ohmega-demo/bin/python tools/demo_video.py      (needs PyQt6 + ffmpeg)
"""
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

OUT_W, OUT_H, FPS = 1600, 1000, 30     # the video
K = 1.5                                # the scene is drawn 1.5× bigger, so zoomed shots stay sharp
A = 1.55                               # app pixels per logical pixel, in video coordinates
S = A * K                              # … and in scene coordinates
os.environ["QT_QPA_PLATFORM"] = "offscreen"
os.environ["QT_SCALE_FACTOR"] = f"{S:.4f}"
ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from PyQt6.QtCore import QPointF, QRectF, Qt  # noqa: E402
from PyQt6.QtGui import (QColor, QFont, QImage, QLinearGradient, QPainter, QPainterPath, QPen,  # noqa: E402
                         QPixmap, QPolygonF, QRadialGradient)
from PyQt6.QtWidgets import QApplication  # noqa: E402

import main  # noqa: E402

SAMPLES = Path.home() / ".cache" / "ohmega-demo"
MEDIA = ROOT / "docs" / "media"
W, H = OUT_W * K, OUT_H * K            # scene size
WIN_W, WIN_H = 820, 500                # logical size of the app window (its minimum is 800 × 580 → 820 × 500 fits)
TITLE_H = 38 * K
ORANGE = QColor("#e8692a")
FONT = "Noto Sans"

# name, LUFS it is generated at, seconds, kind
ALBUM = "Neon Summer"
TRACKS = [
    (f"{ALBUM}/01 Sunrise Intro.flac", -21.0, 40, "pad"),
    (f"{ALBUM}/02 City Lights.flac", -10.5, 40, "beat"),
    (f"{ALBUM}/03 Afterglow.flac", -12.0, 40, "beat2"),
    ("Festival Anthem (Club Mix).mp3", -6.5, 40, "beat"),
    ("Late Night Podcast - Ep. 12.opus", -19.5, 40, "voice"),
    ("Lo-fi Study Session.ogg", -16.5, 40, "beat2"),
    ("Field Recording - Rain.wav", -27.0, 40, "rain"),
]
SOURCES = {
    "pad": "aevalsrc='0.25*sin(2*PI*220*t)*(0.7+0.3*sin(2*PI*0.25*t))+0.2*sin(2*PI*277.18*t)+0.2*sin(2*PI*329.63*t)':s=44100:c=stereo",
    "beat": "aevalsrc='0.35*sin(2*PI*55*t)*exp(-9*mod(t,0.5))+0.15*sin(2*PI*440*t)*(mod(floor(t*4),2))+0.1*sin(2*PI*659.25*t)':s=44100:c=stereo",
    "beat2": "aevalsrc='0.3*sin(2*PI*65.4*t)*exp(-7*mod(t,0.6))+0.15*sin(2*PI*392*t)*(0.5+0.5*sin(2*PI*0.5*t))+0.1*sin(2*PI*523.25*t)':s=44100:c=stereo",
    "voice": "aevalsrc='0.3*sin(2*PI*(160+30*sin(2*PI*3*t))*t)*(0.5+0.5*sin(2*PI*2.5*t))':s=44100:c=stereo",
    "rain": "anoisesrc=c=pink:a=0.3:r=44100,aformat=channel_layouts=stereo",
}
CODEC = {".flac": ["-c:a", "flac"], ".mp3": ["-c:a", "libmp3lame", "-b:a", "192k"], ".opus": ["-c:a", "libopus", "-b:a", "96k"],
         ".ogg": ["-c:a", "libvorbis", "-q:a", "5"], ".wav": ["-c:a", "pcm_s16le"]}


def lufs(path):
    r = subprocess.run(["ffmpeg", "-nostats", "-i", str(path), "-af", "ebur128", "-f", "null", "-"],
                       capture_output=True, text=True)
    return float(re.findall(r"I:\s+(-?[\d.]+) LUFS", r.stderr)[-1])


def make_samples():
    """Fresh sample tracks at very different loudness (regenerated each run: the demo normalizes them)."""
    shutil.rmtree(SAMPLES, ignore_errors=True)
    for name, target, secs, kind in TRACKS:
        out = SAMPLES / name
        out.parent.mkdir(parents=True, exist_ok=True)
        raw = out.with_suffix(".raw.wav")
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-f", "lavfi", "-i", SOURCES[kind], "-t", str(secs),
                        "-c:a", "pcm_s16le", str(raw)], check=True)
        gain = target - lufs(raw)
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", str(raw), "-af", f"volume={gain:.2f}dB",
                        *CODEC[out.suffix], str(out)], check=True)
        raw.unlink()
        print(f"  {name}: {lufs(out):.1f} LUFS")


# ── the app ──────────────────────────────────────────────────────────────────
main.PROFILES_PATH = Path(tempfile.mkdtemp()) / "profiles.json"
_popen = subprocess.Popen


def _no_notify(args, *a, **k):
    if args and args[0] == "notify-send":
        return None
    return _popen(args, *a, **k)


app = QApplication(["ohmega"])
app.setApplicationName("Ohmega")
app.setStyle("Fusion")
win = None
ICON = QPixmap(str(ROOT / "ohmega.png"))


def font(px, weight=QFont.Weight.Normal):
    f = QFont(FONT)
    f.setPixelSize(int(px))
    f.setWeight(weight)
    return f


# ── composing a frame ────────────────────────────────────────────────────────
class Scene:
    """Everything is drawn into a scene 1.5× the video size; a camera crops and scales it into the frame.
    Captions are drawn on the final frame, so they keep their size while the camera zooms."""

    def __init__(self):
        self.win_x = (W - WIN_W * S) / 2
        self.win_y = 26 * K
        self.cursor = QPointF(W * 0.62, H * 0.97)
        self.caption = ""
        self.ripple = 0.0                       # 1 → 0 after a click
        self.ghost = None                       # (text, sub) of a dragged item, drawn at the cursor
        self.highlight_drop = False
        self.cam = [W / 2, H / 2, 1.0]          # centre x, centre y, zoom
        self.cam_to = list(self.cam)
        self.frames = 0
        self.last = self.clean = None
        enc = ["ffmpeg", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgra", "-s", f"{OUT_W}x{OUT_H}",
               "-r", str(FPS), "-i", "-"]
        MEDIA.mkdir(parents=True, exist_ok=True)
        self.encoders = [
            _popen(enc + ["-c:v", "libx264", "-preset", "slow", "-tune", "animation", "-crf", "26",
                          "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-an", str(MEDIA / "demo.mp4")],
                   stdin=subprocess.PIPE),
            _popen(enc + ["-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "40", "-row-mt", "1", "-deadline", "good",
                          "-cpu-used", "4", "-pix_fmt", "yuv420p", "-an", str(MEDIA / "demo.webm")],
                   stdin=subprocess.PIPE),
        ]

    # positions in scene coordinates
    def to_scene(self, widget, point):
        g = widget.mapTo(win, point)
        return QPointF(self.win_x + g.x() * S, self.win_y + TITLE_H + g.y() * S)

    def rect_of(self, widget, rect=None):
        r = rect if rect is not None else widget.rect()
        a, b = self.to_scene(widget, r.topLeft()), self.to_scene(widget, r.bottomRight())
        return QRectF(a, b)

    def app_point(self, widget):
        return self.to_scene(widget, widget.rect().center())

    def cell_rect(self, row, col):
        t = win.table
        return self.rect_of(t.viewport(), t.visualItemRect(t.item(row, col)))

    # camera
    def wide(self):
        self.cam_to = [W / 2, H / 2, 1.0]

    def focus(self, rect, zmax=1.75, pad=0.08):
        """Zoom so that `rect` fills the frame above the caption band."""
        w, h = rect.width() * (1 + 2 * pad), rect.height() * (1 + 2 * pad)
        z = max(1.0, min(zmax, W / w, (H * 0.82) / h))
        vis_h = H / z
        self.cam_to = [rect.center().x(), rect.center().y() + vis_h * 0.07, z]

    def source_rect(self):
        x, y, z = self.cam
        w, h = W / z, H / z
        x = min(max(x, w / 2), W - w / 2)
        y = min(max(y, h / 2), H - h / 2)
        return QRectF(x - w / 2, y - h / 2, w, h)

    def render(self):
        app.processEvents()
        for i in range(3):                       # smooth camera: ease towards the target
            self.cam[i] += (self.cam_to[i] - self.cam[i]) * 0.15
        shot = win.grab().toImage()
        shot.setDevicePixelRatio(1.0)            # draw the grab in its real pixels
        scene = QImage(int(W), int(H), QImage.Format.Format_ARGB32_Premultiplied)
        p = QPainter(scene)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)
        self.draw_backdrop(p)
        self.draw_window(p, shot)
        if self.highlight_drop:
            rr = self.rect_of(win.drop_area).adjusted(4 * K, 4 * K, -4 * K, -4 * K)
            p.setBrush(QColor(232, 105, 42, 40))
            p.setPen(QPen(ORANGE, 3 * K, Qt.PenStyle.DashLine))
            p.drawRoundedRect(rr, 10 * K, 10 * K)
        if self.ghost:
            self.draw_ghost(p, *self.ghost)
        if self.ripple > 0:
            r = (36 * (1 - self.ripple) + 8) * K
            p.setPen(QPen(QColor(232, 105, 42, int(230 * self.ripple)), 3.5 * K))
            p.setBrush(Qt.BrushStyle.NoBrush)
            p.drawEllipse(self.cursor, r, r)
            self.ripple = max(0.0, self.ripple - 0.07)
        self.draw_cursor(p)
        p.end()

        out = QImage(OUT_W, OUT_H, QImage.Format.Format_ARGB32_Premultiplied)
        q = QPainter(out)
        q.setRenderHint(QPainter.RenderHint.Antialiasing)
        q.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)
        q.drawImage(QRectF(0, 0, OUT_W, OUT_H), scene, self.source_rect())
        self.clean = out.copy()                  # the frame without its caption (poster, og image)
        if self.caption:
            self.draw_caption(q)
        q.end()
        data = out.constBits().asstring(out.sizeInBytes())
        for e in self.encoders:
            e.stdin.write(data)
        self.frames += 1
        self.last = out
        return out

    def draw_backdrop(self, p):
        bg = QLinearGradient(0, 0, W, H)
        bg.setColorAt(0, QColor("#21180f"))
        bg.setColorAt(1, QColor("#0d0b0a"))
        p.fillRect(QRectF(0, 0, W, H), bg)
        glow = QRadialGradient(QPointF(W * 0.5, H * 0.05), W * 0.6)
        glow.setColorAt(0, QColor(232, 105, 42, 60))
        glow.setColorAt(1, QColor(232, 105, 42, 0))
        p.fillRect(QRectF(0, 0, W, H), glow)

    def draw_window(self, p, shot):
        ww, wh = shot.width(), shot.height()
        frame = QRectF(self.win_x, self.win_y, ww, wh + TITLE_H)
        p.setPen(Qt.PenStyle.NoPen)
        for i in range(16, 0, -2):                                    # soft shadow
            p.setBrush(QColor(0, 0, 0, 12))
            p.drawRoundedRect(frame.adjusted(-i * K, (-i + 10) * K, i * K, (i + 10) * K), (14 + i) * K, (14 + i) * K)
        path = QPainterPath()
        path.addRoundedRect(frame, 12 * K, 12 * K)
        p.setClipPath(path)
        p.fillRect(frame, QColor("#e9e9e8"))
        p.drawImage(QPointF(self.win_x, self.win_y + TITLE_H), shot)
        p.setClipping(False)
        p.drawPixmap(QRectF(self.win_x + 12 * K, self.win_y + 8 * K, 22 * K, 22 * K), ICON, QRectF(ICON.rect()))
        p.setPen(QColor("#2b2b2b"))
        p.setFont(font(15 * K, QFont.Weight.DemiBold))
        p.drawText(QRectF(self.win_x, self.win_y, ww, TITLE_H), Qt.AlignmentFlag.AlignCenter, "Ohmega")
        p.setPen(QPen(QColor("#555"), 1.6 * K))
        cy = self.win_y + TITLE_H / 2
        for k, kind in enumerate(("min", "max", "close")):          # Breeze-like window buttons
            cx = self.win_x + ww - (84 - k * 30) * K
            d = 5 * K
            if kind == "min":
                p.drawLine(QPointF(cx - d, cy - 2 * K), QPointF(cx, cy + 3 * K)); p.drawLine(QPointF(cx, cy + 3 * K), QPointF(cx + d, cy - 2 * K))
            elif kind == "max":
                p.drawLine(QPointF(cx - d, cy + 3 * K), QPointF(cx, cy - 2 * K)); p.drawLine(QPointF(cx, cy - 2 * K), QPointF(cx + d, cy + 3 * K))
            else:
                p.drawLine(QPointF(cx - d, cy - d), QPointF(cx + d, cy + d)); p.drawLine(QPointF(cx - d, cy + d), QPointF(cx + d, cy - d))
        p.setPen(QPen(QColor(255, 255, 255, 30), K))
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawRoundedRect(frame, 12 * K, 12 * K)

    def draw_cursor(self, p):
        c, k = self.cursor, 1.15 * K
        pts = [(0, 0), (0, 25), (6.5, 19), (11, 29), (15, 27.5), (10.5, 18), (19, 18)]
        p.setPen(QPen(QColor("#111"), 1.6 * K))
        p.setBrush(QColor("white"))
        p.drawPolygon(QPolygonF([QPointF(c.x() + x * k, c.y() + y * k) for x, y in pts]))

    def draw_ghost(self, p, text, sub):
        box = QRectF(self.cursor.x() + 18 * K, self.cursor.y() + 16 * K, 300 * K, 66 * K)
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(QColor(0, 0, 0, 80))
        p.drawRoundedRect(box.translated(0, 6 * K), 12 * K, 12 * K)
        p.setBrush(QColor(255, 255, 255, 248))
        p.drawRoundedRect(box, 12 * K, 12 * K)
        icon = QRectF(box.x() + 12 * K, box.y() + 14 * K, 38 * K, 38 * K)
        p.setBrush(ORANGE)
        p.drawRoundedRect(icon, 8 * K, 8 * K)
        p.setPen(QColor("white"))
        p.setFont(font(20 * K, QFont.Weight.Bold))
        p.drawText(icon, Qt.AlignmentFlag.AlignCenter, "♪")
        p.setPen(QColor("#1b1b1b"))
        p.setFont(font(17 * K, QFont.Weight.DemiBold))
        p.drawText(QRectF(box.x() + 62 * K, box.y() + 9 * K, 230 * K, 26 * K), Qt.AlignmentFlag.AlignVCenter, text)
        p.setPen(QColor("#6b6b6b"))
        p.setFont(font(14 * K))
        p.drawText(QRectF(box.x() + 62 * K, box.y() + 34 * K, 230 * K, 22 * K), Qt.AlignmentFlag.AlignVCenter, sub)

    def draw_caption(self, q):
        q.setFont(font(33, QFont.Weight.DemiBold))
        tw = q.fontMetrics().horizontalAdvance(self.caption)
        box = QRectF((OUT_W - tw) / 2 - 34, OUT_H - 100, tw + 68, 68)
        q.setPen(Qt.PenStyle.NoPen)
        q.setBrush(QColor(0, 0, 0, 90))
        q.drawRoundedRect(box.translated(0, 5), 34, 34)
        q.setBrush(QColor(22, 17, 14, 240))
        q.drawRoundedRect(box, 34, 34)
        q.setPen(QPen(QColor(232, 105, 42, 150), 2))
        q.setBrush(Qt.BrushStyle.NoBrush)
        q.drawRoundedRect(box, 34, 34)
        q.setPen(QColor("#f6eee8"))
        q.drawText(box, Qt.AlignmentFlag.AlignCenter, self.caption)

    # ── motion helpers ──
    def hold(self, seconds):
        for _ in range(int(seconds * FPS)):
            self.render()

    def move(self, target, seconds=0.7):
        start = QPointF(self.cursor)
        n = max(1, int(seconds * FPS))
        for i in range(1, n + 1):
            t = i / n
            self.cursor = start + (target - start) * (t * t * (3 - 2 * t))
            self.render()

    def click(self, widget):
        self.move(self.app_point(widget))
        self.ripple = 1.0
        self.hold(0.15)
        widget.click()

    def wait_for(self, thread, extra=0.4, speed=1.5):
        """Frames while a worker runs, paced to real time (sped up `speed`×) — never faster than the real app."""
        t0, n = time.monotonic(), 0
        while thread.isRunning():
            if n < (time.monotonic() - t0) * FPS / speed:
                self.render()
                n += 1
            else:
                app.processEvents()
                time.sleep(0.004)
        self.hold(extra)

    def finish(self):
        for e in self.encoders:
            e.stdin.close()
            e.wait()


def rows_rect(s, first, last, cols=(0, 3)):
    a, b = s.cell_rect(first, cols[0]), s.cell_rect(last, cols[1])
    return QRectF(a.topLeft(), b.bottomRight())


def table_shot(s):
    """Buttons row + all seven rows: the whole working area, so a click is always in frame."""
    return s.rect_of(win.btn_add).united(s.rect_of(win.btn_apply)).united(rows_rect(s, 0, 6))


def main_demo():
    global win
    print("Generating sample tracks…")
    make_samples()
    win = main.MainWindow()
    win.resize(WIN_W, WIN_H)
    win.show()
    app.processEvents()
    subprocess.Popen = _no_notify                       # no desktop notification at the end
    s = Scene()
    table_top = lambda: s.rect_of(win.table)            # noqa: E731

    s.caption = "Ohmega makes every track equally loud"
    s.hold(1.3)

    # 1 — drop an album folder
    album = sorted(str(p) for p in (SAMPLES / ALBUM).glob("*") if p.is_file())
    s.caption = "Drop a folder — it becomes one album"
    s.move(QPointF(W * 0.06, H * 0.32), 0.35)
    s.ghost = (ALBUM, "Folder · 3 tracks")
    drop = s.app_point(win.drop_area)
    s.move(drop + QPointF(-160 * K, -10 * K), 0.9)
    s.highlight_drop = True
    s.hold(0.35)
    s.ghost, s.highlight_drop = None, False
    win.add_entries([(f, str(Path(f).parent)) for f in album])
    app.processEvents()
    s.focus(rows_rect(s, 0, 2).united(s.rect_of(win.drop_area)), zmax=1.3)
    s.hold(1.0)

    # 2 — and loose tracks
    loose = [str(SAMPLES / n) for n, *_ in TRACKS if "/" not in n]
    s.caption = "…or single tracks, in any format"
    s.wide()
    s.move(QPointF(W * 0.80, H * 0.32), 0.45)
    s.ghost = ("4 files", "MP3 · OPUS · OGG · WAV")
    s.move(drop + QPointF(40 * K, -10 * K), 0.9)
    s.highlight_drop = True
    s.hold(0.35)
    s.ghost, s.highlight_drop = None, False
    win.add_entries([(f, None) for f in loose])
    app.processEvents()
    s.focus(rows_rect(s, 0, 6), zmax=1.3)
    s.hold(0.9)

    # 3 — scan
    s.caption = "Scan measures true loudness (EBU R128)"
    s.focus(table_shot(s))
    s.click(win.btn_scan)
    s.focus(rows_rect(s, 0, 6), zmax=1.3)
    s.wait_for(win.worker, 0.3)
    s.caption = "From −6.7 to −27 LUFS — all over the place"
    s.move(s.cell_rect(3, 2).center() + QPointF(30 * K, 10 * K), 0.6)
    s.hold(1.8)

    # 4 — normalize
    s.caption = "Normalize writes the gain into the audio itself"
    s.focus(table_shot(s))
    s.click(win.btn_apply)
    s.focus(rows_rect(s, 0, 6), zmax=1.3)
    s.wait_for(win.apply_worker, 0.9)

    # 5 — proof
    s.caption = "Scan again: every single lands on −14 LUFS"
    s.focus(table_shot(s))
    s.click(win.btn_scan)
    s.focus(rows_rect(s, 0, 6), zmax=1.3)
    s.wait_for(win.worker, 0.2)
    s.focus(rows_rect(s, 3, 6), zmax=1.4)
    s.move(s.cell_rect(4, 2).center() + QPointF(40 * K, 8 * K), 0.6)
    s.hold(1.4)
    poster = s.clean
    s.caption = "…and the album moved as one, keeping its dynamics"
    s.focus(rows_rect(s, 0, 2), zmax=1.4)
    s.move(s.cell_rect(1, 2).center() + QPointF(40 * K, 8 * K), 0.5)
    s.hold(1.9)

    # outro
    s.caption = "No tags, no player settings — it just plays right"
    s.wide()
    s.cursor = QPointF(W + 80, H + 80)
    s.hold(2.4)
    s.finish()

    poster.save(str(MEDIA / "demo-poster.jpg"), "JPG", 88)
    og = poster.scaled(1200, 750, Qt.AspectRatioMode.IgnoreAspectRatio, Qt.TransformationMode.SmoothTransformation)
    og.copy(0, 30, 1200, 630).save(str(MEDIA / "og.png"))
    shot = win.grab()                                   # the app alone (no captions) for the README/metainfo
    shot.scaled(int(WIN_W * 1.25), int(WIN_H * 1.25), Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation).save(str(ROOT / "docs" / "screenshots" / "hero.png"))
    print(f"{s.frames} frames ({s.frames / FPS:.1f} s) → {MEDIA}")


if __name__ == "__main__":
    main_demo()
