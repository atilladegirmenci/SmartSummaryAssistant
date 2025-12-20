using Microsoft.AspNetCore.Mvc;
using SmartVoiceNotes.Core.Constants;
using SmartVoiceNotes.Core.DTOs;
using SmartVoiceNotes.Core.Helpers;
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
            {
                var error = ErrorResponseHelper.CreateFileValidationError(
                    "No file provided",
                    "Please upload an audio or video file");
                return BadRequest(error);
            }

            // audio and video formats are allowed
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!FileConstants.AllSupportedExtensions.Contains(extension))
            {
                var error = ErrorResponseHelper.CreateFileValidationError(
                    "Unsupported file format",
                    $"Supported formats: {string.Join(", ", FileConstants.AllSupportedExtensions)}");
                return BadRequest(error);
            }

            // file size check
            if (file.Length > FileConstants.MaxFileSizeBytes)
            {
                var error = ErrorResponseHelper.CreateFileValidationError(
                    "File size exceeds limit",
                    $"Maximum allowed size: {FileConstants.MaxFileSizeDisplay}");
                return BadRequest(error);
            }

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
                    var error = ErrorResponseHelper.CreateProcessingError(
                        "Groq",
                        "Failed to transcribe audio",
                        ex.Message);
                    return StatusCode(500, error);
                }

                //gemini test
                ProcessResponseDto result;
                try
                {
                    result = await _summaryService.SummarizeTextAsync(transcription,language, sourceType, style, includeQuiz, qCount, isVideo);
                }
                catch (Exception ex)
                {
                    var error = ErrorResponseHelper.CreateProcessingError(
                        "Gemini",
                        "Failed to generate summary",
                        ex.Message);
                    return StatusCode(500, error);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                var error = ErrorResponseHelper.CreateGeneralError(
                    "Unexpected error during audio processing",
                    ex.Message);
                return StatusCode(500, error);
            }
        }
        [HttpPost("process-youtube")]
        public async Task<IActionResult> ProcessYoutube([FromQuery] string url, [FromQuery] string language, [FromQuery] string sourceType, [FromQuery] string style, [FromQuery] bool includeQuiz, [FromQuery] short qCount)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                var error = ErrorResponseHelper.CreateFileValidationError(
                    "YouTube URL is required",
                    "Please provide a valid YouTube URL");
                return BadRequest(error);
            }

            try
            {
                var transcription = await _transcriptionService.TranscribeYoutubeAsync(url);

                var resultDto = await _summaryService.SummarizeTextAsync(transcription,language,sourceType,style,includeQuiz,qCount,true);

                return Ok(resultDto);
            }
            catch (Exception ex)
            {
                var error = ErrorResponseHelper.CreateGeneralError(
                    "Failed to process YouTube video",
                    ex.Message);
                return StatusCode(500, error);
            }
        }
    }
}