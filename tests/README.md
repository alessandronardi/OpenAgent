# 🧪 OpenAgent — Suite di Test Automatizzati

Tutti i test di integrazione, sicurezza e validazione funzionale di **OpenAgent** sono racchiusi in questa cartella.

---

## 🚀 Come Eseguire i Test

I test sono scritti in PowerShell per .NET 8 e richiedono **PowerShell 7 (`pwsh`)**.

### 1. Eseguire l'intera suite di test
Dalla radice del progetto:
```powershell
pwsh -File tests/run_all_tests.ps1
```

### 2. Esecuzione rapida offline (senza avvio del modello locale)
Per eseguire tutti i test unitari e di sandboxing saltando l'inferenza live:
```powershell
pwsh -File tests/run_all_tests.ps1 -SkipLiveServer
```

### 3. Eseguire un singolo Sprint o modulo
Puoi filtrare lo sprint desiderato:
```powershell
pwsh -File tests/run_all_tests.ps1 -Sprint 5
# Oppure lanciando direttamente il file dello sprint:
pwsh -File tests/test_sprint5.ps1
```

---

## 📋 Struttura della Suite

| Script | Modulo / Sprint | Descrizione |
| :--- | :--- | :--- |
| **`run_all_tests.ps1`** | **Runner Unificato** | Esegue in sequenza tutti i test suite, raccoglie i tempi di esecuzione e genera un report riassuntivo con esito [PASS]/[FAIL]. |
| **`test_process_manager.ps1`** | **Sprint 1 — Core** | Verifica la corretta compilazione delle librerie del modello e il ciclo di vita del `LlamaProcessManager` agganciato a Win32 Job Objects. |
| **`test_spike.ps1`** | **Sprint 0 — Spike** | Benchmark hardware tramite `llama-bench`, avvio standalone di `llama-server` e validazione del Function Calling via endpoint OpenAI-compatible (`/v1/chat/completions`). |
| **`test_sprint2.ps1`** | **Sprint 2 — Tools & Sandbox** | Tool Registry, sandboxing del file system (`list_directory`, `read_file` con limite 8192 byte, `move_or_rename`), `PermissionManager`, allowlist percorsi e transizioni della `AgentStateMachine`. |
| **`test_sprint3.ps1`** | **Sprint 3 — Audit & Web Safety** | Logging su SQLite (`SqliteAuditRepository`), flusso di conferma utente (Accetta/Rifiuta), tool di estrazione testo Web (`FetchUrlTextTool`, sanitizzazione HTML) e blocco percorsi protetti. |
| **`test_sprint4.ps1`** | **Sprint 4 — Multi-Step & Loops** | Loop multi-step ricorsivo bounded (`AgentController`), tracker semantico anti-loop (`SemanticCallTracker`), gestione timeout e cancellazione (`CancellationToken`), logica Drag-and-Drop e test end-to-end live. |
| **`test_sprint5.ps1`** | **Sprint 5 — Memory & Settings** | Persistenza impostazioni su SQLite (`SqliteUserSettingsRepository`), memoria esplicita a lungo termine (`IUserMemoryRepository`), tool di memorizzazione/richiamo (`remember_user_preference`, `recall_user_memory`), iniezione dinamica nel system prompt, Windows Autostart e verifica pacchetto release. |
| **`test_hardware_profiler.ps1`** | **Hardware Profiler** | Rilevamento automatico specifiche hardware (`HardwareDetector`: CPU, core, RAM, GPU) e auto-tuning euristico dei parametri ottimali (`HardwareProfiler`). |
| **`test_gemini_cloud_integration.ps1`** | **Cloud Integration (Gemini)** | Integrazione ibrida Cloud (`GeminiClient`: 3.8 Flash + 3.5 Flash-Lite), fallback resiliente, rilevamento complessità prompt (`ModelTaskRouter`) ed escalation su 2 errori locali con consenso privacy. |
| **`test_voice_service.ps1`** | **Voce & Whisper STT** | Verifica del motore vocale locale Whisper C++ (`Whisper.net`), modello `ggml-small.bin` ad alta precisione e cattura audio `NAudio`. |

---

## 🧹 Gestione File Temporanei

Tutti i database di prova, sandbox e file fittizi generati durante i test vengono confinati nella cartella temporanea `tests/temp/` e ripuliti automaticamente al termine dell'esecuzione, garantendo che la radice del repository e le cartelle di progetto restino sempre pulite.
