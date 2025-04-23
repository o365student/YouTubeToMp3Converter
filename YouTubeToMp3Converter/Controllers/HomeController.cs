using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace YouTubeToMp3Converter.Controllers
{
    public class HomeController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<HomeController> _logger;

        public HomeController(IWebHostEnvironment env, ILogger<HomeController> logger)
        {
            _env = env;
            _logger = logger;
        }

        // GET /
        public IActionResult Index() => View();

        // POST /Home/Download  表單送來時執行
        [HttpPost]
        public async Task<IActionResult> Download(string url, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                ModelState.AddModelError(string.Empty, "URL 不可為空");
                return View("Index");                     // 直接回到同一頁並顯示錯誤
            }

            string finalPath = null!;

            try
            {
                await foreach (var prog in RunYtDlpAndFfmpegAsync(url, ct))
                {
                    // 你可以改用 SignalR / SSE 把進度推給前端
                    _logger.LogInformation("[{Percent,6:P0}] {Text}", prog.Percent, prog.Text);
                }

                finalPath = progTempMp3Path;              // 見下方方法
                var bytes = System.IO.File.ReadAllBytes(finalPath);
                return File(bytes, "audio/mpeg", "download.mp3");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下載或轉檔失敗");
                ModelState.AddModelError(string.Empty, "下載／轉檔失敗：" + ex.Message);
                return View("Index");
            }
            finally
            {
                if (finalPath != null && System.IO.File.Exists(finalPath))
                    System.IO.File.Delete(finalPath);     // 下載完就刪
            }
        }

        // ----------------------- 私有方法 -----------------------------

        private string progTempMp3Path = string.Empty;     // 暫存最終 MP3 路徑

        private async IAsyncEnumerable<ProgressInfo> RunYtDlpAndFfmpegAsync(
            string youtubeUrl,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var rawPath = Path.Combine(tempDir, "raw.webm");
            var mp3Path = Path.Combine(tempDir, "audio.mp3");
            var ytDlpExe = Path.Combine(_env.ContentRootPath, "tools", "yt-dlp.exe");
            var ffmpegExe = Path.Combine(_env.ContentRootPath, "tools", "ffmpeg.exe");

            try
            {
                // 1. 下載
                var ytArgs = $"-f bestaudio -o \"{rawPath}\" {youtubeUrl}";
                await foreach (var p in RunProcessAsync(ytDlpExe, ytArgs, "yt-dlp", ct))
                    yield return p;

                // 2. 轉 MP3
                var ffArgs =
                    $"-y -i \"{rawPath}\" -c:a libmp3lame -b:a 128k -progress pipe:1 \"{mp3Path}\"";
                await foreach (var p in RunProcessAsync(ffmpegExe, ffArgs, "ffmpeg", ct))
                    yield return p;

                // 3. 複製到系統 Temp 供回傳
                progTempMp3Path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
                System.IO.File.Copy(mp3Path, progTempMp3Path, true);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        private async IAsyncEnumerable<ProgressInfo> RunProcessAsync(
            string fileName,
            string args,
            string tag,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var buffer = new ConcurrentQueue<string>();

            // 讀 stdout
            _ = Task.Run(async () =>
            {
                while (!proc.HasExited && !ct.IsCancellationRequested)
                {
                    var line = await proc.StandardOutput.ReadLineAsync(ct);
                    if (line != null) buffer.Enqueue(line);
                }
            }, ct);

            // 讀 stderr
            _ = Task.Run(async () =>
            {
                while (!proc.HasExited && !ct.IsCancellationRequested)
                {
                    var line = await proc.StandardError.ReadLineAsync(ct);
                    if (line != null) buffer.Enqueue(line);
                }
            }, ct);

            int lastPct = 0;
            while (!proc.HasExited || !buffer.IsEmpty)
            {
                while (buffer.TryDequeue(out var line))
                {
                    lastPct = ParsePercent(line, lastPct);
                    yield return new ProgressInfo(tag, lastPct / 100d, line);
                }
                await Task.Delay(100, ct);
            }

            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"{tag} 失敗 (ExitCode={proc.ExitCode})");
        }

        private static int ParsePercent(string line, int fallback)
        {
            // yt-dlp： "[download]  42.8% ..."
            var m = System.Text.RegularExpressions.Regex.Match(line, @"([\d\.]+)%");
            if (m.Success && double.TryParse(m.Groups[1].Value, out var d))
                return (int)d;

            // ffmpeg：progress=end
            if (line.StartsWith("progress=") && line.EndsWith("end"))
                return 100;

            return fallback;
        }

        private record ProgressInfo(string Source, double Percent, string Text);
    }
}
