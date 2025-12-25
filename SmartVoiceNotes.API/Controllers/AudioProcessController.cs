using Microsoft.AspNetCore.Mvc;
using SmartVoiceNotes.Core.DTOs;
using SmartVoiceNotes.Core.Interfaces;
using System.Text.Json;

namespace SmartVoiceNotes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioProcessController : ControllerBase
    {
        private readonly ITranscriptionService _transcriptionService;
        private readonly IAiSummaryService _summaryService;

        // Constructor Injection
        public AudioProcessController(ITranscriptionService transcriptionService, IAiSummaryService summaryService)
        {
            _transcriptionService = transcriptionService;
            _summaryService = summaryService;
        }

        [HttpPost("upload")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = 524288000)] // 500 MB
        public async Task<IActionResult> UploadAudio(IFormFile file, [FromQuery] string language, [FromQuery] string sourceType, [FromQuery] string style, [FromQuery] bool includeQuiz, [FromQuery] short qCount)
        {
            

            if (file == null || file.Length == 0)
                return BadRequest("Please upload a audio or video file");

            // audio and video formats are allowed
            var allowedExtensions = new[] { ".mp3", ".wav", ".m4a", ".mp4", ".mov", ".avi", ".mkv" };
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!allowedExtensions.Contains(extension))
                return BadRequest("File format does not allowed");

            // file size: 500Mb
            if (file.Length > 500 * 1024 * 1024)
                return BadRequest("Max file size is 500Mb");

            var ext = Path.GetExtension(file.FileName).ToLower();
            var videoExtensions = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" };

            bool isVideo = videoExtensions.Contains(ext);

            try
            {
                using var stream = file.OpenReadStream();
    
                //groq test
                string transcription;
                try
                {
                    transcription = await _transcriptionService.TranscribeAudioAsync(stream, file.FileName);

                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"HATA KAYNAĞI: GROQ (Ses Çevirme). Detay: {ex.Message}");
                }

                //gemini test
                var result = new ProcessResponseDto();
                try
                {
                    result = await _summaryService.SummarizeTextAsync(transcription,language, sourceType, style, includeQuiz, qCount, isVideo);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"ERROR SOURCE: GEMINI (summerizeing). info: {ex.Message}");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"General error: {ex.Message}");
            }
        }
        // tihs method finds the link to download mp3 from youtube using cobalt api. does not downloads the file, only gets the link
        [HttpPost("get-youtube-link")]
        public async Task<IActionResult> GetYoutubeLink([FromBody] YoutubeLinkRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest("URL cannot be empty");

            // Video ID'sini bul (Piped API, ID ile çalışır)
            string videoId = ExtractVideoId(request.Url);
            if (string.IsNullOrEmpty(videoId))
                return BadRequest("Geçersiz YouTube linki.");

            // Piped ve Invidious Sunucu Listesi (Azure dostu olanlar)
            var apiInstances = new[]
            {
                $"https://pipedapi.kavin.rocks/streams/{videoId}",     // En sağlamı
                $"https://api.piped.otter.sh/streams/{videoId}",      // Yedek 1
                $"https://pipedapi.drgns.space/streams/{videoId}",    // Yedek 2
                $"https://inv.tux.pizza/api/v1/videos/{videoId}",     // Invidious Alternatifi
                $"https://vid.uff.net/api/v1/videos/{videoId}"        // Invidious Yedek
            };

            using var client = new HttpClient();
            // Tarayıcı taklidi (Önemli)
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
            client.Timeout = TimeSpan.FromSeconds(10); // Hızlı pes etsin, sıradakine geçsin

            foreach (var apiUrl in apiInstances)
            {
                try
                {
                    var response = await client.GetAsync(apiUrl);
                    if (!response.IsSuccessStatusCode) continue;

                    var jsonString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonString);

                    string downloadUrl = null;

                    // 1. PIPED API Mantığı
                    if (apiUrl.Contains("piped"))
                    {
                        if (doc.RootElement.TryGetProperty("audioStreams", out JsonElement audioStreams))
                        {
                            // En yüksek kaliteli ses dosyasını bul (m4a veya mp3)
                            foreach (var stream in audioStreams.EnumerateArray())
                            {
                                if (stream.TryGetProperty("url", out JsonElement urlEl))
                                {
                                    downloadUrl = urlEl.GetString();
                                    // İlk bulduğunu al ve kaç (Genelde en iyisi ilki olur)
                                    break;
                                }
                            }
                        }
                    }
                    // 2. INVIDIOUS API Mantığı
                    else
                    {
                        if (doc.RootElement.TryGetProperty("adaptiveFormats", out JsonElement formats))
                        {
                            foreach (var format in formats.EnumerateArray())
                            {
                                // Sadece ses olan ve audio/mp4 olanı bul
                                if (format.TryGetProperty("type", out JsonElement typeEl) && typeEl.GetString().Contains("audio/mp4"))
                                {
                                    if (format.TryGetProperty("url", out JsonElement urlEl))
                                    {
                                        downloadUrl = urlEl.GetString();
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        return Ok(new { downloadUrl });
                    }
                }
                catch
                {
                    continue; // Sıradaki sunucuyu dene
                }
            }

            return StatusCode(500, "Hiçbir API sunucusundan link alınamadı. (Piped/Invidious Fail)");
        }

        // Yardımcı Metot: YouTube ID'sini Linkten Söker
        private string ExtractVideoId(string url)
        {
            try
            {
                var uri = new Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

                // "v" parametresine bak (youtube.com/watch?v=...)
                if (query.AllKeys.Contains("v"))
                {
                    return query["v"];
                }

                // Kısa linklere bak (youtu.be/...)
                if (uri.Host.Contains("youtu.be"))
                {
                    return uri.AbsolutePath.Trim('/');
                }
            }
            catch { }
            return null;
        }

        // DTO Class
        public class YoutubeLinkRequest
        {
            public string Url { get; set; }
        }

        
    }
}