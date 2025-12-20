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
        public async Task<string> TranscribeYoutubeAsync(string youtubeUrl)
        {
            var youtube = new YoutubeClient();

            try
            {
                var video = await youtube.Videos.GetAsync(youtubeUrl);
                var title = video.Title; 

                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(youtubeUrl);

                // get audio only
                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (streamInfo == null)
                    throw new InvalidOperationException("No audio stream available for this YouTube video. The video may be unavailable or restricted.");

                var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");

                await youtube.Videos.Streams.DownloadAsync(streamInfo, tempFilePath);

                try
                {
                    using (var stream = File.OpenRead(tempFilePath))
                    {
                        return await TranscribeAudioAsync(stream, "youtube_audio.mp3");
                    }
                }
                finally
                {
                    if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to process YouTube video. Ensure the URL is valid and the video is accessible. Error: {ex.Message}", ex);
            }
        }
        public async Task<string> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            // Save the stream to a temp file
            var tempFilePath = Path.GetTempFileName();
            var inputPath = Path.ChangeExtension(tempFilePath, Path.GetExtension(fileName));

            var filesToDelete = new List<string> { inputPath, tempFilePath };

            try
            {
                // Write stream to disk
                using (var fileStream = File.Create(inputPath))
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
                // 2. FILE ANALYSIS
                var fileInfo = new FileInfo(inputPath);
                long sizeInBytes = fileInfo.Length;

                if (sizeInBytes < ProcessingConstants.ChunkThresholdBytes)
                {
                    return await SendToGroqApi(inputPath);
                }

                // if its large: Chunking
                return await ProcessLargeFileAsync(inputPath);
            }
            finally
            {
                foreach (var file in filesToDelete)
                {
                    if (File.Exists(file)) File.Delete(file);
                }
            }
        }

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
                foreach (var chunk in chunks)
                {
                    if (File.Exists(chunk)) File.Delete(chunk);
                }
            }

            return transcriptionBuilder.ToString();
        }

        private bool IsVideoFile(string path)
        {
            return FileConstants.VideoExtensions.Contains(Path.GetExtension(path).ToLower());
        }

        private async Task ExtractAudioFromVideoAsync(string videoPath, string outputPath)
        {
            await FFMpegArguments
                .FromFileInput(videoPath)
                .OutputToFile(outputPath, true, options => options
                    .WithCustomArgument($"-vn -acodec libmp3lame -q:a {ProcessingConstants.AudioQualitySetting}"))
                .ProcessAsynchronously();
        }

        private async Task<string> SendToGroqApi(string filePath)
        {
            using var content = new MultipartFormDataContent();

            using var fileStream = File.OpenRead(filePath);
            var fileContent = new StreamContent(fileStream);

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
            
            var textProperty = doc.RootElement.GetProperty("text");
            return textProperty.GetString() 
                ?? throw new InvalidOperationException("Transcription API returned null text.");
        }
    }
}