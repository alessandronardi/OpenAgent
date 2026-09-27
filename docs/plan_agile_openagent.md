# Piano di Sviluppo Agile: Desktop AI Assistant (OpenAgent)
**Target Hardware:** 8 GB RAM | AMD Radeon RX Vega 10 (iGPU)  
**Modello Primario:** Spark-X 2.5 (1.7B Q4_K_M per default, 4B opzionale)  
**Stack Core:** C# / .NET 8/9 + WPF | `llama-server` (Vulkan) | SQLite  
**Filosofia:** Zero-Bloat, Agent Safe Sandbox, Feedback Visivo dello Stato.

---

## 1. Identità Visiva e Scelte di Stile
* **Personaggio:** **Fantasmino in Pixel-Art retrò**, animato a basso frame rate (2–4 FPS in idle per risparmio CPU/GPU, 8–10 FPS negli stati attivi).
* **Interazione & Chat:** Fumetto ("balloon") compatto con angoli arrotondati posizionato sopra o accanto al fantasmino, corredato da una mini prompt-bar a comparsa (on-demand su click o hotkey globale).
* **Design System:** **Fluent Design scuro moderno** (ispirato a Windows 11: sfondo scuro neutro, effetto glass/acrylic sul fumetto, bordi sottili e tipografia pulita).

---

## 2. Strategia di Ottimizzazione Risorse (Budget 8 GB RAM)

Dato il vincolo rigido di 8 GB di memoria condivisa tra OS, iGPU Vega 10 e applicazioni utente:

| Componente | Memoria Stimata | Note |
| :--- | :--- | :--- |
| **Windows 10/11 + Servizi base** | ~3.5 – 4.0 GB | Baseline del sistema operativo |
| **VRAM Riservata (Vega 10)** | ~1.0 – 2.0 GB | Allocazione dinamica/statica BIOS |
| **Spark-X 2.5 1.7B (Q4_K_M)** | **~1.1 – 1.3 GB** | Pesi caricati via Vulkan offload su iGPU |
| **KV Cache (Hybrid Attention 4k)** | **~0.15 – 0.25 GB** | Bassissimo consumo grazie all'architettura ibrida |
| **WPF Shell (OpenAgent)** | **~0.15 – 0.25 GB** | Rendering a basso frame rate (2–4 FPS in idle) |
| **Margine Libero per l'Utente** | **~1.5 – 2.0 GB** | Navigatore o editor sempre reattivo senza swap su disco |

> **Decisione Tecnica:** **Spark-X 2.5 1.7B** è il modello predefinito. La variante 4B viene mantenuta come profilo configurabile se l'utente non ha altri carichi pesanti aperti.

---

## 2. Roadmap Agile (Sprint da 2-3 giorni)

### Sprint 0 — Spike Tecnico & Validazione Hardware (Priorità Massima)
*Obiettivo: Verificare token/s, consumo memoria reale e function calling su Vega 10 prima di sviluppare l'interfaccia.*
- [x] Setup `llama-server` precompilato con backend Vulkan (`ggml-vulkan.dll`).
- [x] Download pesi GGUF Spark-X 2.5 (1.7B Q4_K_M).
- [x] Benchmark inferenza su Vega 10: verifica di non saturare la memoria condivisa.
- [x] Test di Function Calling con `--jinja`: invocazione con schema JSON minimale per 2 tool di prova.

### Sprint 1 — Shell Desktop WPF & Gestione Processo
*Obiettivo: Finestra trasparente, zero processi orfani, tray icon e input prompt.*
- [x] Progetto WPF (.NET 8/9) con `AllowsTransparency="True"` e `WindowStyle="None"`.
- [x] Implementazione P/Invoke Win32 per click-through on-demand (`WS_EX_TRANSPARENT`).
- [x] Icona nella System Tray (Notifica/Mostra/Nascondi/Esci).
- [x] `ProcessManager` in C# per avviare/fermare `llama-server.exe` agganciato a un **Win32 Job Object** (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`).
- [x] Scorciatoia globale (`RegisterHotKey`) per mostrare la barra di input/chat.

### Sprint 2 — Modello, Macchina a Stati & Tool Sandbox File
*Obiettivo: Esecuzione del primo round-trip con modifica file controllata.*
- [x] Implementazione client HTTP asincrono verso `http://127.0.0.1:8080/v1/chat/completions`.
- [x] Visual State Machine (Idle, Thinking, ToolActive, Speaking, Error).
- [x] Registry `ITool` con validazione parametri via `System.Text.Json`.
- [x] Tool File:
  - `list_directory(path)`
  - `read_file(path, max_bytes=8192)` (hard cap per tutelare il contesto)
  - `move_or_rename(src, dest)`
- [x] `PermissionManager`: allowlist su `Documents`, `Downloads`, `Workspace`; blocco cartelle di sistema.

### Sprint 3 — Web Leggero, Sicurezza & Audit SQLite
*Obiettivo: Informazioni dal web a costo zero RAM e log verificabile.*
- [x] Tool Web: `fetch_url_text(url)` tramite `HttpClient` + parser compatto (HtmlAgilityPack) senza browser headless.
- [x] Dialogo di conferma UI obbligatorio per operazioni classificate come `DESTRUCTIVE` (cancellazione, sovrascrittura).
- [x] Database SQLite locale (`Microsoft.Data.Sqlite`) per tracciare ogni tool eseguito, orario e payload (Audit Trail).

### Sprint 4 — Agent Multi-Step Loop & Rifinitura
*Obiettivo: Esecuzione autonoma di task composti e rifinitura grafica.*
- [x] Loop multi-step limitato (max 4 passaggi consecutivi di tool).
- [x] Rilevamento loop (interruzione immediata se il modello richiede ripetutamente lo stesso tool con gli stessi argomenti).
- [x] Integrazione asset grafici definitivi (sprite/animazioni) concordati con l'utente.
- [x] Drag-and-Drop di file sulla finestra del personaggio per iniezione rapida del percorso.

### Sprint 5 — Prodotto Finale (V1.0): Settings UI, Memoria Esplicita, Autostart & Packaging
*Obiettivo: Trasformare l'assistente in un prodotto completo e distribuibile a zero manutenzione.*
- [x] **Pannello Impostazioni (Settings Window/Flyout in WPF)**:
  - Scelta profilo modello (Spark-X 1.7B default, 4B opzionale).
  - Parametri hardware: thread CPU (default 6), context size (4k/8k), porta server.
  - Gestione visiva della Allowlist con `FolderBrowserDialog` (aggiunta/rimozione cartelle autorizzate).
- [x] **Memoria Esplicita Persistente (SQLite User Memory)**:
  - Tabella `user_memory` in SQLite (`key`, `value`, `updated_at`).
  - Iniezione dinamica delle preferenze memorizzate nel System Prompt all'avvio sessione.
  - Tool dedicato `remember_user_preference(key, value)` e `recall_user_memory(key_pattern)` per memorizzazione esplicita.
- [x] **Integrazione con Windows & Autostart**:
  - Opzione "Avvia con Windows" configurabile da Tray/Settings (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
  - Gestione avvio silenzioso nella tray icon (`--minimized`).
- [x] **Packaging & Script di Pubblicazione**:
  - Script di build per release autonoma (`dotnet publish -c Release`).
  - Confezionamento pacchetto di installazione / archivio ZIP con runtime llama preconfigurato (`build_release.ps1`).
- [x] **Suite di Validazione Finale (`test_sprint5.ps1`)**:
  - Test automatici di persistenza impostazioni, memoria utente e verifica non-regressione (44 test superati con successo).

---

## 3. Criteri di Accettazione "Definition of Done"
1. **RAM Globale:** L'applicazione completa (WPF + llama-server + modello 1.7B) non deve superare 1.8 GB di RAM totale.
2. **Affidabilità Chiusura:** Chiudendo l'app dalla tray, nessun processo residuo `llama-server.exe` deve rimanere attivo in memoria.
3. **Sicurezza Filesystem:** Nessun file fuori dalla allowlist può essere letto o modificato.
4. **Persistenza & Autonomia:** Impostazioni, allowlist e memoria dell'agente sopravvivono ai riavvii di Windows.
