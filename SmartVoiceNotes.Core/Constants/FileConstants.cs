namespace SmartVoiceNotes.Core.Constants
{
    /// <summary>
    /// File-related constants for validation and processing
    /// </summary>
    public static class FileConstants
    {
        /// <summary>
        /// Supported audio file extensions
        /// </summary>
        public static readonly string[] AudioExtensions = { ".mp3", ".wav", ".m4a" };

        /// <summary>
        /// Supported video file extensions
        /// </summary>
        public static readonly string[] VideoExtensions = { ".mp4", ".mov", ".avi", ".mkv", ".webm" };

        /// <summary>
        /// All supported file extensions (audio + video)
        /// </summary>
        public static readonly string[] AllSupportedExtensions = 
            AudioExtensions.Concat(VideoExtensions).ToArray();

        /// <summary>
        /// Maximum file size allowed for uploads (500 MB in bytes)
        /// </summary>
        public const long MaxFileSizeBytes = 500L * 1024 * 1024;

        /// <summary>
        /// Maximum file size in a human-readable format
        /// </summary>
        public const string MaxFileSizeDisplay = "500MB";
    }
}
