using YoutubeDLSharp;
using YoutubeDLSharp.Options;
using Microsoft.Extensions.Configuration;
using SmartVoiceNotes.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using FFMpegCore; // Büyük dosya işleme için hala buna ihtiyacımız var

namespace SmartVoiceNotes.Infrastructure
{
    public class GroqTranscriptionService : ITranscriptionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private string _ffmpegPath;
        private string _ffprobePath;
        private string _ytDlpPath;

        public GroqTranscriptionService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["AiSettings:GroqApiKey"];
            ConfigureTools();
        }

        private void ConfigureTools()
        {
            try
            {
                // 1. Ana dizini bul (/home/site/wwwroot)
                var appRoot = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                var toolsFolder = Path.Combine(appRoot, "ffmpeg");

                if (!Directory.Exists(toolsFolder))
                    Console.WriteLine($"WARNING: Tools folder not found at {toolsFolder}");

                // 2. Dosya yollarını belirle (Linux/Windows uyumlu)
                bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

                string ffmpegName = isLinux ? "ffmpeg" : "ffmpeg.exe";
                string ffprobeName = isLinux ? "ffprobe" : "ffprobe.exe";
                string ytdlpName = isLinux ? "yt-dlp" : "yt-dlp.exe"; // Windows'ta test ediyorsan .exe indirmen lazım

                _ffmpegPath = Path.Combine(toolsFolder, ffmpegName);
                _ffprobePath = Path.Combine(toolsFolder, ffprobeName);
                _ytDlpPath = Path.Combine(toolsFolder, ytdlpName);

                // 3. FFMpegCore Ayarı
                GlobalFFOptions.Configure(new FFOptions { BinaryFolder = toolsFolder });

                // 4. Linux İzinleri (chmod +x)
                if (isLinux)
                {
                    SetExecutable(_ffmpegPath);
                    SetExecutable(_ffprobePath);
                    SetExecutable(_ytDlpPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Tools Setup Error: {ex.Message}");
            }
        }

        private void SetExecutable(string path)
        {
            if (File.Exists(path))
            {
                try { System.Diagnostics.Process.Start("chmod", $"+x \"{path}\"").WaitForExit(); }
                catch { }
            }
        }

        public async Task<string> TranscribeYoutubeAsync(string youtubeUrl)
        {
            // yt-dlp'yi başlatıyoruz
            var ytdl = new YoutubeDL();
            ytdl.YoutubeDLPath = _ytDlpPath;
            ytdl.FFmpegPath = _ffmpegPath;

            // Geçici dosya yolu
            var tempFolder = Path.GetTempPath();
            var outputFileName = $"{Guid.NewGuid()}.mp3";
            var outputFilePath = Path.Combine(tempFolder, outputFileName);

            try
            {
                // İndirme Ayarları (En iyi ses kalitesi)
                var options = new OptionSet()
                {
                    ExtractAudio = true,
                    AudioFormat = AudioConversionFormat.Mp3,
                    AudioQuality = 0, // En iyi kalite
                    Output = outputFilePath, // Dosya adı şablonu
                };

                // ÖNEMLİ: YouTube yavaşlatmasını aşmak için tarayıcı maskesi
                // yt-dlp bunu otomatik yapar ama biz garantiye alalım
                // options.AddCustomOption("--user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)..."); 

                // İndirmeyi Başlat
                var res = await ytdl.RunAudioDownload(youtubeUrl, AudioConversionFormat.Mp3, default,null, null, options);

                if (!res.Success)
                {
                    // Hata detayını yakala
                    string errorMsg = string.Join(" | ", res.ErrorOutput);
                    throw new Exception($"yt-dlp Error: {errorMsg}");
                }

                // yt-dlp bazen dosya adını kendi tamamlar, doğru dosyayı bulalım
                string actualFile = res.Data; // İndirilen dosyanın tam yolu
                if (!File.Exists(actualFile))
                {
                    // Bazen data boş dönerse tahmin ettiğimiz yola bak
                    // yt-dlp şablon kullandığı için dosya adı değişebilir, temp klasördeki en yeni mp3'ü alabiliriz worst case
                    actualFile = outputFilePath; // Çoğunlukla .mp3 ekler
                    if (!File.Exists(actualFile)) actualFile += ".mp3";
                }

                using (var stream = File.OpenRead(actualFile))
                {
                    return await TranscribeAudioAsync(stream, "youtube_audio.mp3");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Process Failed: {ex.Message}");
            }
        }

        // --- DİĞER METOTLARIN (TranscribeAudioAsync, SendToGroqApi vb.) AYNEN KALSIN ---
        // Sadece sınıfın üst kısmını ve TranscribeYoutubeAsync metodunu değiştirdik.
        // Aşağıya mevcut kodundaki TranscribeAudioAsync ve diğerlerini yapıştır.

        public async Task<string> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            // ... SENİN MEVCUT KODLARIN ...
            // (Burayı aynen koru, sadece yukarıdaki constructor ve TranscribeYoutubeAsync değişti)

            // Ufak bir düzeltme: FFProbe analizinde yolu bulması için GlobalFFOptions yukarıda ayarlandı, sorun yok.
            // Kopyala yapıştır yaparken eski TranscribeAudioAsync kodunu buraya eklemeyi unutma.

            // GEÇİCİ OLARAK KODU TAMAMLAMAK ADINA BURAYA SENİN ESKİ KODUNU ÖZETLİYORUM:
            var tempFilePath = Path.GetTempFileName();
            var inputPath = Path.ChangeExtension(tempFilePath, Path.GetExtension(fileName));
            if (string.IsNullOrEmpty(Path.GetExtension(inputPath))) inputPath += ".mp3";

            var filesToDelete = new List<string> { inputPath, tempFilePath };

            try
            {
                using (var fileStream = File.Create(inputPath))
                {
                    await audioStream.CopyToAsync(fileStream);
                }

                // IsVideoFile kontrolü vb... (Aynen kalsın)

                // 2. DOSYA ANALİZİ
                var fileInfo = new FileInfo(inputPath);
                if (fileInfo.Length < 20 * 1024 * 1024)
                {
                    return await SendToGroqApi(inputPath);
                }

                return await ProcessLargeFileAsync(inputPath);
            }
            finally
            {
                foreach (var file in filesToDelete)
                {
                    if (File.Exists(file)) try { File.Delete(file); } catch { }
                }
            }
        }

        private async Task<string> ProcessLargeFileAsync(string inputPath)
        {
            // ... SENİN MEVCUT KODLARIN ...
            // FFMpegArguments.FromFileInput... kısımları aynen kalsın.
            // Sadece buraya tekrar kopyalamadım yer kaplamasın diye.

            // KODU TAMAMLAMAK İÇİN AŞAĞIDAKİLERİ DE EKLE:
            var transcriptionBuilder = new StringBuilder();
            var mediaInfo = await FFProbe.AnalyseAsync(inputPath);
            var duration = mediaInfo.Duration;
            var chunkDuration = TimeSpan.FromMinutes(10);
            var chunks = new List<string>();

            try
            {
                for (var currentTime = TimeSpan.Zero; currentTime < duration; currentTime += chunkDuration)
                {
                    var chunkPath = Path.ChangeExtension(Path.GetTempFileName(), ".mp3");
                    chunks.Add(chunkPath);

                    await FFMpegArguments
                        .FromFileInput(inputPath)
                        .OutputToFile(chunkPath, true, options => options
                            .Seek(currentTime)
                            .WithDuration(chunkDuration))
                        .ProcessAsynchronously();

                    var chunkText = await SendToGroqApi(chunkPath);
                    transcriptionBuilder.Append(chunkText + " ");
                }
            }
            finally
            {
                foreach (var chunk in chunks) if (File.Exists(chunk)) try { File.Delete(chunk); } catch { }
            }
            return transcriptionBuilder.ToString();
        }

        private bool IsVideoFile(string path)
        {
            var videoExtensions = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" };
            return videoExtensions.Contains(Path.GetExtension(path).ToLower());
        }

        private async Task ExtractAudioFromVideoAsync(string videoPath, string outputPath)
        {
            await FFMpegArguments
                .FromFileInput(videoPath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument("-vn -acodec libmp3lame -q:a 2"))
                .ProcessAsynchronously();
        }

        private async Task<string> SendToGroqApi(string filePath)
        {
            // ... SENİN MEVCUT KODLARIN ...
            using var content = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(filePath);
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            content.Add(fileContent, "file", Path.GetFileName(filePath));
            content.Add(new StringContent("whisper-large-v3"), "model");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            var response = await _httpClient.PostAsync("https://api.groq.com/openai/v1/audio/transcriptions", content);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq API Error: {err}");
            }
            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            if (doc.RootElement.TryGetProperty("text", out JsonElement textElement))
            {
                return textElement.GetString();
            }
            return "No text found.";
        }
    }
}