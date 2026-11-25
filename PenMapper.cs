// =====================================================================
//  Pen Mapper  -  geef de knoppen van je pen eigen functies
//  Werkt met Windows Ink-pennen (MPP, USI, Wacom AES, ...).
//  Gedeelde onderdelen staan in Common.cs, het ronde menu in Radial.cs.
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
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace PenMapper
{
    static class AppInfo
    {
        public const string Name = "Pen Mapper";
        public const string Id = "PenMapper";
        public const string Version = "1.1";
        public static readonly string Glyph = "" + (char)0xEDC6;
        public static string Tagline { get { return L.T("knoppen naar wens", "your buttons, your way"); } }
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
        RadialMenu radial;
        RadialConfig radialCfg;
        PenButton radialHoldButton;
        System.Windows.Forms.Timer learnTimeout;
        string lastLive = "";

        public MainForm(string startPage, EventWaitHandle showEvent, EventWaitHandle exitEvent)
        {
            mappingOn = Settings.GetBool("mapping", true);
            osdOn = Settings.GetBool("melding", true);

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = AppInfo.Name;
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

            radialCfg = new RadialConfig();
            radialCfg.Load();
            radial = new RadialMenu();
            radial.Picked += it => RunAction(it.Action, it.Param);

            BuildSidebar();
            BuildButtonsPage();
            BuildRadialPage();
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

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "/demo-radial") >= 0)
            {
                var demo = new System.Windows.Forms.Timer { Interval = 800 };
                demo.Tick += (s, e) =>
                {
                    demo.Stop();
                    var b = Screen.PrimaryScreen.WorkingArea;
                    radial.ShowAt(new Point(b.Left + b.Width / 2, b.Top + b.Height / 2), radialCfg.Items, false);
                };
                demo.Start();
            }

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
                    tray.ShowBalloonTip(3000, AppInfo.Name,
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
            AddNav(side, "radial", "", "Radial menu", 150);
            AddNav(side, "instellingen", "", L.T("Instellingen", "Settings"), 196);
            AddNav(side, "hulp", "", L.T("Hulp en test", "Help and test"), 242);

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
                        : (b.Key == "onder" && s.Gesture == "houden") ? "radial"
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

        // ---------------------------------------------------- pagina: radial menu
        void BuildRadialPage()
        {
            var p = NewPage("radial", "Radial menu",
                L.T("Kies wat er in de 8 vakken van het ronde menu staat. Open het met de actie 'Radial menu openen'.",
                    "Choose what goes in the 8 slices of the round menu. Open it with the 'Open radial menu' action."));
            radialCfg.BuildEditor(p, PX, 100, CW);
            AddButton(p, L.T("Radial menu testen", "Test radial menu"), PX, 100 + 16 + RadialConfig.Count * 46 + 16, 200, 38, true,
                (s, e) => radial.ShowAt(Cursor.Position, radialCfg.Items, false));
        }

        // ---------------------------------------------------- pagina: instellingen
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
            int li = Array.IndexOf(langIds, Settings.Get("taal", "en"));
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
                "•  'Radial menu openen' toont een rond menu met 8 acties; 'Snelmenu openen' een lijst met alle acties.\n\n" +
                "•  Sluit je dit venster, dan blijft Pen Mapper actief rechtsonder in het systeemvak.",
                "•  Hold the pen just above the screen (don't touch it) and press a side button.\n\n" +
                "•  Each button has three settings: a single click, pressing twice quickly, or holding it.\n\n" +
                "•  If a button has a double-click action, a single click waits a moment for a second click.\n\n" +
                "•  Pick your pen at the top right of 'Buttons'. Not listed, or a button is wrong? Click 'Learn' and press that button.\n\n" +
                "•  'Open radial menu' shows a round menu with 8 actions; 'Open quick menu' a list of all actions.\n\n" +
                "•  Closing this window keeps Pen Mapper running in the system tray."),
                Theme.Regular(10), Theme.Muted, 22, 48, CW - 44, 240, ContentAlignment.TopLeft);

            AddLabel(p, AppInfo.Name + " " + AppInfo.Version + "   ·   " +
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
                Text = AppInfo.Name,
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
            if (b.Held)
            {
                if (radialHoldButton == b && radial.IsOpen) { radialHoldButton = null; radial.CommitHover(); }
                return;
            }
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
            if (a.Id == "radial") radialHoldButton = g == 2 ? b : null;
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
                            case "radial":
                                radial.ShowAt(Cursor.Position, radialCfg.Items, radialHoldButton != null);
                                return;
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
}