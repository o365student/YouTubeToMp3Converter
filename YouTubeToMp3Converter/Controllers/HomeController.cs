using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using YouTubeToMp3Converter.Models;

namespace YouTubeToMp3Converter.Controllers
{
    public class HomeController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<HomeController> _log;

        private const double DL_WEIGHT = 0.50;   // 下載佔比
        private const double ENC_WEIGHT = 0.50;   // 轉檔佔比
        private const double ENC_MAX = 99.9;   // 轉檔階段最高顯示 99.9%

        private static readonly ConcurrentDictionary<string, DownloadJob> _jobs = new();

        public HomeController(IWebHostEnvironment env, ILogger<HomeController> log)
        {
            _env = env;
            _log = log;
        }

        /*───────────────── 首頁 ─────────────────*/
        public IActionResult Index() => View();

        /*────────────── 1. 開始下載 ──────────────*/
        [HttpPost]
        public async Task<IActionResult> Start([FromForm] string url, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
                return BadRequest("URL 不可為空");

            var title = await GetTitleAsync(url, ct);
            if (string.IsNullOrEmpty(title))
                return BadRequest("無法取得影片標題");

            var job = new DownloadJob { Title = title };
            _jobs[job.Id] = job;

            _ = Task.Run(() => RunJobAsync(job, url), ct);   // 背景執行
            return Json(new { jobId = job.Id, title });
        }

        /*────────────── 2. SSE 進度 ──────────────*/
        public async Task Progress(string jobId, CancellationToken ct)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
                return;

            Response.Headers.Add("Content-Type", "text/event-stream");

            while (!ct.IsCancellationRequested && !job.Completed && job.Error == null)
            {
                await Response.WriteAsync($"data: {job.Percent:F1}\n\n", ct);
                await Response.Body.FlushAsync(ct);
                await Task.Delay(400, ct);
            }

            if (job.Error != null)
                await Response.WriteAsync($"event: error\ndata: {job.Error}\n\n", ct);
            else
                await Response.WriteAsync($"event: complete\ndata: {job.Id}\n\n", ct);

            await Response.Body.FlushAsync(ct);
        }

        /*────────────── 3. 下載檔案 ──────────────*/
        public IActionResult File(string jobId)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || !System.IO.File.Exists(job.FilePath))
                return NotFound();

            var safeName = $"{SanitizeFileName(job.Title)}.mp3";
            var fs = System.IO.File.OpenRead(job.FilePath);

            // 用完即刪
            Response.OnCompleted(() =>
            {
                fs.Dispose();
                System.IO.File.Delete(job.FilePath);
                _jobs.TryRemove(jobId, out _);
                return Task.CompletedTask;
            });

            return File(fs, "audio/mpeg", safeName, enableRangeProcessing: true);
        }

        /*───────────────── 私有區域 ─────────────────*/

        /* 1) 抓影片標題 */
        private async Task<string> GetTitleAsync(string url, CancellationToken ct)
        {
            var ytdlp = Path.Combine(_env.ContentRootPath, "tools", "yt-dlp.exe");
            var psi = new ProcessStartInfo
            {
                FileName = ytdlp,
                Arguments = $"--print \"%(title)s\" {url}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var title = await p.StandardOutput.ReadLineAsync(ct) ?? "";
            await p.WaitForExitAsync(ct);
            return title.Trim();
        }

        /* 2) ffprobe 取秒數 */
        private async Task<double> GetDurationAsync(string file)
        {
            var ffprobe = Path.Combine(_env.ContentRootPath, "tools", "ffprobe.exe");
            var psi = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{file}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var str = await p.StandardOutput.ReadLineAsync() ?? "0";
            await p.WaitForExitAsync();
            return double.TryParse(str, out var s) ? s : 0;
        }

        /* 3) 背景工作 */
        private async Task RunJobAsync(DownloadJob job, string url)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), job.Id);
                Directory.CreateDirectory(tempDir);

                var raw = Path.Combine(tempDir, "raw.webm");
                var mp3 = Path.Combine(tempDir, "audio.mp3");
                var ytdl = Path.Combine(_env.ContentRootPath, "tools", "yt-dlp.exe");
                var ff = Path.Combine(_env.ContentRootPath, "tools", "ffmpeg.exe");

                /*─── (A) 下載 0~50% ───*/
                await RunProcessAsync(
                    exe: ytdl,
                    args: $"-f bestaudio -o \"{raw}\" {url}",
                    progress: pct => job.Percent = Math.Max(job.Percent, pct * DL_WEIGHT)
                );

                /*─── (B) 轉檔 50~100% ───*/
                var duration = await GetDurationAsync(raw);
                if (duration <= 0) duration = 1;          // 避免除 0

                await RunProcessAsync(
                    exe: ff,
                    args: $"-y -i \"{raw}\" -c:a libmp3lame -b:a 128k -progress pipe:1 -nostats -loglevel error \"{mp3}\"",
                    progress: pct =>
                    {
                        // pct 0~100 → 50~100，最高只到 99.9
                        var overall = 50 + Math.Min(pct, ENC_MAX) * ENC_WEIGHT;
                        job.Percent = Math.Max(job.Percent, overall);
                    },
                    durationSec: duration
                );

                job.FilePath = mp3;
                job.Percent = 100;   // 最後一次回報 100
                job.Completed = true;
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                _log.LogError(ex, "Job {Id} 失敗", job.Id);
            }
        }

        /* 4) 共用執行程式並回報進度 */
        private async Task RunProcessAsync(
            string exe,
            string args,
            Action<double> progress,
            double durationSec = 0)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            double last = 0;
            double Clamp(double p) { p = Math.Clamp(p, 0, 100); last = Math.Max(last, p); return last; }

            async Task ParseAsync(StreamReader sr)
            {
                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    // yt-dlp：[download]  34.6%
                    var m1 = Regex.Match(line, @"\s(\d{1,3}\.\d)%");
                    if (m1.Success && double.TryParse(m1.Groups[1].Value, out var p1))
                    {
                        progress(Clamp(p1));
                        continue;
                    }

                    // ffmpeg：out_time_ms=123456789
                    if (durationSec > 0 &&
                        line.StartsWith("out_time_ms=") &&
                        long.TryParse(line["out_time_ms=".Length..], out var us))
                    {
                        var pct = us / 1_000_000.0 / durationSec * 100.0;
                        progress(Clamp(pct));
                    }
                }
            }

            await Task.WhenAll(ParseAsync(proc.StandardOutput), ParseAsync(proc.StandardError));
            await proc.WaitForExitAsync();

            progress(Clamp(100));   // 最終 100%
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"{exe} 失敗 (ExitCode={proc.ExitCode})");
        }

        /* 5) 檔名過濾 */
        private static string SanitizeFileName(string name)
            => string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
    }
}
