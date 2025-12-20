using AngleSharp.Text;
using Microsoft.Extensions.Configuration;
using SmartVoiceNotes.Core.DTOs;
using SmartVoiceNotes.Core.Interfaces;
using System.ComponentModel;
using System;
using System.Text;
using System.Text.Json;

namespace SmartVoiceNotes.Infrastructure
{
    public class GeminiSummaryService : IAiSummaryService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public GeminiSummaryService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["AiSettings:GeminiApiKey"]
                ?? throw new InvalidOperationException("Gemini API key is not configured. Please set AiSettings:GeminiApiKey in configuration.");
        }

        public async Task<ProcessResponseDto> SummarizeTextAsync(string text, string language, string sourceType, string summaryStyle, bool includeQuiz, short questionCount, bool isVideo)
        {
            var cleanKey = _apiKey.Trim();
            string prompt = string.Empty;

            string context = isVideo 
                ? "Note: This transcript is derived from a video recording. Pay attention to visual references made by the speaker (e.g., 'look here', 'on this slide')."
                : "Note: This transcript is from an audio recording.";

            string mathInstraction = @"CRITICAL INSTRUCTION FOR MATH:
            If the summary contains mathematical formulas, equations, or symbols, YOU MUST write them in LaTeX format.
                 -Enclose inline math in single dollar signs(e.g., $E = mc ^ 2$).
                 - Enclose block equations in double dollar signs(e.g., $$ \int_0 ^\infty x ^ 2 dx $$).";

            if (includeQuiz)
            {
                prompt = $@"
                Analyze the following {sourceType} text carefully.
                1. Write a {summaryStyle} summary in {language} language.
                2. Create {questionCount} multiple-choice quiz questions in {language} language.
                {context}

                {mathInstraction}
    
                Text: {text}";
            }
            else
            {
                prompt = $@"
                Analyze the following {sourceType} text carefully.
                1. Write a {summaryStyle} summary in {language} language.
                {context}

                {mathInstraction}
    
                Text: {text}";
            }
           

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            // Gemini 2.5 Flash
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={cleanKey}";

            var response = await _httpClient.PostAsync(url, jsonContent);

            // Error handling
            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                throw new Exception($"Google API Error ({response.StatusCode}): {errorJson}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                throw new Exception("AI content safety policy prevented response generation.");
            }


            // Parsing the nested JSON response from Gemini
            var geminiText = doc.RootElement.GetProperty("candidates")[0]
                .GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString()
                ?? throw new InvalidOperationException("Gemini API returned null text in response.");

            return new ProcessResponseDto
            {
                
                Summary = geminiText,
                QuizQuestions = new List<string>(),
                OriginalTranscription = text
            };

        }
    }
}