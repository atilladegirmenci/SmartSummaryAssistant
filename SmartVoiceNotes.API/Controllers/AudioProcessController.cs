using Microsoft.AspNetCore.Mvc;
using SmartVoiceNotes.Core.Constants;
using SmartVoiceNotes.Core.DTOs;
using SmartVoiceNotes.Core.Helpers;
using SmartVoiceNotes.Core.Interfaces;
using System.Text.Json;

namespace SmartVoiceNotes.API.Controllers
{
    /// <summary>
    /// Controller for processing audio and video files into transcriptions and summaries
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AudioProcessController : ControllerBase
    {
        private readonly ITranscriptionService _transcriptionService;
        private readonly IAiSummaryService _summaryService;

        public AudioProcessController(ITranscriptionService transcriptionService, IAiSummaryService summaryService)
        {
            _transcriptionService = transcriptionService;
            _summaryService = summaryService;
        }

        /// <summary>
        /// Uploads and processes an audio or video file
        /// </summary>
        /// <param name="file">Audio or video file to process (max 500MB)</param>
        /// <param name="language">Target language for the summary</param>
        /// <param name="sourceType">Type of content (e.g., lecture, meeting)</param>
        /// <param name="style">Summary style (e.g., detailed, brief)</param>
        /// <param name="includeQuiz">Whether to generate quiz questions</param>
        /// <param name="qCount">Number of quiz questions to generate (0-20)</param>
        /// <returns>Processed response with transcription and summary</returns>
        [HttpPost("upload")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = 524288000)] // 500 MB
        public async Task<IActionResult> UploadAudio(IFormFile file, [FromQuery] string language, [FromQuery] string sourceType, [FromQuery] string style, [FromQuery] bool includeQuiz, [FromQuery] short qCount)
        {
            // Validate file
            if (file == null)
            {
                var error = ErrorResponseHelper.CreateFileValidationError("File validation failed", "No file provided");
                return BadRequest(error);
            }

            var fileError = ValidationHelper.ValidateFileInfo(file.FileName, file.Length);
            if (fileError != null)
            {
                var error = ErrorResponseHelper.CreateFileValidationError("File validation failed", fileError);
                return BadRequest(error);
            }

            // Validate processing parameters
            var paramError = ValidationHelper.ValidateProcessingParameters(language, sourceType, style, qCount);
            if (paramError != null)
            {
                var error = ErrorResponseHelper.CreateFileValidationError("Invalid parameters", paramError);
                return BadRequest(error);
            }

            var extension = Path.GetExtension(file.FileName).ToLower();
            bool isVideo = FileConstants.VideoExtensions.Contains(extension);

            try
            {
                using var stream = file.OpenReadStream();
    
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
        
        /// <summary>
        /// Processes a YouTube video by downloading and transcribing its audio
        /// </summary>
        /// <param name="url">YouTube video URL (youtube.com or youtu.be)</param>
        /// <param name="language">Target language for the summary</param>
        /// <param name="sourceType">Type of content (e.g., lecture, meeting)</param>
        /// <param name="style">Summary style (e.g., detailed, brief)</param>
        /// <param name="includeQuiz">Whether to generate quiz questions</param>
        /// <param name="qCount">Number of quiz questions to generate (0-20)</param>
        /// <returns>Processed response with transcription and summary</returns>
        [HttpPost("process-youtube")]
        public async Task<IActionResult> ProcessYoutube([FromQuery] string url, [FromQuery] string language, [FromQuery] string sourceType, [FromQuery] string style, [FromQuery] bool includeQuiz, [FromQuery] short qCount)
        {
            // Validate YouTube URL
            var urlError = ValidationHelper.ValidateYoutubeUrl(url);
            if (urlError != null)
            {
                var error = ErrorResponseHelper.CreateFileValidationError("Invalid YouTube URL", urlError);
                return BadRequest(error);
            }

            // Validate processing parameters
            var paramError = ValidationHelper.ValidateProcessingParameters(language, sourceType, style, qCount);
            if (paramError != null)
            {
                var error = ErrorResponseHelper.CreateFileValidationError("Invalid parameters", paramError);
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