using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartVoiceNotes.Core.DTOs
{
    public class ProcessResponseDto
    {
        public required string OriginalTranscription { get; set; } 
        public required string Summary { get; set; }               
        public required List<string> QuizQuestions { get; set; }   
    }
}
