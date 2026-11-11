using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace IRRCompanion
{
    /// <summary>A newer release found on GitHub.</summary>
    public sealed class UpdateInfo
    {
        public Version Version;
        public string Tag, Notes, PageUrl, ExeUrl, Sha256;
        public long Size;
    }

    /// <summary>
    /// Self-update from the GitHub releases: reads the latest release, downloads its IRRCompanion.exe next to the
    /// running exe, checks its size and SHA-256 (given by GitHub), then swaps the files when the app closes and
    /// starts the new version. A running exe can be renamed but not overwritten: it becomes IRRCompanion.exe.old,
    /// deleted by the new version at start.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "mordeci/irr-companion";
        private const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        private const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

        /// <summary>Version of this exe (major.minor.build).</summary>
        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static string CurrentText { get { return "v" + Current.ToString(3); } }

        private static string ExePath { get { return Application.ExecutablePath; } }
        private static string NewPath { get { return ExePath + ".new"; } }
        private static string OldPath { get { return ExePath + ".old"; } }

        /// <summary>Downloaded and verified update, installed when the app closes.</summary>
        public static UpdateInfo Ready;

        private static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "IRRCompanion/" + Current.ToString(3);
            r.Timeout = 15000;
            r.ReadWriteTimeout = 30000;
            return r;
        }

        /// <summary>Latest release if it is newer than this exe, else null. Throws on network errors.</summary>
        public static UpdateInfo Check()
        {
            var req = Request(LatestApi);
            req.Accept = "application/vnd.github+json";
            string text;
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                text = r.ReadToEnd();
            var root = Json.Parse(text) as JObj;
            if (root == null) return null;
            string tag = root.Str("tag_name") ?? "";
            Version v;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out v)) return null;
            v = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            if (v <= Current) return null;
            var info = new UpdateInfo
            {
                Version = v, Tag = tag, Notes = PlainNotes(root.Str("body") ?? ""),
                PageUrl = root.Str("html_url") ?? ReleasesPage,
            };
            foreach (var x in root.Arr("assets") ?? new System.Collections.Generic.List<object>())
            {
                var a = x as JObj;
                if (a == null || !string.Equals(a.Str("name"), "IRRCompanion.exe", StringComparison.OrdinalIgnoreCase)) continue;
                string url = a.Str("browser_download_url") ?? "";
                if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;  // only this repository's releases
                info.ExeUrl = url;
                info.Size = (long)a.Num("size");
                string digest = a.Str("digest") ?? "";
                if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.Sha256 = digest.Substring(7).ToLowerInvariant();
            }
            return info;
        }

        /// <summary>Release notes as plain text: the hand-written part (before the "---" line of the download
        /// instructions), without Markdown marks, bullets as •, wrapped lines joined.</summary>
        public static string PlainNotes(string md)
        {
            var lines = md.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var result = new System.Collections.Generic.List<string>();
            foreach (var raw in lines)
            {
                if (raw.Trim() == "---") break;
                string line = raw.Replace("**", "").Replace("`", "");
                if (line.StartsWith("  ") && line.Trim().Length > 0 && result.Count > 0 && result[result.Count - 1].Length > 0)
                {
                    result[result.Count - 1] += " " + line.Trim();  // continuation of the previous line
                    continue;
                }
                if (line.StartsWith("- ")) line = "•  " + line.Substring(2);
                result.Add(line.TrimEnd());
            }
            return string.Join("\r\n", result).Trim();
        }

        /// <summary>True when the folder of the exe can be written (not the case in Program Files).</summary>
        public static bool CanInstall()
        {
            try
            {
                string probe = Path.Combine(Path.GetDirectoryName(ExePath), ".irr-update-probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Downloads the new exe next to this one and checks it. progress: 0..1. Throws with a readable message.</summary>
        public static void Download(UpdateInfo info, Action<double> progress, CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(info.ExeUrl)) throw new InvalidOperationException("This release has no IRRCompanion.exe to download.");
            if (string.IsNullOrEmpty(info.Sha256)) throw new InvalidOperationException("GitHub gave no checksum for this download: update by hand from the release page.");
            string tmp = NewPath + ".part";
            try
            {
                using (var resp = Request(info.ExeUrl).GetResponse())
                using (var input = resp.GetResponseStream())
                using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var sha = SHA256.Create())
                {
                    var buf = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        output.Write(buf, 0, n);
                        sha.TransformBlock(buf, 0, n, null, 0);
                        done += n;
                        if (info.Size > 0) progress(Math.Min(1, done / (double)info.Size));
                    }
                    sha.TransformFinalBlock(buf, 0, 0);
                    if (info.Size > 0 && done != info.Size)
                        throw new IOException("The download is incomplete (" + done + " of " + info.Size + " bytes).");
                    string hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                    if (hash != info.Sha256)
                        throw new IOException("The downloaded file does not match the release (SHA-256 check failed).");
                }
                if (File.Exists(NewPath)) File.Delete(NewPath);
                File.Move(tmp, NewPath);
                Ready = info;
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                throw;
            }
        }

        /// <summary>Called after the window closed: swaps the exe files and starts the new version.</summary>
        public static void InstallIfReady()
        {
            if (Ready == null || !File.Exists(NewPath)) return;
            try
            {
                if (File.Exists(OldPath)) File.Delete(OldPath);
                File.Move(ExePath, OldPath);
                try { File.Move(NewPath, ExePath); }
                catch { File.Move(OldPath, ExePath); throw; }
                Process.Start(new ProcessStartInfo(ExePath, "--updated " + Current.ToString(3)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExePath) });
            }
            catch (Exception ex)
            {
                MessageBox.Show("The update could not be installed:\n" + ex.Message + "\n\nDownload it from " + Ready.PageUrl,
                    "IRR Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>At start: deletes the previous exe (.old) and a leftover download. The old process may still be
        /// closing for a moment, so it retries in the background.</summary>
        public static void CleanUp()
        {
            var t = new Thread(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    try
                    {
                        if (File.Exists(OldPath)) File.Delete(OldPath);
                        if (File.Exists(NewPath + ".part")) File.Delete(NewPath + ".part");
                        if (Ready == null && File.Exists(NewPath)) File.Delete(NewPath);
                        return;
                    }
                    catch (Exception) { Thread.Sleep(500); }
                }
            }) { IsBackground = true };
            t.Start();
        }

        /// <summary>Version string given by the previous exe after an update (--updated x.y.z), else null.</summary>
        public static string UpdatedFrom(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--updated") return args[i + 1];
            return null;
        }

        public static string Format(Version v) { return "v" + v.ToString(3); }

        public static string Today() { return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    }
}
