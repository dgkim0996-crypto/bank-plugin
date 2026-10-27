using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BankMode
{
    /// <summary>Edits the site's list and previews the current classifier against live executable paths.</summary>
    public sealed class SettingsAssistantDialog : Form
    {
        readonly Settings settings;
        readonly List<Site> sites;
        readonly ListBox siteList = new ListBox();
        readonly TextBox nameBox = new TextBox();
        readonly TextBox urlBox = new TextBox();
        readonly ListView preview = new ListView();
        readonly Label previewSummary = new Label();
        readonly Button editButton = new Button();
        readonly Button removeButton = new Button();

        SettingsAssistantDialog(Settings source)
        {
            if (source == null) throw new ArgumentNullException("source");
            settings = source;
            sites = (source.Sites ?? new List<Site>()).Where(s => s != null)
                .Select(s => new Site { Name = s.Name ?? "", Url = s.Url ?? "" }).ToList();
            Text = "사이트 관리 및 분류 미리보기";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(900, 620);
            ShowInTaskbar = false;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var sitePage = new TabPage("등록 사이트");
            var previewPage = new TabPage("분류 미리보기");
            tabs.TabPages.Add(sitePage);
            tabs.TabPages.Add(previewPage);
            Controls.Add(tabs);

            BuildSitePage(sitePage);
            BuildPreviewPage(previewPage);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var ok = new Button { Text = "적용", Width = 88, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "취소", Width = 88, DialogResult = DialogResult.Cancel };
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);
            Controls.Add(footer);
            AcceptButton = ok;
            CancelButton = cancel;
            ok.Click += (s, e) => CommitSites();
            RefreshSites();
        }

        public static bool Show(IWin32Window owner, Settings settings)
        {
            using (var dialog = new SettingsAssistantDialog(settings))
                return dialog.ShowDialog(owner) == DialogResult.OK;
        }

        void BuildSitePage(TabPage page)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
            page.Controls.Add(layout);

            siteList.Dock = DockStyle.Fill;
            siteList.IntegralHeight = false;
            siteList.SelectedIndexChanged += (s, e) => LoadSelectedSite();
            layout.Controls.Add(siteList, 0, 0);

            var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(12, 0, 0, 0) };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(editor, 1, 0);
            editor.Controls.Add(new Label { Text = "사이트 이름", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            editor.Controls.Add(nameBox, 1, 0);
            editor.Controls.Add(new Label { Text = "주소", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            editor.Controls.Add(urlBox, 1, 1);
            var note = new Label { Text = "HTTP 또는 HTTPS 주소를 입력하세요. 계정 정보, IP 주소, localhost, 기본 포트가 아닌 포트는 등록할 수 없습니다.", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, AutoSize = false };
            editor.Controls.Add(note, 1, 2);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            var add = new Button { Text = "추가", Width = 76 };
            editButton.Text = "선택 수정"; editButton.Width = 88;
            removeButton.Text = "선택 삭제"; removeButton.Width = 88;
            actions.Controls.Add(add); actions.Controls.Add(editButton); actions.Controls.Add(removeButton);
            editor.Controls.Add(actions, 1, 3);
            add.Click += (s, e) => AddSite();
            editButton.Click += (s, e) => EditSite();
            removeButton.Click += (s, e) => RemoveSite();
        }

        void BuildPreviewPage(TabPage page)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);
            var header = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var refresh = new Button { Text = "현재 항목 다시 검색", AutoSize = true };
            previewSummary.Text = "검색을 누르면 현재 서비스·시작프로그램·실행 중 프로세스의 경로를 분류 규칙과 비교합니다.";
            previewSummary.AutoSize = true;
            previewSummary.Margin = new Padding(12, 9, 0, 0);
            header.Controls.Add(refresh); header.Controls.Add(previewSummary);
            layout.Controls.Add(header, 0, 0);
            preview.Dock = DockStyle.Fill;
            preview.View = View.Details;
            preview.FullRowSelect = true;
            preview.GridLines = true;
            preview.Columns.Add("분류", 210);
            preview.Columns.Add("종류", 120);
            preview.Columns.Add("항목", 190);
            preview.Columns.Add("실행 파일 경로", 560);
            layout.Controls.Add(preview, 0, 1);
            refresh.Click += (s, e) => RefreshPreview();
        }

        void RefreshSites()
        {
            int selected = siteList.SelectedIndex;
            siteList.BeginUpdate();
            siteList.Items.Clear();
            foreach (var site in sites) siteList.Items.Add(site.Name + "  —  " + site.Url);
            siteList.EndUpdate();
            if (sites.Count > 0) siteList.SelectedIndex = Math.Min(Math.Max(selected, 0), sites.Count - 1);
            editButton.Enabled = removeButton.Enabled = sites.Count > 0 && siteList.SelectedIndex >= 0;
        }

        void LoadSelectedSite()
        {
            int index = siteList.SelectedIndex;
            bool selected = index >= 0 && index < sites.Count;
            editButton.Enabled = removeButton.Enabled = selected;
            if (selected) { nameBox.Text = sites[index].Name; urlBox.Text = sites[index].Url; }
        }

        bool ReadEditor(out string name, out string url)
        {
            name = (nameBox.Text ?? "").Trim();
            url = (urlBox.Text ?? "").Trim();
            if (name.Length == 0) return Error("사이트 이름을 입력해 주세요.");
            if (name.Length > 80) return Error("사이트 이름은 80자 이하로 입력해 주세요.");
            if (name.Any(char.IsControl)) return Error("사이트 이름에는 줄바꿈이나 제어 문자를 넣을 수 없습니다.");
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !(uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
                return Error("주소는 http:// 또는 https://로 시작하는 유효한 웹 주소여야 합니다.");
            if (!string.IsNullOrEmpty(uri.UserInfo)) return Error("주소에 아이디나 비밀번호를 포함할 수 없습니다.");
            if (!uri.IsDefaultPort) return Error("기본 포트(HTTP 80, HTTPS 443)만 사용할 수 있습니다.");
            string host = (uri.IdnHost ?? "").TrimEnd('.');
            IPAddress ip;
            if (host.Length == 0 || IPAddress.TryParse(host, out ip) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
                return Error("IP 주소나 localhost 주소는 등록할 수 없습니다. 도메인 이름을 입력해 주세요.");
            string[] labels = host.Split('.');
            if (host.Length > 253 || labels.Length < 2 || labels.Any(label => label.Length == 0 || label.Length > 63 ||
                !Regex.IsMatch(label, "^[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?$")))
                return Error("인터넷에서 사용하는 도메인 주소를 입력해 주세요.");
            url = uri.AbsolutePath == "/" ? uri.GetLeftPart(UriPartial.Authority) : uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return true;
        }

        void AddSite()
        {
            string name, url;
            if (!ReadEditor(out name, out url)) return;
            if (sites.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Error("같은 이름의 사이트가 이미 있습니다."); return; }
            sites.Add(new Site { Name = name, Url = url });
            RefreshSites();
            siteList.SelectedIndex = sites.Count - 1;
        }

        void EditSite()
        {
            int index = siteList.SelectedIndex;
            if (index < 0 || index >= sites.Count) return;
            string name, url;
            if (!ReadEditor(out name, out url)) return;
            if (sites.Where((s, i) => i != index).Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Error("같은 이름의 사이트가 이미 있습니다."); return; }
            sites[index] = new Site { Name = name, Url = url };
            RefreshSites();
            siteList.SelectedIndex = index;
        }

        void RemoveSite()
        {
            int index = siteList.SelectedIndex;
            if (index < 0 || index >= sites.Count) return;
            sites.RemoveAt(index);
            RefreshSites();
        }

        void CommitSites()
        {
            settings.Sites = sites.Select(s => new Site { Name = s.Name, Url = s.Url }).ToList();
        }

        void RefreshPreview()
        {
            preview.Items.Clear();
            previewSummary.Text = "검색 중…";
            try
            {
                var classifier = new Classifier(settings);
                int count = 0;
                foreach (var item in Scanner.ScanServices())
                {
                    var rule = classifier.Find(item.ExePath);
                    if (rule != null) { AddPreview(rule.Name, "서비스", item.DisplayName + " (" + item.Name + ")", item.ExePath); count++; }
                }
                foreach (var item in Scanner.ScanRun())
                {
                    var rule = classifier.Find(item.ExePath);
                    if (rule != null) { AddPreview(rule.Name, "시작프로그램", item.Name, item.ExePath); count++; }
                }
                foreach (var item in Scanner.ScanProcesses())
                {
                    var rule = classifier.Find(item.FilePath);
                    if (rule != null) { AddPreview(rule.Name, "실행 중 프로세스", item.Name + " (PID " + item.Pid + ")", item.FilePath); count++; }
                }
                previewSummary.Text = count == 0 ? "일치하는 항목이 없습니다." : count + "개 항목이 분류 규칙과 일치합니다.";
            }
            catch (Exception ex)
            {
                previewSummary.Text = "검색 중 오류가 발생했습니다.";
                MessageBox.Show(this, "현재 실행 항목을 검색하지 못했습니다.\r\n" + ex.Message, "분류 미리보기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void AddPreview(string rule, string kind, string item, string path)
        {
            var row = new ListViewItem(rule ?? "");
            row.SubItems.Add(kind);
            row.SubItems.Add(item ?? "");
            row.SubItems.Add(path ?? "경로를 확인할 수 없음");
            preview.Items.Add(row);
        }

        bool Error(string message)
        {
            MessageBox.Show(this, message, "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
    }
}
