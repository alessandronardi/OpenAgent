# 👻 OpenAgent — Assistente Desktop AI Locale & Ibrido

Benvenuto in **OpenAgent**, il tuo assistente desktop per Windows intelligente, discreto, reattivo e orientato alla **massima privacy**.  
OpenAgent combina la riservatezza e l'immediatezza dell'inferenza **100% locale** (tramite `llama.cpp` e modelli Whisper STT) con la flessibilità del **routing intelligente multi-modello** e dell'**escalation opzionale su Cloud (Google Gemini)** per i compiti più complessi.

---

## 🌟 Caratteristiche Principali

* 🔒 **Architettura Privacy-First:** Di default, ogni calcolo, testo e file risiede esclusivamente sul tuo computer. Nessun dato viene trasmesso a server esterni senza il tuo consenso preventivo.
* 🎙️ **Dettatura Vocale Offline (Whisper STT):** Parla direttamente con l'assistente! Trascrizione vocale locale in tempo reale con motore nativo C++ Whisper e modello compatto integrato (nessun streaming audio cloud).
* 🧠 **Routing Intelligente dei Modelli (ModelTaskRouter):** Assegna automaticamente o manualmente la richiesta al modello più efficace per il tipo di compito (file system, comandi desktop, ricerca web, sintesi o logica).
* ☁️ **Cloud Routing & Escalation Ibrida (Google Gemini):** Integrazione nativa con **Gemini 3.8 Flash** (1 milione di token di contesto) per compiti ad alta complessità e architettura software, con fallback resiliente su **Gemini 3.5 Flash-Lite** e policy di escalation automatica dopo 2 errori locali.
* ⚡ **Hardware Profiler & Auto-Tune:** Rileva all'avvio la CPU, i core fisici/logici, la RAM e il supporto Vulkan del PC, calibrando in automatico thread, contesti e timeout.
* 🛡️ **Zero Trust Sandbox & Allowlist:** Nessuna operazione di modifica o cancellazione file avviene senza autorizzazione grafica esplicita. Le cartelle di sistema Windows sono protette a livello di kernel.
* 🎨 **Avatar Retro Pixel-Art & Click-Through:** Una presenza discreta sullo schermo con animazioni (respiro idle, battito delle palpebre, outfit dinamici per i tool) e modalità *Click-Through* per non intralciare il lavoro quotidiano.

---

## 🚀 Come Avviarlo

1. Fai doppio clic sul file **`OpenAgent.exe`** (presente nella cartella principale).
2. L'avatar pixel-art comparirà nell'angolo in basso a destra dello schermo, insieme all'icona nella barra delle applicazioni / tray vicino all'orologio di Windows.
3. Quando il pallino accanto al messaggio diventa verde (**"In ascolto"**), OpenAgent è pronto ad assisterti.

---

## 💬 Modalità di Interazione e Scorciatoie Rapide

| Azione | Scorciatoia / Controllo | Descrizione |
| :--- | :--- | :--- |
| **Mostra / Nascondi Chat** | **`Ctrl + Spazio`** *(oppure `Alt + Spazio`)* | Fa apparire istantaneamente la barra di digitazione da qualsiasi programma o finestra. |
| **Input Vocale / Dettatura** | **`Ctrl + Shift + V`** *(o pulsante 🎙️)* | Avvia/arresta la registrazione vocale; la trascrizione locale appare direttamente nel prompt. |
| **Invia Richiesta** | **`Invio`** *(o freccia ➔)* | Invia la richiesta all'agente ed avvia il ciclo di analisi e inferenza. |
| **Interrompi Operazione** | **`Esc`** *(o pulsante ⏹ rosso)* | Blocca immediatamente l'esecuzione dell'agente o cancella la registrazione in corso. |
| **Drag & Drop File/Cartelle** | *Trascina sul fumetto* | Rilascia file o cartelle sull'overlay dell'avatar per predisporre l'analisi automatica. |
| **Sposta l'Avatar** | *Trascina con tasto sinistro* | Tieni premuto il mouse sulla mascotte per posizionarla dove preferisci sul desktop. |
| **Modalità Click-Through** | *Icona Lucchetto 🔓/🔒* | Permette ai clic del mouse di passare attraverso la finestra, utile per tenerla sempre visibile. |
| **Cambio Modello Rapido** | *Clic sul badge profilo* | Clicca su `· Modello Locale` nell'intestazione per visualizzare e alternare al volo i profili. |

---

## 🎙️ Funzionalità Vocale (Speech-to-Text Locale)

OpenAgent include un sottosistema vocale completamente integrato basato su **Whisper.net** e **NAudio**:
* **100% Offline & Privato:** L'audio catturato dal microfono (16 kHz, 16-bit mono) viene elaborato direttamente in memoria dalla libreria nativa C++ `whisper.dll`. Nessun pacchetto audio esce dalla macchina.
* **Modello Integrato (`ggml-tiny.bin`):** Modello compatto (~75 MB) ottimizzato per lingua italiana ed inglese, pre-caricato all'avvio per tempi di risposta inferiori a un secondo.
* **Auto-Recovery:** Se il file del modello non è presente, OpenAgent può scaricarlo automaticamente e predisporre le librerie native senza configurazione manuale.

---

## 🧠 Modelli Supportati & Routing Intelligente

OpenAgent supporta un'ampia gamma di modelli locali GGUF e cloud:

### 1. Modelli Locali (In-Memory tramite `llama.cpp` Vulkan/CPU)

| Profilo | Modello | RAM Richiesta | Velocità Indicativa | Specializzazione Primaria |
| :--- | :--- | :--- | :--- | :--- |
| **Desktop & Tools** | `Qwen2.5-Coder 1.5B` | ~1.4 GB | ~23.5 tok/s *(Ultra-rapido)* | Chiamata accurata dei tool, file system, percorsi Windows e script. |
| **Web & Reasoning** | `Qwen2.5 3B Instruct` | ~2.6 - 3.3 GB | ~12.2 tok/s *(Ponderato)* | Navigazione web, estrazione testi da URL, sintesi contesti lunghi. |
| **Logica & Sintesi** | `Gemma-2 2B Instruct` | ~1.6 - 2.1 GB | ~18.0 tok/s *(Bilanciato)* | Redazione fluida, correzione bozze, sintesi e logica concettuale. |
| **Generale / Batteria** | `Spark-X 2.5 1.7B` | ~1.2 - 1.9 GB | ~20.0 tok/s *(Leggero)* | Conversazioni quotidiane, promemoria, impatto minimo sulla batteria. |
| **Personalizzato** | *File GGUF a scelta* | Dipende dai pesi | Variabile | Possibilità di selezionare qualsiasi modello compatibile `llama.cpp`. |

### 2. Modello Cloud Ibrido (Google Gemini)

* **Gemini 3.8 Flash (Primario):** Contesto enorme fino a **1 milione di token**, ideale per analisi di interi progetti, refactoring architetturale (CQRS, thread-safety, dependency injection) e compiti multi-step complessi.
* **Gemini 3.5 Flash-Lite (Fallback):** Entra in funzione automaticamente in caso di rate limit (HTTP 429) o sovraccarico temporaneo di rete.

### 3. ModelTaskRouter Euristico

Il motore interno `ModelTaskRouter` analizza costantemente i prompt dell'utente classificandoli in domini:
* 📂 **Desktop & Tool Calling:** Attiva il profilo Coder 1.5B per la massima precisione su file e cartelle.
* 🌐 **Web Research:** Attiva Qwen 3B quando rileva URL o richieste di ricerca online.
* 📝 **Text Reasoning & Synthesis:** Attiva Gemma-2 2B per traduzioni, saggi e riassunti.
* ⚡ **Complex Reasoning Cloud:** Rileva prompt lunghi (>750 caratteri) o richieste architetturali complesse proponendo l'uso di Gemini 3.8 Flash.

---

## ☁️ Cloud Routing & Policy di Privacy

La connessione al Cloud è strettamente **opzionale** e conforme al principio di sovranità dei dati:
1. **Consenso Preventivo:** Anche se configurato con la chiave API, OpenAgent mostrerà sempre una richiesta di autorizzazione a schermo prima di inviare dati a Gemini.
2. **Escalation su 2 Errori:** Se il modello locale non riesce a formulare una risposta valida o incontra eccezioni per 2 passaggi consecutivi, l'assistente suggerirà automaticamente:  
   *"Il modello locale ha riscontrato un errore per 2 passaggi consecutivi. Vuoi passare a Google Gemini 3.8 Flash nel Cloud per completare la richiesta?"*
3. **Archiviazione Sicura:** La chiave API viene salvata unicamente nel database locale SQLite del dispositivo (`data/openagent_data.db`).

---

## ⚙️ Pannello Impostazioni

Apri le impostazioni cliccando sull'icona dell'ingranaggio (**⚙️**) o dal menu della System Tray:

* **Modello & Hardware:** Seleziona il profilo attivo, consulta l'occupazione RAM in tempo reale, configura thread CPU, contesto e timeout, oppure applica con un clic le impostazioni raccomandate dal profiler hardware.
* **Cartelle Autorizzate (Allowlist):** Gestisci in quali directory OpenAgent è autorizzato ad operare (di default: *Documenti*, *Download*, *Desktop* e cartella di lavoro). I percorsi critici di Windows sono categoricamente inaccessibili.
* **Memoria Agente (`user_memory`):** Consulta, inserisci, modifica o cancella le informazioni e preferenze persistenti che l'agente carica all'avvio (es. stile di risposta, percorsi di progetto preferiti).
* **Cloud (Gemini):** Inserisci la tua chiave API di Google AI Studio (disponibile gratuitamente), verifica la connettività in tempo reale con il pulsante *⚡ Verifica Chiave*, e configura le policy di escalation e routing automatico.

---

## 🔒 Uscita dall'Applicazione

1. Fai clic destro sull'icona del fantasmino nell'area notifiche di Windows (vicino all'orologio).
2. Seleziona **"Esci"**.

---

## 📁 Struttura del Repository per Sviluppatori

La soluzione è strutturata in progetti C# .NET 8 puliti e disaccoppiati:

```
OPENAGENT/
├── OpenAgent.exe                      # Binario eseguibile standalone pronto all'avvio
├── OpenAgent.sln                      # Soluzione Microsoft Visual Studio / .NET 8
├── build_release.ps1                  # Script di compilazione, packaging e creazione ZIP
│
├── src/
│   ├── OpenAgent.App/                 # Shell grafica WPF (Avatar pixel-art, Speech Bubble, VoiceInputService, Tray)
│   ├── OpenAgent.Agent/               # Controller multi-step, StateMachine, ModelTaskRouter, Security Sandbox
│   ├── OpenAgent.Model/               # Integrazione llama-server (Win32 Job Objects), LlamaCppClient e GeminiClient
│   ├── OpenAgent.Tools/               # Tool sandboxati: FileSystem, Web Search/Fetch, Memory Management
│   └── OpenAgent.Data/                # Repository SQLite (Audit trail, User Memory, Settings) & HardwareProfiler
│
├── models/                            # Pesi quantizzati GGUF locali e modello Whisper (ggml-tiny.bin)
├── runtime/llama/                     # Binari runtime precompilati llama.cpp con accelerazione Vulkan / CPU
├── runtimes/win-x64/                  # Librerie native C++ (whisper.dll, ggml-whisper, SQLite native)
├── docs/                              # Specifiche architetturali, roadmap e piani sprint
├── tests/                             # Suite automatizzata di test PowerShell 7 per sandbox, cloud e voce
└── dist/                              # Cartella di output delle release e pacchetti compressi
```

### Esecuzione della Suite di Test

Tutti i test possono essere eseguiti con **PowerShell 7 (`pwsh`)**:

```powershell
# Esegui l'intera suite di test unificata (Process, Sandbox, Audit, Multi-Step, Memory, Hardware, Cloud, Voice):
pwsh -File tests/run_all_tests.ps1

# Esegui i test saltando l'avvio del server locale:
pwsh -File tests/run_all_tests.ps1 -SkipLiveServer

# Esegui solo la suite di test Cloud Gemini:
pwsh -File tests/test_gemini_cloud_integration.ps1

# Esegui solo la suite di test Voce / Whisper:
pwsh -File tests/test_voice_service.ps1
```

Per maggiori approfondimenti sull'architettura e sul modello di sicurezza, consulta la cartella [`docs/`](docs/README.md).
