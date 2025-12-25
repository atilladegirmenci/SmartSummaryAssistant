using FFMpegCore;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using Microsoft.Extensions.Configuration;
using SmartVoiceNotes.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SmartVoiceNotes.Infrastructure
{
    public class GroqTranscriptionService : ITranscriptionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public GroqTranscriptionService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["AiSettings:GroqApiKey"];
            ConfigureFFmpeg();
        }
        public async Task<string> TranscribeYoutubeAsync(string youtubeUrl)
        {
            // 1. "BEN TARAYICIYIM" MASKESİ (User-Agent Spoofing)
            // Azure IP'leri engellendiği için, isteği sanki gerçek bir Chrome tarayıcısı yapıyormuş gibi gösteriyoruz.
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://www.youtube.com/");

            var youtube = new YoutubeClient(httpClient);

            try
            {
                // Video bilgilerini al
                var video = await youtube.Videos.GetAsync(youtubeUrl);

                // Manifest (akış) bilgilerini al
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(youtubeUrl);

                // Sadece ses akışını al (En yüksek kalite)
                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (streamInfo == null)
                    throw new Exception("Audio stream could not found (Manifest empty).");

                // Geçici dosya yolu
                var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");

                // İndirme işlemi
                await youtube.Videos.Streams.DownloadAsync(streamInfo, tempFilePath);

                try
                {
                    using (var stream = File.OpenRead(tempFilePath))
                    {
                        // İndirilen dosyayı ses analizine gönder
                        return await TranscribeAudioAsync(stream, "youtube_audio.mp3");
                    }
                }
                finally
                {
                    // Temizlik
                    if (File.Exists(tempFilePath))
                    {
                        try { File.Delete(tempFilePath); } catch { /* Silinemezse logla ama patlatma */ }
                    }
                }
            }
            catch (YoutubeExplode.Exceptions.VideoUnavailableException)
            {
                throw new Exception("YouTube says 'Video Unavailable'. This is likely an IP ban on Azure servers. Try a different video or wait.");
            }
            catch (Exception ex)
            {
                throw new Exception($"YouTube Process Error: {ex.Message}");
            }
        }
        public async Task<string> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            //save the stram to a temp file 
            var tempFilePath = Path.GetTempFileName(); // C:\Users\Temp\tmp123.tmp or something like that
            var inputPath = Path.ChangeExtension(tempFilePath, Path.GetExtension(fileName));

            var filesToDelete = new List<string> { inputPath, tempFilePath }; //keep track of files to be deleted

            try
            {
                // Stream'i diske yaz
                using (var fileStream = File.Create(inputPath))
                {
                    await audioStream.CopyToAsync(fileStream);
                }

                
                if(IsVideoFile(inputPath))
                {
                    var extractedAudioPath = Path.ChangeExtension(Path.GetTempFileName(), ".mp3");
                    filesToDelete.Add(extractedAudioPath);

                    await ExtractAudioFromVideoAsync(inputPath, extractedAudioPath);

                    inputPath = extractedAudioPath;
                }
                // 2. DOSYA ANALİZİ
                var fileInfo = new FileInfo(inputPath);
                long sizeInBytes = fileInfo.Length;
                long limitInBytes = 20 * 1024 * 1024; // 20 MB 

                if (sizeInBytes < limitInBytes)
                {
                    return await SendToGroqApi(inputPath);
                }

                // if its large: Chunking
                return await ProcessLargeFileAsync(inputPath);
            }
            finally
            {
                foreach (var file in filesToDelete)
                {
                    if (File.Exists(file)) File.Delete(file);
                }
            }
        }

        private async Task<string> ProcessLargeFileAsync(string inputPath)
        {
            var transcriptionBuilder = new StringBuilder();

            var mediaInfo = await FFProbe.AnalyseAsync(inputPath);
            var duration = mediaInfo.Duration;

            // 10 minute pieces safe for groq
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
                foreach (var chunk in chunks)
                {
                    if (File.Exists(chunk)) File.Delete(chunk);
                }
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
                    .WithCustomArgument("-vn -acodec libmp3lame -q:a 2")) // High quality MP3
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
            return doc.RootElement.GetProperty("text").GetString();
        }
        private void ConfigureFFmpeg()
        {
            try
            {
                // 1. Çalışan uygulamanın ana dizinini bul
                var appRoot = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

                // 2. ffmpeg klasörünü belirle (/home/site/wwwroot/ffmpeg)
                var ffmpegFolder = Path.Combine(appRoot, "ffmpeg");

                // Klasör yoksa loglara düşmesi için veya alternatif yollara bakılabilir
                if (!Directory.Exists(ffmpegFolder))
                {
                    Console.WriteLine($"WARNING: FFmpeg folder not found at {ffmpegFolder}");
                }

                // 3. FFMpegCore kütüphanesine "Dosyalar burada!" de.
                GlobalFFOptions.Configure(new FFOptions { BinaryFolder = ffmpegFolder });

                // 4. Linux İzinleri (chmod +x) - Çok Kritik!
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // ffmpeg dosyası
                    var ffmpegBinary = Path.Combine(ffmpegFolder, "ffmpeg");
                    if (File.Exists(ffmpegBinary))
                    {
                        System.Diagnostics.Process.Start("chmod", $"+x \"{ffmpegBinary}\"").WaitForExit();
                    }

                    // ffprobe dosyası
                    var ffprobeBinary = Path.Combine(ffmpegFolder, "ffprobe");
                    if (File.Exists(ffprobeBinary))
                    {
                        System.Diagnostics.Process.Start("chmod", $"+x \"{ffprobeBinary}\"").WaitForExit();
                    }
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile uygulama çökmesin, loglayıp devam etsin
                Console.WriteLine($"FFmpeg Setup Error: {ex.Message}");
            }
        }
    }
}