namespace Qonaqet.ArchitectureTests.Support;

/// <summary>Test icra qovluğundan yuxarı qalxaraq <c>backend/</c> kökünü (Qonaqet.sln) tapır.</summary>
internal static class RepoPaths
{
    public static string BackendRoot { get; } = Find();

    public static string Src => Path.Combine(BackendRoot, "src");

    public static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(p));

    public static IEnumerable<string> ProjectFiles() =>
        Directory.EnumerateFiles(Src, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(p));

    public static string Relative(string path) => Path.GetRelativePath(BackendRoot, path).Replace('\\', '/');

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("bin") || parts.Contains("obj");
    }

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Qonaqet.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("backend/Qonaqet.sln tapılmadı.");
    }
}
