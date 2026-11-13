using System.Text.Json;

namespace QuadroSense.Desktop.Services;

public sealed record InstalledIntegration(
    string Id,
    string Name,
    string Version,
    string GameExecutable,
    string SupportedBuild,
    string Status,
    string ManifestPath,
    string? EntryPoint);

public static class IntegrationDiscovery
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<InstalledIntegration> FindAll()
    {
        var manifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots())
        {
            foreach (var folder in new[] { "Mods", "ModsIntegrations" })
            {
                var path = Path.Combine(root, folder);
                if (!Directory.Exists(path)) continue;
                try
                {
                    foreach (var manifest in Directory.EnumerateFiles(path, "manifest.json", SearchOption.AllDirectories))
                        manifests.Add(manifest);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }

        var result = new List<InstalledIntegration>();
        foreach (var manifestPath in manifests)
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), Json);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id)) continue;
                var entryPoint = string.IsNullOrWhiteSpace(manifest.EntryPoint)
                    ? null
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.EntryPoint));
                var status = entryPoint is null ? "Manifest found" : File.Exists(entryPoint) ? "Ready" : "Entry point missing";
                result.Add(new(manifest.Id, manifest.Name, manifest.Version, manifest.GameExecutable,
                    manifest.SupportedBuild, status, manifestPath, entryPoint));
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return result
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.Status == "Ready")
                .ThenByDescending(x => x.Version, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> Roots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddParents(AppContext.BaseDirectory, roots);
        AddParents(Directory.GetCurrentDirectory(), roots);
        return roots;
    }

    private static void AddParents(string path, HashSet<string> roots)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        for (var depth = 0; directory is not null && depth < 5; depth++, directory = directory.Parent)
            roots.Add(directory.FullName);
    }

    private sealed record Manifest
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string GameExecutable { get; init; } = "";
        public string SupportedBuild { get; init; } = "";
        public string EntryPoint { get; init; } = "";
    }
}
