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

            // Cobalt Sunucu Listesi
            var cobaltInstances = new[]
            {
                "https://co.wuk.sh/api/json",      // 1. Tercih
                "https://api.cobalt.tools/api/json",
                "https://cobalt.api.sc/api/json",
                "https://api.gsc.sh/api/json"
            };

            using var client = new HttpClient();
            // Backend'den API'ye giderken Tarayıcı taklidi yapıyoruz
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Origin", "https://cobalt.tools");
            client.DefaultRequestHeaders.Add("Referer", "https://cobalt.tools/");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            foreach (var apiUrl in cobaltInstances)
            {
                try
                {
                    var requestBody = new
                    {
                        url = request.Url,
                        aFormat = "mp3",
                        isAudioOnly = true,
                        filenamePattern = "classic"
                    };

                    var jsonContent = new StringContent(
                        JsonSerializer.Serialize(requestBody),
                        System.Text.Encoding.UTF8,
                        "application/json");

                    var response = await client.PostAsync(apiUrl, jsonContent);

                    if (!response.IsSuccessStatusCode) continue;

                    var jsonString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonString);

                    string downloadUrl = null;

                    if (doc.RootElement.TryGetProperty("url", out JsonElement urlElement))
                    {
                        downloadUrl = urlElement.GetString();
                    }
                    else if (doc.RootElement.TryGetProperty("picker", out JsonElement pickerElement))
                    {
                        foreach (var item in pickerElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("url", out JsonElement pickUrl))
                            {
                                downloadUrl = pickUrl.GetString();
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        // Başarılı linki Frontend'e dönüyoruz
                        return Ok(new { downloadUrl });
                    }
                }
                catch
                {
                    continue;
                }
            }

            return StatusCode(500, "Cobalt API link üretemedi. Lütfen daha sonra tekrar deneyin.");
        }

        // DTO Class (Dosyanın en altına veya uygun yere ekle)
        public class YoutubeLinkRequest
        {
            public string Url { get; set; }
        }
    }
}