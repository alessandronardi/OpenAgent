# ⚙️ OpenAgent — Runtime llama.cpp

Questa cartella è destinata a contenere i binari di inferenza locale di **`llama.cpp`** con accelerazione hardware (Vulkan / CPU).

A causa delle dimensioni e delle dipendenze binarie native di Windows x64, i file `.exe` e `.dll` sono esclusi dal tracciamento Git standard.

---

## 📁 File Richiesti per l'Esecuzione Locale

Per eseguire il server di inferenza locale senza dipendenze cloud, posiziona in questa cartella:

* `llama-server.exe`
* `llama-server-impl.dll`
* `llama.dll`
* `llama-common.dll`
* `ggml.dll`
* `ggml-vulkan.dll` (per accelerazione GPU Vulkan)
* `ggml-cpu-*.dll` (per inferenza su architetture CPU)
* `ggml-base.dll`
* `libomp.dll`

I binari ufficiali precompilati per Windows (MSVC o Vulkan) sono disponibili nella sezione [Releases di ggerganov/llama.cpp](https://github.com/ggerganov/llama.cpp/releases).

> [!NOTE]
> Se utilizzi il pacchetto distribuibile completo di OpenAgent (`dist/OpenAgent_v1.0_win-x64.zip`), il runtime Vulkan/CPU è già incluso e configurato.
