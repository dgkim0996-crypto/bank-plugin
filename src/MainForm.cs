using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BankMode
{
    class BufferedListView : ListView
    {
        public BufferedListView() { DoubleBuffered = true; }
    }

    public class MainForm : Form
    {
        public const string WindowTitle = "은행 모드";

        static readonly Color Accent = Color.FromArgb(37, 99, 235);
        static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
        static readonly Color Green = Color.FromArgb(22, 163, 74);
        static readonly Color Amber = Color.FromArgb(217, 119, 6);
        static readonly Color Gray = Color.FromArgb(107, 114, 128);
        static readonly Color Ink = Color.FromArgb(17, 24, 39);
        static readonly Color Soft = Color.FromArgb(241, 243, 246);
        static readonly Color SoftDark = Color.FromArgb(229, 231, 235);

        readonly bool autoOn;
        readonly string snapshotPath;
        readonly float scale;

        Settings settings;
        bool settingsBroken;
        List<BankModule> modules = new List<BankModule>();
        bool busy, populating, refreshing, turnedOn, closeAfterOff, retryOffAvailable;
        int opFailures;
        string latestReleaseUrl;

        Label lblDot, lblStatus, lblSub;
        Button btnOn, btnOff;
        LinkLabel retryLink, updateLink;
        ComboBox cboSite;
        BufferedListView list;
        TextBox txtLog;
        System.Windows.Forms.Timer timer;

        int S(int px) { return (int)Math.Round(px * scale); }

        public MainForm(bool autoOn, string snapshotPath)
        {
            this.autoOn = autoOn;
            this.snapshotPath = snapshotPath;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;

            Text = WindowTitle;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("맑은 고딕", 9.5f);
            BackColor = Color.White;
            ForeColor = Ink;
            ClientSize = new Size(S(600), S(680));
            MinimumSize = new Size(S(500), S(560));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (snapshotPath != null)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-5000, -5000);
                ShowInTaskbar = false;
            }

            BuildUi();

            timer = new System.Windows.Forms.Timer { Interval = 5000 };
            timer.Tick += (s, e) => QuickRefresh();
            Shown += async (s, e) => await OnShownAsync();
            FormClosing += OnClosing;
        }

        // ===================== 화면 구성 =====================

        void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(S(20), S(16), S(20), S(12)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // 머리말 + 상태
            var title = new Label { Text = "은행 모드", Font = new Font(Font.FontFamily, 18f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0) };
            lblDot = new Label { Text = "●", AutoSize = true, ForeColor = Gray, Font = new Font(Font.FontFamily, 11f), Margin = new Padding(0, 0, S(2), 0) };
            lblStatus = new Label { Text = "확인 중…", AutoSize = true, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Margin = new Padding(0) };
            var statusRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(6), 0, 0) };
            statusRow.Controls.Add(lblDot);
            statusRow.Controls.Add(lblStatus);
            lblSub = new Label { AutoSize = true, ForeColor = Gray, Margin = new Padding(0, S(2), 0, S(14)), MaximumSize = new Size(S(560), 0) };
            var header = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            header.Controls.AddRange(new Control[] { title, statusRow, lblSub });

            // 켜기 / 끄기
            btnOn = MakeButton("은행 모드 켜기", Accent, Color.White, AccentDark);
            btnOff = MakeButton("끄기", Soft, Ink, SoftDark);
            btnOn.Margin = new Padding(0, 0, S(6), 0);
            btnOff.Margin = new Padding(S(6), 0, 0, 0);
            btnOn.Click += async (s, e) => await DoOn();
            btnOff.Click += async (s, e) => await DoOff();
            var buttons = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, S(12)) };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            buttons.Controls.Add(btnOn, 0, 0);
            buttons.Controls.Add(btnOff, 1, 0);

            // 켠 다음 열 사이트
            var siteLabel = new Label { Text = "켠 다음 열 사이트", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, S(10), 0) };
            cboSite = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0) };
            cboSite.SelectedIndexChanged += (s, e) =>
            {
                if (populating || settings == null) return;
                settings.LastSite = cboSite.SelectedIndex <= 0 ? "" : (string)cboSite.SelectedItem;
                SaveSettings();
            };
            var siteRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, S(14)) };
            siteRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            siteRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            siteRow.Controls.Add(siteLabel, 0, 0);
            siteRow.Controls.Add(cboSite, 1, 0);

            // 모듈 목록
            var caption = new Label { Text = "이 PC의 은행 보안 프로그램 — 체크한 것만 켜고 꺼요", AutoSize = true, ForeColor = Gray, Margin = new Padding(0, 0, 0, S(6)) };
            list = new BufferedListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                MultiSelect = false,
                ShowItemToolTips = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Margin = new Padding(0)
            };
            list.Columns.Add("프로그램", S(250));
            list.Columns.Add("상태", S(80));
            list.Columns.Add("메모리", S(75), HorizontalAlignment.Right);
            list.Columns.Add("부팅 시", S(110));
            list.ItemChecked += OnItemChecked;

            // 진행 기록
            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(249, 250, 251),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(Font.FontFamily, 9f),
                Margin = new Padding(0, S(10), 0, 0)
            };

            // 아래 링크
            var links = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, S(8), 0, 0) };
            links.Controls.Add(MakeLink("새로고침", async () => await Reload()));
            links.Controls.Add(MakeLink("바탕화면 바로가기 만들기", CreateShortcut));
            links.Controls.Add(MakeLink("설정 파일 열기", OpenSettingsFile));
            links.Controls.Add(MakeLink("사이트 관리·분류 미리보기", ManageSettings));
            links.Controls.Add(MakeLink("진단 정보 저장", ExportDiagnostics));
            links.Controls.Add(MakeLink("원래대로 되돌리기", async () => await DoRestore()));
            links.Controls.Add(MakeLink("크롬 확장 연결", ConnectChrome));
            retryLink = MakeLink("남은 항목 다시 끄기", async () => await DoRetryOff());
            retryLink.Visible = false;
            links.Controls.Add(retryLink);
            updateLink = MakeLink("업데이트 확인 중…", OpenLatestRelease);
            updateLink.Visible = false;
            links.Controls.Add(updateLink);

            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 머리말
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(64)));   // 버튼
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 사이트
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 목록 설명
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // 목록
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(130)));  // 기록
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 링크
            root.Controls.Add(header, 0, 0);
            root.Controls.Add(buttons, 0, 1);
            root.Controls.Add(siteRow, 0, 2);
            root.Controls.Add(caption, 0, 3);
            root.Controls.Add(list, 0, 4);
            root.Controls.Add(txtLog, 0, 5);
            root.Controls.Add(links, 0, 6);
            Controls.Add(root);
        }

        Button MakeButton(string text, Color back, Color fore, Color hover)
        {
            var b = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                UseVisualStyleBackColor = false,
                Font = new Font(Font.FontFamily, 11.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = hover;
            b.FlatAppearance.MouseDownBackColor = hover;
            return b;
        }

        LinkLabel MakeLink(string text, Action onClick)
        {
            var l = new LinkLabel
            {
                Text = text,
                AutoSize = true,
                LinkColor = Accent,
                ActiveLinkColor = AccentDark,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = new Padding(0, 0, S(14), 0)
            };
            l.LinkClicked += (s, e) => { if (!busy) onClick(); };
            return l;
        }

        // ===================== 상태 표시 =====================

        async Task OnShownAsync()
        {
            LoadSettings();
            await FullRefresh();
            if (snapshotPath != null)
            {
                Log("준비됐어요. 은행 업무 전에 '은행 모드 켜기'를 누르세요.");
                SaveSnapshot();
                Close();
                return;
            }
            timer.Start();
            ShowRestoreNotice();
            CheckForUpdatesAsync();
            if (autoOn) await DoOn();
            else Log("준비됐어요. 은행 업무 전에 '은행 모드 켜기'를 누르세요.");
        }

        void LoadSettings()
        {
            string err;
            settings = Store.LoadSettings(Store.SettingsPath, out err);
            settingsBroken = err != null;
            if (settingsBroken) Log("  ! 설정 파일을 읽지 못해 기본값으로 동작해요 (파일은 그대로 둠): " + err);
            int invalidPatterns = Classifier.InvalidMatchCount(settings);
            if (invalidPatterns > 0) Log("  ! 위험하거나 모호한 분류 패턴 " + invalidPatterns + "개를 건너뛰어요. 설정 파일에서 짧은 이름 대신 공급업체 폴더나 실행 파일 이름을 지정하세요.");

            populating = true;
            cboSite.Items.Clear();
            cboSite.Items.Add("열지 않음");
            int sel = 0;
            foreach (var s in settings.Sites)
            {
                int i = cboSite.Items.Add(s.Name);
                if (s.Name == settings.LastSite) sel = i;
            }
            cboSite.SelectedIndex = sel;
            populating = false;
        }

        void SaveSettings()
        {
            if (settingsBroken) return;
            try { Store.SaveSettings(Store.SettingsPath, settings); }
            catch (Exception ex) { Log("  ! 설정 저장 실패: " + ex.Message); }
        }

        async Task FullRefresh()
        {
            var st = settings;
            modules = await Task.Run(() => Scanner.Discover(st));
            Render();
        }

        async void QuickRefresh()
        {
            if (busy || refreshing || WindowState == FormWindowState.Minimized) return;
            refreshing = true;
            try
            {
                var st = settings;
                var mods = modules;
                bool unknown = await Task.Run(() => Scanner.RefreshStatus(st, mods));
                if (busy) return;
                if (unknown) await FullRefresh();
                else Render();
            }
            catch { }
            finally { refreshing = false; }
        }

        IEnumerable<BankModule> Managed() { return modules.Where(m => !m.Excluded); }

        void Render()
        {
            populating = true;
            list.BeginUpdate();
            bool same = list.Items.Count == modules.Count;
            for (int i = 0; same && i < modules.Count; i++) same = list.Items[i].Text == modules[i].Name;
            if (!same)
            {
                list.Items.Clear();
                foreach (var m in modules)
                    list.Items.Add(new ListViewItem(new[] { m.Name, "", "", "" }) { UseItemStyleForSubItems = false });
            }
            for (int i = 0; i < modules.Count; i++) Fill(list.Items[i], modules[i]);
            list.EndUpdate();
            populating = false;
            RenderStatus();
        }

        void Fill(ListViewItem it, BankModule m)
        {
            if (it.Checked == m.Excluded) it.Checked = !m.Excluded;
            Color fore = m.Excluded ? Gray : Ink;
            if (it.ForeColor != fore) it.ForeColor = fore;

            string state = m.IsRunning ? "실행 중" : (m.HasBootItems ? "꺼짐" : "대기");
            Set(it.SubItems[1], state, m.Excluded ? Gray : (m.IsRunning ? Green : Gray));
            Set(it.SubItems[2], m.Processes.Count > 0 ? Mb(m.Memory) : "-", fore);
            string boot = !m.HasBootItems ? "-" : (m.AutoStarts ? "자동 실행" : "꺼둠");
            Set(it.SubItems[3], boot, m.AutoStarts && !m.Excluded ? Amber : fore);
            it.ToolTipText = Describe(m);
        }

        static void Set(ListViewItem.ListViewSubItem si, string text, Color color)
        {
            if (si.Text != text) si.Text = text;
            if (si.ForeColor != color) si.ForeColor = color;
        }

        static string Describe(BankModule m)
        {
            var sb = new StringBuilder(m.Rule.Desc ?? "");
            foreach (var s in m.Services)
                sb.Append("\n서비스: ").Append(s.DisplayName).Append(" (").Append(s.StartType == 2 ? "자동" : s.StartType == 4 ? "사용 안 함" : "수동").Append(s.Running ? ", 실행 중)" : ")");
            foreach (var r in m.RunEntries)
                sb.Append("\n시작프로그램: ").Append(r.Name).Append(r.Enabled ? " (켜짐)" : " (꺼둠)");
            foreach (var p in m.Processes)
                sb.Append("\n실행 중: ").Append(p.Name).Append(".exe ").Append(Mb(p.Memory));
            if (!m.HasBootItems && m.Processes.Count == 0)
                sb.Append("\n따로 켜고 끌 항목이 없어요 (다른 모듈이 필요할 때 불러 씀)");
            return sb.ToString();
        }

        static string Mb(long bytes) { return (bytes / 1048576.0).ToString("0") + " MB"; }

        void RenderStatus()
        {
            if (busy) return;
            var managed = Managed().ToList();
            if (managed.Count == 0)
            {
                lblDot.ForeColor = Gray;
                lblStatus.Text = "관리할 프로그램 없음";
                lblSub.Text = modules.Count == 0 ? "이 PC에서 은행 보안 프로그램을 찾지 못했어요." : "목록에서 관리할 프로그램을 체크하세요.";
                return;
            }
            var sum = Summary.Of(modules);
            string sub;
            if (sum.On)
            {
                lblDot.ForeColor = sum.Partial ? Amber : Green;
                lblStatus.Text = sum.Partial ? "일부 켜짐" : "켜짐";
                sub = sum.Running + "/" + sum.Managed + "개 모듈 실행 중 · 메모리 " + Mb(sum.Memory) + " 사용";
            }
            else
            {
                lblDot.ForeColor = Gray;
                lblStatus.Text = "꺼짐";
                sub = "은행 보안 프로그램이 모두 쉬고 있어요";
            }
            if (sum.BootAuto > 0) sub += "\n부팅할 때 저절로 켜지는 프로그램 " + sum.BootAuto + "개 — '끄기'를 누르면 막아 둬요";
            lblSub.Text = sub;
        }

        void SetBusy(bool on, string text)
        {
            busy = on;
            btnOn.Enabled = btnOff.Enabled = cboSite.Enabled = list.Enabled = !on;
            UseWaitCursor = on;
            if (on)
            {
                lblDot.ForeColor = Accent;
                lblStatus.Text = text;
            }
            else RenderStatus();
        }

        void Log(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), line); return; }
            if (line.StartsWith("  !")) opFailures++;
            txtLog.AppendText((txtLog.TextLength > 0 ? "\r\n" : "") + DateTime.Now.ToString("HH:mm:ss") + "  " + line);
        }

        Ctx NewCtx() { return new Ctx { Settings = settings, StatePath = Store.StatePath, Log = this.Log, Owner = Handle }; }

        // ===================== 켜기 / 끄기 =====================

        async Task DoOn()
        {
            if (busy) return;
            SetBusy(true, "켜는 중…");
            opFailures = 0;
            try
            {
                Log("은행 모드를 켜요");
                await FullRefresh();
                var c = NewCtx();
                var mods = modules;
                if (!await Task.Run(() => Bank.TurnOn(c, mods))) return;
                turnedOn = true;
                await Task.Delay(800);
                await FullRefresh();
                int running = Managed().Count(m => m.IsRunning);
                Log(opFailures == 0 ? "켜기 완료 — " + running + "개 실행 중" : "켜기 완료 — 일부 항목은 실패했어요 (! 표시 참고)");

                Site site = SelectedSite();
                if (site != null)
                {
                    await Task.Delay(1200);   // 로컬 보안 서버가 뜰 시간
                    OpenUrl(site);
                }
            }
            catch (Exception ex) { Log("  ! 오류: " + ex.Message); }
            finally { SetBusy(false, null); }
        }

        async Task DoOff()
        {
            if (busy) return;
            SetBusy(true, "끄는 중…");
            opFailures = 0;
            try
            {
                Log("은행 모드를 꺼요");
                await FullRefresh();
                long before = Managed().Sum(m => m.Memory);
                var c = NewCtx();
                bool done = await Task.Run(() => Bank.TurnOff(c));
                retryOffAvailable = c.Failures > 0;
                if (!done) return;
                await Task.Delay(500);
                await FullRefresh();
                turnedOn = false;

                long freed = before - Managed().Sum(m => m.Memory);
                int still = Managed().Count(m => m.IsRunning);
                string msg = "끄기 완료";
                if (freed > 0) msg += " — 메모리 약 " + Mb(freed) + " 확보";
                if (still > 0) msg += " (" + still + "개는 아직 실행 중: 자체 보호 기능 때문일 수 있어요)";
                Log(msg);
            }
            catch (Exception ex) { Log("  ! 오류: " + ex.Message); }
            finally { SetBusy(false, null); if (retryLink != null) retryLink.Visible = retryOffAvailable; }
        }

        async Task DoRetryOff()
        {
            if (busy || !retryOffAvailable) return;
            SetBusy(true, "남은 항목 다시 끄는 중…");
            opFailures = 0;
            try
            {
                Log("아직 켜져 있거나 처리가 끝나지 않은 항목만 다시 꺼요");
                await FullRefresh();
                var c = NewCtx();
                bool done = await Task.Run(() => Bank.RetryOff(c));
                retryOffAvailable = !done || c.Failures > 0;
                if (!done) return;
                await Task.Delay(500);
                await FullRefresh();
                Log(c.Failures == 0 ? "남은 항목 재시도 완료" : "일부 항목은 아직 꺼지지 않았어요 (! 표시 참고)");
            }
            catch (Exception ex) { retryOffAvailable = true; Log("  ! 재시도 오류: " + ex.Message); }
            finally { SetBusy(false, null); if (retryLink != null) retryLink.Visible = retryOffAvailable; }
        }

        async Task DoRestore()
        {
            var state = Store.LoadState(Store.StatePath);
            if (state.Services.Count == 0 && state.RunApproved.Count == 0 && state.PendingChanges.Count == 0)
            {
                MessageBox.Show(this, "되돌릴 변경 사항이 없어요.\n아직 '끄기'로 바꾼 자동 시작 설정이 없습니다.", WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetBusy(true, "되돌리는 중…");
            opFailures = 0;
            try
            {
                Log("자동 시작 설정을 원래대로 되돌려요");
                var c = NewCtx();
                if (!await Task.Run(() => Bank.Restore(c))) return;
                await FullRefresh();
                Log(opFailures == 0 ? "되돌리기 완료 — 다음 부팅부터 원래처럼 자동 실행돼요" : "되돌리기 중 일부 실패 (! 표시 참고)");
            }
            catch (Exception ex) { Log("  ! 오류: " + ex.Message); }
            finally { SetBusy(false, null); }
        }

        // ===================== 기타 =====================

        Site SelectedSite()
        {
            if (cboSite.SelectedIndex <= 0) return null;
            string name = (string)cboSite.SelectedItem;
            return settings.Sites.FirstOrDefault(s => s.Name == name);
        }

        void OpenUrl(Site site)
        {
            string url = (site.Url ?? "").Trim();
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                Log("  ! " + site.Name + ": 주소는 http:// 또는 https:// 로 시작해야 해요");
                return;
            }
            Log(site.Name + " 여는 중");
            try { using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { } }
            catch (Exception ex) { Log("  ! 사이트 열기 실패: " + ex.Message); }
        }

        void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (populating || settings == null) return;
            string name = e.Item.Text;
            settings.Excluded.Remove(name);
            if (!e.Item.Checked) settings.Excluded.Add(name);
            foreach (var m in modules) if (m.Name == name) m.Excluded = !e.Item.Checked;
            SaveSettings();
            BeginInvoke(new Action(Render));
        }

        async Task Reload()
        {
            LoadSettings();
            await FullRefresh();
            Log("새로고침했어요");
        }

        void OpenSettingsFile()
        {
            try
            {
                if (!File.Exists(Store.SettingsPath)) Store.SaveSettings(Store.SettingsPath, settings);
                using (Process.Start("notepad.exe", "\"" + Store.SettingsPath + "\"")) { }
                Log("설정 파일을 열었어요. 사이트나 모듈을 고친 뒤 저장하고 '새로고침'을 누르세요.");
            }
            catch (Exception ex) { Log("  ! 설정 파일 열기 실패: " + ex.Message); }
        }

        void ManageSettings()
        {
            if (settingsBroken && MessageBox.Show(this,
                "현재 설정 파일을 읽지 못해 기본 설정으로 열었습니다. 계속하면 잘못된 설정 파일을 새 설정으로 덮어씁니다. 진행할까요?",
                WindowTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (!SettingsAssistantDialog.Show(this, settings)) return;
            if (!settings.Sites.Any(s => string.Equals(s.Name, settings.LastSite, StringComparison.OrdinalIgnoreCase))) settings.LastSite = "";
            bool wasBroken = settingsBroken;
            settingsBroken = false;
            try { Store.SaveSettings(Store.SettingsPath, settings); }
            catch (Exception ex)
            {
                settingsBroken = wasBroken;
                Log("  ! 사이트 설정 저장 실패: " + ex.Message);
                return;
            }
            LoadSettings();
            Log("사이트 목록을 저장했어요. 분류 미리보기는 현재 설정으로 다시 검색합니다.");
        }

        void ExportDiagnostics()
        {
            DiagnosticsReport.Save(this, settings, txtLog.Text);
        }

        void ShowRestoreNotice()
        {
            StateData state = Store.LoadState(Store.StatePath);
            if (state.PendingChanges.Count > 0)
                Log("  ! 이전 작업이 중간에 끝나 확인이 필요한 변경 " + state.PendingChanges.Count + "개가 있어요. '원래대로 되돌리기'에서 복구할 수 있습니다.");
            else if (state.Services.Count > 0 || state.RunApproved.Count > 0)
                Log("  - 원래대로 되돌릴 자동 시작 설정이 남아 있어요. 작업을 마친 뒤 '원래대로 되돌리기'를 누르세요.");
        }

        async void CheckForUpdatesAsync()
        {
            var update = await UpdateChecker.CheckAsync();
            if (IsDisposed || update == null) return;
            latestReleaseUrl = update.ReleaseUrl;
            updateLink.Text = "새 버전 " + update.Latest + " 다운로드";
            updateLink.Visible = true;
            Log("새 버전 " + update.Latest + "을 사용할 수 있어요. 아래 다운로드 링크를 눌러 확인하세요.");
        }

        void OpenLatestRelease()
        {
            if (string.IsNullOrWhiteSpace(latestReleaseUrl)) return;
            try { using (Process.Start(new ProcessStartInfo(latestReleaseUrl) { UseShellExecute = true })) { } }
            catch (Exception ex) { Log("  ! 업데이트 페이지를 열지 못했어요: " + ex.Message); }
        }

        void CreateShortcut()
        {
            object shell = null, lnk = null;
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "은행 모드.lnk");
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                shell = Activator.CreateInstance(t);
                lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { Application.ExecutablePath });
                lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { "--on" });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { Application.ExecutablePath + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "은행 보안 프로그램을 한 번에 켜고 은행 사이트를 엽니다" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                Log("바탕화면에 '은행 모드' 바로가기를 만들었어요 — 더블클릭하면 바로 켜지고 사이트가 열려요");
            }
            catch (Exception ex) { Log("  ! 바로가기 만들기 실패: " + ex.Message); }
            finally
            {
                if (lnk != null) Marshal.FinalReleaseComObject(lnk);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        // 크롬 툴바 버튼(확장 프로그램)이 이 앱을 부를 수 있게 등록. 이미 연결돼 있으면 끊을지 묻는다
        void ConnectChrome()
        {
            try
            {
                if (NativeHost.IsRegistered())
                {
                    if (MessageBox.Show(this, "크롬 확장과 이미 연결돼 있어요.\n\n연결을 끊을까요? 끊으면 크롬 툴바의 은행 모드 버튼이 동작하지 않아요.",
                            WindowTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    NativeHost.Unregister();
                    Log("크롬 확장 연결을 끊었어요");
                    return;
                }

                NativeHost.Register();
                Log("크롬 확장과 연결했어요 — 크롬 툴바의 은행 모드 버튼으로 켜고 끌 수 있어요");
                if (MessageBox.Show(this, "크롬 확장과 연결했어요.\n\n확장 프로그램을 아직 설치하지 않았다면 크롬 웹스토어에서 '은행 모드'를 설치하세요.\n\n웹스토어 페이지를 열까요?",
                        WindowTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    using (Process.Start(new ProcessStartInfo(NativeHost.StoreUrl) { UseShellExecute = true })) { }
            }
            catch (Exception ex) { Log("  ! 크롬 확장 연결 실패: " + ex.Message); }
        }

        // 은행 모드를 켠 채로 창을 닫으면 끌지 물어본다
        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (snapshotPath != null || closeAfterOff || e.CloseReason != CloseReason.UserClosing) return;
            if (busy) { e.Cancel = true; return; }
            if (!turnedOn || !Managed().Any(m => m.IsRunning)) return;
            var r = MessageBox.Show(this, "은행 보안 프로그램이 아직 켜져 있어요.\n끄고 닫을까요?\n\n예 — 끄고 닫기\n아니요 — 켜 둔 채 닫기",
                WindowTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) e.Cancel = true;
            else if (r == DialogResult.Yes)
            {
                e.Cancel = true;
                OffThenClose();
            }
        }

        async void OffThenClose()
        {
            await DoOff();
            closeAfterOff = true;
            Close();
        }

        void SaveSnapshot()
        {
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(snapshotPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
