namespace SmartVoiceNotes.Core.Constants
{
    /// <summary>
    /// Constants for audio/video processing operations
    /// </summary>
    public static class ProcessingConstants
    {
        /// <summary>
        /// File size threshold for determining if chunking is needed (20 MB in bytes)
        /// </summary>
        public const long ChunkThresholdBytes = 20L * 1024 * 1024;

        /// <summary>
        /// Duration of each audio chunk when splitting large files (10 minutes)
        /// </summary>
        public static readonly TimeSpan ChunkDuration = TimeSpan.FromMinutes(10);

        /// <summary>
        /// FFmpeg audio quality setting for MP3 extraction
        /// Range: 0-9, where 2 is high quality, 9 is low quality
        /// </summary>
        public const string AudioQualitySetting = "2";

        /// <summary>
        /// Output audio format for extracted audio from videos
        /// </summary>
        public const string OutputAudioFormat = ".mp3";
    }
}
