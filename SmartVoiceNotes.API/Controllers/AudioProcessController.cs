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
        private readonly IConfiguration _configuration; // Şifreleri okumak için

        public AudioProcessController(ITranscriptionService transcriptionService, IAiSummaryService summaryService, IConfiguration configuration)
        {
            _transcriptionService = transcriptionService;
            _summaryService = summaryService;
            _configuration = configuration;
        }

        [HttpPost("upload")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = 524288000)] // 500 MB
        public async Task<IActionResult> UploadAudio(IFormFile file, [FromQuery] string language, [FromQuery] string sourceType, [FromQuery] string style, [FromQuery] bool includeQuiz, [FromQuery] short qCount)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Please upload a audio or video file");

            var allowedExtensions = new[] { ".mp3", ".wav", ".m4a", ".mp4", ".mov", ".avi", ".mkv" };
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!allowedExtensions.Contains(extension))
                return BadRequest("File format does not allowed");

            if (file.Length > 500 * 1024 * 1024)
                return BadRequest("Max file size is 500Mb");

            var ext = Path.GetExtension(file.FileName).ToLower();
            var videoExtensions = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" };
            bool isVideo = videoExtensions.Contains(ext);

            try
            {
                using var stream = file.OpenReadStream();

                // 1. Groq (Transcription)
                string transcription;
                try
                {
                    transcription = await _transcriptionService.TranscribeAudioAsync(stream, file.FileName);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"HATA KAYNAĞI: GROQ (Ses Çevirme). Detay: {ex.Message}");
                }

                // 2. Gemini (Summary)
                var result = new ProcessResponseDto();
                try
                {
                    result = await _summaryService.SummarizeTextAsync(transcription, language, sourceType, style, includeQuiz, qCount, isVideo);
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

        [HttpPost("process-youtube")]
        public async Task<IActionResult> ProcessYoutube([FromQuery] string url, [FromQuery] string language, [FromQuery] string sourceType, [FromQuery] string style, [FromQuery] bool includeQuiz, [FromQuery] short qCount)
        {
            if (string.IsNullOrWhiteSpace(url)) return BadRequest("URL cannot be empty");

            // Video ID'sini bul
            string videoId = ExtractVideoId(url);
            if (string.IsNullOrEmpty(videoId)) return BadRequest("Invalid YouTube URL");

            // API Key'i Azure Ayarlarından (veya appsettings.json'dan) oku
            string rapidApiKey = _configuration["RapidApiKey"];
            if (string.IsNullOrEmpty(rapidApiKey))
            {
                return StatusCode(500, "Server Config Error: 'RapidApiKey' bulunamadı.");
            }

            var tempFilePath = Path.GetTempFileName();
            var mp3Path = Path.ChangeExtension(tempFilePath, ".mp3");

            try
            {
                // 1. RAPID API'den İndirme Linkini Al
                string downloadUrl = "";
                using (var client = new HttpClient())
                {
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        // RapidAPI: YouTube MP36 Endpoint
                        RequestUri = new Uri($"https://youtube-mp36.p.rapidapi.com/dl?id={videoId}"),
                        Headers =
                        {
                            { "X-RapidAPI-Key", rapidApiKey },
                            { "X-RapidAPI-Host", "youtube-mp36.p.rapidapi.com" },
                        },
                    };

                    using (var response = await client.SendAsync(request))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            var err = await response.Content.ReadAsStringAsync();
                            return StatusCode(500, $"RapidAPI Hatası: {response.StatusCode}. Detay: {err}");
                        }

                        var body = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(body);

                        // API yanıtını kontrol et (link veya url dönebilir)
                        if (doc.RootElement.TryGetProperty("link", out JsonElement linkEl))
                            downloadUrl = linkEl.GetString();
                        else if (doc.RootElement.TryGetProperty("url", out JsonElement urlEl))
                            downloadUrl = urlEl.GetString();
                        else
                            return StatusCode(500, "RapidAPI geçerli bir indirme linki döndürmedi.");
                    }
                }

                // 2. Dosyayı RapidAPI sunucusundan indir (Azure -> RapidAPI)
                using (var client = new HttpClient())
                {
                    var fileBytes = await client.GetByteArrayAsync(downloadUrl);
                    await System.IO.File.WriteAllBytesAsync(mp3Path, fileBytes);
                }

                // 3. Dosyayı İşle (Groq + Gemini)
                using (var stream = System.IO.File.OpenRead(mp3Path))
                {
                    var transcription = await _transcriptionService.TranscribeAudioAsync(stream, "youtube_audio.mp3");
                    var resultDto = await _summaryService.SummarizeTextAsync(transcription, language, sourceType, style, includeQuiz, qCount, true);
                    return Ok(resultDto);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Process Error: {ex.Message}");
            }
            finally
            {
                // Temizlik
                if (System.IO.File.Exists(mp3Path)) System.IO.File.Delete(mp3Path);
                if (System.IO.File.Exists(tempFilePath)) System.IO.File.Delete(tempFilePath);
            }
        }

        private string ExtractVideoId(string url)
        {
            try
            {
                var uri = new Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                if (query.AllKeys.Contains("v")) return query["v"];
                if (uri.Host.Contains("youtu.be")) return uri.AbsolutePath.Trim('/');
            }
            catch { }
            return null;
        }
    }
}