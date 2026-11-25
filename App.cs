// =====================================================================
//  Pen Mapper  -  geef de knoppen van je pen eigen functies
//  Werkt met Windows Ink-pennen (MPP, USI, Wacom AES, ...).
//  De knoppen worden rechtstreeks van de pen gelezen (HID Raw Input),
//  dus het werkt ook terwijl de pen boven het scherm zweeft.
//
//  Bouwen: zie build.ps1 (gebruikt de C#-compiler die in Windows zit).
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Pen Mapper")]
[assembly: AssemblyProduct("Pen Mapper")]
[assembly: AssemblyDescription("Pen Mapper")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PenMapper
{
    // ------------------------------------------------------------- start
    static class Program
    {
        public const string AppName = "Pen Mapper";
        public const string Version = "1.0";
        public const string ShowEventName = "PenMapper_Show_7F3A";
        public const string ExitEventName = "PenMapper_Exit_7F3A";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PenMapper";

        [STAThread]
        static void Main(string[] args)
        {
            bool tray = false, restart = false, uninstall = false;
            string page = null, snapshot = null;
            foreach (var a in args)
            {
                if (a.Equals("/tray", StringComparison.OrdinalIgnoreCase)) tray = true;
                if (a.Equals("/restart", StringComparison.OrdinalIgnoreCase)) restart = true;
                if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                if (a.StartsWith("/page:", StringComparison.OrdinalIgnoreCase)) page = a.Substring(6);
                if (a.StartsWith("/snapshot:", StringComparison.OrdinalIgnoreCase)) snapshot = a.Substring(10);
            }

            Settings.Load();
            L.Init();
            foreach (var a in args)
                if (a.StartsWith("/lang:", StringComparison.OrdinalIgnoreCase)) L.En = a.Substring(6) == "en";   // ontwikkel-hulp
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (uninstall)
            {
                bool quiet = false;
                foreach (var a in args) if (a.Equals("/quiet", StringComparison.OrdinalIgnoreCase)) quiet = true;
                Uninstaller.Run(quiet);
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "PenMapper_SingleInstance_7F3A", out created))
            {
                var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
                if (!created)
                {
                    if (!restart)
                    {
                        showEvent.Set();       // al actief: vraag die om zich te tonen
                        return;
                    }
                    try { if (!mutex.WaitOne(8000)) return; }
                    catch (AbandonedMutexException) { }
                }

                var form = new MainForm(page, showEvent, exitEvent);
                IntPtr forceHandle = form.Handle;   // venster bestaat ook als het verborgen start
                if (!tray) form.Show();
                if (snapshot != null) TakeSnapshot(form, snapshot);
                Application.Run();
                try { mutex.ReleaseMutex(); } catch { }
            }
        }

        // ontwikkel-hulp: venster als afbeelding opslaan en afsluiten
        static void TakeSnapshot(Form form, string path)
        {
            var t = new System.Windows.Forms.Timer { Interval = 1000 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                Environment.Exit(0);
            };
            t.Start();
        }
    }

    // ------------------------------------------------------------- taal
    static class L
    {
        public static bool En;

        public static void Init()
        {
            string pref = Settings.Get("taal", "auto");
            if (pref == "nl") En = false;
            else if (pref == "en") En = true;
            else En = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "nl"
                   && CultureInfo.CurrentCulture.TwoLetterISOLanguageName != "nl";   // ook landinstelling NL/BE telt
        }

        public static string T(string nl, string en) { return En ? en : nl; }
    }

    // ------------------------------------------------------------- thema
    static class Theme
    {
        public static readonly Color Bg = Hex("16161B");
        public static readonly Color Side = Hex("0F0F13");
        public static readonly Color Card = Hex("202028");
        public static readonly Color Field = Hex("2C2C37");
        public static readonly Color FieldHover = Hex("383846");
        public static readonly Color Line = Hex("2E2E39");
        public static readonly Color Text = Hex("F2F2F5");
        public static readonly Color Muted = Hex("9A9AAA");
        public static readonly Color Accent = Hex("7C8CFF");
        public static readonly Color AccentHover = Hex("94A1FF");
        public static readonly Color AccentDim = Hex("3A4170");
        public static readonly Color Good = Hex("3DD68C");
        public static readonly Color Off = Hex("4A4A55");
        public static readonly Color Bad = Hex("FF6B6B");
        public static readonly string IconFont;
        public static readonly float S;     // schaalfactor van het scherm (1 = 100%)

        static Theme()
        {
            IconFont = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
        }

        static Color Hex(string h) { return ColorTranslator.FromHtml("#" + h); }

        static bool HasFont(string name)
        {
            using (var fc = new InstalledFontCollection())
                foreach (var ff in fc.Families)
                    if (ff.Name == name) return true;
            return false;
        }

        public static Font Regular(float size) { return new Font("Segoe UI", size, FontStyle.Regular); }
        public static Font Semi(float size) { return new Font("Segoe UI Semibold", size, FontStyle.Regular); }
        public static Font Bold(float size) { return new Font("Segoe UI", size, FontStyle.Bold); }
        public static Font Icons(float size) { return new Font(IconFont, size, FontStyle.Regular); }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }
    }

    // ------------------------------------------------------- instellingen
    static class Settings
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PenMapper");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");
        static readonly Dictionary<string, string> data = new Dictionary<string, string>();

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) data[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        public static string Get(string key, string def)
        {
            string v;
            return data.TryGetValue(key, out v) ? v : def;
        }

        public static int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key, ""), out v) ? v : def;
        }

        public static bool GetBool(string key, bool def)
        {
            return Get(key, def ? "1" : "0") == "1";
        }

        public static void Set(string key, string value)
        {
            data[key] = (value ?? "").Replace("\r", " ").Replace("\n", " ");
            Save();
        }

        public static void SetBool(string key, bool v) { Set(key, v ? "1" : "0"); }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                foreach (var kv in data) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ------------------------------------------------------------- acties
    enum ActionKind { None, Keys, Special, Shortcut, Text, Run }

    class ActionDef
    {
        public readonly string Id, Category, Name, Keys;
        public readonly ActionKind Kind;
        public ActionDef(string id, string cat, string name, ActionKind kind, string keys)
        {
            Id = id; Category = cat; Name = name; Kind = kind; Keys = keys;
        }
        public bool NeedsParam
        {
            get { return Kind == ActionKind.Shortcut || Kind == ActionKind.Text || Kind == ActionKind.Run; }
        }
        public override string ToString() { return Category == "" ? Name : Category + ": " + Name; }
    }

    static class Actions
    {
        public static readonly List<ActionDef> All = new List<ActionDef>();

        static void K(string id, string cat, string nl, string en, string keys) { All.Add(new ActionDef(id, cat, L.T(nl, en), ActionKind.Keys, keys)); }
        static void S(string id, string cat, string nl, string en) { All.Add(new ActionDef(id, cat, L.T(nl, en), ActionKind.Special, null)); }

        static Actions()
        {
            string mouse = L.T("Muis", "Mouse"), edit = L.T("Bewerken", "Edit"), kb = L.T("Toetsenbord", "Keyboard"),
                screen = L.T("Scherm", "Screen"), nav = L.T("Navigatie", "Navigation"), win = L.T("Vensters", "Windows"),
                media = "Media", pres = L.T("Presentatie", "Presentation"), app = "Pen Mapper", own = L.T("Eigen", "Custom");

            All.Add(new ActionDef("none", "", L.T("Niets doen", "Do nothing"), ActionKind.None, null));

            S("rclick", mouse, "Rechtermuisklik", "Right click");
            S("mclick", mouse, "Middelste muisklik", "Middle click");
            S("dblclick", mouse, "Dubbelklik", "Double click");
            S("wheelup", mouse, "Scroll omhoog", "Scroll up");
            S("wheeldown", mouse, "Scroll omlaag", "Scroll down");

            K("undo", edit, "Ongedaan maken (Ctrl+Z)", "Undo (Ctrl+Z)", "Ctrl+Z");
            K("redo", edit, "Opnieuw (Ctrl+Y)", "Redo (Ctrl+Y)", "Ctrl+Y");
            K("copy", edit, "Kopiëren (Ctrl+C)", "Copy (Ctrl+C)", "Ctrl+C");
            K("cut", edit, "Knippen (Ctrl+X)", "Cut (Ctrl+X)", "Ctrl+X");
            K("paste", edit, "Plakken (Ctrl+V)", "Paste (Ctrl+V)", "Ctrl+V");
            K("selall", edit, "Alles selecteren (Ctrl+A)", "Select all (Ctrl+A)", "Ctrl+A");
            K("save", edit, "Opslaan (Ctrl+S)", "Save (Ctrl+S)", "Ctrl+S");
            K("find", edit, "Zoeken (Ctrl+F)", "Find (Ctrl+F)", "Ctrl+F");
            K("enter", edit, "Enter", "Enter", "Enter");
            K("backspace", edit, "Backspace", "Backspace", "Back");
            K("delete", edit, "Delete", "Delete", "Delete");
            K("escape", edit, "Escape", "Escape", "Escape");

            K("osk", kb, "Schermtoetsenbord aan/uit", "On-screen keyboard on/off", "Win+Ctrl+O");
            S("touchkb", kb, "Aanraaktoetsenbord aan/uit", "Touch keyboard on/off");
            K("emoji", kb, "Emoji-paneel (Win+.)", "Emoji panel (Win+.)", "Win+OemPeriod");
            K("dictate", kb, "Dicteren (Win+H)", "Dictation (Win+H)", "Win+H");
            K("cliphist", kb, "Klembordgeschiedenis (Win+V)", "Clipboard history (Win+V)", "Win+V");

            K("snip", screen, "Knipprogramma (Win+Shift+S)", "Snipping Tool (Win+Shift+S)", "Win+Shift+S");
            K("prtsc", screen, "Screenshot opslaan (Win+PrtSc)", "Save screenshot (Win+PrtSc)", "Win+PrintScreen");
            K("zoomin", screen, "Inzoomen (Ctrl +)", "Zoom in (Ctrl +)", "Ctrl+Oemplus");
            K("zoomout", screen, "Uitzoomen (Ctrl -)", "Zoom out (Ctrl -)", "Ctrl+OemMinus");
            K("zoomreset", screen, "Zoom herstellen (Ctrl+0)", "Reset zoom (Ctrl+0)", "Ctrl+D0");
            K("magon", screen, "Vergrootglas aan", "Magnifier on", "Win+Add");
            K("magoff", screen, "Vergrootglas uit", "Magnifier off", "Win+Escape");

            K("left", nav, "Pijl links", "Arrow left", "Left");
            K("right", nav, "Pijl rechts", "Arrow right", "Right");
            K("up", nav, "Pijl omhoog", "Arrow up", "Up");
            K("down", nav, "Pijl omlaag", "Arrow down", "Down");
            K("pgup", nav, "Pagina omhoog", "Page up", "PageUp");
            K("pgdn", nav, "Pagina omlaag", "Page down", "PageDown");
            K("home", nav, "Begin (Home)", "Home", "Home");
            K("end", nav, "Einde (End)", "End", "End");
            K("back", nav, "Terug (Alt+Links)", "Back (Alt+Left)", "Alt+Left");
            K("forward", nav, "Vooruit (Alt+Rechts)", "Forward (Alt+Right)", "Alt+Right");
            K("tabnext", nav, "Volgend tabblad", "Next tab", "Ctrl+Tab");
            K("tabprev", nav, "Vorig tabblad", "Previous tab", "Ctrl+Shift+Tab");
            K("tabnew", nav, "Nieuw tabblad", "New tab", "Ctrl+T");
            K("tabclose", nav, "Tabblad sluiten", "Close tab", "Ctrl+W");

            K("taskview", win, "Taakweergave (Win+Tab)", "Task view (Win+Tab)", "Win+Tab");
            K("alttab", win, "Wisselen tussen apps", "Switch apps", "Ctrl+Alt+Tab");
            K("desktop", win, "Bureaublad tonen (Win+D)", "Show desktop (Win+D)", "Win+D");
            K("start", win, "Startmenu", "Start menu", "Win");
            K("explorer", win, "Verkenner (Win+E)", "File Explorer (Win+E)", "Win+E");
            K("settings", win, "Windows-instellingen (Win+I)", "Windows Settings (Win+I)", "Win+I");
            K("quick", win, "Snelle instellingen (Win+A)", "Quick settings (Win+A)", "Win+A");
            K("notif", win, "Meldingen (Win+N)", "Notifications (Win+N)", "Win+N");
            K("maximize", win, "Venster maximaliseren", "Maximize window", "Win+Up");
            K("minimize", win, "Venster minimaliseren", "Minimize window", "Win+Down");
            K("closewin", win, "Venster sluiten (Alt+F4)", "Close window (Alt+F4)", "Alt+F4");
            S("lock", win, "Scherm vergrendelen", "Lock screen");

            K("play", media, "Afspelen / pauze", "Play / pause", "MediaPlayPause");
            K("next", media, "Volgend nummer", "Next track", "MediaNextTrack");
            K("prev", media, "Vorig nummer", "Previous track", "MediaPreviousTrack");
            K("volup", media, "Volume omhoog", "Volume up", "VolumeUp");
            K("voldown", media, "Volume omlaag", "Volume down", "VolumeDown");
            K("mute", media, "Geluid dempen", "Mute", "VolumeMute");

            K("slideshow", pres, "Diavoorstelling starten (F5)", "Start slideshow (F5)", "F5");
            K("slidenext", pres, "Volgende dia", "Next slide", "PageDown");
            K("slideprev", pres, "Vorige dia", "Previous slide", "PageUp");
            K("black", pres, "Zwart scherm (B)", "Black screen (B)", "B");

            S("menu", app, "Snelmenu openen", "Open quick menu");
            S("pause", app, "Pauzeren / hervatten", "Pause / resume");

            All.Add(new ActionDef("shortcut", own, L.T("Eigen sneltoets...", "Custom shortcut..."), ActionKind.Shortcut, null));
            All.Add(new ActionDef("text", own, L.T("Tekst typen...", "Type text..."), ActionKind.Text, null));
            All.Add(new ActionDef("run", own, L.T("Programma of website openen...", "Open program or website..."), ActionKind.Run, null));
        }

        public static ActionDef ById(string id)
        {
            foreach (var a in All) if (a.Id == id) return a;
            return null;
        }
    }

    // ---------------------------------------------------- invoer sturen
    static class Sender
    {
        public static bool TryParse(string combo, out List<Keys> mods, out Keys key)
        {
            mods = new List<Keys>();
            key = Keys.None;
            if (string.IsNullOrEmpty(combo)) return false;
            foreach (var raw in combo.Split('+'))
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                string low = t.ToLowerInvariant();
                if (low == "ctrl" || low == "control") mods.Add(Keys.ControlKey);
                else if (low == "shift") mods.Add(Keys.ShiftKey);
                else if (low == "alt") mods.Add(Keys.Menu);
                else if (low == "win") mods.Add(Keys.LWin);
                else if (t.Length == 1 && char.IsDigit(t[0])) key = Keys.D0 + (t[0] - '0');
                else
                {
                    Keys k;
                    if (!Enum.TryParse(t, true, out k)) return false;
                    key = k;
                }
            }
            return mods.Count > 0 || key != Keys.None;
        }

        public static void Combo(string combo)
        {
            List<Keys> mods;
            Keys key;
            if (!TryParse(combo, out mods, out key)) return;
            var list = new List<Native.INPUT>();
            foreach (var m in mods) list.Add(KeyInput(m, false));
            if (key != Keys.None)
            {
                list.Add(KeyInput(key, false));
                list.Add(KeyInput(key, true));
            }
            for (int i = mods.Count - 1; i >= 0; i--) list.Add(KeyInput(mods[i], true));
            Native.SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
        }

        static bool IsExtended(Keys k)
        {
            switch (k)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                case Keys.Insert: case Keys.Delete: case Keys.LWin: case Keys.RWin:
                case Keys.Apps: case Keys.Divide: case Keys.PrintScreen: case Keys.NumLock:
                case Keys.RControlKey: case Keys.RMenu:
                case Keys.VolumeUp: case Keys.VolumeDown: case Keys.VolumeMute:
                case Keys.MediaPlayPause: case Keys.MediaNextTrack: case Keys.MediaPreviousTrack: case Keys.MediaStop:
                case Keys.BrowserBack: case Keys.BrowserForward: case Keys.BrowserHome:
                    return true;
            }
            return false;
        }

        static Native.INPUT KeyInput(Keys k, bool up)
        {
            var inp = new Native.INPUT();
            inp.type = 1;
            inp.U.ki.wVk = (ushort)k;
            inp.U.ki.wScan = (ushort)Native.MapVirtualKey((uint)k, 0);
            uint flags = 0;
            if (up) flags |= 0x0002;
            if (IsExtended(k)) flags |= 0x0001;
            inp.U.ki.dwFlags = flags;
            return inp;
        }

        public static void Text(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            var list = new List<Native.INPUT>();
            foreach (char c in s)
            {
                for (int up = 0; up < 2; up++)
                {
                    var inp = new Native.INPUT();
                    inp.type = 1;
                    inp.U.ki.wScan = c;
                    inp.U.ki.dwFlags = 0x0004 | (up == 1 ? 0x0002u : 0u);
                    list.Add(inp);
                }
            }
            Native.SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public static void MouseClick(uint downFlag, uint upFlag, int count)
        {
            var list = new List<Native.INPUT>();
            for (int i = 0; i < count; i++)
            {
                list.Add(MouseInput(downFlag, 0));
                list.Add(MouseInput(upFlag, 0));
            }
            Native.SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public static void Wheel(int delta)
        {
            var arr = new[] { MouseInput(0x0800, delta) };
            Native.SendInput(1, arr, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        static Native.INPUT MouseInput(uint flags, int data)
        {
            var inp = new Native.INPUT();
            inp.type = 0;
            inp.U.mi.dwFlags = flags;
            inp.U.mi.mouseData = unchecked((uint)data);
            return inp;
        }

        public static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (int)(k - Keys.NumPad0);
            switch (k)
            {
                case Keys.Oemplus: return "+";
                case Keys.OemMinus: return "-";
                case Keys.OemPeriod: return ".";
                case Keys.Oemcomma: return ",";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Escape: return "Esc";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Space: return L.T("Spatie", "Space");
            }
            return k.ToString();
        }

        public static string Pretty(string combo)
        {
            List<Keys> mods;
            Keys key;
            if (!TryParse(combo, out mods, out key)) return "";
            var parts = new List<string>();
            foreach (var m in mods)
                parts.Add(m == Keys.ControlKey ? "Ctrl" : m == Keys.ShiftKey ? "Shift" : m == Keys.Menu ? "Alt" : "Win");
            if (key != Keys.None) parts.Add(KeyName(key));
            return string.Join(" + ", parts.ToArray());
        }
    }

    // ------------------------------------------------ aanraaktoetsenbord
    static class TouchKeyboard
    {
        [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITipInvocation { void Toggle(IntPtr hwnd); }

        public static void Toggle()
        {
            if (Process.GetProcessesByName("osk").Length > 0)
            {
                Sender.Combo("Win+Ctrl+O");
                return;
            }
            bool wasVisible = IsVisible();
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    var t = Type.GetTypeFromCLSID(new Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376"));
                    var tip = (ITipInvocation)Activator.CreateInstance(t);
                    tip.Toggle(Native.GetDesktopWindow());
                    Marshal.ReleaseComObject(tip);
                    break;
                }
                catch
                {
                    try
                    {
                        Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                            @"microsoft shared\ink\TabTip.exe"));
                    }
                    catch { }
                    Thread.Sleep(500);
                }
            }
            if (wasVisible) return;
            // Sommige Windows-versies negeren het commando: dan het schermtoetsenbord gebruiken
            var timer = new System.Windows.Forms.Timer { Interval = 900 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!IsVisible()) Sender.Combo("Win+Ctrl+O");
            };
            timer.Start();
        }

        static bool IsVisible()
        {
            IntPtr h = Native.FindWindow("Windows.UI.Core.CoreWindow", "Windows Input Experience");
            if (h == IntPtr.Zero) return false;
            int cloaked;
            Native.DwmGetWindowAttribute(h, 14, out cloaked, 4);
            return cloaked == 0 && Native.IsWindowVisible(h);
        }
    }

    // ----------------------------------------------------- pen uitlezen
    enum PenSignal { None = 0, Barrel = 1, Barrel2 = 2, Eraser = 3 }

    class PenInput
    {
        public event Action<HashSet<int>> Report;
        readonly Dictionary<IntPtr, IntPtr> preparsed = new Dictionary<IntPtr, IntPtr>();
        readonly ushort[] usages = new ushort[64];

        public bool Register(IntPtr hwnd)
        {
            var devs = new Native.RAWINPUTDEVICE[2];
            devs[0].UsagePage = 0x0D; devs[0].Usage = 0x02; devs[0].Flags = 0x100; devs[0].Target = hwnd; // pen
            devs[1].UsagePage = 0x0D; devs[1].Usage = 0x01; devs[1].Flags = 0x100; devs[1].Target = hwnd; // digitizer
            return Native.RegisterRawInputDevices(devs, 2, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
        }

        public void Handle(IntPtr lParam)
        {
            uint hdr = (uint)(8 + 2 * IntPtr.Size);
            uint size = 0;
            Native.GetRawInputData(lParam, 0x10000003, IntPtr.Zero, ref size, hdr);
            if (size == 0) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.GetRawInputData(lParam, 0x10000003, buf, ref size, hdr) == uint.MaxValue) return;
                if (Marshal.ReadInt32(buf, 0) != 2) return;          // RIM_TYPEHID
                IntPtr pp = GetPreparsed(Marshal.ReadIntPtr(buf, 8));
                if (pp == IntPtr.Zero) return;
                int reportSize = Marshal.ReadInt32(buf, (int)hdr);
                int count = Marshal.ReadInt32(buf, (int)hdr + 4);
                for (int i = 0; i < count; i++)
                    Parse(pp, new IntPtr(buf.ToInt64() + hdr + 8 + (long)i * reportSize), reportSize);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        IntPtr GetPreparsed(IntPtr dev)
        {
            IntPtr pp;
            if (preparsed.TryGetValue(dev, out pp)) return pp;
            uint size = 0;
            Native.GetRawInputDeviceInfo(dev, 0x20000005, IntPtr.Zero, ref size);
            if (size == 0) return IntPtr.Zero;
            pp = Marshal.AllocHGlobal((int)size);
            if (Native.GetRawInputDeviceInfo(dev, 0x20000005, pp, ref size) == uint.MaxValue)
            {
                Marshal.FreeHGlobal(pp);
                return IntPtr.Zero;
            }
            preparsed[dev] = pp;
            return pp;
        }

        void Parse(IntPtr pp, IntPtr report, int len)
        {
            uint n = (uint)usages.Length;
            int status = Native.HidP_GetUsages(0, 0x0D, 0, usages, ref n, pp, report, (uint)len);
            if (status != 0x00110000) return;   // HIDP_STATUS_SUCCESS
            var set = new HashSet<int>();
            for (int i = 0; i < n; i++) set.Add(usages[i]);
            if (Report != null) Report(set);
        }

        public static bool Has(HashSet<int> set, PenSignal s)
        {
            switch (s)
            {
                case PenSignal.Barrel: return set.Contains(0x44);
                case PenSignal.Barrel2: return set.Contains(0x5A);
                case PenSignal.Eraser: return set.Contains(0x45) || set.Contains(0x3C);
            }
            return false;
        }

        public static PenSignal First(HashSet<int> set)
        {
            if (Has(set, PenSignal.Barrel)) return PenSignal.Barrel;
            if (Has(set, PenSignal.Barrel2)) return PenSignal.Barrel2;
            if (Has(set, PenSignal.Eraser)) return PenSignal.Eraser;
            return PenSignal.None;
        }

        public static string SignalName(PenSignal s)
        {
            switch (s)
            {
                case PenSignal.Barrel: return L.T("knop-signaal", "button signal");
                case PenSignal.Barrel2: return L.T("2e knop-signaal", "2nd button signal");
                case PenSignal.Eraser: return L.T("gum-signaal", "eraser signal");
            }
            return L.T("geen signaal", "no signal");
        }
    }

    // ------------------------------------------------------- pen-profielen
    // Standaard-knopnamen en -signalen per penmodel. Met 'Leren' kan elke knop
    // altijd nog op het echte signaal van de pen worden afgestemd.
    class PenProfile
    {
        public readonly string Id, Name, LowerTitle, LowerHint, UpperTitle, UpperHint;
        public readonly PenSignal LowerSig, UpperSig;

        public PenProfile(string id, string name, string lowTitle, string lowHint, PenSignal lowSig,
            string upTitle, string upHint, PenSignal upSig)
        {
            Id = id; Name = name;
            LowerTitle = lowTitle; LowerHint = lowHint; LowerSig = lowSig;
            UpperTitle = upTitle; UpperHint = upHint; UpperSig = upSig;
        }

        public override string ToString() { return Name; }

        public static readonly List<PenProfile> All = new List<PenProfile>();

        static PenProfile()
        {
            string lower = L.T("Onderste knop", "Lower button"), upper = L.T("Bovenste knop", "Upper button");
            string nearTip = L.T("dichtst bij de punt", "closest to the tip");
            All.Add(new PenProfile("hp-mpp2", "HP MPP 2.0 Tilt Pen / HP Rechargeable Pen",
                lower, nearTip, PenSignal.Eraser, upper, "", PenSignal.Barrel));
            All.Add(new PenProfile("mpp", L.T("Andere MPP-pen (Dell, Lenovo, Asus, Acer, ...)", "Other MPP pen (Dell, Lenovo, Asus, Acer, ...)"),
                lower, nearTip, PenSignal.Barrel, upper, "", PenSignal.Eraser));
            All.Add(new PenProfile("surface", "Microsoft Surface Pen / Slim Pen",
                L.T("Zijknop", "Side button"), "", PenSignal.Barrel,
                L.T("Gum-kant", "Eraser end"), L.T("pen omgedraaid boven het scherm", "pen flipped over the screen"), PenSignal.Eraser));
            All.Add(new PenProfile("wacom", L.T("Wacom AES-pen (o.a. Lenovo, Fujitsu, Samsung)", "Wacom AES pen (Lenovo, Fujitsu, Samsung, ...)"),
                lower, nearTip, PenSignal.Barrel, upper, "", PenSignal.Barrel2));
            All.Add(new PenProfile("usi", L.T("USI-pen", "USI pen"),
                L.T("Zijknop", "Side button"), "", PenSignal.Barrel,
                L.T("Gum-kant", "Eraser end"), L.T("pen omgedraaid boven het scherm", "pen flipped over the screen"), PenSignal.Eraser));
            All.Add(new PenProfile("custom", L.T("Andere pen (zelf leren)", "Other pen (learn buttons)"),
                L.T("Knop 1", "Button 1"), L.T("klik op Leren", "click Learn"), PenSignal.Barrel,
                L.T("Knop 2", "Button 2"), L.T("klik op Leren", "click Learn"), PenSignal.Eraser));
        }

        public static PenProfile ById(string id)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return null;
        }
    }

    // ------------------------------------------------------- knop-model
    class Slot
    {
        public string Gesture;
        public DarkCombo Combo;
        public FieldBox Field;
        public FlatButton Browse;
        public PenButton Owner;
        public ActionDef Action { get { return Combo.SelectedItem as ActionDef; } }
        public string Param
        {
            get { return Action != null && Action.Kind == ActionKind.Shortcut ? Field.Box.Combo : Field.Box.Text.Trim(); }
        }
    }

    class PenButton
    {
        public string Key, Title, Hint;
        public PenSignal Signal, DefaultSignal;
        public bool Down, Pending, Held;
        public readonly System.Windows.Forms.Timer HoldTimer = new System.Windows.Forms.Timer();
        public readonly System.Windows.Forms.Timer SingleTimer = new System.Windows.Forms.Timer();
        public readonly Slot[] Slots = new Slot[3];
        public Label Led, SubLabel, TitleLabel;
        public FlatButton LearnButton;
    }

    // =====================================================================
    //  Hoofdvenster
    // =====================================================================
    class MainForm : Form
    {
        static readonly string[] GestureKeys = { "enkel", "dubbel", "houden" };
        static readonly int[] DblValues = { 250, 350, 500 };
        static readonly int[] HoldValues = { 400, 600, 900 };

        const int W = 920, H = 640, SB = 220;
        const int PX = 28;                 // marge links in pagina
        const int CW = W - SB - 2 * PX;    // breedte van kaarten

        readonly PenInput pen = new PenInput();
        readonly PenButton[] buttons = new PenButton[2];
        readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        readonly Dictionary<string, NavItem> navs = new Dictionary<string, NavItem>();
        readonly OsdForm osd = new OsdForm();

        NotifyIcon tray;
        ContextMenuStrip trayMenu, quickMenu;
        ToolStripMenuItem trayToggle;
        Label penDot, penLabel, mapDot, mapLabel, liveLabel;
        ToggleSwitch tglMapping, tglAutostart, tglOsd;
        DarkCombo cmbDbl, cmbHold;
        bool mappingOn, osdOn, penSeen, exiting, toldTray, loading;
        PenButton learning;
        PenProfile profile;
        System.Windows.Forms.Timer learnTimeout;
        string lastLive = "";

        public MainForm(string startPage, EventWaitHandle showEvent, EventWaitHandle exitEvent)
        {
            mappingOn = Settings.GetBool("mapping", true);
            osdOn = Settings.GetBool("melding", true);

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = Program.AppName;
            ClientSize = new Size(W, H);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Regular(10);
            DoubleBuffered = true;
            var icon = LoadAppIcon(32);
            if (icon != null) Icon = icon;

            profile = PenProfile.ById(Settings.Get("pen", "hp-mpp2")) ?? PenProfile.All[0];
            buttons[0] = new PenButton { Key = "onder" };
            buttons[1] = new PenButton { Key = "boven" };
            ApplyProfileData();
            foreach (var b in buttons)
            {
                b.Signal = (PenSignal)Settings.GetInt(b.Key + ".signaal", (int)b.DefaultSignal);
                PenButton bb = b;
                b.HoldTimer.Tick += (s, e) => OnHold(bb);
                b.SingleTimer.Tick += (s, e) => OnSingle(bb);
            }

            BuildSidebar();
            BuildButtonsPage();
            BuildSettingsPage();
            BuildHelpPage();
            BuildMenus();
            LoadSlots();

            ResumeLayout(false);
            PerformAutoScale();

            // teksten pas na het schalen zetten (anders verschuiven sommige labels)
            SetMapping(mappingOn, false);
            foreach (var b in buttons) SetLed(b);

            ShowPage(startPage != null && pages.ContainsKey(startPage) ? startPage : "knoppen");

            pen.Report += OnPenReport;
            learnTimeout = new System.Windows.Forms.Timer { Interval = 10000 };
            learnTimeout.Tick += (s, e) => StopLearning();

            StartWaiter(showEvent, ShowMain);
            StartWaiter(exitEvent, ExitApp);
        }

        void StartWaiter(EventWaitHandle ev, Action action)
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    ev.WaitOne();
                    try { BeginInvoke(action); } catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ----------------------------------------------------- venster
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            if (!pen.Register(Handle) && penLabel != null)
            {
                penLabel.Text = L.T("Pen kon niet worden aangemeld", "Could not connect to the pen");
                penDot.ForeColor = Theme.Bad;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x00FF) pen.Handle(m.LParam);   // WM_INPUT
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!toldTray)
                {
                    toldTray = true;
                    tray.ShowBalloonTip(3000, Program.AppName,
                        L.T("Pen Mapper blijft actief. Dubbelklik het pen-icoon rechtsonder om het venster te openen.",
                            "Pen Mapper keeps running. Double-click the pen icon in the system tray to open it."), ToolTipIcon.None);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            exiting = true;
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Application.Exit();
        }

        public static Icon LoadAppIcon(int size)
        {
            try
            {
                var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PenMapper.app.ico");
                if (s == null) return null;
                return new Icon(s, size, size);
            }
            catch { return null; }
        }

        // ----------------------------------------------------- hulpjes
        static Label AddLabel(Control parent, string text, Font font, Color fg, int x, int y, int w, int h,
            ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            var rp = parent as RoundPanel;
            var l = new Label
            {
                Text = text, Font = font, ForeColor = fg,
                BackColor = rp != null ? rp.Fill : parent.BackColor,
                Location = new Point(x, y), Size = new Size(w, h),
                TextAlign = align, AutoSize = false, UseMnemonic = false
            };
            parent.Controls.Add(l);
            return l;
        }

        static RoundPanel AddCard(Control parent, int x, int y, int w, int h)
        {
            var c = new RoundPanel { Fill = Theme.Card, BackColor = parent.BackColor, Location = new Point(x, y), Size = new Size(w, h) };
            parent.Controls.Add(c);
            return c;
        }

        Panel NewPage(string name, string title, string sub, int titleWidth = CW)
        {
            var p = new Panel { Location = new Point(SB, 0), Size = new Size(W - SB, H), BackColor = Theme.Bg, Visible = false };
            Controls.Add(p);
            pages[name] = p;
            AddLabel(p, title, Theme.Bold(19), Theme.Text, PX, 22, titleWidth, 42);
            AddLabel(p, sub, Theme.Regular(10), Theme.Muted, PX, 64, CW, 22);
            return p;
        }

        FlatButton AddButton(Control parent, string text, int x, int y, int w, int h, bool accent, EventHandler click)
        {
            var b = new FlatButton
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                Fill = accent ? Theme.Accent : Theme.Field, HoverFill = accent ? Theme.AccentHover : Theme.FieldHover,
                ForeColor = accent ? Color.White : Theme.Text
            };
            b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        // ----------------------------------------------------- zijbalk
        void BuildSidebar()
        {
            var side = new SidebarPanel { Location = new Point(0, 0), Size = new Size(SB, H), BackColor = Theme.Side };
            Controls.Add(side);

            AddNav(side, "knoppen", "", L.T("Knoppen", "Buttons"), 104);
            AddNav(side, "instellingen", "", L.T("Instellingen", "Settings"), 150);
            AddNav(side, "hulp", "", L.T("Hulp en test", "Help and test"), 196);

            penDot = AddLabel(side, "●", Theme.Regular(9), Theme.Off, 22, H - 74, 18, 22);
            penLabel = AddLabel(side, L.T("Pen nog niet gezien", "Pen not seen yet"), Theme.Regular(9), Theme.Muted, 42, H - 74, 170, 22);
            mapDot = AddLabel(side, "●", Theme.Regular(9), Theme.Good, 22, H - 48, 18, 22);
            mapLabel = AddLabel(side, "-", Theme.Regular(9), Theme.Muted, 42, H - 48, 170, 22);
        }

        void AddNav(Control parent, string page, string glyph, string text, int y)
        {
            var n = new NavItem { Glyph = glyph, Text = text, Location = new Point(12, y), Size = new Size(SB - 24, 40) };
            n.Click += (s, e) => ShowPage(page);
            parent.Controls.Add(n);
            navs[page] = n;
        }

        void ShowPage(string name)
        {
            foreach (var kv in pages) kv.Value.Visible = kv.Key == name;
            foreach (var kv in navs) kv.Value.Selected = kv.Key == name;
        }

        // ------------------------------------------------- pagina: knoppen
        void BuildButtonsPage()
        {
            var p = NewPage("knoppen", L.T("Knoppen", "Buttons"),
                L.T("Kies per knop wat één klik, een dubbelklik en vasthouden doen.",
                    "Choose what a single click, a double click and holding do for each button."), CW - 360);

            // pen-keuzemenu rechtsboven
            AddLabel(p, "Pen", Theme.Semi(10), Theme.Muted, PX + CW - 300 - 46, 26, 40, 30, ContentAlignment.MiddleRight);
            var penCombo = new DarkCombo { Location = new Point(PX + CW - 300, 26), Width = 300 };
            foreach (var pp in PenProfile.All) penCombo.Items.Add(pp);
            penCombo.SelectedItem = profile;
            penCombo.SelectedIndexChanged += (s, e) =>
            {
                var np = penCombo.SelectedItem as PenProfile;
                if (np != null && np != profile) ApplyProfile(np);
            };
            p.Controls.Add(penCombo);

            BuildButtonCard(p, buttons[0], 100);
            BuildButtonCard(p, buttons[1], 330);
            AddLabel(p, L.T("Werkt een knop niet, of zijn ze omgedraaid? Klik op 'Leren' en druk de knop in.",
                            "A button not working, or are they swapped? Click 'Learn' and press the button."),
                Theme.Regular(9), Theme.Muted, PX, 560, CW, 22);
        }

        void BuildButtonCard(Panel page, PenButton b, int y)
        {
            string[] gestureNames = { L.T("Eén klik", "Single click"), L.T("Dubbelklik", "Double click"), L.T("Vasthouden", "Hold") };
            var card = AddCard(page, PX, y, CW, 216);
            b.TitleLabel = AddLabel(card, b.Title, Theme.Semi(12), Theme.Text, 22, 14, 220, 28);
            b.SubLabel = AddLabel(card, "-", Theme.Regular(9), Theme.Muted, 22, 42, 400, 20);
            b.Led = AddLabel(card, "-", Theme.Semi(10), Theme.Muted, CW - 22 - 92 - 150, 18, 140, 26, ContentAlignment.MiddleRight);

            b.LearnButton = AddButton(card, L.T("Leren", "Learn"), CW - 22 - 92, 16, 92, 32, false, (s, e) => StartLearning(b));
            UpdateSubLabel(b);

            for (int i = 0; i < 3; i++)
            {
                int ry = 76 + i * 44;
                AddLabel(card, gestureNames[i], Theme.Regular(10), Theme.Muted, 22, ry, 104, 32);

                var slot = new Slot { Gesture = GestureKeys[i], Owner = b };
                slot.Combo = new DarkCombo { Location = new Point(130, ry + 1), Width = 300 };
                foreach (var a in Actions.All) slot.Combo.Items.Add(a);
                card.Controls.Add(slot.Combo);

                slot.Field = new FieldBox { Location = new Point(442, ry), Size = new Size(CW - 442 - 22 - 40, 32), Visible = false };
                card.Controls.Add(slot.Field);

                Slot sl = slot;
                slot.Browse = AddButton(card, "...", CW - 22 - 34, ry, 34, 32, false, (s, e) => BrowseProgram(sl));
                slot.Browse.Visible = false;

                slot.Combo.SelectedIndexChanged += (s, e) =>
                {
                    if (!loading) { sl.Field.Box.Combo = ""; sl.Field.Box.Text = ""; }
                    UpdateParamUI(sl);
                    SaveSlot(sl);
                };
                slot.Field.Box.TextChanged += (s, e) => SaveSlot(sl);
                b.Slots[i] = slot;
            }
        }

        // knopnamen en standaardsignalen uit het gekozen pen-profiel overnemen
        void ApplyProfileData()
        {
            buttons[0].Title = profile.LowerTitle; buttons[0].Hint = profile.LowerHint; buttons[0].DefaultSignal = profile.LowerSig;
            buttons[1].Title = profile.UpperTitle; buttons[1].Hint = profile.UpperHint; buttons[1].DefaultSignal = profile.UpperSig;
        }

        void ApplyProfile(PenProfile np)
        {
            StopLearning();
            profile = np;
            Settings.Set("pen", np.Id);
            ApplyProfileData();
            foreach (var b in buttons)
            {
                b.Signal = b.DefaultSignal;
                Settings.Set(b.Key + ".signaal", ((int)b.Signal).ToString());
                b.TitleLabel.Text = b.Title;
                UpdateSubLabel(b);
            }
            osd.ShowMessage(np.Name);
        }

        void UpdateSubLabel(PenButton b)
        {
            if (learning == b)
            {
                b.SubLabel.Text = L.T("Druk nu deze knop in terwijl de pen boven het scherm zweeft...",
                                      "Now press this button while the pen hovers over the screen...");
                b.SubLabel.ForeColor = Theme.Accent;
                return;
            }
            string hint = b.Hint == "" ? "" : b.Hint + "   ·   ";
            b.SubLabel.Text = hint + PenInput.SignalName(b.Signal);
            b.SubLabel.ForeColor = Theme.Muted;
        }

        void UpdateParamUI(Slot s)
        {
            var a = s.Action;
            bool needs = a != null && a.NeedsParam;
            s.Field.Visible = needs;
            s.Browse.Visible = a != null && a.Kind == ActionKind.Run;
            s.Field.Width = s.Browse.Visible ? CW - 442 - 22 - 40 : CW - 442 - 22;
            if (!needs) return;
            s.Field.Box.ShortcutMode = a.Kind == ActionKind.Shortcut;
            s.Field.Box.Cue = a.Kind == ActionKind.Shortcut ? L.T("klik en druk een sneltoets", "click and press a shortcut")
                : a.Kind == ActionKind.Text ? L.T("tekst om te typen", "text to type")
                : L.T("programma of https://...", "program or https://...");
        }

        void LoadSlots()
        {
            loading = true;
            foreach (var b in buttons)
                foreach (var s in b.Slots)
                {
                    string def = (b.Key == "onder" && s.Gesture == "enkel") ? "osk"
                        : (b.Key == "onder" && s.Gesture == "houden") ? "menu"
                        : (b.Key == "boven" && s.Gesture == "enkel") ? "rclick" : "none";
                    var a = Actions.ById(Settings.Get(b.Key + "." + s.Gesture, def)) ?? Actions.All[0];
                    s.Combo.SelectedItem = a;
                    string param = Settings.Get(b.Key + "." + s.Gesture + ".waarde", "");
                    UpdateParamUI(s);
                    if (a.Kind == ActionKind.Shortcut) s.Field.Box.SetCombo(param);
                    else s.Field.Box.Text = param;
                }
            loading = false;
        }

        void SaveSlot(Slot s)
        {
            if (loading) return;
            var a = s.Action;
            string k = s.Owner.Key + "." + s.Gesture;
            Settings.Set(k, a != null ? a.Id : "none");
            Settings.Set(k + ".waarde", a != null && a.NeedsParam ? s.Param : "");
        }

        void BrowseProgram(Slot s)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = L.T("Kies een programma", "Choose a program"),
                Filter = L.T("Programma's", "Programs") + "|*.exe;*.lnk;*.bat;*.cmd|" + L.T("Alle bestanden", "All files") + "|*.*"
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) s.Field.Box.Text = dlg.FileName;
            }
        }

        // ------------------------------------------------ pagina: instellingen
        void BuildSettingsPage()
        {
            var p = NewPage("instellingen", L.T("Instellingen", "Settings"), L.T("Hoe Pen Mapper werkt en reageert.", "How Pen Mapper behaves."));
            const int rows = 6;
            var card = AddCard(p, PX, 100, CW, rows * 64 + 16);

            tglMapping = SettingToggle(card, 0, L.T("Mapping aan", "Mapping on"), L.T("Zet alle pen-acties aan of uit.", "Turn all pen actions on or off."), mappingOn);
            tglMapping.CheckedChanged += (s, e) => SetMapping(tglMapping.Checked, true);

            tglAutostart = SettingToggle(card, 1, L.T("Start met Windows", "Start with Windows"),
                L.T("Pen Mapper start vanzelf op de achtergrond.", "Pen Mapper starts automatically in the background."), Autostart.IsOn);
            tglAutostart.CheckedChanged += (s, e) => Autostart.Set(tglAutostart.Checked);

            tglOsd = SettingToggle(card, 2, L.T("Melding bij actie", "Show action label"),
                L.T("Toon een klein label bij je pen als een actie wordt uitgevoerd.", "Show a small label near your pen when an action runs."), osdOn);
            tglOsd.CheckedChanged += (s, e) => { osdOn = tglOsd.Checked; Settings.SetBool("melding", osdOn); };

            cmbDbl = SettingCombo(card, 3, L.T("Dubbelklik-snelheid", "Double-click speed"), L.T("Hoe snel je twee keer moet drukken.", "How fast you need to press twice."),
                new[] { L.T("Snel (250 ms)", "Fast (250 ms)"), L.T("Normaal (350 ms)", "Normal (350 ms)"), L.T("Langzaam (500 ms)", "Slow (500 ms)") }, "dubbelklik", 1);
            cmbHold = SettingCombo(card, 4, L.T("Vasthouden na", "Hold after"), L.T("Hoe lang je de knop ingedrukt houdt.", "How long you keep the button pressed."),
                new[] { L.T("Kort (400 ms)", "Short (400 ms)"), L.T("Normaal (600 ms)", "Normal (600 ms)"), L.T("Lang (900 ms)", "Long (900 ms)") }, "vasthouden", 1);

            string[] langIds = { "auto", "nl", "en" };
            var cmbLang = SettingCombo(card, 5, L.T("Taal", "Language"), L.T("Pen Mapper start opnieuw als je dit wijzigt.", "Pen Mapper restarts when you change this."),
                new[] { L.T("Automatisch (Windows)", "Automatic (Windows)"), "Nederlands", "English" }, null, 0);
            int li = Array.IndexOf(langIds, Settings.Get("taal", "auto"));
            cmbLang.SelectedIndex = li < 0 ? 0 : li;
            cmbLang.SelectedIndexChanged += (s, e) =>
            {
                Settings.Set("taal", langIds[cmbLang.SelectedIndex]);
                try { Process.Start(Application.ExecutablePath, "/restart /page:instellingen"); } catch { }
                ExitApp();
            };

            int by = 100 + rows * 64 + 16 + 16;
            AddButton(p, L.T("Knop-signalen herstellen", "Reset button signals"), PX, by, 230, 38, false, (s, e) =>
            {
                foreach (var b in buttons) { b.Signal = b.DefaultSignal; Settings.Set(b.Key + ".signaal", ((int)b.Signal).ToString()); UpdateSubLabel(b); }
                osd.ShowMessage(L.T("Knop-signalen hersteld", "Button signals reset"));
            });
            AddButton(p, L.T("Snelmenu openen", "Open quick menu"), PX + 242, by, 180, 38, false, (s, e) => quickMenu.Show(Cursor.Position));
        }

        void SettingTexts(RoundPanel card, int i, string title, string desc)
        {
            int y = 8 + i * 64;
            if (i > 0)
                card.Controls.Add(new Panel { BackColor = Theme.Line, Location = new Point(22, y), Size = new Size(CW - 44, 1) });
            AddLabel(card, title, Theme.Semi(10), Theme.Text, 22, y + 10, 400, 22);
            AddLabel(card, desc, Theme.Regular(9), Theme.Muted, 22, y + 32, 420, 20);
        }

        ToggleSwitch SettingToggle(RoundPanel card, int i, string title, string desc, bool state)
        {
            SettingTexts(card, i, title, desc);
            var t = new ToggleSwitch { Checked = state, Location = new Point(CW - 22 - 48, 8 + i * 64 + 19), Size = new Size(48, 26), BackColor = Theme.Card };
            card.Controls.Add(t);
            return t;
        }

        DarkCombo SettingCombo(RoundPanel card, int i, string title, string desc, string[] items, string key, int def)
        {
            SettingTexts(card, i, title, desc);
            var c = new DarkCombo { Location = new Point(CW - 22 - 210, 8 + i * 64 + 17), Width = 210 };
            c.Items.AddRange(items);
            if (key != null)
            {
                int v = Settings.GetInt(key, def);
                c.SelectedIndex = (v >= 0 && v < items.Length) ? v : def;
                c.SelectedIndexChanged += (s, e) => Settings.Set(key, c.SelectedIndex.ToString());
            }
            card.Controls.Add(c);
            return c;
        }

        // ---------------------------------------------------- pagina: hulp
        void BuildHelpPage()
        {
            var p = NewPage("hulp", L.T("Hulp en test", "Help and test"),
                L.T("Controleer of je pen gezien wordt en lees hoe alles werkt.", "Check that your pen is detected and learn how everything works."));

            var live = AddCard(p, PX, 100, CW, 120);
            AddLabel(live, L.T("Live pen-signaal", "Live pen signal"), Theme.Semi(11), Theme.Text, 22, 14, 300, 26);
            liveLabel = AddLabel(live, L.T("Beweeg de pen vlak boven het scherm...", "Move the pen just above the screen..."), Theme.Semi(11), Theme.Accent, 22, 46, CW - 44, 28);
            AddLabel(live, L.T("Druk een knop in: je ziet hier welk signaal de pen doorgeeft.", "Press a button: you will see which signal the pen sends."),
                Theme.Regular(9), Theme.Muted, 22, 80, CW - 44, 22);

            var how = AddCard(p, PX, 236, CW, 300);
            AddLabel(how, L.T("Zo gebruik je het", "How to use it"), Theme.Semi(11), Theme.Text, 22, 14, 300, 26);
            AddLabel(how, L.T(
                "•  Houd de pen vlak boven het scherm (niet aanraken) en druk een zijknop in.\n\n" +
                "•  Per knop stel je drie dingen in: één klik, twee keer snel drukken, of ingedrukt houden.\n\n" +
                "•  Heeft een knop een dubbelklik-actie, dan wacht 'één klik' heel even of er nog een klik komt.\n\n" +
                "•  Kies je pen rechtsboven op 'Knoppen'. Staat hij er niet bij of klopt een knop niet? Klik op 'Leren' en druk die knop in.\n\n" +
                "•  'Snelmenu openen' toont bij je pen een menu met alle acties.\n\n" +
                "•  Sluit je dit venster, dan blijft Pen Mapper actief rechtsonder in het systeemvak.",
                "•  Hold the pen just above the screen (don't touch it) and press a side button.\n\n" +
                "•  Each button has three settings: a single click, pressing twice quickly, or holding it.\n\n" +
                "•  If a button has a double-click action, a single click waits a moment for a second click.\n\n" +
                "•  Pick your pen at the top right of 'Buttons'. Not listed, or a button is wrong? Click 'Learn' and press that button.\n\n" +
                "•  'Open quick menu' shows a menu with all actions right at your pen.\n\n" +
                "•  Closing this window keeps Pen Mapper running in the system tray."),
                Theme.Regular(10), Theme.Muted, 22, 48, CW - 44, 240, ContentAlignment.TopLeft);

            AddLabel(p, Program.AppName + " " + Program.Version + "   ·   " +
                L.T("werkt met Windows Ink-pennen (MPP, USI, Wacom AES)", "works with Windows Ink pens (MPP, USI, Wacom AES)"),
                Theme.Regular(9), Theme.Muted, PX, 560, CW, 22);
        }

        // ------------------------------------------------------- menu's
        void BuildMenus()
        {
            trayMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Theme.Regular(10), ShowImageMargin = false, ShowCheckMargin = true };
            trayMenu.Items.Add(L.T("Pen Mapper openen", "Open Pen Mapper"), null, (s, e) => ShowMain());
            trayMenu.Items.Add(L.T("Snelmenu", "Quick menu"), null, (s, e) => quickMenu.Show(Cursor.Position));
            trayToggle = new ToolStripMenuItem(L.T("Mapping aan", "Mapping on"), null, (s, e) => SetMapping(!mappingOn, true));
            trayMenu.Items.Add(trayToggle);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(L.T("Afsluiten", "Exit"), null, (s, e) => ExitApp());
            foreach (ToolStripItem it in trayMenu.Items) it.ForeColor = Theme.Text;

            tray = new NotifyIcon
            {
                Icon = LoadAppIcon(16) ?? SystemIcons.Application,
                Text = Program.AppName,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            tray.DoubleClick += (s, e) => ShowMain();

            quickMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Theme.Regular(10), ShowImageMargin = false };
            var cats = new Dictionary<string, ToolStripMenuItem>();
            foreach (var a in Actions.All)
            {
                if (a.Kind == ActionKind.None || a.NeedsParam || a.Id == "menu") continue;
                ToolStripMenuItem cat;
                if (!cats.TryGetValue(a.Category, out cat))
                {
                    cat = new ToolStripMenuItem(a.Category) { ForeColor = Theme.Text };
                    ((ToolStripDropDownMenu)cat.DropDown).ShowImageMargin = false;
                    cat.DropDown.Renderer = new DarkRenderer();
                    cats[a.Category] = cat;
                    quickMenu.Items.Add(cat);
                }
                ActionDef act = a;
                cat.DropDownItems.Add(new ToolStripMenuItem(a.Name, null, (s, e) => RunAction(act, "")) { ForeColor = Theme.Text });
            }
        }

        void SetMapping(bool on, bool save)
        {
            mappingOn = on;
            if (tglMapping != null && tglMapping.Checked != on) tglMapping.Checked = on;
            mapDot.ForeColor = on ? Theme.Good : Theme.Off;
            mapLabel.Text = on ? L.T("Mapping actief", "Mapping active") : L.T("Gepauzeerd", "Paused");
            if (trayToggle != null) trayToggle.Checked = on;
            if (save) Settings.SetBool("mapping", on);
        }

        // ---------------------------------------------------- leren
        void StartLearning(PenButton b)
        {
            if (learning == b) { StopLearning(); return; }
            var prev = learning;
            learning = b;
            if (prev != null) { prev.LearnButton.Text = L.T("Leren", "Learn"); UpdateSubLabel(prev); }
            b.LearnButton.Text = L.T("Stop", "Stop");
            UpdateSubLabel(b);
            learnTimeout.Stop();
            learnTimeout.Start();
        }

        void StopLearning()
        {
            learnTimeout.Stop();
            var b = learning;
            learning = null;
            if (b != null) { b.LearnButton.Text = L.T("Leren", "Learn"); UpdateSubLabel(b); }
        }

        // ------------------------------------------------ pen-signalen
        void OnPenReport(HashSet<int> set)
        {
            if (!penSeen)
            {
                penSeen = true;
                penDot.ForeColor = Theme.Good;
                penLabel.Text = L.T("Pen verbonden", "Pen connected");
            }
            UpdateLive(set);

            if (learning != null)
            {
                var sig = PenInput.First(set);
                if (sig == PenSignal.None) return;
                var lb = learning;
                foreach (var other in buttons)
                    if (other != lb && other.Signal == sig)
                    {
                        other.Signal = lb.Signal;
                        Settings.Set(other.Key + ".signaal", ((int)other.Signal).ToString());
                        UpdateSubLabel(other);
                    }
                lb.Signal = sig;
                Settings.Set(lb.Key + ".signaal", ((int)sig).ToString());
                StopLearning();
                foreach (var x in buttons) { x.Down = PenInput.Has(set, x.Signal); SetLed(x); }
                osd.ShowMessage(lb.Title + L.T(" herkend", " recognized"));
                return;
            }

            foreach (var b in buttons)
            {
                bool pressed = PenInput.Has(set, b.Signal);
                if (pressed == b.Down) continue;
                b.Down = pressed;
                SetLed(b);
                if (pressed) OnPress(b); else OnRelease(b);
            }
        }

        void SetLed(PenButton b)
        {
            b.Led.Text = b.Down ? "●  " + L.T("ingedrukt", "pressed") : "○  " + L.T("los", "released");
            b.Led.ForeColor = b.Down ? Theme.Accent : Theme.Muted;
        }

        void UpdateLive(HashSet<int> set)
        {
            var parts = new List<string>();
            if (set.Contains(0x32)) parts.Add(L.T("in bereik", "in range"));
            if (set.Contains(0x42)) parts.Add(L.T("punt raakt scherm", "tip touches screen"));
            if (set.Contains(0x44)) parts.Add(PenInput.SignalName(PenSignal.Barrel));
            if (set.Contains(0x5A)) parts.Add(PenInput.SignalName(PenSignal.Barrel2));
            if (set.Contains(0x45) || set.Contains(0x3C)) parts.Add(PenInput.SignalName(PenSignal.Eraser));
            string s = parts.Count == 0 ? L.T("pen buiten bereik", "pen out of range") : string.Join("   •   ", parts.ToArray());
            if (s != lastLive) { lastLive = s; liveLabel.Text = s; }
        }

        // ----------------------------------------------------- gebaren
        bool HasAction(PenButton b, int g)
        {
            var a = b.Slots[g].Action;
            return a != null && a.Kind != ActionKind.None;
        }

        void OnPress(PenButton b)
        {
            b.Held = false;
            if (HasAction(b, 2))
            {
                b.HoldTimer.Interval = HoldValues[Math.Max(0, cmbHold.SelectedIndex)];
                b.HoldTimer.Start();
            }
        }

        void OnRelease(PenButton b)
        {
            b.HoldTimer.Stop();
            if (b.Held) return;
            if (HasAction(b, 1))
            {
                if (b.Pending)
                {
                    b.Pending = false;
                    b.SingleTimer.Stop();
                    Fire(b, 1);
                }
                else
                {
                    b.Pending = true;
                    b.SingleTimer.Interval = DblValues[Math.Max(0, cmbDbl.SelectedIndex)];
                    b.SingleTimer.Start();
                }
            }
            else Fire(b, 0);
        }

        void OnHold(PenButton b)
        {
            b.HoldTimer.Stop();
            b.Held = true;
            b.Pending = false;
            b.SingleTimer.Stop();
            Fire(b, 2);
        }

        void OnSingle(PenButton b)
        {
            b.SingleTimer.Stop();
            b.Pending = false;
            Fire(b, 0);
        }

        void Fire(PenButton b, int g)
        {
            var slot = b.Slots[g];
            var a = slot.Action;
            if (a == null || a.Kind == ActionKind.None) return;
            if (!mappingOn && a.Id != "pause") return;
            string param = slot.Param;
            BeginInvoke(new Action(() => RunAction(a, param)));
        }

        // ------------------------------------------------- acties uitvoeren
        void RunAction(ActionDef a, string param)
        {
            try
            {
                switch (a.Kind)
                {
                    case ActionKind.Keys:
                        Sender.Combo(a.Keys);
                        break;
                    case ActionKind.Shortcut:
                        if (param == "") return;
                        Sender.Combo(param);
                        break;
                    case ActionKind.Text:
                        if (param == "") return;
                        Sender.Text(param);
                        break;
                    case ActionKind.Run:
                        if (param == "") return;
                        Process.Start(new ProcessStartInfo(param) { UseShellExecute = true });
                        break;
                    case ActionKind.Special:
                        switch (a.Id)
                        {
                            case "rclick": Sender.MouseClick(0x0008, 0x0010, 1); break;
                            case "mclick": Sender.MouseClick(0x0020, 0x0040, 1); break;
                            case "dblclick": Sender.MouseClick(0x0002, 0x0004, 2); break;
                            case "wheelup": Sender.Wheel(360); break;
                            case "wheeldown": Sender.Wheel(-360); break;
                            case "touchkb": TouchKeyboard.Toggle(); break;
                            case "lock": Native.LockWorkStation(); break;
                            case "menu": quickMenu.Show(Cursor.Position); return;
                            case "pause":
                                SetMapping(!mappingOn, true);
                                if (osdOn) osd.ShowMessage(mappingOn ? L.T("Mapping hervat", "Mapping resumed") : L.T("Mapping gepauzeerd", "Mapping paused"));
                                return;
                        }
                        break;
                }
                if (osdOn) osd.ShowMessage(a.Name);
            }
            catch (Exception ex)
            {
                if (osdOn) osd.ShowMessage(L.T("Mislukt: ", "Failed: ") + ex.Message);
            }
        }
    }

    // ------------------------------------------------------ autostart
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "PenMapper";

        public static bool IsOn
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(Name) != null;
            }
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\" /tray");
                else if (k.GetValue(Name) != null) k.DeleteValue(Name);
            }
        }
    }

    // ------------------------------------------------------ verwijderen
    static class Uninstaller
    {
        public static void Run(bool quiet)
        {
            if (!quiet && MessageBox.Show(L.T("Wil je Pen Mapper van deze computer verwijderen?", "Do you want to remove Pen Mapper from this computer?"),
                    Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            // draaiende Pen Mapper netjes laten afsluiten
            try { EventWaitHandle.OpenExisting(Program.ExitEventName).Set(); } catch { }
            int me = Process.GetCurrentProcess().Id;
            for (int i = 0; i < 30; i++)
            {
                bool other = false;
                foreach (var p in Process.GetProcessesByName("Pen Mapper")) if (p.Id != me) other = true;
                if (!other) break;
                Thread.Sleep(100);
            }
            foreach (var p in Process.GetProcessesByName("Pen Mapper"))
                if (p.Id != me) try { p.Kill(); } catch { }

            try { Autostart.Set(false); } catch { }
            foreach (var lnk in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Pen Mapper.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pen Mapper.lnk") })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKey, false); } catch { }
            try { if (Directory.Exists(Settings.Dir)) Directory.Delete(Settings.Dir, true); } catch { }

            // programmamap pas na afsluiten verwijderen (alleen onze eigen map)
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (Path.GetFileName(dir) == "Pen Mapper")
            {
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"")
                    { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                }
                catch { }
            }
            if (!quiet)
                MessageBox.Show(L.T("Pen Mapper is verwijderd.", "Pen Mapper has been removed."), Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // =====================================================================
    //  Eigen controls
    // =====================================================================
    class RoundPanel : Panel
    {
        public Color Fill = Theme.Card;
        public int Radius = 12;
        public RoundPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.FillRound(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), Radius * Theme.S, Fill);
        }
    }

    class SidebarPanel : Panel
    {
        public SidebarPanel() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var badge = new RectangleF(22 * s, 26 * s, 40 * s, 40 * s);
            using (var br = new LinearGradientBrush(badge, Color.FromArgb(0x95, 0xA3, 0xFF), Color.FromArgb(0x5B, 0x6C, 0xF0), 45f))
            using (var path = Theme.Round(badge, 10 * s))
                g.FillPath(br, path);
            using (var f = Theme.Icons(16))
                TextRenderer.DrawText(g, "", f, Rectangle.Round(badge), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (var f = Theme.Bold(13))
                TextRenderer.DrawText(g, Program.AppName, f, new Point((int)(72 * s), (int)(26 * s)), Theme.Text);
            using (var f = Theme.Regular(8.5f))
                TextRenderer.DrawText(g, L.T("knoppen naar wens", "your buttons, your way"), f, new Point((int)(73 * s), (int)(50 * s)), Theme.Muted);
        }
    }

    class NavItem : Control
    {
        public string Glyph = "";
        bool selected, hover;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public NavItem()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Side;
            Font = Theme.Semi(10);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = Theme.S;
            g.Clear(Theme.Side);
            if (selected || hover)
                Theme.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 8 * s, selected ? Theme.Card : Color.FromArgb(0x1A, 0x1A, 0x20));
            if (selected)
                Theme.FillRound(g, new RectangleF(0, Height * 0.25f, 3 * s, Height * 0.5f), 1.5f * s, Theme.Accent);
            Color fg = selected ? Theme.Text : Theme.Muted;
            using (var f = Theme.Icons(12))
                TextRenderer.DrawText(g, Glyph, f, new Rectangle((int)(10 * s), 0, (int)(30 * s), Height), selected ? Theme.Accent : fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(48 * s), 0, Width - (int)(48 * s), Height), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    class FlatButton : Control
    {
        public Color Fill = Theme.Field, HoverFill = Theme.FieldHover;
        bool hover;
        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.Semi(10);
            ForeColor = Theme.Text;
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var rp = Parent as RoundPanel;
            g.Clear(rp != null ? rp.Fill : (Parent != null ? Parent.BackColor : Theme.Bg));
            Theme.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), 8 * Theme.S, hover ? HoverFill : Fill);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    class ToggleSwitch : Control
    {
        bool isOn;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return isOn; }
            set
            {
                if (isOn == value) return;
                isOn = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }
        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            Theme.FillRound(g, r, r.Height / 2, isOn ? Theme.Accent : Theme.Off);
            float d = r.Height - 6;
            float x = isOn ? r.Right - d - 3 : r.X + 3;
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, x, r.Y + 3, d, d);
        }
    }

    class DarkCombo : ComboBox
    {
        public DarkCombo()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Font = Theme.Regular(10);
            MaxDropDownItems = 18;
            IntegralHeight = false;
            DropDownHeight = 460;
            ItemHeight = Font.Height + 10;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetWindowTheme(Handle, "DarkMode_CFD", null);
            ItemHeight = Font.Height + 10;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F || m.Msg == 0x0085)   // WM_PAINT / WM_NCPAINT: rand en pijl zelf tekenen
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    int bw = SystemInformation.VerticalScrollBarWidth + 2;
                    var arrow = new Rectangle(Width - bw - 1, 1, bw, Height - 2);
                    using (var b = new SolidBrush(Theme.Field)) g.FillRectangle(b, arrow);
                    using (var f = Theme.Icons(8))
                        TextRenderer.DrawText(g, "", f, arrow, Theme.Muted,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    using (var p = new Pen(Focused ? Theme.Accent : Theme.Line))
                        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool sel = (e.State & DrawItemState.Selected) != 0 && !edit;
            using (var b = new SolidBrush(sel ? Theme.AccentDim : Theme.Field))
                g.FillRectangle(b, e.Bounds);
            var item = Items[e.Index];
            var a = item as ActionDef;
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            if (a != null && a.Category != "")
            {
                string cat = a.Category + "  ·  ";
                var size = TextRenderer.MeasureText(g, cat, Font, r.Size, flags);
                TextRenderer.DrawText(g, cat, Font, r, Theme.Muted, flags);
                var r2 = new Rectangle(r.X + size.Width - 6, r.Y, Math.Max(0, r.Width - size.Width + 6), r.Height);
                TextRenderer.DrawText(g, a.Name, Font, r2, Theme.Text, flags);
            }
            else
            {
                TextRenderer.DrawText(g, a != null ? a.Name : item.ToString(), Font, r,
                    a != null && a.Kind == ActionKind.None ? Theme.Muted : Theme.Text, flags);
            }
        }
    }

    // Afgerond invoerveld met daarin een tekstvak of sneltoets-recorder
    class FieldBox : RoundPanel
    {
        public readonly ParamBox Box = new ParamBox();
        public FieldBox()
        {
            Fill = Theme.Field;
            Radius = 8;
            Controls.Add(Box);
        }
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            var rp = Parent as RoundPanel;
            BackColor = rp != null ? rp.Fill : (Parent != null ? Parent.BackColor : Theme.Bg);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = (int)(10 * Theme.S);
            Box.Location = new Point(pad, (Height - Box.Height) / 2);
            Box.Width = Width - 2 * pad;
        }
        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }
    }

    class ParamBox : TextBox
    {
        bool shortcutMode;
        string cue = "";
        public string Combo = "";

        public ParamBox()
        {
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Font = Theme.Regular(10);
        }

        public bool ShortcutMode
        {
            get { return shortcutMode; }
            set { shortcutMode = value; ShortcutsEnabled = !value; }
        }

        public string Cue
        {
            get { return cue; }
            set { cue = value ?? ""; ApplyCue(); }
        }

        void ApplyCue() { if (IsHandleCreated) Native.SendMessage(Handle, 0x1501, (IntPtr)1, cue); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyCue(); }

        public void SetCombo(string combo)
        {
            Combo = combo ?? "";
            Text = Sender.Pretty(Combo);
        }

        protected override bool IsInputKey(Keys keyData) { return shortcutMode || base.IsInputKey(keyData); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (shortcutMode && Focused)
            {
                Record(keyData & Keys.KeyCode, (keyData & Keys.Control) != 0, (keyData & Keys.Shift) != 0, (keyData & Keys.Alt) != 0);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (shortcutMode)
            {
                Record(e.KeyCode, e.Control, e.Shift, e.Alt);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (shortcutMode) { e.Handled = true; return; }
            base.OnKeyPress(e);
        }

        void Record(Keys key, bool ctrl, bool shift, bool alt)
        {
            bool win = (Native.GetKeyState(0x5B) & 0x8000) != 0 || (Native.GetKeyState(0x5C) & 0x8000) != 0;
            bool isMod = key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin;
            if (!ctrl && !shift && !alt && !win && (key == Keys.Back || key == Keys.Delete))
            {
                SetCombo("");
                return;
            }
            if (isMod) return;   // wacht op de echte toets
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (shift) parts.Add("Shift");
            if (alt) parts.Add("Alt");
            if (win) parts.Add("Win");
            parts.Add(key.ToString());
            SetCombo(string.Join("+", parts.ToArray()));
        }
    }

    // Kleine melding bij de pen
    class OsdForm : Form
    {
        readonly Label label;
        readonly System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer { Interval = 1200 };

        public OsdForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Field;
            TopMost = true;
            label = new Label
            {
                AutoSize = true, Font = Theme.Semi(10), ForeColor = Theme.Text, BackColor = Theme.Field,
                Padding = new Padding(16, 9, 16, 9), Location = new Point(0, 0), UseMnemonic = false
            };
            Controls.Add(label);
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x20 | 0x80 | 0x8;   // NOACTIVATE | TRANSPARENT | TOOLWINDOW | TOPMOST
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2;
            Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);
        }

        public void ShowMessage(string text)
        {
            label.Text = text;
            ClientSize = label.PreferredSize;
            var p = Cursor.Position;
            var area = Screen.FromPoint(p).WorkingArea;
            Location = new Point(Math.Min(p.X + 28, area.Right - Width - 8), Math.Min(p.Y + 28, area.Bottom - Height - 8));
            if (!Visible) Show();
            Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            hideTimer.Stop();
            hideTimer.Start();
        }
    }

    // Donkere menu's
    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color MenuItemBorder { get { return Theme.AccentDim; } }
        public override Color MenuItemSelected { get { return Theme.AccentDim; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.AccentDim; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.AccentDim; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.AccentDim; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.AccentDim; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.Card; } }
        public override Color CheckBackground { get { return Theme.AccentDim; } }
        public override Color CheckSelectedBackground { get { return Theme.AccentDim; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Theme.Text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Muted;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, e.ImageRectangle, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // =====================================================================
    //  Windows-API
    // =====================================================================
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        public static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")]
        public static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

        [DllImport("hid.dll")]
        public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
            [Out] ushort[] usageList, ref uint usageLength, IntPtr preparsedData, IntPtr report, uint reportLength);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int vk);

        [DllImport("user32.dll")]
        public static extern bool LockWorkStation();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string title);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
    }
}
