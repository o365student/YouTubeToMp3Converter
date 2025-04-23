# YouTubeToMp3Converter  
> ASP.NET Core 9 + yt-dlp + FFmpeg/FFprobe  
> 將 YouTube 音訊快速轉檔為 MP3，支援即時進度條與串流下載

---

## 功能特色
| 功能 | 說明 |
| --- | --- |
| 🔗 **複製網址 → 轉檔** | 貼上 YouTube 連結，一鍵產生 MP3 |
| 📈 **即時進度條** | 下載 0 %→50 %，轉檔 50 %→100 % 持續更新 |
| 🎧 **自動命名** | 以 YouTube 標題作為檔名，例如 `MySong.mp3` |
| ⚡ **串流下載** | 100 % 時立即開始下載，無額外等待 |
| 🗑 **Temp 檔自動清理** | 檔案傳輸完畢後自動刪除暫存 |
| 🌐 **Azure App Service 相容** | Windows App Service 可直接部署；Linux 容器請自建映像 |

---

## 目錄結構
YouTubeToMp3Converter/
├── Controllers/
│   └── HomeController.cs     # 核心邏輯
├── Views/
│   └── Home/
│       └── Index.cshtml      # Razor UI + Progress Bar
├── Models/
│   └── DownloadJob.cs
├── tools/                    # 外部工具 (自行下載)
│   ├── yt-dlp.exe
│   ├── ffmpeg.exe
│   └── ffprobe.exe
└── README.md



---

## 先決條件
| 項目 | 版本建議 |
| --- | --- |
| **.NET SDK** | 9.0 Preview x (VS 2022 17.10+) |
| **yt-dlp** | 2025.03.10 以後版本 |
| **FFmpeg/FFprobe (Windows static build)** | 6.x 以上 |
| **Visual Studio 2022** | 建議安裝 **ASP.NET 與 Web 開發工作負載** |

> 下載 FFmpeg → <https://www.gyan.dev/ffmpeg/builds/>  
> 下載 yt-dlp → <https://github.com/yt-dlp/yt-dlp/releases>

---

## 快速啟動 (本機)

```bash
git clone https://github.com/<你的帳號>/YouTubeToMp3Converter.git
cd YouTubeToMp3Converter

# tools 資料夾放入 yt-dlp.exe / ffmpeg.exe / ffprobe.exe
dotnet restore
dotnet run


瀏覽器開啟 http://localhost:5000
貼上 YouTube 連結 → 轉檔 → 等到進度 100 % 即會自動下載 MP3。
