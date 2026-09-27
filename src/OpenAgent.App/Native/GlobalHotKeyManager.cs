using System;
using System.Windows.Interop;

namespace OpenAgent.App.Native;

public sealed class GlobalHotKeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly int _defaultHotKeyId;
    private readonly System.Collections.Generic.Dictionary<int, Action> _handlers = new();
    private readonly System.Collections.Generic.HashSet<int> _registeredIds = new();
    private bool _disposed;

    public event Action? HotKeyPressed;

    public GlobalHotKeyManager(IntPtr hwnd, int defaultHotKeyId = 9001)
    {
        _hwnd = hwnd;
        _defaultHotKeyId = defaultHotKeyId;

        _source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("Impossibile ottenere HwndSource dalla finestra.");
        _source.AddHook(HwndHook);
    }

    public bool Register(NativeMethods.KeyModifiers modifiers, uint virtualKey)
    {
        return Register(_defaultHotKeyId, modifiers, virtualKey, () => HotKeyPressed?.Invoke());
    }

    public bool Register(int hotKeyId, NativeMethods.KeyModifiers modifiers, uint virtualKey, Action? callback = null)
    {
        if (_registeredIds.Contains(hotKeyId))
        {
            Unregister(hotKeyId);
        }

        bool registered = NativeMethods.RegisterHotKey(_hwnd, hotKeyId, (uint)modifiers, virtualKey);
        if (registered)
        {
            _registeredIds.Add(hotKeyId);
            if (callback != null)
            {
                _handlers[hotKeyId] = callback;
            }
        }
        return registered;
    }

    public void Unregister()
    {
        Unregister(_defaultHotKeyId);
    }

    public void Unregister(int hotKeyId)
    {
        if (_registeredIds.Contains(hotKeyId))
        {
            NativeMethods.UnregisterHotKey(_hwnd, hotKeyId);
            _registeredIds.Remove(hotKeyId);
            _handlers.Remove(hotKeyId);
        }
    }

    public void UnregisterAll()
    {
        foreach (int id in _registeredIds.ToArray())
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }
        _registeredIds.Clear();
        _handlers.Clear();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var action))
            {
                action?.Invoke();
                handled = true;
            }
            else if (id == _defaultHotKeyId)
            {
                HotKeyPressed?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            UnregisterAll();
            _source.RemoveHook(HwndHook);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
