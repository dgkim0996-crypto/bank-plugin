using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BankMode
{
    static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr h, int cmd);

        // BankMode.exe            창 열기
        // BankMode.exe --on       창을 열고 바로 켜기 (바탕화면 바로가기용)
        // BankMode.exe --scan 파일  탐색 결과만 파일로 저장 (아무것도 바꾸지 않음)
        // BankMode.exe --native-host  크롬 확장용 Native Messaging 호스트 (크롬은 chrome-extension://…/ 를 첫 인자로 실행)
        [STAThread]
        static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (cmd == "--elevated") return Elevated(args);
            if (cmd == "--uninstall") return Uninstall(args);
            if (cmd == "--register-host") return RegisterHost();
            if (cmd == "--scan") return Scan(args.Length > 1 ? args[1] : Path.Combine(Store.Dir, "scan.txt"));
            if (cmd == "--native-host" || cmd.StartsWith("chrome-extension://")) return NativeHost.Run();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (cmd == "--snapshot")
            {
                Application.Run(new MainForm(false, args.Length > 1 ? args[1] : "snapshot.png"));
                return 0;
            }

            bool created;
            using (new Mutex(true, @"Local\BankMode.Main", out created))
            {
                if (!created)
                {
                    IntPtr h = FindWindow(null, MainForm.WindowTitle);
                    if (h != IntPtr.Zero)
                    {
                        ShowWindow(h, 9 /* SW_RESTORE */);
                        SetForegroundWindow(h);
                    }
                    return 0;
                }
                Application.Run(new MainForm(cmd == "--on", null));
            }
            return 0;
        }

        static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        // 관리자 권한으로 다시 실행된 쪽: 결과를 로그 파일에 남기고 끝난다
        static int Elevated(string[] args)
        {
            string op = args.Length > 1 ? args[1] : "";
            string logPath = Arg(args, "--log") ?? Path.Combine(Store.Dir, "elevated.log");
            using (var w = new StreamWriter(logPath, true, new UTF8Encoding(false)))
            {
                w.AutoFlush = true;
                string err;
                var c = new Ctx
                {
                    Settings = Store.LoadSettings(Arg(args, "--settings") ?? Store.SettingsPath, out err),
                    StatePath = Arg(args, "--state") ?? Store.StatePath,
                    Log = line => w.WriteLine(line)
                };
                try
                {
                    if (op == "on") Ops.OnElevated(c);
                    else if (op == "off") Ops.OffElevated(c);
                    else if (op == "restore") Ops.RestoreElevated(c);
                    else c.Fail("알 수 없는 작업: " + op);
                }
                catch (Exception ex) { c.Fail("오류: " + ex.Message); }
                return c.Failures == 0 ? 0 : 1;
            }
        }

        // 제거 프로그램이 부르는 모드: 은행 모드가 바꿔 둔 자동 시작 설정을 원래대로 되돌리고
        // 크롬 호스트 등록을 지운다. 되돌릴 서비스가 있으면 관리자 확인 창이 한 번 뜬다.
        // 제거 자체를 막지 않도록 무슨 일이 있어도 0을 돌려준다.
        static int Uninstall(string[] args)
        {
            string logPath = Arg(args, "--log") ?? Path.Combine(Store.Dir, "uninstall.log");
            try
            {
                Directory.CreateDirectory(Store.Dir);
                using (var w = new StreamWriter(logPath, false, new UTF8Encoding(false)))
                {
                    w.AutoFlush = true;
                    string err;
                    var c = new Ctx
                    {
                        Settings = Store.LoadSettings(Store.SettingsPath, out err),
                        StatePath = Store.StatePath,
                        Log = line => w.WriteLine(line)
                    };
                    c.Log("제거: 보안 프로그램의 자동 시작 설정을 원래대로 되돌립니다");
                    try { Bank.Restore(c); }
                    catch (Exception ex) { c.Fail("되돌리기 오류: " + ex.Message); }
                    try { NativeHost.Unregister(); c.Log("크롬 확장 연결을 해제했어요"); }
                    catch (Exception ex) { c.Fail("크롬 연결 해제 오류: " + ex.Message); }
                    c.Log(c.Failures == 0 ? "완료" : "일부 항목은 되돌리지 못했어요 (위 ! 참고)");
                }
            }
            catch { }
            return 0;
        }

        // 설치 프로그램이 부르는 모드: 크롬 확장이 이 앱을 찾을 수 있게 등록 (내 계정 레지스트리라 관리자 권한 불필요)
        static int RegisterHost()
        {
            try { NativeHost.Register(); return 0; }
            catch { return 1; }
        }

        static int Scan(string outPath)
        {
            string err;
            var st = Store.LoadSettings(Store.SettingsPath, out err);
            var sb = new StringBuilder();
            if (err != null) sb.AppendLine("설정 파일 오류: " + err);

            var sw = Stopwatch.StartNew();
            var mods = Scanner.Discover(st);
            long full = sw.ElapsedMilliseconds;
            sw.Restart();
            Scanner.RefreshStatus(st, mods);
            sb.AppendLine("전체 탐색 " + full + " ms / 상태 갱신 " + sw.ElapsedMilliseconds + " ms");
            sb.AppendLine("켜기에 관리자 권한 필요: " + Ops.OnNeedsAdmin(mods) + " / 끄기에 필요: " + Ops.OffNeedsAdmin(mods));
            sb.AppendLine();

            foreach (var m in mods)
            {
                sb.AppendLine("[" + m.Name + "] " + (m.Excluded ? "관리 안 함" : "관리") + " · " + (m.IsRunning ? "실행 중" : "꺼짐") +
                              " · " + (m.Memory / 1048576) + " MB · 부팅 시 " + (m.AutoStarts ? "자동" : "꺼둠") + (m.Installed ? " · 설치 목록에 있음" : ""));
                foreach (var s in m.Services)
                    sb.AppendLine("  서비스   " + s.Name + " | " + s.DisplayName + " | 시작유형 " + s.StartType + (s.Delayed ? "(지연)" : "") + " | " + (s.Running ? "실행 중" : "중지") + " | " + s.ExePath);
                foreach (var r in m.RunEntries)
                    sb.AppendLine("  시작     " + r.Location + " | " + r.Name + " | " + (r.Enabled ? "켜짐" : "꺼둠") + " | exe=" + r.ExePath + " | args=" + Scanner.ExtractArgs(r.Command));
                foreach (var p in m.Processes)
                    sb.AppendLine("  프로세스 " + p.Name + " (" + p.Pid + ") " + (p.Memory / 1048576) + " MB | " + p.FilePath);
            }
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
            return 0;
        }
    }
}
