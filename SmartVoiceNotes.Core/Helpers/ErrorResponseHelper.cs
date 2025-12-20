using SmartVoiceNotes.Core.DTOs;

namespace SmartVoiceNotes.Core.Helpers
{
    /// <summary>
    /// Helper for creating consistent error responses
    /// </summary>
    public static class ErrorResponseHelper
    {
        public static ErrorResponseDto CreateFileValidationError(string message, string? details = null)
        {
            return new ErrorResponseDto
            {
                ErrorCode = "FILE_VALIDATION_ERROR",
                Message = message,
                Details = details
            };
        }

        public static ErrorResponseDto CreateProcessingError(string source, string message, string? details = null)
        {
            return new ErrorResponseDto
            {
                ErrorCode = $"{source.ToUpperInvariant()}_ERROR",
                Message = message,
                Details = details
            };
        }

        public static ErrorResponseDto CreateConfigurationError(string message, string? details = null)
        {
            return new ErrorResponseDto
            {
                ErrorCode = "CONFIGURATION_ERROR",
                Message = message,
                Details = details
            };
        }

        public static ErrorResponseDto CreateGeneralError(string message, string? details = null)
        {
            return new ErrorResponseDto
            {
                ErrorCode = "GENERAL_ERROR",
                Message = message,
                Details = details
            };
        }
    }
}
