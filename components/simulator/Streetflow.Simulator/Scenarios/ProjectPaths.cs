using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

internal static class ProjectPaths
{
  // Apply the same portable path rules to folder imports, ZIP entries and manifest references.
  public static string Normalize(string relative)
  {
    if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.StartsWith('\\') || ContainsUnsafeCharacter(relative))
    {
      throw Invalid($"Unsafe project path: {relative}");
    }

    string[] parts = relative.Replace('\\', '/').Split('/');
    foreach (string part in parts)
    {
      string stem = part.Split('.')[0];
      bool reserved = IsReservedName(stem);
      if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') || reserved)
      {
        throw Invalid($"Unsafe project path: {relative}");
      }
    }

    return string.Join('/', parts);
  }

  private static bool ContainsUnsafeCharacter(string value)
  {
    foreach (char character in value)
    {
      if (char.IsControl(character) || ":<>\"|?*".Contains(character))
      {
        return true;
      }
    }

    return false;
  }

  private static bool IsReservedName(string stem)
  {
    if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    bool hasDevicePrefix = stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase);
    return stem.Length == 4 && hasDevicePrefix && stem[3] >= '1' && stem[3] <= '9';
  }

  public static string Resolve(string directory, string relative, bool mustExist = true)
  {
    string normalized = Normalize(relative);
    string root = Path.GetFullPath(directory);
    string path = root;
    RejectLink(root);
    foreach (string part in normalized.Split('/'))
    {
      path = Path.Combine(path, part);
      if (File.Exists(path) || System.IO.Directory.Exists(path))
      {
        RejectLink(path);
      }
    }

    if (mustExist && !File.Exists(path))
    {
      throw Invalid($"Project file does not exist: {relative}");
    }

    return path;
  }

  public static void RejectLink(string path)
  {
    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
    {
      throw Invalid($"Symbolic links are not supported in project packages: {Path.GetFileName(path)}");
    }
  }

  public static ApiException Invalid(string message) => new(400, "invalid_project", message);
}
