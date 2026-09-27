namespace OpenAgent.Agent.StateMachine;

public enum AgentState
{
    /// <summary>
    /// Inattivo / In ascolto pronto per nuove richieste.
    /// </summary>
    Idle,

    /// <summary>
    /// Elaborazione del prompt e inferenza del modello LLM in corso.
    /// </summary>
    Thinking,

    /// <summary>
    /// Esecuzione attiva di un tool o richiesta di permesso utente.
    /// </summary>
    ToolActive,

    /// <summary>
    /// Elaborazione in corso tramite modello Cloud (es. Google Gemini 3.8 Flash).
    /// </summary>
    CloudProcessing,

    /// <summary>
    /// Generazione e presentazione della risposta testuale all'utente.
    /// </summary>
    Speaking,

    /// <summary>
    /// Si è verificato un errore durante l'inferenza o l'esecuzione.
    /// </summary>
    Error
}
