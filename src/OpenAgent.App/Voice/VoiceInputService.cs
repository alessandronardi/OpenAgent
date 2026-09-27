using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace OpenAgent.App.Voice;

public sealed class VoiceInputService : IDisposable
{
    private readonly string _modelPath;
    private WhisperFactory? _whisperFactory;
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioStream;
    private WaveFileWriter? _waveWriter;
    private readonly object _lock = new();

    private bool _isRecording;
    private bool _isProcessing;
    private bool _disposed;

    public bool IsRecording => _isRecording;
    public bool IsProcessing => _isProcessing;

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    public VoiceInputService(string? modelPath = null)
    {
        ConfigureNativeLibraryPath();

        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            _modelPath = modelPath;
        }
        else
        {
            // Default model location in models/ggml-tiny.bin
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localModelsDir = Path.Combine(Directory.GetCurrentDirectory(), "models", "ggml-tiny.bin");
            string appModelsDir = Path.Combine(baseDir, "models", "ggml-tiny.bin");

            _modelPath = File.Exists(localModelsDir) ? localModelsDir : appModelsDir;
        }
    }

    public bool IsModelAvailable()
    {
        return File.Exists(_modelPath);
    }

    public async Task<bool> EnsureModelAvailableAsync(IProgress<double>? progress = null)
    {
        if (IsModelAvailable())
        {
            return true;
        }

        try
        {
            StatusChanged?.Invoke("Scaricamento modello Whisper locale (75 MB)...");
            string? dir = Path.GetDirectoryName(_modelPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Tiny);
            await using var fileStream = File.OpenWrite(_modelPath);
            await modelStream.CopyToAsync(fileStream);

            StatusChanged?.Invoke("Modello Whisper locale pronto.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Errore nel download del modello Whisper: {ex.Message}");
            return false;
        }
    }

    private static bool _nativeLibraryConfigured;

    public static void ConfigureNativeLibraryPath()
    {
        if (_nativeLibraryConfigured) return;

        try
        {
            string baseDir = AppContext.BaseDirectory;
            string currentDir = Directory.GetCurrentDirectory();

            string[] candidates = new[]
            {
                baseDir,
                currentDir,
                Path.Combine(baseDir, "runtimes", "win-x64"),
                Path.Combine(currentDir, "runtimes", "win-x64"),
                Path.Combine(currentDir, "dist", "OpenAgent_v1.0"),
                Path.Combine(currentDir, "src", "OpenAgent.App", "bin", "Debug", "net8.0-windows"),
                Path.Combine(currentDir, "src", "OpenAgent.App", "bin", "Release", "net8.0-windows", "win-x64")
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate)) continue;

                string runtimeSubdir = Path.Combine(candidate, "runtimes", "win-x64", "whisper.dll");
                string directDll = Path.Combine(candidate, "whisper.dll");

                if (File.Exists(runtimeSubdir))
                {
                    // Whisper.net's GetSafeDirectoryName calls Path.GetDirectoryName on LibraryPath.
                    // By passing a file path inside candidate, GetDirectoryName returns candidate!
                    Whisper.net.LibraryLoader.RuntimeOptions.LibraryPath = Path.Combine(candidate, "OpenAgent.dll");
                    _nativeLibraryConfigured = true;
                    return;
                }
                else if (File.Exists(directDll))
                {
                    Whisper.net.LibraryLoader.RuntimeOptions.LibraryPath = Path.Combine(candidate, "OpenAgent.dll");
                    _nativeLibraryConfigured = true;
                    return;
                }
            }
        }
        catch
        {
            // Ignore and let default loader attempt resolution
        }
    }

    public void PreloadModel()
    {
        if (_whisperFactory == null && File.Exists(_modelPath))
        {
            try
            {
                ConfigureNativeLibraryPath();
                _whisperFactory = WhisperFactory.FromPath(_modelPath);
            }
            catch (Exception ex)
            {
                // Attempt self-healing: if runtimes folder exists in known sources, copy to current base
                try
                {
                    string baseDir = AppContext.BaseDirectory;
                    string runtimesDest = Path.Combine(baseDir, "runtimes", "win-x64");
                    string[] possibleSources = new[]
                    {
                        Path.Combine(Directory.GetCurrentDirectory(), "runtimes", "win-x64"),
                        Path.Combine(Directory.GetCurrentDirectory(), "dist", "OpenAgent_v1.0", "runtimes", "win-x64"),
                        Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenAgent.App", "bin", "Debug", "net8.0-windows", "runtimes", "win-x64")
                    };

                    foreach (var src in possibleSources)
                    {
                        if (Directory.Exists(src) && File.Exists(Path.Combine(src, "whisper.dll")))
                        {
                            if (!Directory.Exists(runtimesDest)) Directory.CreateDirectory(runtimesDest);
                            foreach (var file in Directory.GetFiles(src, "*.dll"))
                            {
                                File.Copy(file, Path.Combine(runtimesDest, Path.GetFileName(file)), true);
                            }
                            Whisper.net.LibraryLoader.RuntimeOptions.LibraryPath = Path.Combine(baseDir, "OpenAgent.dll");
                            _whisperFactory = WhisperFactory.FromPath(_modelPath);
                            return;
                        }
                    }
                }
                catch { }

                ErrorOccurred?.Invoke($"Errore inizializzazione motore Whisper: {ex.Message}");
            }
        }
    }

    public bool StartRecording()
    {
        if (_isRecording)
        {
            return false;
        }

        if (WaveInEvent.DeviceCount == 0)
        {
            ErrorOccurred?.Invoke("Nessun microfono rilevato nel sistema. Collega un dispositivo audio.");
            return false;
        }

        try
        {
            lock (_lock)
            {
                _audioStream = new MemoryStream();

                // Whisper expects 16,000 Hz, 16-bit, Mono audio
                var waveFormat = new WaveFormat(16000, 16, 1);
                _waveIn = new WaveInEvent
                {
                    WaveFormat = waveFormat,
                    BufferMilliseconds = 100
                };

                _waveWriter = new WaveFileWriter(_audioStream, waveFormat);

                _waveIn.DataAvailable += (s, e) =>
                {
                    lock (_lock)
                    {
                        if (_waveWriter != null && e.BytesRecorded > 0)
                        {
                            _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
                        }
                    }
                };

                _waveIn.RecordingStopped += (s, e) =>
                {
                    // Recording stopped event hook
                };

                _waveIn.StartRecording();
                _isRecording = true;
            }

            RecordingStarted?.Invoke();
            StatusChanged?.Invoke("🎙️ In ascolto...");
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Errore durante l'avvio della registrazione: {ex.Message}");
            CleanupRecordingResources();
            return false;
        }
    }

    public async Task<string> StopRecordingAndTranscribeAsync(string language = "it")
    {
        if (!_isRecording && _audioStream == null)
        {
            return string.Empty;
        }

        _isRecording = false;
        _isProcessing = true;
        RecordingStopped?.Invoke();
        StatusChanged?.Invoke("🎙️ Trascrizione vocale in corso...");

        try
        {
            lock (_lock)
            {
                try
                {
                    _waveIn?.StopRecording();
                }
                catch { }

                try
                {
                    _waveWriter?.Flush();
                }
                catch { }
            }

            // Small delay to allow any pending buffer write
            await Task.Delay(50);

            if (_audioStream == null || _audioStream.Length < 4000)
            {
                // Audio too short (< 0.2s)
                StatusChanged?.Invoke("Audio troppo breve.");
                return string.Empty;
            }

            if (!File.Exists(_modelPath))
            {
                bool downloaded = await EnsureModelAvailableAsync();
                if (!downloaded)
                {
                    return string.Empty;
                }
            }

            if (_whisperFactory == null)
            {
                ConfigureNativeLibraryPath();
                _whisperFactory = WhisperFactory.FromPath(_modelPath);
            }

            _audioStream.Position = 0;

            // Process audio with Whisper
            using var processor = _whisperFactory.CreateBuilder()
                .WithLanguage(language)
                .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 6))
                .Build();

            var sb = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(_audioStream))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                {
                    sb.Append(segment.Text);
                    sb.Append(' ');
                }
            }

            string result = sb.ToString().Trim();
            return result;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Errore durante la trascrizione vocale: {ex.Message}");
            return string.Empty;
        }
        finally
        {
            _isProcessing = false;
            CleanupRecordingResources();
        }
    }

    public void CancelRecording()
    {
        _isRecording = false;
        _isProcessing = false;
        CleanupRecordingResources();
        RecordingStopped?.Invoke();
    }

    private void CleanupRecordingResources()
    {
        lock (_lock)
        {
            try
            {
                _waveIn?.Dispose();
            }
            catch { }
            _waveIn = null;

            try
            {
                _waveWriter?.Dispose();
            }
            catch { }
            _waveWriter = null;

            try
            {
                _audioStream?.Dispose();
            }
            catch { }
            _audioStream = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CancelRecording();
            _whisperFactory?.Dispose();
            _whisperFactory = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
