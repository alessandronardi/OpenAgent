using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenAgent.Model.ProcessManager;

public enum ServerStatus
{
    Stopped,
    Starting,
    LoadingModel,
    Ready,
    Error
}

public sealed class LlamaProcessManager : IDisposable
{
    private readonly LlamaProcessConfig _config;
    private readonly HttpClient _httpClient;
    private JobObject? _jobObject;
    private Process? _process;
    private ServerStatus _status = ServerStatus.Stopped;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public event Action<ServerStatus>? StatusChanged;
    public event Action<string>? LogMessageReceived;

    public void Log(string message)
    {
        LogMessageReceived?.Invoke(message);
    }

    public ServerStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(_status);
            }
        }
    }

    public int? CurrentPid => _process is { HasExited: false } ? _process.Id : null;
    public bool IsReady => Status == ServerStatus.Ready;

    public LlamaProcessManager(LlamaProcessConfig? config = null)
    {
        _config = config ?? new LlamaProcessConfig();
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await StartInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> SwitchModelAsync(string newModelPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newModelPath);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            LogMessageReceived?.Invoke($"Richiesto hot-swap modello verso: {newModelPath}");
            await StopInternalAsync();
            _config.ModelPath = newModelPath;
            return await StartInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<bool> StartInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_process is { HasExited: false } && Status == ServerStatus.Ready)
            {
                return true;
            }

            while (true)
            {
                Status = ServerStatus.Starting;
                LogMessageReceived?.Invoke($"Avvio llama-server da: {_config.ExecutablePath} (Device: {_config.Device})");

                string exeAbs = Path.GetFullPath(_config.ExecutablePath);
                string modelAbs = Path.GetFullPath(_config.ModelPath);

                if (!File.Exists(exeAbs))
                {
                    Status = ServerStatus.Error;
                    throw new FileNotFoundException($"Eseguibile llama-server non trovato: {exeAbs}");
                }

                if (!File.Exists(modelAbs))
                {
                    Status = ServerStatus.Error;
                    throw new FileNotFoundException($"Pesi modello GGUF non trovati: {modelAbs}");
                }

                // Create JobObject
                _jobObject?.Dispose();
                _jobObject = new JobObject();

                string args = $"-m \"{modelAbs}\" --host {_config.Host} --port {_config.Port} -c {_config.ContextSize} " +
                              $"--device {_config.Device} -t {_config.Threads} " +
                              $"{(_config.EnableJinja ? "--jinja" : "")} " +
                              $"--log-file \"{Path.GetFullPath(_config.LogFilePath)}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = exeAbs,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(exeAbs) ?? AppContext.BaseDirectory
                };

                _process = new Process { StartInfo = startInfo };
                if (!_process.Start())
                {
                    Status = ServerStatus.Error;
                    throw new InvalidOperationException("Impossibile avviare il processo llama-server.");
                }

                // Attach immediately to the JobObject to prevent orphans even if crash occurs now
                _jobObject.AssignProcess(_process);
                LogMessageReceived?.Invoke($"Processo llama-server avviato (PID: {_process.Id}) e assegnato al Job Object.");

                // Poll /health endpoint
                bool ready = await WaitForHealthAsync(_config.StartupTimeoutSeconds, cancellationToken);
                if (ready)
                {
                    Status = ServerStatus.Ready;
                    LogMessageReceived?.Invoke($"llama-server pronto su {_config.BaseUrl} (RAM: {GetWorkingSetMegaBytes():F1} MB)");
                    return true;
                }

                // If not ready and device was not "none", try CPU fallback
                if (!string.Equals(_config.Device, "none", StringComparison.OrdinalIgnoreCase))
                {
                    LogMessageReceived?.Invoke($"Avvio con dispositivo '{_config.Device}' non riuscito. Fallback automatico su CPU ('none')...");
                    _config.Device = "none";
                    await StopInternalAsync();
                    continue;
                }

                Status = ServerStatus.Error;
                await StopInternalAsync();
                return false;
            }
        }
        catch (Exception ex)
        {
            Status = ServerStatus.Error;
            LogMessageReceived?.Invoke($"Errore avvio server: {ex.Message}");
            await StopInternalAsync();
            throw;
        }
    }

    private async Task<bool> WaitForHealthAsync(int timeoutSeconds, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        string healthUrl = $"{_config.BaseUrl}/health";

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_process == null || _process.HasExited)
            {
                LogMessageReceived?.Invoke($"Processo llama-server terminato inaspettatamente (ExitCode: {_process?.ExitCode})");
                return false;
            }

            try
            {
                var response = await _httpClient.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("status", out var statusProp))
                    {
                        string? statusStr = statusProp.GetString();
                        if (string.Equals(statusStr, "ok", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(statusStr, "ready", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (string.Equals(statusStr, "loading model", StringComparison.OrdinalIgnoreCase))
                        {
                            Status = ServerStatus.LoadingModel;
                        }
                    }
                }
                else if ((int)response.StatusCode == 503)
                {
                    Status = ServerStatus.LoadingModel;
                }
            }
            catch
            {
                // Server port not open yet, continue polling
            }

            await Task.Delay(1000, cancellationToken);
        }

        return false;
    }

    public async Task StopAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await StopInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task StopInternalAsync()
    {
        if (_process is { HasExited: false })
        {
            LogMessageReceived?.Invoke($"Arresto llama-server (PID: {_process.Id})...");
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                LogMessageReceived?.Invoke($"Avviso arresto: {ex.Message}");
            }
        }

        _process?.Dispose();
        _process = null;

        _jobObject?.Dispose();
        _jobObject = null;

        Status = ServerStatus.Stopped;
        LogMessageReceived?.Invoke("llama-server arrestato con successo.");
    }

    public double GetWorkingSetMegaBytes()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Refresh();
                return _process.WorkingSet64 / (1024.0 * 1024.0);
            }
        }
        catch
        {
            // Ignore if process just exited
        }
        return 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            try
            {
                if (_process is { HasExited: false })
                {
                    _process.Kill(entireProcessTree: true);
                }
                _process?.Dispose();
            }
            catch { }

            _jobObject?.Dispose();
            _lock.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
