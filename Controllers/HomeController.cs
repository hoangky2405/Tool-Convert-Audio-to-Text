using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using mp4ToText1.Models;
using Xabe.FFmpeg;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;

namespace mp4ToText1.Controllers
{
    public class HomeController : Controller
    {
        private readonly TranscriptionService _transcriptionService;
        private readonly IWebHostEnvironment _env;

        public HomeController(TranscriptionService transcriptionService, IWebHostEnvironment env)
        {
            _transcriptionService = transcriptionService;
            _env = env;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        // Giới hạn 2GB
        [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
        public async Task<IActionResult> ConvertAudioAjax(IFormFile fileUpload, string lang)
        {
            if (fileUpload == null || fileUpload.Length == 0)
                return Json(new { success = false, message = "Vui lòng chọn file!" });

            string inputPath = "";
            string audioPath = "";

            try
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                // Tạo tên file
                var uniqueFileName = $"{DateTime.Now.Ticks}_{fileUpload.FileName}";
                inputPath = Path.Combine(uploadsFolder, uniqueFileName);
                audioPath = Path.Combine(uploadsFolder, Path.GetFileNameWithoutExtension(inputPath) + ".wav");

                // 1. Lưu file Video gốc
                using (var stream = new FileStream(inputPath, FileMode.Create))
                {
                    await fileUpload.CopyToAsync(stream);
                }

                // 2. Chuyển đổi Video -> Audio chuẩn cho Whisper (16kHz, Mono)
                IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
                var conversion = FFmpeg.Conversions.New()
                    .AddStream(mediaInfo.Streams)
                    .SetOutput(audioPath)
                    .SetOverwriteOutput(true)
                    .AddParameter("-ar 16000")
                    .AddParameter("-ac 1")
                    .AddParameter("-c:a pcm_s16le")
                    .AddParameter("-vn");

                await conversion.Start();

                // 3. Gọi Service AI (Chạy GPU)
                // Sau khi hàm này chạy xong, VRAM sẽ được giải phóng nhờ lệnh 'using' bên trong Service
                List<TranscriptSegment> segments = await _transcriptionService.TranscribeAudioAsync(audioPath, lang);

                // 4. Dọn dẹp file tạm
                CleanupFiles(inputPath, audioPath);

                return Json(new { success = true, data = segments });
            }
            catch (Exception ex)
            {
                CleanupFiles(inputPath, audioPath);
                Console.WriteLine(ex.ToString());

                // Check lỗi CUDA cụ thể để báo user
                if (ex.Message.Contains("DllNotFoundException") || ex.Message.Contains("library"))
                {
                    return Json(new { success = false, message = "Server lỗi GPU: Thiếu CUDA Toolkit hoặc Driver." });
                }

                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        private void CleanupFiles(string input, string audio)
        {
            try
            {
                if (System.IO.File.Exists(input)) System.IO.File.Delete(input);
                if (System.IO.File.Exists(audio)) System.IO.File.Delete(audio);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }
}