namespace OpenAgent.Tools;

public enum ToolRiskLevel
{
    /// <summary>
    /// Operazione di sola lettura (es. visualizza directory, leggi file, recupera URL).
    /// </summary>
    Read = 0,

    /// <summary>
    /// Operazione di modifica sicura o creazione senza perdita dati (es. rinomina, sposta, crea file nuovo).
    /// </summary>
    Modify = 1,

    /// <summary>
    /// Operazione distruttiva con potenziale perdita dati o sovrascrittura (es. elimina, sovrascrivi).
    /// </summary>
    Destructive = 2
}
