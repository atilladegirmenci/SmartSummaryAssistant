using FFMpegCore;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using Microsoft.Extensions.Configuration;
using SmartVoiceNotes.Core.Constants;
using SmartVoiceNotes.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SmartVoiceNotes.Infrastructure
{
    /// <summary>
    /// Implementation of transcription service using Groq's Whisper API
    /// </summary>
    public class GroqTranscriptionService : ITranscriptionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public GroqTranscriptionService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration[ApiConstants.Groq.ConfigKeyPath] 
                ?? throw new InvalidOperationException($"Groq API key is not configured. Please set {ApiConstants.Groq.ConfigKeyPath} in configuration.");
        }
        
        /// <inheritdoc/>
        public async Task<string> TranscribeYoutubeAsync(string youtubeUrl)
        {
            if (string.IsNullOrWhiteSpace(youtubeUrl))
                throw new ArgumentException("YouTube URL cannot be empty", nameof(youtubeUrl));

            var youtube = new YoutubeClient();
            string? tempFilePath = null;

            try
            {
                var video = await youtube.Videos.GetAsync(youtubeUrl);
                var title = video.Title; 

                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(youtubeUrl);

                // Get audio only
                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (streamInfo == null)
                    throw new InvalidOperationException("No audio stream available for this YouTube video. The video may be unavailable or restricted.");

                tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");

                await youtube.Videos.Streams.DownloadAsync(streamInfo, tempFilePath);

                await using (var stream = File.OpenRead(tempFilePath))
                {
                    return await TranscribeAudioAsync(stream, "youtube_audio.mp3");
                }
            }
            catch (Exception ex) when (!(ex is InvalidOperationException || ex is ArgumentException))
            {
                throw new InvalidOperationException($"Failed to process YouTube video. Ensure the URL is valid and the video is accessible. Error: {ex.Message}", ex);
            }
            finally
            {
                if (tempFilePath != null)
                {
                    try
                    {
                        if (File.Exists(tempFilePath))
                        {
                            File.Delete(tempFilePath);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup failures
                    }
                }
            }
        }
        
        /// <inheritdoc/>
        public async Task<string> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            if (audioStream == null)
                throw new ArgumentNullException(nameof(audioStream), "Audio stream cannot be null");
            
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be empty", nameof(fileName));

            // Save the stream to a temp file
            var tempFilePath = Path.GetTempFileName();
            var inputPath = Path.ChangeExtension(tempFilePath, Path.GetExtension(fileName));

            var filesToDelete = new List<string> { inputPath, tempFilePath };

            try
            {
                // Write stream to disk
                await using (var fileStream = File.Create(inputPath))
                {
                    await audioStream.CopyToAsync(fileStream);
                }

                
                if(IsVideoFile(inputPath))
                {
                    var extractedAudioPath = Path.ChangeExtension(Path.GetTempFileName(), ProcessingConstants.OutputAudioFormat);
                    filesToDelete.Add(extractedAudioPath);

                    await ExtractAudioFromVideoAsync(inputPath, extractedAudioPath);

                    inputPath = extractedAudioPath;
                }
                
                // File analysis
                var fileInfo = new FileInfo(inputPath);
                
                if (!fileInfo.Exists)
                    throw new FileNotFoundException("Temporary file was not created successfully", inputPath);
                
                long sizeInBytes = fileInfo.Length;

                if (sizeInBytes < ProcessingConstants.ChunkThresholdBytes)
                {
                    return await SendToGroqApi(inputPath);
                }

                // If large: Chunking
                return await ProcessLargeFileAsync(inputPath);
            }
            finally
            {
                // Cleanup: delete all temporary files
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup failures - these are temp files that will be cleaned eventually
                    }
                }
            }
        }

        /// <summary>
        /// Processes large audio files by splitting them into chunks and transcribing each chunk
        /// </summary>
        private async Task<string> ProcessLargeFileAsync(string inputPath)
        {
            var transcriptionBuilder = new StringBuilder();

            var mediaInfo = await FFProbe.AnalyseAsync(inputPath);
            var duration = mediaInfo.Duration;

            var chunkDuration = ProcessingConstants.ChunkDuration;
            var chunks = new List<string>();

            try
            {
                for (var currentTime = TimeSpan.Zero; currentTime < duration; currentTime += chunkDuration)
                {
                    var chunkPath = Path.ChangeExtension(Path.GetTempFileName(), ProcessingConstants.OutputAudioFormat);
                    chunks.Add(chunkPath);

                    await FFMpegArguments
                        .FromFileInput(inputPath)
                        .OutputToFile(chunkPath, true, options => options
                            .Seek(currentTime)
                            .WithDuration(chunkDuration))
                        .ProcessAsynchronously();

                    var chunkText = await SendToGroqApi(chunkPath);

                    transcriptionBuilder.Append(chunkText + " ");
                }
            }
            finally
            {
                // Cleanup: delete all chunk files
                foreach (var chunk in chunks)
                {
                    try
                    {
                        if (File.Exists(chunk))
                        {
                            File.Delete(chunk);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup failures - these are temp files
                    }
                }
            }

            return transcriptionBuilder.ToString();
        }

        /// <summary>
        /// Determines if a file is a video based on its extension
        /// </summary>
        private bool IsVideoFile(string path)
        {
            return FileConstants.VideoExtensions.Contains(Path.GetExtension(path).ToLower());
        }

        /// <summary>
        /// Extracts audio from a video file using FFmpeg
        /// </summary>
        private async Task ExtractAudioFromVideoAsync(string videoPath, string outputPath)
        {
            await FFMpegArguments
                .FromFileInput(videoPath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument($"-vn -acodec libmp3lame -q:a {ProcessingConstants.AudioQualitySetting}"))
                .ProcessAsynchronously();
        }

        /// <summary>
        /// Sends an audio file to the Groq API for transcription
        /// </summary>
        private async Task<string> SendToGroqApi(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Audio file not found for transcription", filePath);

            using var content = new MultipartFormDataContent();

            await using var fileStream = File.OpenRead(filePath);
            using var fileContent = new StreamContent(fileStream);

            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            content.Add(fileContent, "file", Path.GetFileName(filePath));
            content.Add(new StringContent(ApiConstants.Groq.ModelName), "model");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.PostAsync(ApiConstants.Groq.BaseUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Groq API request failed with status {response.StatusCode}. Response: {err}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            
            if (!doc.RootElement.TryGetProperty("text", out var textProperty))
                throw new InvalidOperationException("Transcription API response missing 'text' property. Response may be malformed.");
            
            return textProperty.GetString() 
                ?? throw new InvalidOperationException("Transcription API returned null text.");
        }
    }
}