namespace SmartVoiceNotes.Core.DTOs
{
    /// <summary>
    /// Structured error response for API endpoints
    /// </summary>
    public class ErrorResponseDto
    {
        public required string ErrorCode { get; set; }
        public required string Message { get; set; }
        public string? Details { get; set; }
        public Dictionary<string, string[]>? ValidationErrors { get; set; }
    }
}
