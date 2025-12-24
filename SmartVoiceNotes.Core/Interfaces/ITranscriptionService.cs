using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartVoiceNotes.Core.Interfaces
{
    /// <summary>
    /// Service for transcribing audio from various sources
    /// </summary>
    public interface ITranscriptionService
    {
        /// <summary>
        /// Transcribes audio from a stream
        /// </summary>
        /// <param name="audioStream">The audio/video stream to transcribe</param>
        /// <param name="fileName">Original file name with extension (used for format detection)</param>
        /// <returns>The transcribed text</returns>
        Task<string> TranscribeAudioAsync(Stream audioStream, string fileName);

        /// <summary>
        /// Transcribes audio from a YouTube video
        /// </summary>
        /// <param name="ytUrl">YouTube video URL</param>
        /// <returns>The transcribed text from the video's audio</returns>
        Task<string> TranscribeYoutubeAsync(string ytUrl);
    }
}
