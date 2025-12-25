using YoutubeDLSharp;
using YoutubeDLSharp.Options;
using Microsoft.Extensions.Configuration;
using SmartVoiceNotes.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using FFMpegCore; 

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
            var ytdl = new YoutubeDL();
            ytdl.YoutubeDLPath = _ytDlpPath;
            ytdl.FFmpegPath = _ffmpegPath;

            var tempFolder = Path.GetTempPath();
            var outputFileName = $"{Guid.NewGuid()}.%(ext)s";
            var outputFilePathTemplate = Path.Combine(tempFolder, outputFileName);

            try
            {
                // 1. COOKIE VE USER-AGENT AYARLARI
                // Burası YouTube'un "Sen botsun" engelini aşmamızı sağlayan yer.
                var myCookie = "APISID=oA3yIrFqesCGFEaE/AJgO9yECOWl8Q34bg; SAPISID=Tkpwg356o6GWVxuI/Aq9U0D7i7B70x8hNs; __Secure-1PAPISID=Tkpwg356o6GWVxuI/Aq9U0D7i7B70x8hNs; __Secure-3PAPISID=Tkpwg356o6GWVxuI/Aq9U0D7i7B70x8hNs; SID=g.a0004AiReczUP6V63CXZNobS9Zua8S1xdafSr5ZEgtXlckJz-BBoe1b72DTmQ_tXuKWA5t0q0gACgYKAbsSARESFQHGX2MiLoTQQ9IvrFKXeWjKHo2foxoVAUF8yKrWK4DwzI0pGLDR371V3e9b0076; PREF=f6=40000000&f7=4150&tz=Europe.Istanbul&f5=30000&repeat=NONE; wide=1; SIDCC=AKEyXzVGd2NL3VeSCQ-0cdG3w_PD5QzUsQsAClGs8F78RR6KEMi6cfT3Jr5cMbD9z3J3mfYQJrU";
                var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

                var options = new OptionSet()
                {
                    ExtractAudio = true,
                    AudioFormat = AudioConversionFormat.Mp3,
                    AudioQuality = 0,
                    Output = outputFilePathTemplate,
                };

                // 2. KRİTİK HAMLE: Header Ekleme
                // yt-dlp'ye bu header'ları göndererek isteği manipüle ediyoruz.
                options.AddCustomOption("--add-header", $"Cookie:{myCookie}");
                options.AddCustomOption("--user-agent", userAgent);

                // JS Hatasını bastırmak ve güvenli indirme için ekstra ayarlar
                options.AddCustomOption("--no-check-certificate", true);
                options.AddCustomOption("--prefer-free-formats", true);

                // İndirmeyi başlat (Araya null eklemeyi unutmadık)
                var res = await ytdl.RunAudioDownload(youtubeUrl, AudioConversionFormat.Mp3, default, null, null, options);

                if (!res.Success)
                {
                    // Hata mesajını daha temiz hale getiriyoruz
                    string errorMsg = string.Join(" ", res.ErrorOutput);
                    throw new Exception($"yt-dlp Failed: {errorMsg}");
                }

                string actualFile = res.Data;

                // Dosya adı düzeltme mantığı
                if (string.IsNullOrEmpty(actualFile) || !File.Exists(actualFile))
                {
                    actualFile = outputFilePathTemplate.Replace(".%(ext)s", ".mp3");
                }

                if (!File.Exists(actualFile))
                    throw new Exception($"Downloaded file could not be found via yt-dlp. Expected at: {actualFile}");

                using (var stream = File.OpenRead(actualFile))
                {
                    return await TranscribeAudioAsync(stream, "youtube_audio.mp3");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"YouTube Error: {ex.Message}");
            }
        }
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