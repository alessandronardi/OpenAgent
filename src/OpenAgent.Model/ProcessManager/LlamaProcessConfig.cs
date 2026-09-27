namespace OpenAgent.Model.ProcessManager;

public sealed class LlamaProcessConfig
{
    public string ExecutablePath { get; set; } = "runtime/llama/llama-server.exe";
    public string ModelPath { get; set; } = "models/Spark-X2.5-1.7B-Q4_K_M.gguf";
    public int Port { get; set; } = 8080;
    public string Host { get; set; } = "127.0.0.1";
    public int ContextSize { get; set; } = 4096;
    public int Threads { get; set; } = 6;
    public string Device { get; set; } = "none"; // CPU fallback as validated in Sprint 0
    public bool EnableJinja { get; set; } = true;
    public string LogFilePath { get; set; } = "data/llama_server.log";
    public int StartupTimeoutSeconds { get; set; } = 45;

    public string BaseUrl => $"http://{Host}:{Port}";
}
