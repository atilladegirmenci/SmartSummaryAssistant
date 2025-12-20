using Microsoft.AspNetCore.Mvc;
using SmartVoiceNotes.Core.Constants;
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
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!FileConstants.AllSupportedExtensions.Contains(extension))
                return BadRequest("File format does not allowed");

            // file size check
            if (file.Length > FileConstants.MaxFileSizeBytes)
                return BadRequest($"Max file size is {FileConstants.MaxFileSizeDisplay}");

            bool isVideo = FileConstants.VideoExtensions.Contains(extension);

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
                    return StatusCode(500, $"ERROR SOURCE: GROQ (Transcription). Info: {ex.Message}");
                }

                //gemini test
                ProcessResponseDto result;
                try
                {
                    result = await _summaryService.SummarizeTextAsync(transcription,language, sourceType, style, includeQuiz, qCount, isVideo);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"ERROR SOURCE: GEMINI (summarizing). Info: {ex.Message}");
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