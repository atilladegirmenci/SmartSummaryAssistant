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
                var appRoot = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                var toolsFolder = Path.Combine(appRoot, "ffmpeg");

                if (!Directory.Exists(toolsFolder))
                    Console.WriteLine($"WARNING: Tools folder not found at {toolsFolder}");

                bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

                string ffmpegName = isLinux ? "ffmpeg" : "ffmpeg.exe";
                string ffprobeName = isLinux ? "ffprobe" : "ffprobe.exe";
                string ytdlpName = isLinux ? "yt-dlp" : "yt-dlp.exe";

                _ffmpegPath = Path.Combine(toolsFolder, ffmpegName);
                _ffprobePath = Path.Combine(toolsFolder, ffprobeName);
                _ytDlpPath = Path.Combine(toolsFolder, ytdlpName);

                GlobalFFOptions.Configure(new FFOptions { BinaryFolder = toolsFolder });

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
            // Cloudflare'i aşmak için sağlam bir User-Agent ve Referer şart
            var chromeUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36";

            // Güçlü sunucular listesi
            var cobaltInstances = new[]
            {
        "https://co.wuk.sh/api/json",      // En sağlamı
        "https://api.cobalt.tools/api/json",
        "https://cobalt.api.sc/api/json",
        "https://api.gsc.sh/api/json"
    };

            string downloadUrl = null;

            // Her seferinde temiz bir client oluşturalım ki önceki hatalar etkilemesin
            using var client = new HttpClient();

            // 1. ADIM: İsteği "Tarayıcı" gibi göster (Cloudflare Korumasını Aşmak İçin)
            client.DefaultRequestHeaders.Add("User-Agent", chromeUserAgent);
            client.DefaultRequestHeaders.Add("Origin", "https://cobalt.tools"); // Kendisiymiş gibi davran
            client.DefaultRequestHeaders.Add("Referer", "https://cobalt.tools/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            foreach (var apiUrl in cobaltInstances)
            {
                try
                {
                    var requestBody = new
                    {
                        url = youtubeUrl,
                        aFormat = "mp3",
                        isAudioOnly = true,
                        // Bazı instance'lar filenamePattern ister
                        filenamePattern = "classic"
                    };

                    var jsonContent = new StringContent(
                        System.Text.Json.JsonSerializer.Serialize(requestBody),
                        Encoding.UTF8,
                        "application/json");

                    var response = await client.PostAsync(apiUrl, jsonContent);

                    if (!response.IsSuccessStatusCode) continue;

                    var responseString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseString);

                    // Başarılı yanıtı yakala
                    if (doc.RootElement.TryGetProperty("url", out JsonElement urlElement))
                    {
                        downloadUrl = urlElement.GetString();
                        break;
                    }
                    // Bazıları 'picker' veya 'stream' dönebilir, onları da kontrol edelim
                    else if (doc.RootElement.TryGetProperty("picker", out JsonElement pickerElement))
                    {
                        // Bazen liste döner, ilkini al
                        foreach (var item in pickerElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("url", out JsonElement pickUrl))
                            {
                                downloadUrl = pickUrl.GetString();
                                break;
                            }
                        }
                        if (downloadUrl != null) break;
                    }
                }
                catch
                {
                    // Hata alırsan sessizce diğer sunucuya geç
                    continue;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new Exception("YouTube bağlantısı kurulamadı (Cloudflare veya API engeli).");
            }

            // --- 2. ADIM: DOSYAYI İNDİR ---

            var tempFilePath = Path.GetTempFileName();
            var mp3Path = Path.ChangeExtension(tempFilePath, ".mp3");

            try
            {
                // Dosyayı indirirken de aynı User-Agent'ı kullanmalıyız!
                using var downloadClient = new HttpClient();
                downloadClient.DefaultRequestHeaders.Add("User-Agent", chromeUserAgent);

                using (var stream = await downloadClient.GetStreamAsync(downloadUrl))
                using (var fileStream = File.Create(mp3Path))
                {
                    await stream.CopyToAsync(fileStream);
                }

                // --- 3. ADIM: GROQ'A GÖNDER ---
                using (var audioStream = File.OpenRead(mp3Path))
                {
                    return await TranscribeAudioAsync(audioStream, "youtube_audio.mp3");
                }
            }
            finally
            {
                if (File.Exists(mp3Path)) File.Delete(mp3Path);
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            }
        }


        // --- HELPER METHOD TO CREATE NETSCAPE COOKIE FILE ---
        private void CreateCookieFile(string path)
        {
            // The raw cookie string you provided
            var rawCookies = "APISID=oA3yIrFqesCGFEaE/AJgO9yECOWl8Q34bg; SAPISID=Tkpwg356o6GWVxuI/Aq9U0D7i7B70x8hNs; __Secure-1PAPISID=Tkpwg356o6GWVxuI/Aq9U0D7i7B70x8hNs; __Secure-3PAPISID=Tkpwg356o6GWVxuI/Aq9U0D7i7B70x8hNs; SID=g.a0004AiReczUP6V63CXZNobS9Zua8S1xdafSr5ZEgtXlckJz-BBoe1b72DTmQ_tXuKWA5t0q0gACgYKAbsSARESFQHGX2MiLoTQQ9IvrFKXeWjKHo2foxoVAUF8yKrWK4DwzI0pGLDR371V3e9b0076; PREF=f6=40000000&f7=4150&tz=Europe.Istanbul&f5=30000&repeat=NONE; wide=1; SIDCC=AKEyXzVGd2NL3VeSCQ-0cdG3w_PD5QzUsQsAClGs8F78RR6KEMi6cfT3Jr5cMbD9z3J3mfYQJrU";

            var sb = new StringBuilder();
            sb.AppendLine("# Netscape HTTP Cookie File");
            sb.AppendLine("# This file was generated by SmartVoiceNotes code");
            sb.AppendLine("# https://curl.haxx.se/rfc/cookie_spec.html");
            sb.AppendLine();

            var parts = rawCookies.Split(';');
            foreach (var part in parts)
            {
                var trimPart = part.Trim();
                if (string.IsNullOrEmpty(trimPart)) continue;

                var eqIndex = trimPart.IndexOf('=');
                if (eqIndex > 0)
                {
                    var key = trimPart.Substring(0, eqIndex);
                    var value = trimPart.Substring(eqIndex + 1);

                    // Format: .domain (tab) flag (tab) path (tab) secure (tab) expiration (tab) name (tab) value
                    // Expiration: 253402300799 is roughly year 9999 (never expire for session)
                    sb.AppendLine($".youtube.com\tTRUE\t/\tFALSE\t253402300799\t{key}\t{value}");
                }
            }

            File.WriteAllText(path, sb.ToString());
        }

        // --- KEEP YOUR EXISTING METHODS BELOW (TranscribeAudioAsync, etc.) ---

        public async Task<string> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
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