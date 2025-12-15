using FFMpegCore;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using Microsoft.Extensions.Configuration;
using SmartVoiceNotes.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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
        }
        public async Task<string> TranscribeYoutubeAsync(string youtubeUrl)
        {
            var youtube = new YoutubeClient();

            try
            {
                var video = await youtube.Videos.GetAsync(youtubeUrl);
                var title = video.Title; // İstersen başlığı loglayabilirsin

                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(youtubeUrl);

                // get audio only
                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (streamInfo == null)
                    throw new Exception("Audio stream could not found.");

                var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");

                await youtube.Videos.Streams.DownloadAsync(streamInfo, tempFilePath);

                try
                {
                    using (var stream = File.OpenRead(tempFilePath))
                    {
                        return await TranscribeAudioAsync(stream, "youtube_audio.mp3");
                    }
                }
                finally
                {
                    if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Could not finish: {ex.Message}");
            }
        }
        public async Task<string> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            //save the stram to a temp file 
            var tempFilePath = Path.GetTempFileName(); // C:\Users\Temp\tmp123.tmp gibi bir dosya oluşturur
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
                long limitInBytes = 20 * 1024 * 1024; // 20 MB Güvenli Sınır

                // EĞER KÜÇÜKSE: Direkt gönder (Eski yöntem)
                if (sizeInBytes < limitInBytes)
                {
                    return await SendToGroqApi(inputPath);
                }

                // EĞER BÜYÜKSE: Parçala ve Yönet (Chunking) 🔪
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

            // Dosyanın süresini öğrenelim
            var mediaInfo = await FFProbe.AnalyseAsync(inputPath);
            var duration = mediaInfo.Duration;

            // 10 dakikalık parçalara bölelim (Groq için güvenli süre)
            var chunkDuration = TimeSpan.FromMinutes(10);
            var chunks = new List<string>();

            try
            {
                for (var currentTime = TimeSpan.Zero; currentTime < duration; currentTime += chunkDuration)
                {
                    // Geçici parça dosyası ismi
                    var chunkPath = Path.ChangeExtension(Path.GetTempFileName(), ".mp3");
                    chunks.Add(chunkPath);

                    // FFMPEG: Dosyayı kesiyoruz
                    // -ss : Başlangıç zamanı
                    // -t  : Ne kadar süreceği
                    await FFMpegArguments
                        .FromFileInput(inputPath)
                        .OutputToFile(chunkPath, true, options => options
                            .Seek(currentTime)
                            .WithDuration(chunkDuration))
                        .ProcessAsynchronously();

                    // Kesilen parçayı hemen API'ye gönderip metni alalım
                    var chunkText = await SendToGroqApi(chunkPath);

                    // Metinleri uc uca ekle
                    transcriptionBuilder.Append(chunkText + " ");
                }
            }
            finally
            {
                // Oluşturduğumuz küçük parça dosyalarını silelim
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

        // API Çağırma İşini Tek Bir Metoda İndirgedik (DRY Prensibi)
        private async Task<string> SendToGroqApi(string filePath)
        {
            using var content = new MultipartFormDataContent();

            // Dosyayı diskten oku
            using var fileStream = File.OpenRead(filePath);
            var fileContent = new StreamContent(fileStream);

            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            content.Add(fileContent, "file", Path.GetFileName(filePath));
            content.Add(new StringContent("whisper-large-v3"), "model");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.PostAsync("https://api.groq.com/openai/v1/audio/transcriptions", content);

            // Hata varsa fırlat (Controller yakalar)
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq API Error: {err}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            return doc.RootElement.GetProperty("text").GetString();
        }
    }
}