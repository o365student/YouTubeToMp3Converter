# 🎵 YouTube 轉 MP3 下載器

這是一個使用 ASP.NET Core MVC 開發的應用程式，允許使用者輸入 YouTube 影片網址，並將其轉換為 MP3 音訊檔案下載。

## 🚀 功能特色

- 輸入 YouTube 影片網址，下載對應的 MP3 音訊檔案。
- 顯示轉換進度條，提供即時的轉換狀態。
- 使用 [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode) 套件解析 YouTube 影片資訊。
- 整合 FFmpeg 進行音訊轉換。

## 🛠️ 系統需求

- Visual Studio 2022
- .NET 9 SDK
- FFmpeg（需將 `ffmpeg.exe` 放置於指定目錄）

## 📦 安裝與執行

1. **克隆此專案：**

   ```bash
   git clone https://github.com/yourusername/YouTubeToMp3Converter.git
