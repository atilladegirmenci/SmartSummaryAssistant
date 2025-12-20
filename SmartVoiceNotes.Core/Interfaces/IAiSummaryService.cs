using SmartVoiceNotes.Core.DTOs;

namespace SmartVoiceNotes.Core.Interfaces
{
    /// <summary>
    /// Service for generating AI-powered summaries of text content
    /// </summary>
    public interface IAiSummaryService
    {
        /// <summary>
        /// Generates a summary of the provided text with customizable options
        /// </summary>
        /// <param name="text">The text content to summarize</param>
        /// <param name="language">Target language for the summary</param>
        /// <param name="sourceType">Type of source content (e.g., "lecture", "meeting", "article")</param>
        /// <param name="style">Summary style (e.g., "detailed", "brief", "technical")</param>
        /// <param name="includeQuiz">Whether to include quiz questions in the response</param>
        /// <param name="qCount">Number of quiz questions to generate (if includeQuiz is true)</param>
        /// <param name="isVideo">Whether the source was a video (affects context understanding)</param>
        /// <returns>A response containing the summary, original transcription, and optional quiz questions</returns>
        Task<ProcessResponseDto> SummarizeTextAsync(string text, string language, string sourceType, string style, bool includeQuiz, short qCount, bool isVideo);
    }
}
