using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using System.Windows.Forms;

namespace BankMode
{
    // ===================== 설정 =====================

    public class VendorRule
    {
        public string Name { get; set; }
        public string Desc { get; set; }
        // 실행 파일 경로에 들어 있으면 이 모듈로 분류 ("AhnLab/Safe Transaction" → ...\AhnLab\Safe Transaction\...)
        public List<string> Match { get; set; }
        public VendorRule() { Match = new List<string>(); }
    }

    public class Site
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    public static class SiteCatalog
    {
        public static List<Site> Load()
        {
            string exeDir = Path.GetDirectoryName(Bank.ExePath);
            string[] paths =
            {
                Path.Combine(exeDir, "chrome-extension", "sites.json"),
                Path.GetFullPath(Path.Combine(exeDir, "..", "chrome-extension", "sites.json"))
            };
            foreach (string path in paths)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var sites = new JavaScriptSerializer().Deserialize<List<Site>>(File.ReadAllText(path, Encoding.UTF8));
                    if (sites != null) return sites.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Url)).ToList();
                }
                catch { }
            }
            return new List<Site>();
        }
    }

    public class Settings
    {
        public List<VendorRule> Vendors { get; set; }
        public List<string> Excluded { get; set; }
        public List<Site> Sites { get; set; }
        public string LastSite { get; set; }

        public Settings()
        {
            Vendors = new List<VendorRule>();
            Excluded = new List<string>();
            Sites = new List<Site>();
            LastSite = "";
        }

        static VendorRule V(string name, string desc, params string[] match)
        {
            return new VendorRule { Name = name, Desc = desc, Match = match.ToList() };
        }

        public static Settings CreateDefault()
        {
            var s = new Settings();
            // 패턴은 '실행 파일 경로에 이 문자열이 들어 있으면 이 모듈'이라는 뜻(대소문자 무시, 슬래시=폴더 구분).
            // 백신(안랩 V3, 잉카 TACHYON)·게임 보안(게임가드)·일반 프로그램과 이름이 겹치는 것은
            // 반드시 폴더 경로까지 붙여서(예: "AhnLab/Safe Transaction") 은행 모듈만 잡히게 했다.

            // ── 키보드 보안 · 안티해킹 ──
            s.Vendors.Add(V("AhnLab Safe Transaction", "키보드 해킹·악성코드 차단 (안랩 ASTx)", "AhnLab/Safe Transaction"));
            s.Vendors.Add(V("AhnLab Online Security", "구버전 안랩 금융 보안 (방화벽·키보드)", "AhnLab/ASP"));
            s.Vendors.Add(V("nProtect Online Security", "키보드·방화벽·악성코드 차단 (잉카)", "INCAInternet/nProtect Online Security"));
            s.Vendors.Add(V("nProtect KeyCrypt", "키보드 입력 암호화 (잉카, 구버전)", "System32/npkcmsvc", "SysWOW64/npkcmsvc", "System32/npkcsvc", "SysWOW64/npkcsvc"));
            s.Vendors.Add(V("nProtect Netizen", "구버전 개인방화벽 (잉카)", "INCAInternet/nProtect Netizen", "npstartersvc.exe", "npnj5agent.exe"));
            s.Vendors.Add(V("TouchEn nxKey", "키보드 입력 암호화 (라온시큐어 nxKey·nxWeb)", "RaonSecure", "TouchEn", "CKAgentNXE.exe"));
            s.Vendors.Add(V("CrossEX", "보안 플러그인 실행기 (TouchEn nxKey 연동)", "iniLINE/CrossEX"));
            s.Vendors.Add(V("Kings Online Security", "키보드 보안 (킹스정보통신 KOS)", "Kings/KOS"));
            s.Vendors.Add(V("SoftCamp 보안", "키보드 보안·개인방화벽 (소프트캠프)", "SoftCamp/SCWSSP", "SoftCamp/Secure KeyStroke"));

            // ── 공동인증서 로그인 · 전자서명 ──
            s.Vendors.Add(V("INISAFE CrossWeb EX", "공동인증서 로그인·전자서명 (이니텍)", "Initech/INISAFE", "Initech/Common", "INISAFE"));
            s.Vendors.Add(V("Veraport", "보안 프로그램 통합 설치·관리 (위즈베라)", "Wizvera/Veraport"));
            s.Vendors.Add(V("Delfino", "공동인증서 로그인·전자서명 (위즈베라)", "Wizvera/Delfino"));
            s.Vendors.Add(V("WIZVERA Process Manager", "Veraport·Delfino 관리 서비스", "Wizvera/Common"));
            s.Vendors.Add(V("AnySign4PC / XecureWeb", "공동인증서 전자서명 (한컴위드)", "SoftForum", "AnySign", "XecureWeb"));
            s.Vendors.Add(V("MagicLine (드림시큐리티)", "공동인증서 전자서명 (MagicLine 계열)", "DreamSecurity", "GPKISecureWebNP"));
            s.Vendors.Add(V("SignKorea", "증권용 공동인증 (코스콤)", "SignKorea"));
            s.Vendors.Add(V("KSign 인증 (케이사인)", "공동인증서 로그인·발급 모듈", "KSign/KCase", "KSign/KSignCASE", "KSign/KSignCSAPI"));
            s.Vendors.Add(V("UniSign / CrossCert", "공동인증서 관리 (한국전자인증)", "Crosscert"));
            s.Vendors.Add(V("SecuKit (KICA)", "공동인증서 전자서명 (한국정보인증)", "KICA/SecuKit", "KICA/Common"));
            s.Vendors.Add(V("KeySharp CertRelay", "인증서 복사·중계 (라온시큐어)", "KeySharp"));
            s.Vendors.Add(V("VestCert", "공동인증 솔루션 (예티소프트)", "VestCert"));

            // ── 단말정보 · 문서 보안 ──
            s.Vendors.Add(V("IPinside LWS Agent", "접속 PC 정보 확인 (부정 접속 방지, 인터리젠)", "IPinside", "Interezen"));
            s.Vendors.Add(V("MarkAny", "증명서·문서 출력 보안 (위변조 방지)", "MarkAny"));

            s.Sites.AddRange(SiteCatalog.Load());
            return s;
        }
    }

    // 끄기 전에 원래 설정을 기억해 두었다가 '원래대로 되돌리기'에 사용
    public class PendingChange
    {
        public string Kind { get; set; }       // service / run
        public string Key { get; set; }        // service name / location|value name
        public string Before { get; set; }     // service start type / StartupApproved base64 (empty = absent)
        public string After { get; set; }
        public bool Restore { get; set; }
        public bool Track { get; set; }
    }

    public class StateData
    {
        public Dictionary<string, string> Services { get; set; }     // 서비스 이름 → auto / delayed-auto
        public Dictionary<string, string> RunApproved { get; set; }  // 위치|이름 → 원래 StartupApproved 값(base64, 없었으면 "")
        // BankMode가 마지막으로 쓴 값을 기록해, 복원 전에 사용자가 바꾼 값은 덮어쓰지 않는다.
        public Dictionary<string, string> ServiceApplied { get; set; }
        public Dictionary<string, string> RunApplied { get; set; }
        // Write-ahead journal. Each entry is persisted before touching SCM or StartupApproved.
        public List<PendingChange> PendingChanges { get; set; }
        public StateData()
        {
            Services = new Dictionary<string, string>();
            RunApproved = new Dictionary<string, string>();
            ServiceApplied = new Dictionary<string, string>();
            RunApplied = new Dictionary<string, string>();
            PendingChanges = new List<PendingChange>();
        }
    }

    public static class Store
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BankMode");
        public static string SettingsPath { get { return Path.Combine(Dir, "settings.json"); } }
        public static string StatePath { get { return Path.Combine(Dir, "state.json"); } }

        public static Settings LoadSettings(string path, out string error)
        {
            error = null;
            if (!File.Exists(path))
            {
                var d = Settings.CreateDefault();
                try { SaveJson(path, d); } catch { }
                return d;
            }
            try
            {
                var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8)) ?? new Settings();
                if (s.Vendors == null) s.Vendors = new List<VendorRule>();
                if (s.Excluded == null) s.Excluded = new List<string>();
                if (s.Sites == null) s.Sites = new List<Site>();
                if (s.LastSite == null) s.LastSite = "";
                foreach (var v in s.Vendors) if (v.Match == null) v.Match = new List<string>();
                return s;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return Settings.CreateDefault();
            }
        }

        public static void SaveSettings(string path, Settings s) { SaveJson(path, s); }

        public static StateData LoadState(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var d = new JavaScriptSerializer().Deserialize<StateData>(File.ReadAllText(path, Encoding.UTF8));
                    if (d != null)
                    {
                        if (d.Services == null) d.Services = new Dictionary<string, string>();
                        if (d.RunApproved == null) d.RunApproved = new Dictionary<string, string>();
                        if (d.ServiceApplied == null) d.ServiceApplied = new Dictionary<string, string>();
                        if (d.RunApplied == null) d.RunApplied = new Dictionary<string, string>();
                        if (d.PendingChanges == null) d.PendingChanges = new List<PendingChange>();
                        return d;
                    }
                }
            }
            catch { }
            return new StateData();
        }

        public static void SaveState(string path, StateData d) { SaveJson(path, d); }

        static void SaveJson(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(Pretty(new JavaScriptSerializer().Serialize(o)));
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        // 사람이 메모장으로 고치기 쉽도록 들여쓰기
        static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inStr = false, esc = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(c).Append(json[i + 1]); i++; break; }
                        indent++;
                        sb.Append(c).Append("\r\n").Append(' ', indent * 2);
                        break;
                    case '}':
                    case ']':
                        indent--;
                        sb.Append("\r\n").Append(' ', indent * 2).Append(c);
                        break;
                    case ',': sb.Append(c).Append("\r\n").Append(' ', indent * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    // ===================== 탐색 =====================

    public class ServiceItem
    {
        public string Name, DisplayName, ExePath;
        public int StartType;   // 2 자동, 3 수동, 4 사용 안 함
        public bool Delayed;
        public bool Running;
        public int Pid;
    }

    public class RunItem
    {
        public string Location;   // HKLM, HKLM32, HKCU
        public string Name, Command, ExePath;
        public bool Enabled;
        public string Key { get { return Location + "|" + Name; } }
        public bool IsMachine { get { return Location != "HKCU"; } }
    }

    public class ProcItem
    {
        public int Pid;
        public string Name, FilePath;
        public long Memory;
    }

    public class BankModule
    {
        public VendorRule Rule;
        public bool Excluded;
        public bool Installed;
        public List<ServiceItem> Services = new List<ServiceItem>();
        public List<RunItem> RunEntries = new List<RunItem>();
        public List<ProcItem> Processes = new List<ProcItem>();

        public string Name { get { return Rule.Name; } }
        public bool IsRunning { get { return Services.Any(s => s.Running) || Processes.Count > 0; } }
        public long Memory { get { return Processes.Sum(p => p.Memory); } }
        public bool HasBootItems { get { return Services.Count + RunEntries.Count > 0; } }
        public bool AutoStarts { get { return Services.Any(s => s.StartType == 2) || RunEntries.Any(r => r.Enabled); } }
    }

    public class Classifier
    {
        readonly List<KeyValuePair<string, VendorRule>> needles = new List<KeyValuePair<string, VendorRule>>();

        public static int InvalidMatchCount(Settings settings)
        {
            if (settings == null || settings.Vendors == null) return 0;
            return settings.Vendors.Where(v => v != null && v.Match != null)
                .Sum(v => v.Match.Count(m => !SafeMatch(Scanner.Norm(m).Trim('\\', ' '))));
        }

        public Classifier(Settings st)
        {
            foreach (var v in st.Vendors)
                foreach (var m in v.Match)
                {
                    string raw = Scanner.Norm(m).Trim('\\', ' ');
                    if (!SafeMatch(raw)) continue;
                    string n = "\\" + raw;
                    needles.Add(new KeyValuePair<string, VendorRule>(n, v));
                }
        }

        static bool SafeMatch(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern) || pattern.Length < 3 || pattern.Contains("..") ||
                pattern.IndexOfAny(new[] { '*', '?', ':', '"', '<', '>', '|' }) >= 0) return false;
            var parts = pattern.Split('\\');
            if (parts.Any(p => string.IsNullOrWhiteSpace(p) || p == ".")) return false;
            string leaf = parts[parts.Length - 1];
            if (leaf.Length < 3) return false;
            // 단일 경로 조각은 일반적인 시스템 폴더 이름으로는 허용하지 않는다.
            if (parts.Length == 1 && (leaf.Length < 7 || new[] {
                "windows", "system32", "syswow64", "program files", "common files", "users", "appdata", "temp", "drivers"
            }.Contains(leaf, StringComparer.OrdinalIgnoreCase))) return false;
            return true;
        }

        public VendorRule Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string p = Scanner.Norm(path);
            foreach (var kv in needles)
            {
                int start = 0;
                while ((start = p.IndexOf(kv.Key, start, StringComparison.Ordinal)) >= 0)
                {
                    int end = start + kv.Key.Length;
                    // Path fragment must end at a path separator, file extension, or path end.
                    if (end == p.Length || p[end] == '\\' || p[end] == '.') return kv.Value;
                    start++;
                }
            }
            return null;
        }
    }

    public class RunLoc
    {
        public string Id;
        public RegistryHive Hive;
        public RegistryView View;
        public string Approved;
    }

    public static class Scanner
    {
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        // 작업 관리자 '시작 앱'과 같은 위치들 (WOW6432Node Run은 StartupApproved\Run32 사용)
        static readonly RunLoc[] RunLocs =
        {
            new RunLoc { Id = "HKLM", Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, Approved = "Run" },
            new RunLoc { Id = "HKLM32", Hive = RegistryHive.LocalMachine, View = RegistryView.Registry32, Approved = "Run32" },
            new RunLoc { Id = "HKCU", Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64, Approved = "Run" },
        };

        public static RunLoc Loc(string id) { return RunLocs.First(l => l.Id == id); }

        public static string Norm(string s) { return (s ?? "").Replace('/', '\\').ToLowerInvariant(); }

        public static string ExtractExe(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            string c = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (c.StartsWith("\""))
            {
                int e = c.IndexOf('"', 1);
                return e > 1 ? c.Substring(1, e - 1) : c.Trim('"');
            }
            int i = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return c.Substring(0, i + 4);
            int sp = c.IndexOf(' ');
            return sp > 0 ? c.Substring(0, sp) : c;
        }

        public static string ExtractArgs(string cmd)
        {
            string c = Environment.ExpandEnvironmentVariables(cmd.Trim());
            string exe = ExtractExe(c) ?? "";
            int start = c.StartsWith("\"") ? exe.Length + 2 : exe.Length;
            return start < c.Length ? c.Substring(start).Trim() : "";
        }

        public static List<BankModule> Discover(Settings st)
        {
            var cls = new Classifier(st);
            var map = new Dictionary<VendorRule, BankModule>();
            Func<VendorRule, BankModule> get = r =>
            {
                BankModule m;
                if (!map.TryGetValue(r, out m)) map[r] = m = new BankModule { Rule = r };
                return m;
            };

            foreach (var s in ScanServices())
            {
                var r = cls.Find(s.ExePath);
                if (r == null) continue;
                s.Running = QueryService(s.Name, out s.Pid);
                get(r).Services.Add(s);
            }
            foreach (var e in ScanRun())
            {
                var r = cls.Find(e.ExePath);
                if (r != null) get(r).RunEntries.Add(e);
            }
            var procs = ScanProcesses();
            foreach (var p in procs)
            {
                var r = cls.Find(p.FilePath);
                if (r != null) get(r).Processes.Add(p);
            }
            foreach (var m in map.Values) AttachServiceProcesses(m, procs);
            foreach (var path in ScanInstalledPaths())
            {
                var r = cls.Find(path);
                if (r != null) get(r).Installed = true;
            }

            var excluded = new HashSet<string>(st.Excluded);
            var list = new List<BankModule>();
            foreach (var v in st.Vendors)
            {
                BankModule m;
                if (map.TryGetValue(v, out m))
                {
                    m.Excluded = excluded.Contains(m.Name);
                    list.Add(m);
                }
            }
            return list;
        }

        // 타이머용 가벼운 갱신: 서비스 상태와 프로세스만 다시 본다. 목록에 없던 모듈이 보이면 true
        public static bool RefreshStatus(Settings st, List<BankModule> mods)
        {
            var cls = new Classifier(st);
            var procs = ScanProcesses();
            var byRule = new Dictionary<VendorRule, List<ProcItem>>();
            foreach (var p in procs)
            {
                var r = cls.Find(p.FilePath);
                if (r == null) continue;
                List<ProcItem> l;
                if (!byRule.TryGetValue(r, out l)) byRule[r] = l = new List<ProcItem>();
                l.Add(p);
            }
            bool unknown = byRule.Keys.Any(r => !mods.Any(m => m.Rule == r));
            foreach (var m in mods)
            {
                foreach (var s in m.Services) s.Running = QueryService(s.Name, out s.Pid);
                List<ProcItem> l;
                m.Processes = byRule.TryGetValue(m.Rule, out l) ? l : new List<ProcItem>();
                AttachServiceProcesses(m, procs);
            }
            return unknown;
        }

        // SYSTEM 권한 서비스 프로세스는 일반 권한으로 경로를 못 읽으니, 서비스가 알려 주는 PID로 메모리를 잡는다
        static void AttachServiceProcesses(BankModule m, List<ProcItem> procs)
        {
            foreach (var s in m.Services)
            {
                if (!s.Running || s.Pid <= 0 || m.Processes.Any(x => x.Pid == s.Pid)) continue;
                var sp = procs.FirstOrDefault(x => x.Pid == s.Pid);
                if (sp != null) m.Processes.Add(new ProcItem { Pid = sp.Pid, Name = sp.Name, FilePath = s.ExePath, Memory = sp.Memory });
            }
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenSCManager(string machine, string database, uint access);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenService(IntPtr scm, string name, uint access);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool QueryServiceStatusEx(IntPtr service, int level, byte[] buffer, int size, out int needed);
        [DllImport("advapi32.dll")]
        static extern bool CloseServiceHandle(IntPtr h);

        // 실행 중이면 true, pid에는 서비스 프로세스 ID (일반 권한으로도 조회 가능)
        public static bool QueryService(string name, out int pid)
        {
            pid = 0;
            IntPtr scm = OpenSCManager(null, null, 0x0001 /* SC_MANAGER_CONNECT */);
            if (scm == IntPtr.Zero) return false;
            try
            {
                IntPtr h = OpenService(scm, name, 0x0004 /* SERVICE_QUERY_STATUS */);
                if (h == IntPtr.Zero) return false;
                try
                {
                    var buf = new byte[36];   // SERVICE_STATUS_PROCESS
                    int needed;
                    if (!QueryServiceStatusEx(h, 0 /* SC_STATUS_PROCESS_INFO */, buf, buf.Length, out needed)) return false;
                    pid = BitConverter.ToInt32(buf, 28);
                    return BitConverter.ToInt32(buf, 4) != 1;   // 1 = SERVICE_STOPPED
                }
                finally { CloseServiceHandle(h); }
            }
            finally { CloseServiceHandle(scm); }
        }

        public static List<ServiceItem> ScanServices()
        {
            var list = new List<ServiceItem>();
            using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var root = hk.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
            {
                if (root == null) return list;
                foreach (string name in root.GetSubKeyNames())
                {
                    try
                    {
                        using (var k = root.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            object type = k.GetValue("Type");
                            if (!(type is int) || ((int)type & 0x30) == 0) continue;   // 커널 드라이버는 건드리지 않음
                            string img = k.GetValue("ImagePath") as string;
                            if (string.IsNullOrEmpty(img)) continue;
                            object start = k.GetValue("Start");
                            object delayed = k.GetValue("DelayedAutostart");
                            list.Add(new ServiceItem
                            {
                                Name = name,
                                DisplayName = (k.GetValue("DisplayName") as string) ?? name,
                                ExePath = ExtractExe(img),
                                StartType = start is int ? (int)start : 3,
                                Delayed = delayed is int && (int)delayed == 1
                            });
                        }
                    }
                    catch { }
                }
            }
            return list;
        }

        public static List<RunItem> ScanRun()
        {
            var list = new List<RunItem>();
            foreach (var loc in RunLocs)
            {
                try
                {
                    using (var hk = RegistryKey.OpenBaseKey(loc.Hive, loc.View))
                    using (var run = hk.OpenSubKey(RunPath))
                    {
                        if (run == null) continue;
                        foreach (string n in run.GetValueNames())
                        {
                            string cmd = run.GetValue(n) as string;
                            if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(cmd)) continue;
                            byte[] b = ReadApproved(loc, n);
                            list.Add(new RunItem
                            {
                                Location = loc.Id,
                                Name = n,
                                Command = cmd,
                                ExePath = ExtractExe(cmd),
                                Enabled = IsApprovedEnabled(b)
                            });
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        public static byte[] ReadApproved(RunLoc loc, string name)
        {
            using (var hk = RegistryKey.OpenBaseKey(loc.Hive, RegistryView.Registry64))
            using (var k = hk.OpenSubKey(ApprovedPath + loc.Approved))
                return k == null ? null : k.GetValue(name) as byte[];
        }

        // StartupApproved 값이 없거나 첫 바이트가 짝수(02 등)면 켜짐, 홀수(03 등)면 '사용 안 함'
        public static bool IsApprovedEnabled(byte[] b)
        {
            return b == null || b.Length == 0 || (b[0] & 1) == 0;
        }

        // value가 null이면 값을 지워 '원래 없던 상태'로 되돌린다
        public static void WriteApproved(RunLoc loc, string name, byte[] value)
        {
            using (var hk = RegistryKey.OpenBaseKey(loc.Hive, RegistryView.Registry64))
            using (var k = hk.CreateSubKey(ApprovedPath + loc.Approved))
            {
                if (value == null) k.DeleteValue(name, false);
                else k.SetValue(name, value, RegistryValueKind.Binary);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder buf, ref int size);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        public static string ProcessPath(int pid)
        {
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally { CloseHandle(h); }
        }

        // 모든 프로세스. 권한이 없어 경로를 못 읽은 것은 FilePath가 null
        public static List<ProcItem> ScanProcesses()
        {
            var list = new List<ProcItem>();
            int self = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id <= 4 || p.Id == self) continue;
                    list.Add(new ProcItem { Pid = p.Id, Name = p.ProcessName, FilePath = ProcessPath(p.Id), Memory = p.WorkingSet64 });
                }
                catch { }
                finally { p.Dispose(); }
            }
            return list;
        }

        // 프로그램 추가/제거 목록 — 켜고 끌 항목은 없어도 '설치됨'으로 보여 주기 위함
        public static List<string> ScanInstalledPaths()
        {
            var list = new List<string>();
            var roots = new[]
            {
                new { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64 },
                new { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry32 },
                new { Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64 },
            };
            foreach (var r in roots)
            {
                try
                {
                    using (var hk = RegistryKey.OpenBaseKey(r.Hive, r.View))
                    using (var u = hk.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (u == null) continue;
                        foreach (string n in u.GetSubKeyNames())
                        {
                            using (var k = u.OpenSubKey(n))
                            {
                                if (k == null) continue;
                                foreach (string f in new[] { "InstallLocation", "UninstallString", "DisplayIcon" })
                                {
                                    string v = k.GetValue(f) as string;
                                    if (!string.IsNullOrEmpty(v)) list.Add(v);
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            return list;
        }
    }

    // ===================== 켜기 / 끄기 =====================

    public class Ctx
    {
        public Settings Settings;
        public string StatePath;
        public Action<string> Log;
        public int Failures;
        public IntPtr Owner;   // 관리자 확인 창을 붙일 창 (없으면 0)

        public void Fail(string msg)
        {
            Failures++;
            Log("  ! " + msg);
        }
    }

    public static class Ops
    {
        static List<BankModule> Managed(Ctx c)
        {
            return Scanner.Discover(c.Settings).Where(m => !m.Excluded).ToList();
        }

        public static bool OnNeedsAdmin(IEnumerable<BankModule> mods)
        {
            // 멈춰 있는 관리 서비스를 켜려면 관리자 권한 필요 ('사용 안 함'을 풀어 시작하는 경우 포함)
            return mods.Where(m => !m.Excluded).Any(m => m.Services.Any(s => !s.Running));
        }

        public static bool OffNeedsAdmin(IEnumerable<BankModule> mods)
        {
            // '사용 안 함(4)'이 아닌 관리 서비스는 끄기 때 사용 안 함으로 바꿔야 하므로 관리자 권한 필요
            return mods.Where(m => !m.Excluded).Any(m =>
                m.Services.Any(s => s.Running || s.StartType != 4) ||
                m.RunEntries.Any(r => r.IsMachine && r.Enabled) ||
                m.Processes.Count > 0);
        }

        public static bool RestoreNeedsAdmin(string statePath)
        {
            var st = Store.LoadState(statePath);
            return st.Services.Count > 0 || st.RunApproved.Keys.Any(k => !k.StartsWith("HKCU|")) ||
                st.PendingChanges.Any(p => p != null && (p.Kind == "service" || (p.Kind == "run" && !string.IsNullOrEmpty(p.Key) && !p.Key.StartsWith("HKCU|", StringComparison.OrdinalIgnoreCase))));
        }

        static string Why(Exception ex)
        {
            var w = ex as Win32Exception ?? ex.InnerException as Win32Exception;
            if (w != null && w.NativeErrorCode == 5) return "권한 거부 (프로그램 자체 보호 기능)";
            return (ex.InnerException ?? ex).Message;
        }

        static string ServiceType(ServiceItem service)
        {
            return service.StartType == 2 ? (service.Delayed ? "delayed-auto" : "auto") :
                service.StartType == 3 ? "demand" : service.StartType == 4 ? "disabled" : "unknown";
        }

        static PendingChange Journal(Ctx c, StateData state, string kind, string key, string before, string after, bool restore, bool track)
        {
            var pending = new PendingChange { Kind = kind, Key = key, Before = before ?? "", After = after ?? "", Restore = restore, Track = track };
            state.PendingChanges.Add(pending);
            // If this durable write fails, caller must not perform the corresponding system mutation.
            Store.SaveState(c.StatePath, state);
            return pending;
        }

        static void RecoverPending(Ctx c, StateData state, bool includeMachine)
        {
            foreach (var pending in state.PendingChanges.ToList())
            {
                if (pending == null || string.IsNullOrEmpty(pending.Kind) || string.IsNullOrEmpty(pending.Key))
                {
                    state.PendingChanges.Remove(pending);
                    Store.SaveState(c.StatePath, state);
                    continue;
                }
                if (!includeMachine && (pending.Kind == "service" || (pending.Kind == "run" && !pending.Key.StartsWith("HKCU|", StringComparison.OrdinalIgnoreCase)))) continue;
                try { ReconcilePending(c, state, pending); }
                catch (Exception ex) { c.Fail("이전 중단 작업 복구 확인 실패 (" + pending.Key + "): " + Why(ex)); }
            }
        }

        static void ReconcilePending(Ctx c, StateData state, PendingChange pending)
        {
            string current;
            if (pending.Kind == "service")
            {
                var service = Scanner.ScanServices().FirstOrDefault(s => string.Equals(s.Name, pending.Key, StringComparison.OrdinalIgnoreCase));
                if (service == null)
                {
                    // A removed service has no state left to restore; discard stale records so
                    // a future service reusing this name is never changed by an old journal.
                    RemoveSavedChange(state, pending);
                    state.PendingChanges.Remove(pending);
                    c.Log("  - 이전 중단 작업: " + pending.Key + " 서비스가 없어 기록을 정리함");
                    Store.SaveState(c.StatePath, state);
                    return;
                }
                current = ServiceType(service);
            }
            else if (pending.Kind == "run")
            {
                int bar = pending.Key.IndexOf('|');
                if (bar < 0) return;
                string location = pending.Key.Substring(0, bar), name = pending.Key.Substring(bar + 1);
                var loc = Scanner.Loc(location);
                byte[] bytes = Scanner.ReadApproved(loc, name);
                current = bytes == null ? "" : Convert.ToBase64String(bytes);
            }
            else return;

            if (string.Equals(current, pending.After, StringComparison.Ordinal))
            {
                if (pending.Restore) RemoveSavedChange(state, pending);
                else if (pending.Track) RecordAppliedChange(state, pending);
                c.Log("  - 이전 중단 작업 확인: " + pending.Key + (pending.Restore ? " 복원 완료" : " 변경 완료"));
            }
            else if (!string.Equals(current, pending.Before, StringComparison.Ordinal))
            {
                c.Log("  - 이전 중단 작업: " + pending.Key + " 값이 달라 현재 값을 보존함");
            }
            state.PendingChanges.Remove(pending);
            Store.SaveState(c.StatePath, state);
        }

        static void RecordAppliedChange(StateData state, PendingChange pending)
        {
            if (pending.Kind == "service")
            {
                if (!state.Services.ContainsKey(pending.Key)) state.Services[pending.Key] = pending.Before;
                state.ServiceApplied[pending.Key] = pending.After;
            }
            else
            {
                if (!state.RunApproved.ContainsKey(pending.Key)) state.RunApproved[pending.Key] = pending.Before;
                state.RunApplied[pending.Key] = pending.After;
            }
        }

        static void RemoveSavedChange(StateData state, PendingChange pending)
        {
            if (pending.Kind == "service")
            {
                state.Services.Remove(pending.Key);
                state.ServiceApplied.Remove(pending.Key);
            }
            else
            {
                state.RunApproved.Remove(pending.Key);
                state.RunApplied.Remove(pending.Key);
            }
        }

        // ---------- 켜기 ----------

        // 관리자 단계: 서비스 시작
        public static void OnElevated(Ctx c)
        {
            var state = Store.LoadState(c.StatePath);
            RecoverPending(c, state, true);
            foreach (var m in Managed(c))
                foreach (var s in m.Services)
                    StartService(c, s, state);
            Store.SaveState(c.StatePath, state);
        }

        // 사용자 단계: 시작프로그램을 부팅 때처럼 일반 권한으로 실행
        public static void OnUser(Ctx c)
        {
            var running = new HashSet<string>(Scanner.ScanProcesses().Where(p => p.FilePath != null).Select(p => Scanner.Norm(p.FilePath)));
            foreach (var m in Managed(c))
                foreach (var r in m.RunEntries)
                {
                    if (string.IsNullOrEmpty(r.ExePath) || !File.Exists(r.ExePath)) continue;
                    string key = Scanner.Norm(r.ExePath);
                    if (running.Contains(key)) continue;
                    try
                    {
                        var psi = new ProcessStartInfo(r.ExePath, Scanner.ExtractArgs(r.Command))
                        {
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(r.ExePath)
                        };
                        using (Process.Start(psi)) { }
                        running.Add(key);
                        c.Log("  - " + Path.GetFileName(r.ExePath) + " 실행");
                    }
                    catch (Exception ex) { c.Fail(Path.GetFileName(r.ExePath) + " 실행 실패: " + Why(ex)); }
                }
        }

        static void StartService(Ctx c, ServiceItem s, StateData state)
        {
            try
            {
                using (var sc = new ServiceController(s.Name))
                {
                    if (sc.Status == ServiceControllerStatus.Running) return;
                    // 끄기 때 '사용 안 함'으로 바꿔 둔 서비스는 켜려면 먼저 '수동'으로 풀어야 시작할 수 있다
                    if (s.StartType == 4)
                    {
                        var pending = Journal(c, state, "service", s.Name, "disabled", "demand", false, true);
                        int result = Sc("config \"" + s.Name + "\" start= demand");
                        ReconcilePending(c, state, pending);
                        if (result != 0)
                        {
                            c.Fail(s.DisplayName + ": '사용 안 함'을 풀지 못해 시작하지 못함");
                            return;
                        }
                    }
                    if (sc.Status != ServiceControllerStatus.StartPending) sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                    c.Log("  - " + s.DisplayName + " 시작");
                }
            }
            catch (System.ServiceProcess.TimeoutException) { c.Fail(s.DisplayName + ": 20초 안에 시작되지 않음"); }
            catch (Exception ex) { c.Fail(s.DisplayName + " 시작 실패: " + Why(ex)); }
        }

        // ---------- 끄기 ----------

        // 사용자 단계: 내 계정의 시작프로그램 끄기 + 내 권한으로 끌 수 있는 프로세스 종료
        public static void OffUser(Ctx c)
        {
            var mods = Managed(c);
            var state = Store.LoadState(c.StatePath);
            RecoverPending(c, state, false);
            DisableRuns(c, mods.SelectMany(m => m.RunEntries).Where(r => !r.IsMachine), state);
            Store.SaveState(c.StatePath, state);
            KillProcesses(c, mods, null, true);   // 권한 부족 실패는 관리자 단계에서 다시 시도하므로 조용히
        }

        // 관리자 단계: 서비스 중지 → 남은 프로세스 종료 → 부팅 자동 시작 막기
        public static void OffElevated(Ctx c)
        {
            var mods = Managed(c);
            var state = Store.LoadState(c.StatePath);
            RecoverPending(c, state, true);

            // 감시(watchdog) 서비스가 프로세스를 되살리지 않도록 서비스부터 멈춘다
            foreach (var m in mods)
                foreach (var s in m.Services)
                    StopService(c, s);

            var reported = new HashSet<int>();
            KillProcesses(c, mods, reported, false);
            Thread.Sleep(1000);
            KillProcesses(c, Managed(c), reported, false);   // 그 사이 되살아난 것 정리

            foreach (var m in mods)
                foreach (var s in m.Services)
                    SetDisabled(c, s, state);
            DisableRuns(c, mods.SelectMany(m => m.RunEntries).Where(r => r.IsMachine), state);

            Store.SaveState(c.StatePath, state);
        }

        static void StopService(Ctx c, ServiceItem s)
        {
            try
            {
                using (var sc = new ServiceController(s.Name))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped) return;
                    if (!sc.CanStop) { c.Fail(s.DisplayName + ": 중지를 허용하지 않는 서비스 (자체 보호)"); return; }
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    c.Log("  - " + s.DisplayName + " 중지");
                }
            }
            catch (System.ServiceProcess.TimeoutException) { c.Fail(s.DisplayName + ": 20초 안에 멈추지 않음"); }
            catch (Exception ex) { c.Fail(s.DisplayName + " 중지 실패: " + Why(ex)); }
        }

        static void KillProcesses(Ctx c, List<BankModule> mods, HashSet<int> reported, bool quiet)
        {
            foreach (var m in mods)
                foreach (var p in m.Processes)
                {
                    try
                    {
                        // PID가 그새 다른 프로그램에 재사용됐을 수 있으니 경로를 다시 확인
                        if (!string.Equals(Scanner.ProcessPath(p.Pid), p.FilePath, StringComparison.OrdinalIgnoreCase)) continue;
                        using (var proc = Process.GetProcessById(p.Pid))
                        {
                            proc.Kill();
                            proc.WaitForExit(3000);
                        }
                        c.Log("  - " + p.Name + ".exe 종료");
                    }
                    catch (ArgumentException) { }            // 이미 종료됨
                    catch (InvalidOperationException) { }    // 이미 종료됨
                    catch (Exception ex)
                    {
                        if (!quiet && (reported == null || reported.Add(p.Pid)))
                            c.Fail(p.Name + ".exe 종료 실패: " + Why(ex));
                    }
                }
        }

        // 서비스를 '사용 안 함(disabled)'으로. '수동'과 달리 은행 사이트가 불러도 켜지지 않는다.
        // 켤 때 OnElevated가 다시 '수동'으로 풀어 주므로 되돌리기와 별개로도 은행 업무엔 지장 없다.
        static void SetDisabled(Ctx c, ServiceItem s, StateData state)
        {
            if (s.StartType == 4) return;   // 이미 사용 안 함
            string orig = ServiceType(s);
            var pending = Journal(c, state, "service", s.Name, orig, "disabled", false, true);
            int result = Sc("config \"" + s.Name + "\" start= disabled");
            ReconcilePending(c, state, pending);
            if (result == 0)
            {
                c.Log("  - " + s.DisplayName + ": 사용 안 함으로 바꿈 (부팅·요청 모두 안 켜짐)");
            }
            else c.Fail(s.DisplayName + ": 시작 유형을 바꾸지 못함 (자체 보호 기능일 수 있음)");
        }

        // 작업 관리자가 '사용 안 함'으로 바꿀 때 쓰는 형식: 03 00 00 00 + 끈 시각(FILETIME)
        static byte[] DisabledBytes()
        {
            var b = new byte[12];
            b[0] = 3;
            Array.Copy(BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()), 0, b, 4, 8);
            return b;
        }

        // 끈 다음 다시 읽어서 정말 꺼졌는지 확인한다.
        // 자체 보호 기능이 있는 프로그램은 쓰기를 오류 없이 무시하거나 곧바로 되돌리기도 한다
        static void DisableRuns(Ctx c, IEnumerable<RunItem> runs, StateData state)
        {
            var written = new List<KeyValuePair<RunItem, byte[]>>();   // 항목 → 끄기 전 값
            foreach (var r in runs)
            {
                if (!r.Enabled) continue;
                try
                {
                    var loc = Scanner.Loc(r.Location);
                    byte[] cur = Scanner.ReadApproved(loc, r.Name);
                    byte[] disabled = DisabledBytes();
                    string before = cur == null ? "" : Convert.ToBase64String(cur);
                    Journal(c, state, "run", r.Key, before, Convert.ToBase64String(disabled), false, true);
                    Scanner.WriteApproved(loc, r.Name, disabled);
                    written.Add(new KeyValuePair<RunItem, byte[]>(r, cur));
                }
                catch (Exception ex) { c.Fail("시작프로그램 '" + r.Name + "' 끄기 실패: " + Why(ex)); }
            }
            if (written.Count == 0) return;

            Thread.Sleep(500);   // 곧바로 되돌리는 경우까지 잡도록 잠깐 기다렸다가 확인
            foreach (var kv in written)
            {
                var r = kv.Key;
                bool off;
                try { off = !Scanner.IsApprovedEnabled(Scanner.ReadApproved(Scanner.Loc(r.Location), r.Name)); }
                catch { off = false; }
                if (!off)
                {
                    var pending = state.PendingChanges.LastOrDefault(p => p != null && p.Kind == "run" && p.Key == r.Key);
                    if (pending != null) ReconcilePending(c, state, pending);
                    c.Fail("시작프로그램 '" + r.Name + "': 껐지만 다시 켜져 있어요 (자체 보호 기능)");
                    continue;
                }
                var completed = state.PendingChanges.LastOrDefault(p => p != null && p.Kind == "run" && p.Key == r.Key);
                if (completed != null) ReconcilePending(c, state, completed);
                c.Log("  - 시작프로그램 '" + r.Name + "' 끔");
            }
        }

        static int Sc(string args)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return p.HasExited ? p.ExitCode : -1;
            }
        }

        // ---------- 원래대로 ----------

        public static void RestoreUser(Ctx c)
        {
            var state = Store.LoadState(c.StatePath);
            RecoverPending(c, state, false);
            RestoreRun(c, state, false);
            Store.SaveState(c.StatePath, state);
        }

        public static void RestoreElevated(Ctx c)
        {
            var state = Store.LoadState(c.StatePath);
            RecoverPending(c, state, true);
            var services = Scanner.ScanServices();
            var classifier = new Classifier(c.Settings);
            foreach (var kv in state.Services.ToList())
            {
                var service = services.FirstOrDefault(s => string.Equals(s.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
                if (service == null)
                {
                    state.Services.Remove(kv.Key);
                    state.ServiceApplied.Remove(kv.Key);
                    c.Log("  - " + kv.Key + ": 서비스가 없어 복원 기록을 정리함");
                    continue;
                }
                if (kv.Value != "auto" && kv.Value != "delayed-auto" && kv.Value != "demand" && kv.Value != "disabled")
                {
                    c.Fail(kv.Key + ": 저장된 시작 유형이 올바르지 않아 복원을 건너뜀");
                    continue;
                }
                string current = ServiceType(service);
                if (string.Equals(current, kv.Value, StringComparison.OrdinalIgnoreCase))
                {
                    state.Services.Remove(kv.Key);
                    state.ServiceApplied.Remove(kv.Key);
                    c.Log("  - " + kv.Key + ": 이미 원래 시작 유형이라 복원 기록을 정리함");
                    continue;
                }
                if (classifier.Find(service.ExePath) == null)
                {
                    c.Fail(kv.Key + ": 현재 보안 프로그램으로 확인되지 않아 복원을 건너뜀");
                    continue;
                }
                string applied;
                if (state.ServiceApplied.TryGetValue(kv.Key, out applied))
                {
                    if (!string.Equals(current, applied, StringComparison.OrdinalIgnoreCase))
                    {
                        c.Fail(kv.Key + ": BankMode가 설정한 뒤 값이 바뀌어 복원을 건너뜀 (현재 설정을 보존함)");
                        continue;
                    }
                }
                else if (service.StartType != 4)
                {
                    // 이전 버전 상태 파일 호환: 이전 버전은 마지막 적용 값을 저장하지 않았다.
                    c.Fail(kv.Key + ": 현재 '사용 안 함' 상태가 아니어서 이전 버전 설정의 복원을 건너뜀");
                    continue;
                }
                var pending = Journal(c, state, "service", kv.Key, current, kv.Value, true, false);
                int result = Sc("config \"" + kv.Key + "\" start= " + kv.Value);
                ReconcilePending(c, state, pending);
                if (result == 0)
                {
                    c.Log("  - " + kv.Key + ": 원래 시작 유형으로 복원");
                }
                else c.Fail(kv.Key + ": 자동 시작 복원 실패");
            }
            RestoreRun(c, state, true);
            Store.SaveState(c.StatePath, state);
        }

        static void RestoreRun(Ctx c, StateData state, bool machine)
        {
            var runs = Scanner.ScanRun();
            var classifier = new Classifier(c.Settings);
            foreach (var kv in state.RunApproved.ToList())
            {
                int bar = kv.Key.IndexOf('|');
                if (bar < 0) continue;
                string locId = kv.Key.Substring(0, bar), name = kv.Key.Substring(bar + 1);
                if ((locId != "HKCU") != machine) continue;
                try
                {
                    var run = runs.FirstOrDefault(r => r.Location == locId && r.Name == name);
                    if (run == null)
                    {
                        state.RunApproved.Remove(kv.Key);
                        state.RunApplied.Remove(kv.Key);
                        c.Log("  - 시작프로그램 '" + name + "': 항목이 없어 복원 기록을 정리함");
                        continue;
                    }
                    var loc = Scanner.Loc(locId);
                    byte[] current = Scanner.ReadApproved(loc, name);
                    byte[] original = kv.Value.Length == 0 ? null : Convert.FromBase64String(kv.Value);
                    if (BytesEqual(current, original))
                    {
                        state.RunApproved.Remove(kv.Key);
                        state.RunApplied.Remove(kv.Key);
                        c.Log("  - 시작프로그램 '" + name + "': 이미 원래 상태라 복원 기록을 정리함");
                        continue;
                    }
                    if (classifier.Find(run.ExePath) == null)
                    {
                        c.Fail("시작프로그램 '" + name + "': 현재 보안 프로그램으로 확인되지 않아 복원을 건너뜀");
                        continue;
                    }
                    string expected;
                    if (state.RunApplied.TryGetValue(kv.Key, out expected))
                    {
                        byte[] expectedBytes = expected.Length == 0 ? null : Convert.FromBase64String(expected);
                        if (!BytesEqual(current, expectedBytes))
                        {
                            c.Fail("시작프로그램 '" + name + "': BankMode가 설정한 뒤 값이 바뀌어 복원을 건너뜀 (현재 설정을 보존함)");
                            continue;
                        }
                    }
                    else if (Scanner.IsApprovedEnabled(current))
                    {
                        // 이전 버전 상태 파일 호환: 이전 버전은 마지막 적용 값을 저장하지 않았다.
                        c.Fail("시작프로그램 '" + name + "': 현재 꺼짐 상태가 아니어서 이전 버전 설정의 복원을 건너뜀");
                        continue;
                    }
                    var pending = Journal(c, state, "run", kv.Key,
                        current == null ? "" : Convert.ToBase64String(current),
                        original == null ? "" : Convert.ToBase64String(original), true, false);
                    Scanner.WriteApproved(loc, name, original);
                    ReconcilePending(c, state, pending);
                    c.Log("  - 시작프로그램 '" + name + "' 복원");
                }
                catch (Exception ex) { c.Fail("시작프로그램 '" + name + "' 복원 실패: " + Why(ex)); }
            }
        }

        static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null) return left == null && right == null;
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }

    // ===================== 공용 진입점 (앱 창 · 크롬 확장) =====================

    // 관리하는 모듈 기준의 한눈 요약
    public class Summary
    {
        public int Found, Managed, Running, BootAuto;
        public long Memory;
        public bool On { get { return Running > 0; } }
        public bool Partial { get { return Running > 0 && Running < Managed; } }
        public bool AllOn { get { return Managed > 0 && Running == Managed; } }

        public static Summary Of(IEnumerable<BankModule> mods)
        {
            var all = mods.ToList();
            var managed = all.Where(m => !m.Excluded).ToList();
            return new Summary
            {
                Found = all.Count,
                Managed = managed.Count,
                Running = managed.Count(m => m.IsRunning),
                BootAuto = managed.Count(m => m.AutoStarts),
                Memory = managed.Sum(m => m.Memory)
            };
        }
    }

    // 화면 없이 켜기·끄기·되돌리기 전체 흐름을 실행한다. 앱 창과 크롬 확장 호스트가 함께 쓴다
    public static class Bank
    {
        public static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }

        public static bool IsAdmin
        {
            get
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // 켜기: 서비스 시작(필요할 때만 관리자 권한) → 시작프로그램 실행. 관리자 확인을 취소하면 false
        public static bool TurnOn(Ctx c, List<BankModule> mods = null)
        {
            return Exclusive(c, () =>
            {
                var targets = mods ?? Scanner.Discover(c.Settings);
                if (Ops.OnNeedsAdmin(targets))
                {
                    var changes = targets.Where(m => !m.Excluded).SelectMany(m =>
                        m.Services.Where(s => !s.Running).Select(s => m.Name + " — 서비스 " + s.DisplayName + PathSuffix(s.ExePath))
                        .Concat(m.RunEntries.Select(r => m.Name + " — 시작프로그램 " + r.Name + PathSuffix(r.ExePath))));
                    if (!ConfirmTargets(c, "켜기", changes) || !RunElevated(c, "on")) return false;
                }
                Ops.OnUser(c);
                return true;
            });
        }

        // 끄기: 내 권한으로 할 수 있는 것부터 → 남은 것만 관리자 권한으로
        public static bool TurnOff(Ctx c)
        {
            return TurnOffCore(c, "끄기", false);
        }

        // 부분 실패 후 다시 시도: 현재 상태를 다시 읽어 아직 켜져 있는 항목만 대상으로 한다.
        public static bool RetryOff(Ctx c)
        {
            return TurnOffCore(c, "남은 항목 다시 끄기", true);
        }

        static bool TurnOffCore(Ctx c, string operation, bool retryOnly)
        {
            return Exclusive(c, () =>
            {
                var targets = Scanner.Discover(c.Settings);
                var unfinished = targets.Where(m => !m.Excluded &&
                    (m.Services.Any(s => s.Running || s.StartType != 4) ||
                     m.RunEntries.Any(r => r.Enabled) || m.Processes.Count > 0)).ToList();
                var state = Store.LoadState(c.StatePath);
                bool pendingAdmin = state.PendingChanges.Any(p => p != null &&
                    (p.Kind == "service" || (p.Kind == "run" && !string.IsNullOrEmpty(p.Key) &&
                    !p.Key.StartsWith("HKCU|", StringComparison.OrdinalIgnoreCase))));
                if (retryOnly && unfinished.Count == 0 && state.PendingChanges.Count == 0)
                {
                    c.Log("  - 다시 시도할 항목이 없습니다. 관리 대상이 모두 꺼져 있어요");
                    return true;
                }

                bool needsAdmin = Ops.OffNeedsAdmin(unfinished) || (retryOnly && pendingAdmin);
                if (needsAdmin)
                {
                    var changes = unfinished.SelectMany(m =>
                        m.Services.Where(s => s.Running || s.StartType != 4).Select(s => m.Name + " — 서비스 " + s.DisplayName + " 중지/자동 시작 해제" + PathSuffix(s.ExePath))
                        .Concat(m.RunEntries.Where(r => r.Enabled).Select(r => m.Name + " — 시작프로그램 " + r.Name + " 사용 안 함" + PathSuffix(r.ExePath)))
                        .Concat(m.Processes.Select(p => m.Name + " — 프로세스 " + p.Name + ".exe 종료" + PathSuffix(p.FilePath))));
                    if (!ConfirmTargets(c, operation, changes)) return false;
                }
                if (needsAdmin && !RunElevated(c, "off")) return false;
                Ops.OffUser(c);
                return true;
            });
        }

        public static bool Restore(Ctx c)
        {
            return Exclusive(c, () =>
            {
                var state = Store.LoadState(c.StatePath);
                if (state.Services.Count == 0 && state.RunApproved.Count == 0 && state.PendingChanges.Count == 0) return true;
                bool needsAdmin = Ops.RestoreNeedsAdmin(c.StatePath);
                var servicePaths = Scanner.ScanServices().ToDictionary(s => s.Name, s => s.ExePath, StringComparer.OrdinalIgnoreCase);
                var runPaths = Scanner.ScanRun().ToDictionary(r => r.Key, r => r.ExePath, StringComparer.OrdinalIgnoreCase);
                var changes = state.Services.Keys.Select(name => "서비스 " + name + " 시작 유형 복원" + PathSuffix(servicePaths.ContainsKey(name) ? servicePaths[name] : null))
                    .Concat(state.RunApproved.Keys.Select(key => "시작프로그램 " + key.Substring(key.IndexOf('|') + 1) + " 상태 복원" + PathSuffix(runPaths.ContainsKey(key) ? runPaths[key] : null)))
                    .Concat(state.PendingChanges.Where(p => p != null).Select(p => p.Kind == "service"
                        ? "서비스 " + p.Key + ": 중단된 변경 상태 확인"
                        : p.Kind == "run" ? "시작프로그램 " + (p.Key ?? "").Substring((p.Key ?? "").IndexOf('|') + 1) + ": 중단된 변경 상태 확인"
                        : "이전 중단 작업 상태 확인"));
                if (!ConfirmTargets(c, "원래대로 되돌리기", changes)) return false;
                if (needsAdmin && !RunElevated(c, "restore")) return false;
                Ops.RestoreUser(c);
                return true;
            });
        }

        sealed class WindowOwner : IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public WindowOwner(IntPtr handle) { Handle = handle; }
        }

        static bool ConfirmTargets(Ctx c, string operation, IEnumerable<string> targets)
        {
            var list = targets.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            if (list.Count == 0) return true;
            var detail = string.Join("\n", list.Take(12).Select(x => "  • " + x));
            if (list.Count > 12) detail += "\n  … 외 " + (list.Count - 12) + "개";
            string prompt = "다음 항목을 " + operation + "합니다. 계속할까요?\n\n" + detail +
                "\n\n계속하면 관리자 권한 확인 창이 이어서 나타날 수 있습니다.";
            IWin32Window owner = c.Owner == IntPtr.Zero ? null : new WindowOwner(c.Owner);
            return MessageBox.Show(owner, prompt, "은행 모드 — 변경 대상 확인", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        static string PathSuffix(string path) { return string.IsNullOrWhiteSpace(path) ? "" : "\n    " + path; }

        // 앱 창과 크롬 확장이 동시에 켜고 끄면 state.json이 엉킬 수 있어 한 번에 하나만 실행
        static bool Exclusive(Ctx c, Func<bool> body)
        {
            using (var m = new Mutex(false, @"Local\BankMode.Op"))
            {
                bool got;
                try { got = m.WaitOne(0); }
                catch (AbandonedMutexException) { got = true; }
                if (!got)
                {
                    c.Fail("다른 곳(앱 창 또는 크롬 확장)에서 이미 켜거나 끄는 중이에요. 끝난 뒤 다시 시도하세요");
                    return false;
                }
                try { return body(); }
                finally { m.ReleaseMutex(); }
            }
        }

        // 서비스 제어처럼 관리자 권한이 필요한 단계만 따로 권한 상승해서 실행
        public static bool RunElevated(Ctx c, string op)
        {
            if (IsAdmin)
            {
                if (op == "on") Ops.OnElevated(c);
                else if (op == "off") Ops.OffElevated(c);
                else Ops.RestoreElevated(c);
                return true;
            }

            c.Log("관리자 권한이 필요해요 — 뜨는 확인 창에서 '예'를 눌러 주세요");
            Directory.CreateDirectory(Store.Dir);
            string logPath = Path.Combine(Store.Dir, "elevated.log");
            File.WriteAllText(logPath, "");
            var psi = new ProcessStartInfo(ExePath,
                "--elevated " + op +
                " --settings \"" + Store.SettingsPath + "\"" +
                " --state \"" + c.StatePath + "\"" +
                " --log \"" + logPath + "\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (c.Owner != IntPtr.Zero)
            {
                // 창이 없는 프로세스(크롬 확장 호스트)는 확인 창이 작업 표시줄에서 깜박이기만 할 수 있어 크롬 창에 붙인다
                psi.ErrorDialog = true;
                psi.ErrorDialogParentHandle = c.Owner;
            }
            try
            {
                using (var p = Process.Start(psi)) p.WaitForExit();
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) throw;
                c.Fail("관리자 권한 요청을 취소해서 이 단계는 건너뛰었어요");
                return false;
            }
            foreach (string line in File.ReadAllLines(logPath, Encoding.UTF8)) c.Log(line);
            return true;
        }
    }
}
