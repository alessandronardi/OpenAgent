# 🐙 OpenAgent — Assistente desktop locale e ibrido

**OpenAgent** è un assistente desktop per Windows progettato per funzionare in locale, garantendo privacy, velocità e controllo sui propri dati.

L'applicazione esegue l'inferenza direttamente sul computer tramite `llama.cpp` (accelerazione Vulkan e CPU) e integra la trascrizione vocale offline con Whisper. Per compiti particolarmente complessi o testi molto lunghi, supporta un sistema di routing opzionale verso modelli cloud (Google Gemini), sempre con richiesta preventiva di autorizzazione.

---

## Caratteristiche principali

* 🔒 **Privacy dei dati:** l'elaborazione di testi, file e comandi avviene in locale. Nessuna informazione viene inviata all'esterno senza consenso esplicito.
* 🎙️ **Dettatura vocale offline (Whisper):** trascrizione del parlato in tempo reale tramite la libreria nativa C++ `whisper.dll` e modello locale compatto, senza streaming audio verso server remoti.
* 🧠 **Routing dei modelli (ModelTaskRouter):** selezione automatica o manuale del modello più adatto al tipo di richiesta (gestione file, comandi di sistema, ricerca web, sintesi o logica).
* ☁️ **Supporto cloud opzionale (Google Gemini):** collegamento con Gemini 3.8 Flash (finestra di contesto da 1 milione di token) per refactoring, architettura software o compiti complessi, con fallback su Gemini 3.5 Flash-Lite e possibilità di escalation automatica dopo due tentativi locali falliti.
* ⚡ **Rilevamento hardware e calibrazione automatica:** all'avvio il profiler analizza CPU, core logici e fisici, RAM disponibile e supporto Vulkan, impostando thread, contesto e timeout ottimali.
* 🛡️ **Sandbox e cartelle autorizzate (allowlist):** qualsiasi operazione di modifica, spostamento o cancellazione di file richiede conferma grafica. Le cartelle di sistema critiche di Windows sono sempre protette.
* 🎨 **Avatar pixel-art e modalità trasparente (click-through):** presenza discreta sullo schermo con animazioni reattive (respiro, battito delle palpebre, accessori dinamici in base allo strumento attivo) e modalità click-through per continuare a usare le finestre sottostanti.

---

## Come avviarlo

1. Fai doppio clic sul file `OpenAgent.exe` nella cartella del programma.
2. Sullo schermo apparirà il polpo in pixel-art (in basso a destra) e l'icona corrispondente nell'area di notifica di Windows (vicino all'orologio).
3. Quando l'indicatore di stato diventa verde (**In ascolto**), OpenAgent è pronto a ricevere richieste.

---

## Modalità di interazione e scorciatoie

| Azione | Scorciatoia / controllo | Descrizione |
| :--- | :--- | :--- |
| **Mostra / nascondi chat** | `Ctrl + Spazio` *(o `Alt + Spazio`)* | Apre la barra di digitazione da qualsiasi finestra attiva. |
| **Dettatura vocale** | `Ctrl + Shift + V` *(o icona 🎙️)* | Avvia o ferma la registrazione audio; il testo trascritto viene inserito nel prompt. |
| **Invia richiesta** | `Invio` *(o freccia ➔)* | Avvia l'elaborazione della richiesta. |
| **Interrompi operazione** | `Esc` *(o pulsante ⏹ rosso)* | Ferma l'operazione in corso o annulla la registrazione vocale. |
| **Trascina file o cartelle** | Trascina sul fumetto | Predispone la lettura o l'analisi della cartella o del file rilasciato. |
| **Sposta il polpo** | Trascina con il tasto sinistro | Permette di riposizionare l'avatar sullo schermo. |
| **Modalità click-through** | Icona lucchetto 🔓/🔒 | Rende la finestra trasparente ai clic del mouse, mantenendola visibile sopra le altre app. |
| **Cambio profilo modello** | Clic sul badge del modello | Mostra e permette di alternare al volo i profili disponibili. |

---

## Funzionalità vocale (speech-to-text locale)

Il modulo vocale si basa sulle librerie **Whisper.net** e **NAudio**:
* **Elaborazione locale:** l'audio catturato dal microfono (16 kHz, 16 bit mono) viene elaborato in memoria dalla libreria nativa C++ `whisper.dll`. Nessun dato audio lascia il computer.
* **Modello compatto (`ggml-tiny.bin`):** modello da circa 75 MB ottimizzato per italiano e inglese, caricato in memoria all'avvio per ridurre al minimo i tempi di risposta.
* **Download automatico:** se il modello non è presente nella cartella `models/`, l'applicazione propone di scaricarlo al primo utilizzo del microfono.

---

## Modelli supportati e routing

OpenAgent supporta modelli locali in formato GGUF e modelli cloud tramite API:

### 1. Modelli locali (`llama.cpp` con accelerazione Vulkan o CPU)

| Profilo | Modello consigliato | RAM indicativa | Velocità | Ambito principale |
| :--- | :--- | :--- | :--- | :--- |
| **Desktop e strumenti** | `qwen2.5-coder-1.5b-instruct-q4_k_m.gguf` | ~1,4 GB | ~23,5 tok/s | Chiamate ai tool, comandi di sistema, percorsi Windows e script. |
| **Web e sintesi** | `qwen2.5-3b-instruct-q4_k_m.gguf` | ~2,6 - 3,3 GB | ~12,2 tok/s | Ricerche online, lettura di URL e sintesi di testi lunghi. |
| **Logica e scrittura** | `gemma-2-2b-it-q4_k_m.gguf` | ~1,6 - 2,1 GB | ~18,0 tok/s | Redazione testi, riassunti, logica concettuale ed efficienza energetica. |
| **Generale / batteria** | `Spark-X2.5-1.7B-Q4_K_M.gguf` | ~1,2 - 1,9 GB | ~20,0 tok/s | Conversazione generica e compiti quotidiani a basso impatto. |
| **Personalizzato** | Qualsiasi file `.gguf` | Dipende dai pesi | Variabile | Possibilità di caricare qualsiasi modello compatibile con `llama.cpp`. |

### 2. Modelli cloud (Google Gemini)

* **Gemini 3.8 Flash (primario):** finestra di contesto fino a 1 milione di token, utile per analisi di interi repository, architetture software complesse e richieste ad alto numero di passaggi.
* **Gemini 3.5 Flash-Lite (fallback):** subentra automaticamente in caso di errori di quota o congestione temporanea della rete.

### 3. Routing automatico (ModelTaskRouter)

Il modulo interno analizza il testo della richiesta e suggerisce il modello più idoneo:
* **Strumenti desktop:** attiva Qwen2.5-Coder per operazioni su file, directory o codice.
* **Ricerche web:** attiva Qwen 3B in presenza di URL o query di navigazione.
* **Sintesi e testi:** attiva Gemma-2 2B per traduzioni, saggi ed elaborazioni testuali.
* **Compiti complessi:** rileva prompt sopra i 750 caratteri o richieste architetturali, proponendo l'uso di Gemini nel cloud.

---

## Integrazione cloud e privacy

L'uso del cloud è facoltativo e governato da regole trasparenti:
1. **Consenso esplicito:** prima di qualsiasi chiamata alle API di Gemini, l'applicazione mostra una richiesta di conferma a schermo.
2. **Escalation su errore:** se il modello locale restituisce un errore per due volte consecutive, l'assistente chiede all'utente se desidera completare l'operazione tramite cloud.
3. **Salvataggio locale della chiave:** la chiave API viene custodita esclusivamente nel database SQLite locale (`data/openagent_data.db`).

---

## Pannello impostazioni

Le impostazioni si aprono cliccando sull'icona dell'ingranaggio (**⚙️**) o dal menu dell'area di notifica:

* **Modello e hardware:** selezione del profilo locale, monitoraggio della RAM occupata, configurazione di thread, contesto e timeout, con possibilità di applicare i valori consigliati dal profiler hardware.
* **Cartelle autorizzate (allowlist):** elenco delle directory in cui l'agente può leggere e operare (di default: *Documenti*, *Download*, *Desktop* e cartella del progetto). I percorsi di sistema Windows rimangono bloccati.
* **Memoria dell'agente (`user_memory`):** visualizzazione e modifica delle preferenze salvate (es. stile delle risposte o percorsi abituali) che vengono iniettate all'avvio di ogni sessione.
* **Cloud (Gemini):** inserimento della chiave API (ottenibile su Google AI Studio), verifica della connessione con misura della latenza e gestione delle regole di escalation.

---

## Chiusura dell'applicazione

1. Fai clic destro sull'icona del polpo nell'area di notifica di Windows (in basso a destra, accanto all'orologio).
2. Seleziona **Esci**.

---

## Struttura del repository

Il progetto è suddiviso in moduli .NET 8 puliti e disaccoppiati:

```
OPENAGENT/
├── OpenAgent.exe                      # Eseguibile principale pronto all'avvio
├── OpenAgent.sln                      # Soluzione di Visual Studio / .NET 8
├── build_release.ps1                  # Script per compilazione, packaging e archivio ZIP
│
├── src/
│   ├── OpenAgent.App/                 # Interfaccia grafica WPF (avatar polpo, messaggi, voce Whisper, tray)
│   ├── OpenAgent.Agent/               # Controller multi-step, macchina a stati, router modelli e sandbox
│   ├── OpenAgent.Model/               # Gestione del processo llama-server, client locale e client Gemini
│   ├── OpenAgent.Tools/               # Strumenti operativi: file system, ricerca web, memoria
│   └── OpenAgent.Data/                # Database SQLite (audit log, memoria, impostazioni) e profiler hardware
│
├── models/                            # Pesi quantizzati GGUF e modello Whisper (ggml-tiny.bin)
├── runtime/llama/                     # Binari di inferenza llama.cpp con accelerazione Vulkan/CPU
├── runtimes/win-x64/                  # Librerie native C++ (whisper.dll, runtimes SQLite)
├── docs/                              # Documentazione tecnica, piano architetturale e roadmap
├── tests/                             # Test automatizzati in PowerShell 7 (sandbox, cloud, memoria, voce)
└── dist/                              # Pacchetti di distribuzione e archivi compressi
```

### Esecuzione dei test

I test richiedono **PowerShell 7 (`pwsh`)**:

```powershell
# Esecuzione completa di tutti i moduli di test:
pwsh -File tests/run_all_tests.ps1

# Esecuzione rapida offline (senza avviare il server locale):
pwsh -File tests/run_all_tests.ps1 -SkipLiveServer

# Test del solo modulo cloud Gemini:
pwsh -File tests/test_gemini_cloud_integration.ps1

# Test del solo modulo vocale Whisper:
pwsh -File tests/test_voice_service.ps1
```

Per maggiori dettagli tecnici sull'architettura e sulla sicurezza, consulta la cartella [`docs/`](docs/README.md).
