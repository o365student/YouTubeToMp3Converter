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

        // 下載→50%，轉檔→100%
        private const double DL_RATIO = 0.50;
        private const double ENC_RATIO = 0.50;

        private static readonly ConcurrentDictionary<string, DownloadJob> _jobs = new();

        public HomeController(IWebHostEnvironment env, ILogger<HomeController> log)
        {
            _env = env;
            _log = log;
        }

        // 首頁
        public IActionResult Index() => View();

        /*──────────────────────── 1. 觸發下載 ────────────────────────*/
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

            _ = Task.Run(() => RunJobAsync(job, url), ct);   // 背景工作
            return Json(new { jobId = job.Id, title });
        }

        /*──────────────────────── 2. SSE 進度 ────────────────────────*/
        public async Task Progress(string jobId, CancellationToken ct)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
                return;

            Response.Headers.Add("Content-Type", "text/event-stream");
            while (!ct.IsCancellationRequested && !job.Completed && job.Error == null)
            {
                await Response.WriteAsync($"data: {job.Percent:F1}\n\n", ct);
                await Response.Body.FlushAsync(ct);
                await Task.Delay(500, ct);
            }

            if (job.Error != null)
                await Response.WriteAsync($"event: error\ndata: {job.Error}\n\n", ct);
            else
                await Response.WriteAsync($"event: complete\ndata: {job.Id}\n\n", ct);

            await Response.Body.FlushAsync(ct);
        }

        /*──────────────────────── 3. 下載檔案 ────────────────────────*/
        public IActionResult File(string jobId)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || !System.IO.File.Exists(job.FilePath))
                return NotFound();

            var safeName = $"{SanitizeFileName(job.Title)}.mp3";
            var bytes = System.IO.File.ReadAllBytes(job.FilePath);

            System.IO.File.Delete(job.FilePath);   // 用完即刪
            _jobs.TryRemove(jobId, out _);

            return File(bytes, "audio/mpeg", safeName);
        }

        /*──────────────────────── 私有區域 ────────────────────────*/
        private async Task<string> GetTitleAsync(string url, CancellationToken ct)
        {
            var yt = Path.Combine(_env.ContentRootPath, "tools", "yt-dlp.exe");
            var psi = new ProcessStartInfo
            {
                FileName = yt,
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

        private async Task RunJobAsync(DownloadJob job, string url)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), job.Id);
                Directory.CreateDirectory(tempDir);

                var raw = Path.Combine(tempDir, "raw.webm");
                var mp3 = Path.Combine(tempDir, "audio.mp3");
                var yt = Path.Combine(_env.ContentRootPath, "tools", "yt-dlp.exe");
                var ff = Path.Combine(_env.ContentRootPath, "tools", "ffmpeg.exe");

                /*─── 下載 (0~50%) ───*/
                await RunProcessAsync(yt, $"-f bestaudio -o \"{raw}\" {url}", pct =>
                {
                    var overall = Math.Max(job.Percent, pct * DL_RATIO);
                    job.Percent = overall;
                });

                /*─── 轉檔 (50~100%) ───*/
                await RunProcessAsync(ff,
                    $"-y -i \"{raw}\" -c:a libmp3lame -b:a 128k -progress pipe:1 \"{mp3}\"",
                    pct =>
                    {
                        var overall = Math.Max(job.Percent, 50 + pct * ENC_RATIO);
                        job.Percent = overall;
                    });

                job.FilePath = mp3;
                job.Percent = 100;
                job.Completed = true;
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                _log.LogError(ex, "Job {Id} 失敗", job.Id);
            }
        }

        private async Task RunProcessAsync(
            string file, string args, Action<double> report)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var last = 0.0;

            double ClampMonotonic(double pct)
            {
                pct = Math.Clamp(pct, 0, 100);
                if (pct < last) pct = last;      // 不倒退
                last = pct;
                return pct;
            }

            async Task ReadAsync(StreamReader sr)
            {
                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    /* yt-dlp → [download]   42.8% */
                    var m = Regex.Match(line, @"\s(\d{1,3}\.\d)%");
                    if (m.Success && double.TryParse(m.Groups[1].Value, out var pct1))
                        report(ClampMonotonic(pct1));

                    /* ffmpeg → out_time_ms=... (這裡示意以百分比字串 progress=42.5 解析) */
                    var m2 = Regex.Match(line, @"progress=(\d{1,3}\.\d+)");
                    if (m2.Success && double.TryParse(m2.Groups[1].Value, out var pct2))
                        report(ClampMonotonic(pct2));
                }
            }

            // 平行讀 stdout / stderr
            await Task.WhenAll(ReadAsync(proc.StandardOutput), ReadAsync(proc.StandardError));
            await proc.WaitForExitAsync();

            report(ClampMonotonic(100));   // 保證結束 = 100%
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"{file} 失敗 (ExitCode={proc.ExitCode})");
        }

        private static string SanitizeFileName(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            return string.Concat(name.Where(c => !bad.Contains(c)));
        }
    }
}
