using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace mp4ToText1.Models
{
    public class TranscriptionService
    {
        private readonly string _modelPath;
        // Lock này chỉ dùng để đảm bảo không tải file trùng nhau, không ảnh hưởng VRAM
        private readonly SemaphoreSlim _downloadLock = new SemaphoreSlim(1, 1);

        public TranscriptionService()
        {
            // RTX 3050 4GB nên dùng Model "Base" hoặc "Small". 
            _modelPath = Path.Combine(Directory.GetCurrentDirectory(), "MLModels", "ggml-base.bin");
        }

        // Hàm này chỉ kiểm tra và tải file về ổ cứng (KHÔNG NẠP VÀO GPU)
        private async Task EnsureModelDownloadedAsync()
        {
            if (File.Exists(_modelPath)) return;

            await _downloadLock.WaitAsync();
            try
            {
                if (File.Exists(_modelPath)) return;

                var folder = Path.GetDirectoryName(_modelPath);
                if (folder != null && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

                using var httpClient = new HttpClient();
                var downloader = new WhisperGgmlDownloader(httpClient);

                Console.WriteLine("Đang tải Model (Base) về ổ cứng...");
                using var modelStream = await downloader.GetGgmlModelAsync(GgmlType.Base);
                using var fileWriter = File.OpenWrite(_modelPath);
                await modelStream.CopyToAsync(fileWriter);
                Console.WriteLine("Tải xong.");
            }
            finally
            {
                _downloadLock.Release();
            }
        }

        public async Task<List<TranscriptSegment>> TranscribeAudioAsync(string audioFilePath, string language)
        {
            // 1. Đảm bảo file model đã có trên ổ cứng
            await EnsureModelDownloadedAsync();

            var results = new List<TranscriptSegment>();

            // 2. Nạp Model vào VRAM (Bắt đầu tốn VRAM từ đây)
            // Dùng khối 'using' -> Khi chạy xong ngoặc nhọn, nó TỰ ĐỘNG hủy Factory và nhả VRAM
            using (var factory = WhisperFactory.FromPath(_modelPath))
            {
                // Cấu hình tham số xử lý
                var builder = factory.CreateBuilder()
                    .WithLanguage(language)
                    .WithThreads(4);

                using (var processor = builder.Build())
                {
                    using (var fileStream = File.OpenRead(audioFilePath))
                    {
                        // Bắt đầu xử lý
                        await foreach (var segment in processor.ProcessAsync(fileStream))
                        {
                            results.Add(new TranscriptSegment
                            {
                                Timestamp = segment.Start.ToString(@"mm\:ss"),
                                Text = segment.Text.Trim()
                            });
                        }
                    }
                }
                // <-- Tới dòng này, processor và factory bị hủy (Dispose).
                // GPU VRAM sẽ tụt về 0 ngay lập tức tại đây.
            }

            return results;
        }
    }
}