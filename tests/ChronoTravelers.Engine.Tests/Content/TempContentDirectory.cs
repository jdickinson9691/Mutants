namespace ChronoTravelers.Engine.Tests.Content;

/// <summary>A scratch directory for writing sample content JSON in tests, cleaned up automatically.</summary>
public sealed class TempContentDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "travelers-content-tests-" + Guid.NewGuid().ToString("N"));

    public TempContentDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    /// <summary>Writes a file at a path relative to this directory, creating any subdirectories needed.</summary>
    public string WriteFile(string relativePath, string contents)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup - a leftover temp dir isn't worth failing a test over
        }
    }
}
