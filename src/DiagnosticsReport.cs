using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BankMode
{
    /// <summary>
    /// Creates a user-reviewed, privacy-filtered text report for support requests.
    /// </summary>
    public static class DiagnosticsReport
    {
        const int MaxLogLines = 80;

        public static void Save(IWin32Window owner, Settings settings, string logText)
        {
            var consent = MessageBox.Show(owner,
                "진단 파일에는 앱/Windows 버전, 현재 탐지된 은행 보안 모듈의 이름과 실행 파일 경로, " +
                "Chrome 연결 등록 상태, 최근 앱 로그 일부가 포함됩니다.\r\n\r\n" +
                "사용자 폴더 경로는 %USERPROFILE%로 바꾸고, URL과 이메일 주소는 가립니다. " +
                "명령줄 인자, 계정 이름, 브라우저 방문 기록 및 사이트 권한 목록은 수집하지 않습니다.\r\n\r\n" +
                "저장 전에 파일을 직접 확인한 뒤 필요한 경우에만 지원 담당자에게 전달하세요. 계속할까요?",
                "진단 정보 내보내기", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            if (consent != DialogResult.Yes) return;

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "진단 정보 저장";
                dialog.Filter = "텍스트 파일 (*.txt)|*.txt";
                dialog.DefaultExt = "txt";
                dialog.AddExtension = true;
                dialog.FileName = "BankMode-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;

                try
                {
                    string report = CreateReport(settings, logText);
                    File.WriteAllText(dialog.FileName, report, new UTF8Encoding(true));
                    MessageBox.Show(owner, "진단 파일을 저장했어요. 전달하기 전에 파일 내용을 확인해 주세요.",
                        "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner,
                        "진단 파일을 저장하지 못했어요. 저장 위치의 권한과 디스크 여유 공간을 확인해 주세요.\r\n\r\n" +
                        Redact(ex.Message), "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        static string CreateReport(Settings settings, string logText)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BankMode 진단 정보");
            sb.AppendLine("생성 시각: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            sb.AppendLine("앱 버전: " + AppVersion());
            sb.AppendLine("Windows: " + Redact(Environment.OSVersion.VersionString) +
                (Environment.Is64BitOperatingSystem ? " (64비트)" : " (32비트)"));
            sb.AppendLine();

            sb.AppendLine("[연결 상태]");
            try
            {
                sb.AppendLine("Chrome Native Messaging 등록: " + (NativeHost.IsRegistered() ? "등록됨" : "등록되지 않음"));
                sb.AppendLine("Chrome 확장 폴더: " + (NativeHost.ExtensionFolder() == null ? "찾을 수 없음" : "찾음"));
            }
            catch
            {
                sb.AppendLine("Chrome 연결 등록 상태: 확인할 수 없음");
            }
            sb.AppendLine("브라우저별 사이트 권한: 개인정보 보호를 위해 읽거나 내보내지 않음");
            sb.AppendLine();

            sb.AppendLine("[현재 탐지 상태]");
            try
            {
                if (settings == null) throw new InvalidOperationException("설정을 읽을 수 없습니다.");
                List<BankModule> modules = Scanner.Discover(settings);
                Summary summary = Summary.Of(modules);
                sb.AppendLine("상태: " + (summary.Partial ? "일부 켜짐" : summary.On ? "켜짐" : "꺼짐"));
                sb.AppendLine("탐지 모듈: " + summary.Found + "개 / 관리 대상: " + summary.Managed + "개 / 실행 중: " + summary.Running + "개");
                sb.AppendLine("자동 시작 항목: " + summary.BootAuto + "개 / 메모리: " + Math.Round(summary.Memory / 1048576.0) + " MB");
                sb.AppendLine();
                sb.AppendLine("[모듈 및 실행 파일 경로]");
                if (modules.Count == 0) sb.AppendLine("탐지된 모듈 없음");
                foreach (BankModule module in modules)
                {
                    sb.AppendLine("- " + SafeText(module.Name) + " | " + (module.Excluded ? "제외됨" : module.IsRunning ? "실행 중" : "중지됨") +
                        (module.Installed ? " | 설치 흔적 있음" : ""));
                    var paths = module.Services.Select(s => s.ExePath)
                        .Concat(module.RunEntries.Select(r => r.ExePath))
                        .Concat(module.Processes.Select(p => p.FilePath))
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (paths.Count == 0) sb.AppendLine("    실행 파일 경로: 확인되지 않음");
                    else foreach (string path in paths) sb.AppendLine("    " + Redact(path));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("현재 상태를 읽지 못함: " + Redact(ex.Message));
            }

            sb.AppendLine();
            sb.AppendLine("[최근 앱 로그 일부]");
            List<string> lines = SafeLogLines(logText);
            if (lines.Count == 0) sb.AppendLine("포함할 로그 없음");
            else foreach (string line in lines) sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("참고: 이 보고서에는 사용자 폴더 경로를 가린 결과가 포함됩니다. 공유 전에 파일 전체를 확인하세요.");
            return sb.ToString();
        }

        static List<string> SafeLogLines(string logText)
        {
            if (string.IsNullOrEmpty(logText)) return new List<string>();
            return logText.Replace("\r", "").Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLastCompat(MaxLogLines)
                .Select(line => Redact(line.Trim()))
                .ToList();
        }

        // Avoid newer LINQ APIs because the desktop application targets older .NET Framework.
        static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> source, int count)
        {
            var queue = new Queue<T>();
            foreach (T item in source)
            {
                queue.Enqueue(item);
                while (queue.Count > count) queue.Dequeue();
            }
            return queue;
        }

        static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            string text = value;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
            {
                text = Regex.Replace(text, Regex.Escape(profile), "%USERPROFILE%", RegexOptions.IgnoreCase);
            }
            string userName = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(userName))
                text = Regex.Replace(text, Regex.Escape(userName), "[계정 가림]", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[이메일 가림]", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"https?://\S+", "[주소 가림]", RegexOptions.IgnoreCase);
            return text;
        }

        static string SafeText(string value)
        {
            return Redact((value ?? "알 수 없음").Replace('\r', ' ').Replace('\n', ' '));
        }

        static string AppVersion()
        {
            try
            {
                Version version = UpdateChecker.InstalledVersion ?? Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "알 수 없음" : version.ToString();
            }
            catch { return "알 수 없음"; }
        }
    }
}
