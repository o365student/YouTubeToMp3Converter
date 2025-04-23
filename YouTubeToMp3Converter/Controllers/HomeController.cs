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
                    // �p�G�ɮפ��Q��w�A��^�w�]�i��
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
                ViewBag.Message = "�п�J���Ī� YouTube �v�����}�C";
                return View("Index");
            }

            try
            {
                var youtube = new YoutubeClient();

                // ���o�v����T
                var video = await youtube.Videos.GetAsync(videoUrl);
                var title = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));

                // ���o���T��y
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
                var audioStreamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (audioStreamInfo == null)
                {
                    ViewBag.Message = "�L�k���o���T��y�C";
                    return View("Index");
                }

                // �]�w�ɮ׸��|
                var downloadsPath = Path.Combine(_env.WebRootPath, "downloads");
                if (!Directory.Exists(downloadsPath))
                    Directory.CreateDirectory(downloadsPath);

                var tempFilePath = Path.Combine(downloadsPath, $"{title}.{audioStreamInfo.Container.Name}");
                var outputFilePath = Path.Combine(downloadsPath, $"{title}.mp3");

                // �U�����T��y
                await youtube.Videos.Streams.DownloadAsync(audioStreamInfo, tempFilePath);

                // �ϥ� FFmpeg �ഫ�� MP3
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

                // �R���Ȧs�ɮ�
                if (System.IO.File.Exists(tempFilePath))
                    System.IO.File.Delete(tempFilePath);

                // ���ѤU���s��
                var downloadUrl = $"/downloads/{title}.mp3";
                ViewBag.DownloadUrl = downloadUrl;
                ViewBag.Message = "�ഫ���\�I���I���H�U�s���U�� MP3 �ɮסC";
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"�o�Ϳ��~�G{ex.Message}";
            }

            return View("Index");
        }
    }
}
