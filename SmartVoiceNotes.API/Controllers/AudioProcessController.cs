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
        [HttpPost("process-youtube")]
        public async Task<IActionResult> ProcessYoutube([FromQuery] string url, [FromQuery] string language, [FromQuery] string sourceType, [FromQuery] string style, [FromQuery] bool includeQuiz, [FromQuery] short qCount)
        {
            if (string.IsNullOrWhiteSpace(url))
                return BadRequest("YouTube URL cannot be empty");

            try
            {
                var transcription = await _transcriptionService.TranscribeYoutubeAsync(url);

                var resultDto = await _summaryService.SummarizeTextAsync(transcription,language,sourceType,style,includeQuiz,qCount,true);

                return Ok(resultDto);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"process error: {ex.Message}");
            }
        }
    }
}