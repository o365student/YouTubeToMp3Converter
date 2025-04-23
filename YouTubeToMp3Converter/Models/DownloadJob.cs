namespace YouTubeToMp3Converter.Models
{
    public class DownloadJob
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string FilePath { get; set; } = "";
        public double Percent { get; set; }
        public bool Completed { get; set; }
        public string? Error { get; set; }
    }
}
