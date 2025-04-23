using Microsoft.AspNetCore.Mvc;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.Diagnostics;

namespace YouTubeToMp3Converter.Controllers
{
    public class HomeController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public HomeController(IWebHostEnvironment env)
        {
            _env = env;
        }

        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public IActionResult GetProgress()
        {
            string progressFilePath = Path.Combine(_env.WebRootPath, "downloads", "progress.txt");
            if (System.IO.File.Exists(progressFilePath))
            {
                try
                {
                    using (var fs = new FileStream(progressFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.StartsWith("progress="))
                            {
                                var progressValue = line.Split('=')[1];
                                return Json(new { progress = progressValue });
                            }
                        }
                    }
                }
                catch (IOException)
                {
                    // 如果檔案仍被鎖定，返回預設進度
                    return Json(new { progress = "0" });
                }
            }
            return Json(new { progress = "0" });
        }


        [HttpPost]
        public async Task<IActionResult> Download(string videoUrl)
        {
            if (string.IsNullOrWhiteSpace(videoUrl))
            {
                ViewBag.Message = "請輸入有效的 YouTube 影片網址。";
                return View("Index");
            }

            try
            {
                var youtube = new YoutubeClient();

                // 取得影片資訊
                var video = await youtube.Videos.GetAsync(videoUrl);
                var title = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));

                // 取得音訊串流
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
                var audioStreamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (audioStreamInfo == null)
                {
                    ViewBag.Message = "無法取得音訊串流。";
                    return View("Index");
                }

                // 設定檔案路徑
                var downloadsPath = Path.Combine(_env.WebRootPath, "downloads");
                if (!Directory.Exists(downloadsPath))
                    Directory.CreateDirectory(downloadsPath);

                var tempFilePath = Path.Combine(downloadsPath, $"{title}.{audioStreamInfo.Container.Name}");
                var outputFilePath = Path.Combine(downloadsPath, $"{title}.mp3");

                // 下載音訊串流
                await youtube.Videos.Streams.DownloadAsync(audioStreamInfo, tempFilePath);

                // 使用 FFmpeg 轉換為 MP3
                var ffmpegPath = Path.Combine(_env.ContentRootPath, "ffmpeg", "ffmpeg.exe");
                var progressFilePath = Path.Combine(_env.WebRootPath, "downloads", "progress.txt");

                var ffmpegArgs = $"-i \"{tempFilePath}\" -c:a libmp3lame -b:a 128k -preset ultrafast -threads 4 \"{outputFilePath}\"";


                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = ffmpegArgs,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                // 刪除暫存檔案
                if (System.IO.File.Exists(tempFilePath))
                    System.IO.File.Delete(tempFilePath);

                // 提供下載連結
                var downloadUrl = $"/downloads/{title}.mp3";
                ViewBag.DownloadUrl = downloadUrl;
                ViewBag.Message = "轉換成功！請點擊以下連結下載 MP3 檔案。";
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"發生錯誤：{ex.Message}";
            }

            return View("Index");
        }
    }
}
