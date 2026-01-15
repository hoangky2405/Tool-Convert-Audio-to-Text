using Microsoft.AspNetCore.Http.Features;
using mp4ToText1.Models;
using Xabe.FFmpeg;

namespace mp4ToText1
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            string ffmpegPath = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg", "bin");
            if (Directory.Exists(ffmpegPath))
            {
                FFmpeg.SetExecutablesPath(ffmpegPath);
            }
            else
            {
                // Fallback nếu không tìm thấy folder, giả định ffmpeg đã có trong biến môi trường Windows
                Console.WriteLine("Cảnh báo: Không tìm thấy thư mục ffmpeg/bin. Hệ thống sẽ thử dùng biến môi trường.");
            }

            // --- CẤU HÌNH UPLOAD FILE LỚN ---
            builder.Services.Configure<FormOptions>(o =>
            {
                o.ValueLengthLimit = int.MaxValue;
                o.MultipartBodyLengthLimit = long.MaxValue; // Mở giới hạn file
                o.MemoryBufferThreshold = int.MaxValue;
            });

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.Limits.MaxRequestBodySize = long.MaxValue; // Mở giới hạn Server Kestrel
            });

            // Đăng ký MVC
            builder.Services.AddControllersWithViews();

            // Đăng ký Service AI là Singleton (Chỉ load Model 1 lần duy nhất)
            builder.Services.AddSingleton<TranscriptionService>();

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}