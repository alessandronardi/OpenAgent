# 📦 OpenAgent — Modelli GGUF & Whisper

Questa cartella è destinata a contenere i pesi quantizzati dei modelli linguistici locali (formato **GGUF**) e del modello di trascrizione vocale (**Whisper**).

I file dei modelli sono esclusi dal repository Git a causa delle loro dimensioni (>1 GB per ciascun LLM).

---

## 📥 Modelli Supportati e Download

I modelli predefiniti utilizzati dai profili di OpenAgent possono essere scaricati liberamente da Hugging Face o tramite gli script inclusi:

### 1. Modelli Linguistici Locali (LLM)

| Profilo | Nome File Atteso | Modello Originale | Link / Download |
| :--- | :--- | :--- | :--- |
| **Desktop & Tools** | `qwen2.5-coder-1.5b-instruct-q4_k_m.gguf` | Qwen2.5-Coder 1.5B Instruct | [HuggingFace - Qwen2.5-Coder-1.5B-Instruct-GGUF](https://huggingface.co/Qwen/Qwen2.5-Coder-1.5B-Instruct-GGUF) |
| **Web & Reasoning** | `qwen2.5-3b-instruct-q4_k_m.gguf` | Qwen2.5 3B Instruct | [HuggingFace - Qwen2.5-3B-Instruct-GGUF](https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF) |
| **Logica & Sintesi** | `gemma-2-2b-it-q4_k_m.gguf` | Google Gemma-2 2B Instruct | [HuggingFace - gemma-2-2b-it-GGUF](https://huggingface.co/bartowski/gemma-2-2b-it-GGUF) oppure `python tools/download_gemma.py` |
| **Generale / Batteria** | `Spark-X2.5-1.7B-Q4_K_M.gguf` | Spark-X 2.5 1.7B (Q4_K_M) | [HuggingFace - Spark-X2.5-GGUF](https://huggingface.co/) |

> [!TIP]
> Puoi aggiungere qualsiasi altro modello GGUF compatibile con `llama.cpp` e selezionarlo dal pannello **Impostazioni ➔ Profilo: Modello GGUF Personalizzato**.

---

### 2. Modello di Trascrizione Vocale (Whisper STT)

* **Nome File Atteso:** `ggml-small.bin` (~466 MB)
* **Caratteristiche:** Modello multilingua ad alta precisione (244M parametri), ideale per la trascrizione e dettatura vocale fluida in lingua italiana senza le allucinazioni o omissioni tipiche del modello compatto `tiny`.
* **Download Automatico:** Al primo avvio o all'uso del microfono (`Ctrl+Shift+V` o icona microfono), OpenAgent verifica la presenza del file e lo scarica automaticamente dalla repository ufficiale Whisper GGML se assente.
* **Download Manuale:** Scaricabile direttamente da [HuggingFace - ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin) e da posizionare in questa cartella (`models/ggml-small.bin`).
