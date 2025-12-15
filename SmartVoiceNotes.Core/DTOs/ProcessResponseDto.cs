using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartVoiceNotes.Core.DTOs
{
    public class ProcessResponseDto
    {
        public string OriginalTranscription { get; set; } 
        public string Summary { get; set; }               
        public List<string> QuizQuestions { get; set; }   
    }
}
