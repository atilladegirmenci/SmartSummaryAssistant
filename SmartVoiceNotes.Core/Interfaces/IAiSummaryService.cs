using SmartVoiceNotes.Core.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartVoiceNotes.Core.Interfaces
{
    public interface IAiSummaryService
    {
        Task<ProcessResponseDto> SummarizeTextAsync(string text,string language, string sourceType, string style, bool includeQuiz, short qCount, bool isVideo);
    }
}
