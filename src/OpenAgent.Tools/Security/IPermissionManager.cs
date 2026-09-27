using System.Collections.Generic;

namespace OpenAgent.Tools.Security;

public enum PathAccessType
{
    Read,
    Modify,
    Destructive
}

public sealed record PermissionCheckResult(bool IsAllowed, string CanonicalPath, string? Reason = null);

public interface IPermissionManager
{
    PermissionCheckResult CheckPathAccess(string path, PathAccessType accessType);
    bool IsPathAllowed(string path, PathAccessType accessType);
    string CanonicalizePath(string path);
    void AddAllowedPath(string path);
    bool RemoveAllowedPath(string path);
    void ResetDefaultPolicies();
    void SetCustomAllowedPaths(IEnumerable<string> paths);
    IReadOnlyCollection<string> AllowedPaths { get; }
    IReadOnlyCollection<string> CustomAllowedPaths { get; }
    IReadOnlyCollection<string> BlockedPaths { get; }
}
