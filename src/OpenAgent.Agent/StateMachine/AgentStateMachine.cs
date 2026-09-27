using System;

namespace OpenAgent.Agent.StateMachine;

public sealed class AgentStateMachine
{
    private readonly object _syncRoot = new();
    private AgentState _currentState = AgentState.Idle;
    private string _currentDetail = "Pronto";

    public AgentState CurrentState
    {
        get
        {
            lock (_syncRoot) return _currentState;
        }
    }

    public string CurrentDetail
    {
        get
        {
            lock (_syncRoot) return _currentDetail;
        }
    }

    public event Action<AgentState, AgentState, string>? StateChanged;

    public void TransitionTo(AgentState newState, string? detail = null)
    {
        AgentState oldState;
        string resolvedDetail;

        lock (_syncRoot)
        {
            if (_currentState == newState && _currentDetail == detail)
            {
                return;
            }

            oldState = _currentState;
            _currentState = newState;
            resolvedDetail = detail ?? GetDefaultDetail(newState);
            _currentDetail = resolvedDetail;
        }

        StateChanged?.Invoke(oldState, newState, resolvedDetail);
    }

    private static string GetDefaultDetail(AgentState state) => state switch
    {
        AgentState.Idle => "In ascolto",
        AgentState.Thinking => "Elaborazione...",
        AgentState.ToolActive => "Esecuzione tool...",
        AgentState.Speaking => "Risposta...",
        AgentState.Error => "Errore",
        _ => state.ToString()
    };
}
