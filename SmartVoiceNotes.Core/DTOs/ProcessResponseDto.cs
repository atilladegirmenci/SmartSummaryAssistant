using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartVoiceNotes.Core.DTOs
{
    /// <summary>
    /// Response containing the result of audio/video processing
    /// </summary>
    public class ProcessResponseDto
    {
        /// <summary>
        /// Original transcription text from the audio/video
        /// </summary>
        public required string OriginalTranscription { get; set; } 

        /// <summary>
        /// AI-generated summary of the transcription
        /// </summary>
        public required string Summary { get; set; }

        /// <summary>
        /// Optional quiz questions generated from the content
        /// </summary>
        public required List<string> QuizQuestions { get; set; }   
    }
}
