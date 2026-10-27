using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BankMode
{
    public sealed class UpdateInfo
    {
        public Version Current { get; internal set; }
        public Version Latest { get; internal set; }
        public string ReleaseUrl { get; internal set; }
    }

    public static class UpdateChecker
    {
        const string ApiUrl = "https://api.github.com/repos/dgkim0996-crypto/bank-plugin/releases/latest";
        static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        public static Task<UpdateInfo> CheckAsync()
        {
            return Task.Run(() => Check());
        }

        public static Version InstalledVersion
        {
            get { Version version; return TryReadCurrentVersion(out version) ? version : null; }
        }

        static UpdateInfo Check()
        {
            try
            {
                Version current;
                if (!TryReadCurrentVersion(out current)) return null;
                DateTime last;
                Version cachedLatest;
                string cachedUrl;
                if (TryReadCache(out last, out cachedLatest, out cachedUrl) && DateTime.UtcNow - last < CacheDuration)
                {
                    return cachedLatest != null && cachedLatest > current && ValidReleaseUrl(cachedUrl)
                        ? new UpdateInfo { Current = current, Latest = cachedLatest, ReleaseUrl = cachedUrl }
                        : null;
                }
                // Cache the attempt before networking too, so an offline PC does not
                // retry the public endpoint on every app launch.
                SaveCache(DateTime.UtcNow, null, null);
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                var request = (HttpWebRequest)WebRequest.Create(ApiUrl);
                request.Method = "GET";
                request.UserAgent = "BankMode-UpdateCheck";
                request.Accept = "application/vnd.github+json";
                request.Timeout = 3500;
                request.ReadWriteTimeout = 3500;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8)) json = reader.ReadToEnd();

                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object tagValue, urlValue;
                if (data == null || !data.TryGetValue("tag_name", out tagValue) || !data.TryGetValue("html_url", out urlValue)) return null;
                Version latest;
                string releaseUrl = Convert.ToString(urlValue);
                if (!Version.TryParse(Convert.ToString(tagValue).Trim().TrimStart('v', 'V'), out latest) || !ValidReleaseUrl(releaseUrl)) return null;
                Uri release = new Uri(releaseUrl);
                SaveCache(DateTime.UtcNow, latest, release.AbsoluteUri);
                return latest > current ? new UpdateInfo { Current = current, Latest = latest, ReleaseUrl = release.AbsoluteUri } : null;
            }
            catch { return null; }
        }

        static bool TryReadCurrentVersion(out Version version)
        {
            version = null;
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string[] paths =
            {
                Path.Combine(exeDir, "chrome-extension", "manifest.json"),
                Path.GetFullPath(Path.Combine(exeDir, "..", "chrome-extension", "manifest.json"))
            };
            foreach (string path in paths)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                    object value;
                    if (data != null && data.TryGetValue("version", out value) && Version.TryParse(Convert.ToString(value), out version)) return true;
                }
                catch { }
            }
            return false;
        }

        static string CachePath { get { return Path.Combine(Store.Dir, "update-check.txt"); } }

        static bool TryReadCache(out DateTime utc, out Version latest, out string url)
        {
            utc = DateTime.MinValue;
            latest = null;
            url = null;
            try
            {
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(CachePath, Encoding.UTF8));
                object checkedAt, version, releaseUrl;
                if (data == null || !data.TryGetValue("checkedAt", out checkedAt)) return false;
                if (!DateTime.TryParse(Convert.ToString(checkedAt), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out utc)) return false;
                if (data.TryGetValue("version", out version)) Version.TryParse(Convert.ToString(version), out latest);
                if (data.TryGetValue("url", out releaseUrl)) url = Convert.ToString(releaseUrl);
                return true;
            }
            catch { return false; }
        }

        static void SaveCache(DateTime utc, Version latest, string url)
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                var data = new Dictionary<string, object>
                {
                    { "checkedAt", utc.ToString("o") },
                    { "version", latest == null ? "" : latest.ToString() },
                    { "url", url ?? "" }
                };
                File.WriteAllText(CachePath, new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
            }
            catch { }
        }

        static bool ValidReleaseUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps &&
                string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
