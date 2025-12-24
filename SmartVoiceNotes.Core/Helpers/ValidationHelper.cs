using SmartVoiceNotes.Core.Constants;

namespace SmartVoiceNotes.Core.Helpers
{
    /// <summary>
    /// Validation helpers for input parameters
    /// </summary>
    public static class ValidationHelper
    {
        /// <summary>
        /// Validates file information for format and size
        /// </summary>
        /// <returns>Null if valid, error message if invalid</returns>
        public static string? ValidateFileInfo(string fileName, long fileSize)
        {
            if (fileSize == 0)
                return "No file provided";

            var extension = Path.GetExtension(fileName).ToLower();
            if (string.IsNullOrEmpty(extension))
                return "File has no extension";

            if (!FileConstants.AllSupportedExtensions.Contains(extension))
                return $"Unsupported file format '{extension}'. Supported formats: {string.Join(", ", FileConstants.AllSupportedExtensions)}";

            if (fileSize > FileConstants.MaxFileSizeBytes)
                return $"File size ({fileSize / (1024.0 * 1024.0):F2} MB) exceeds maximum allowed size of {FileConstants.MaxFileSizeDisplay}";

            return null;
        }

        /// <summary>
        /// Validates a YouTube URL
        /// </summary>
        /// <returns>Null if valid, error message if invalid</returns>
        public static string? ValidateYoutubeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "YouTube URL is required";

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return "Invalid URL format";

            var host = uri.Host.ToLowerInvariant();
            if (!host.Contains("youtube.com") && !host.Contains("youtu.be"))
                return "URL must be from YouTube (youtube.com or youtu.be)";

            return null;
        }

        /// <summary>
        /// Validates query parameters for processing
        /// </summary>
        public static string? ValidateProcessingParameters(string? language, string? sourceType, string? style, short qCount)
        {
            if (string.IsNullOrWhiteSpace(language))
                return "Language parameter is required";

            if (string.IsNullOrWhiteSpace(sourceType))
                return "Source type parameter is required";

            if (string.IsNullOrWhiteSpace(style))
                return "Style parameter is required";

            if (qCount < 0 || qCount > 20)
                return "Question count must be between 0 and 20";

            return null;
        }
    }
}
