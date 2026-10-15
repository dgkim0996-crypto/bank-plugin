using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace BankMode
{
    // 크롬 확장과 주고받는 Native Messaging 호스트
    // 메시지 = 4바이트 길이(리틀 엔디언) + UTF-8 JSON. 요청 {"cmd":"status|on|off","id":…}
    // 작업 중에는 {"type":"progress","line":…}을 보내고, 끝나면 {"type":"result",…} 하나로 답한다
    public static class NativeHost
    {
        public const string Name = "com.bankmode.host";
        // chrome-extension\manifest.json의 "key"에서 나온 개발용(압축 해제 로드) 확장 ID. 키를 바꾸면 이 값도 바꿔야 함
        public const string ExtensionId = "pmjnagnjdiidgplgfeckafeamdiflkbe";
        // 웹스토어에 공개하면 ID가 따로 배정된다. 그 ID를 여기 넣거나 extension-ids.txt에 적으면 그 확장도 허용된다
        public const string PublishedExtensionId = "";
        public static string Origin { get { return "chrome-extension://" + ExtensionId + "/"; } }

        const string RegPath = @"Software\Google\Chrome\NativeMessagingHosts\" + Name;
        static string ManifestPath { get { return Path.Combine(Store.Dir, Name + ".json"); } }
        // 공개 후 배정된 ID를 여기에 한 줄에 하나씩 적으면, 다시 빌드하지 않고도 허용 목록에 더할 수 있다
        static string ExtraIdsPath { get { return Path.Combine(Store.Dir, "extension-ids.txt"); } }

        // 확장 ID는 a~p 32글자
        static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length != 32) return false;
            foreach (char ch in id) if (ch < 'a' || ch > 'p') return false;
            return true;
        }

        // 허용할 확장들: 개발용 + 공개용 상수 + extension-ids.txt
        static List<string> AllowedOrigins()
        {
            var ids = new List<string> { ExtensionId };
            if (IsValidId(PublishedExtensionId)) ids.Add(PublishedExtensionId);
            try
            {
                if (File.Exists(ExtraIdsPath))
                    foreach (string raw in File.ReadAllLines(ExtraIdsPath))
                    {
                        string id = raw.Trim().ToLowerInvariant();
                        if (IsValidId(id)) ids.Add(id);
                    }
            }
            catch { }
            return ids.Distinct().Select(x => "chrome-extension://" + x + "/").ToList();
        }

        static readonly object writeLock = new object();

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        // 크롬이 BankMode.exe chrome-extension://…/ --parent-window=… 로 실행하거나, 직접 --native-host 로 실행
        public static int Run()
        {
            var input = Console.OpenStandardInput();
            var output = Console.OpenStandardOutput();
            var json = new JavaScriptSerializer();
            while (true)
            {
                byte[] body = ReadMessage(input);
                if (body == null) return 0;   // 크롬이 연결을 닫음

                object id = null;
                string cmd = "";
                Dictionary<string, object> resp;
                try
                {
                    var req = json.DeserializeObject(Encoding.UTF8.GetString(body)) as Dictionary<string, object>;
                    if (req != null)
                    {
                        req.TryGetValue("id", out id);
                        object v;
                        if (req.TryGetValue("cmd", out v)) cmd = (v as string ?? "").ToLowerInvariant();
                    }
                    object reqId = id;
                    resp = Handle(cmd, line => Write(output, new Dictionary<string, object> { { "type", "progress" }, { "id", reqId }, { "line", line } }));
                }
                catch (Exception ex)
                {
                    resp = new Dictionary<string, object> { { "ok", false }, { "error", "오류: " + ex.Message } };
                }
                resp["type"] = "result";
                resp["id"] = id;
                resp["cmd"] = cmd;
                if (!Write(output, resp)) return 0;
            }
        }

        static Dictionary<string, object> Handle(string cmd, Action<string> progress)
        {
            string err;
            var settings = Store.LoadSettings(Store.SettingsPath, out err);
            var log = new List<string>();
            var c = new Ctx
            {
                Settings = settings,
                StatePath = Store.StatePath,
                Log = line => { log.Add(line); progress(line); }
            };
            var resp = new Dictionary<string, object>();
            if (err != null) resp["warning"] = "설정 파일을 읽지 못해 기본값으로 동작해요: " + err;

            List<BankModule> mods;
            if (cmd == "status")
            {
                mods = Scanner.Discover(settings);
                resp["ok"] = true;
            }
            else if (cmd == "on")
            {
                c.Owner = ChromeWindow();
                bool done = Bank.TurnOn(c);
                if (done) Thread.Sleep(800);   // 막 띄운 프로그램이 목록에 잡힐 시간
                mods = Scanner.Discover(settings);
                var sum = Summary.Of(mods);
                resp["ok"] = done && c.Failures == 0;
                resp["message"] = !done ? "켜지 못했어요" :
                    c.Failures == 0 ? "켜기 완료 — " + sum.Running + "개 실행 중" : "켜기 완료 — 일부 항목은 실패했어요";
            }
            else if (cmd == "off")
            {
                c.Owner = ChromeWindow();
                long before = Summary.Of(Scanner.Discover(settings)).Memory;
                bool done = Bank.TurnOff(c);
                if (done) Thread.Sleep(500);
                mods = Scanner.Discover(settings);
                var sum = Summary.Of(mods);
                string msg = done ? "끄기 완료" : "끄지 못했어요";
                if (done && before > sum.Memory) msg += " — 메모리 약 " + Mb(before - sum.Memory) + " MB 확보";
                if (done && sum.Running > 0) msg += " (" + sum.Running + "개는 아직 실행 중: 자체 보호 기능 때문일 수 있어요)";
                resp["ok"] = done && c.Failures == 0;
                resp["message"] = msg;
            }
            else
            {
                resp["ok"] = false;
                resp["error"] = "알 수 없는 명령: " + cmd;
                return resp;
            }

            resp["status"] = StatusJson(mods);
            resp["sites"] = settings.Sites
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Url))
                .Select(s => new Dictionary<string, object> { { "name", s.Name ?? "" }, { "url", s.Url.Trim() } })
                .ToList();
            resp["log"] = log;
            resp["failures"] = log.Where(l => l.StartsWith("  !")).Select(l => l.Substring(3).Trim()).ToList();
            return resp;
        }

        static Dictionary<string, object> StatusJson(List<BankModule> mods)
        {
            var sum = Summary.Of(mods);
            return new Dictionary<string, object>
            {
                { "on", sum.On },
                { "running", sum.Running },
                { "managed", sum.Managed },
                { "found", sum.Found },
                { "bootAuto", sum.BootAuto },
                { "memoryMB", Mb(sum.Memory) },
                { "modules", mods.Select(m => new Dictionary<string, object>
                    {
                        { "name", m.Name },
                        { "managed", !m.Excluded },
                        { "running", m.IsRunning },
                        { "memoryMB", Mb(m.Memory) }
                    }).ToList() }
            };
        }

        static long Mb(long bytes) { return (long)Math.Round(bytes / 1048576.0); }

        // 사용자가 방금 확장 버튼을 누른 크롬 창. 관리자 확인 창을 그 앞에 띄우기 위함
        static IntPtr ChromeWindow()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = GetAncestor(h, 3 /* GA_ROOTOWNER */);
            return root != IntPtr.Zero ? root : h;
        }

        // 끝까지 다 읽지 못하면(연결 종료) null
        static byte[] ReadMessage(Stream s)
        {
            byte[] head = ReadExact(s, 4);
            if (head == null) return null;
            int len = BitConverter.ToInt32(head, 0);
            if (len < 0 || len > 1024 * 1024) return null;   // 이 호스트의 요청은 아주 작다. 이상한 길이는 연결 종료로 처리
            return ReadExact(s, len);
        }

        static byte[] ReadExact(Stream s, int n)
        {
            var buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, got, n - got);
                if (r <= 0) return null;
                got += r;
            }
            return buf;
        }

        static bool Write(Stream s, object msg)
        {
            byte[] body = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(msg));
            lock (writeLock)
            {
                try
                {
                    s.Write(BitConverter.GetBytes(body.Length), 0, 4);
                    s.Write(body, 0, body.Length);
                    s.Flush();
                    return true;
                }
                catch (IOException) { return false; }   // 크롬이 먼저 끊음 (팝업을 닫는 등)
            }
        }

        // ===================== 크롬에 호스트 등록 =====================

        // 확장 폴더: 소스 폴더(BankMode\chrome-extension) 또는 exe 옆
        public static string ExtensionFolder()
        {
            string exeDir = Path.GetDirectoryName(Bank.ExePath);
            foreach (string d in new[] { Path.Combine(exeDir, "chrome-extension"), Path.Combine(exeDir, "..", "chrome-extension") })
                if (File.Exists(Path.Combine(d, "manifest.json"))) return Path.GetFullPath(d);
            return null;
        }

        // 지금 실행 중인 exe를 가리키도록 등록돼 있으면 true
        public static bool IsRegistered()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RegPath))
            {
                string manifest = k == null ? null : k.GetValue(null) as string;
                if (string.IsNullOrEmpty(manifest) || !File.Exists(manifest)) return false;
                try
                {
                    var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(manifest, Encoding.UTF8)) as Dictionary<string, object>;
                    object path, origins;
                    if (d == null || !d.TryGetValue("path", out path) || !d.TryGetValue("allowed_origins", out origins)) return false;
                    var list = origins as object[];
                    return string.Equals(path as string, Bank.ExePath, StringComparison.OrdinalIgnoreCase) &&
                           list != null && list.Contains(Origin);
                }
                catch { return false; }
            }
        }

        // 호스트 manifest를 %APPDATA%\BankMode에 쓰고 HKCU 레지스트리에 위치를 적는다 (관리자 권한 필요 없음)
        public static void Register()
        {
            var manifest = new Dictionary<string, object>
            {
                { "name", Name },
                { "description", "BankMode - turn bank security programs on and off" },
                { "path", Bank.ExePath },
                { "type", "stdio" },
                { "allowed_origins", AllowedOrigins().ToArray() }
            };
            Directory.CreateDirectory(Store.Dir);
            File.WriteAllText(ManifestPath, new JavaScriptSerializer().Serialize(manifest), new UTF8Encoding(false));
            using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                k.SetValue(null, ManifestPath);
        }

        public static void Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegPath, false);
            if (File.Exists(ManifestPath)) File.Delete(ManifestPath);
        }
    }
}
