namespace SmartVoiceNotes.Core.Constants
{
    /// <summary>
    /// Constants for external API interactions
    /// </summary>
    public static class ApiConstants
    {
        /// <summary>
        /// Groq API constants
        /// </summary>
        public static class Groq
        {
            public const string BaseUrl = "https://api.groq.com/openai/v1/audio/transcriptions";
            public const string ModelName = "whisper-large-v3";
            public const string ConfigKeyPath = "AiSettings:GroqApiKey";
        }

        /// <summary>
        /// Gemini API constants
        /// </summary>
        public static class Gemini
        {
            public const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
            public const string ModelName = "gemini-2.5-flash";
            public const string ConfigKeyPath = "AiSettings:GeminiApiKey";
        }
    }
}
