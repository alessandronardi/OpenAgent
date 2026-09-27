using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenAgent.Tools.Security;

namespace OpenAgent.Agent.Security;

public class PermissionManager : IPermissionManager
{
    private readonly HashSet<string> _allowedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _customAllowedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blockedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _workspacePath;

    public IReadOnlyCollection<string> AllowedPaths => _allowedPaths.ToList().AsReadOnly();
    public IReadOnlyCollection<string> CustomAllowedPaths => _customAllowedPaths.ToList().AsReadOnly();
    public IReadOnlyCollection<string> BlockedPaths => _blockedPaths.ToList().AsReadOnly();

    public PermissionManager(string? workspacePath = null)
    {
        _workspacePath = !string.IsNullOrWhiteSpace(workspacePath)
            ? Path.GetFullPath(workspacePath)
            : AppDomain.CurrentDomain.BaseDirectory;

        InitializeDefaultPolicies();
    }

    public void InitializeDefaultPolicies()
    {
        _allowedPaths.Clear();
        _customAllowedPaths.Clear();
        _blockedPaths.Clear();

        // 1. ALLOWED ROOTS
        // Documents
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(docs)) AddSystemAllowedPath(docs);

        // Downloads
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            string downloads = Path.Combine(userProfile, "Downloads");
            if (Directory.Exists(downloads)) AddSystemAllowedPath(downloads);
        }

        // Desktop
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktop)) AddSystemAllowedPath(desktop);

        // Workspace
        if (!string.IsNullOrEmpty(_workspacePath))
        {
            AddSystemAllowedPath(_workspacePath);
        }

        // 2. BLOCKED ROOTS (Absolute Veto)
        // Windows directory & System32
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows)) AddBlockedPath(windows);

        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrEmpty(system)) AddBlockedPath(system);

        // Program Files & Program Files (x86)
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(progFiles)) AddBlockedPath(progFiles);

        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(progFilesX86)) AddBlockedPath(progFilesX86);

        // Sensitive User AppData (cookies, tokens, browser profiles, passwords)
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData)) AddBlockedPath(appData);

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData)) AddBlockedPath(localAppData);

        // Sensitive credentials directories
        if (!string.IsNullOrEmpty(userProfile))
        {
            AddBlockedPath(Path.Combine(userProfile, ".ssh"));
            AddBlockedPath(Path.Combine(userProfile, ".aws"));
            AddBlockedPath(Path.Combine(userProfile, ".azure"));
        }
    }

    private void AddSystemAllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string canonical = NormalizePath(Path.GetFullPath(path));
        _allowedPaths.Add(canonical);
    }

    public void AddAllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string canonical = CanonicalizePath(path);

        // Security check: cannot allow a blocked path or root drive
        string? root = Path.GetPathRoot(canonical);
        if (!string.IsNullOrEmpty(root) && canonical.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Non è consentito autorizzare l'intera radice del disco (root).");
        }

        foreach (var blocked in _blockedPaths)
        {
            if (IsSubPathOf(canonical, blocked))
            {
                throw new InvalidOperationException($"Impossibile autorizzare la cartella '{canonical}' perché ricade in un percorso protetto di sistema ('{blocked}').");
            }
        }

        _allowedPaths.Add(canonical);
        _customAllowedPaths.Add(canonical);
    }

    public void AddAllowedDirectory(string path) => AddAllowedPath(path);

    public bool RemoveAllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string canonical = CanonicalizePath(path);

        bool removedFromAllowed = _allowedPaths.Remove(canonical);
        bool removedFromCustom = _customAllowedPaths.Remove(canonical);

        return removedFromAllowed || removedFromCustom;
    }

    public bool RemoveAllowedDirectory(string path) => RemoveAllowedPath(path);

    public void ResetDefaultPolicies()
    {
        InitializeDefaultPolicies();
    }

    public void SetCustomAllowedPaths(IEnumerable<string> paths)
    {
        // Remove existing custom paths from allowed paths
        foreach (var oldCustom in _customAllowedPaths)
        {
            _allowedPaths.Remove(oldCustom);
        }
        _customAllowedPaths.Clear();

        if (paths != null)
        {
            foreach (var p in paths)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    try
                    {
                        AddAllowedPath(p);
                    }
                    catch
                    {
                        // Ignore invalid/blocked paths during bulk load
                    }
                }
            }
        }
    }

    public void AddBlockedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string canonical = NormalizePath(Path.GetFullPath(path));
        _blockedPaths.Add(canonical);
    }

    public string CanonicalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string trimmed = path.Trim().Trim('"', '\'');

        // Resolve shorthand alias names
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (trimmed.Equals("documents", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("docs", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        else if (trimmed.Equals("downloads", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = Path.Combine(userProfile, "Downloads");
        }
        else if (trimmed.Equals("desktop", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }
        else if (trimmed.Equals("workspace", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = _workspacePath;
        }
        else if (trimmed.StartsWith("~/") || trimmed.StartsWith("~\\"))
        {
            trimmed = Path.Combine(userProfile, trimmed[2..]);
        }

        // Treat bare drive letters (e.g. "C:" or "c:") as root "C:\" instead of drive-relative cwd
        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            trimmed += "\\";
        }

        // Relative paths default to workspace
        if (!Path.IsPathRooted(trimmed))
        {
            trimmed = Path.Combine(_workspacePath, trimmed);
        }

        return NormalizePath(Path.GetFullPath(trimmed));
    }

    public PermissionCheckResult CheckPathAccess(string path, PathAccessType accessType)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new PermissionCheckResult(false, string.Empty, "Percorso nullo o vuoto.");
        }

        string canonical;
        try
        {
            canonical = CanonicalizePath(path);
        }
        catch (Exception ex)
        {
            return new PermissionCheckResult(false, path, $"Percorso non valido: {ex.Message}");
        }

        // 1. Check Root Drive isolation (cannot target C:\ directly)
        string? root = Path.GetPathRoot(canonical);
        if (!string.IsNullOrEmpty(root) && canonical.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return new PermissionCheckResult(false, canonical, "L'accesso diretto alla radice del disco (root) è vietato per sicurezza.");
        }

        // 2. Check Blocked List (VETO)
        foreach (var blocked in _blockedPaths)
        {
            if (IsSubPathOf(canonical, blocked))
            {
                return new PermissionCheckResult(
                    false,
                    canonical,
                    $"Il percorso '{canonical}' ricade in una cartella di sistema protetta ('{blocked}').");
            }
        }

        // 3. Check Allowed List
        bool isAllowed = false;
        foreach (var allowed in _allowedPaths)
        {
            if (IsSubPathOf(canonical, allowed))
            {
                isAllowed = true;
                break;
            }
        }

        if (!isAllowed)
        {
            string allowedSummary = string.Join(", ", _allowedPaths.Select(Path.GetFileName));
            return new PermissionCheckResult(
                false,
                canonical,
                $"Il percorso '{canonical}' non rientra nelle cartelle autorizzate ({allowedSummary}).");
        }

        return new PermissionCheckResult(true, canonical);
    }

    public bool IsPathAllowed(string path, PathAccessType accessType)
    {
        return CheckPathAccess(path, accessType).IsAllowed;
    }

    private static bool IsSubPathOf(string candidatePath, string basePath)
    {
        string normCandidate = NormalizePath(candidatePath);
        string normBase = NormalizePath(basePath);

        if (normCandidate.Equals(normBase, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string baseWithSep = normBase.EndsWith(Path.DirectorySeparatorChar)
            ? normBase
            : normBase + Path.DirectorySeparatorChar;

        return normCandidate.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
