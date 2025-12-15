using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartVoiceNotes.Core.Interfaces
{
    public interface ITranscriptionService
    {
        Task<string> TranscribeAudioAsync(Stream audioStream, string fileName);
        Task<string> TranscribeYoutubeAsync(string ytUrl);
    }
}
