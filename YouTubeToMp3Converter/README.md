🎵 YouTube 轉 MP3 下載器
這是一個使用 ASP.NET Core MVC 開發的應用程式，允許使用者輸入 YouTube 影片網址，並將其轉換為 MP3 音訊檔案下載。​

🚀 功能特色
輸入 YouTube 影片網址，下載對應的 MP3 音訊檔案。

顯示轉換進度條，提供即時的轉換狀態。

使用 YoutubeExplode 套件解析 YouTube 影片資訊。

整合 FFmpeg 進行音訊轉換。​

🛠️ 系統需求
Visual Studio 2022

.NET 9 SDK

FFmpeg（需將 ffmpeg.exe 放置於指定目錄）​
GitHub

📦 安裝與執行
克隆此專案：​

bash
複製
編輯
git clone https://github.com/yourusername/YouTubeToMp3Converter.git
開啟專案：​

使用 Visual Studio 2022 開啟解決方案檔案 YouTubeToMp3Converter.sln。

還原 NuGet 套件：​

在 Visual Studio 中，點擊「工具」>「NuGet 套件管理員」>「套件管理器主控台」，執行以下命令：

powershell
複製
編輯
Update-Package -reinstall
設定 FFmpeg 路徑：​

將下載的 ffmpeg.exe 放置於專案的 wwwroot/ffmpeg/ 目錄下，或修改程式碼中 FFmpeg 的路徑設定以符合您的環境。

執行專案：​
Medium
+1
GitHub
+1

按下 F5 或點擊「開始」按鈕，啟動應用程式。

📸 使用方式
在首頁的輸入欄位中，貼上您想轉換的 YouTube 影片網址。

點擊「下載 MP3」按鈕。

等待轉換進度條完成，系統將自動下載 MP3 檔案。​

📁 專案結構
plaintext
複製
編輯
YouTubeToMp3Converter/
├── Controllers/
│   └── HomeController.cs
├── Models/
│   └── VideoModel.cs
├── Views/
│   ├── Home/
│   │   └── Index.cshtml
│   └── Shared/
│       └── _Layout.cshtml
├── wwwroot/
│   ├── downloads/
│   └── ffmpeg/
│       └── ffmpeg.exe
├── appsettings.json
├── Program.cs
└── Startup.cs
🧩 使用的套件
YoutubeExplode：解析 YouTube 影片資訊。

FFmpeg：進行音訊轉換。​

📄 授權條款
本專案採用 MIT 授權條款。詳情請參閱 LICENSE 檔案。​

如果您有任何建議或發現問題，歡迎提出 Issue 或提交 Pull Request。