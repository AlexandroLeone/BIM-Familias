using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BimFamilias.Core;

public sealed class RemoteLibraryCatalog
{
    public string Name { get; set; } = "Biblioteca de Famílias BIM";
    public string Version { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string GeneratedAtUtc { get; set; } = "";
    public List<RemoteLibraryPackage> Packages { get; set; } = [];
}

public sealed class RemoteLibraryPackage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    [JsonPropertyName("sizeBytes")]
    public long Bytes { get; set; }
}

public sealed record LibraryDownloadProgress(string PackageName, long ReceivedBytes, long TotalBytes, int Percent);
public sealed record LibraryInstallReport(string PackageName, int Added, int Skipped, long DownloadedBytes);

public sealed class LibraryDownloadState
{
    public int SchemaVersion { get; set; } = 1;
    public string ManifestUrl { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public List<DownloadedLibraryPackage> Packages { get; set; } = [];
}

public sealed class DownloadedLibraryPackage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime DownloadedAtUtc { get; set; }
    public int Added { get; set; }
    public int Skipped { get; set; }
    public long DownloadedBytes { get; set; }
}

public static class LibraryDownloadSettings
{
    // Replace this URL after publishing the manifest in the public GitHub Release.
    public const string ManifestUrl = "https://raw.githubusercontent.com/AlexandroLeone/BIM-Familias-Biblioteca/main/catalogo-bibliotecas.json";
    public static bool IsConfigured => Uri.TryCreate(ManifestUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

public static class LibraryDownloadService
{
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromHours(2) };
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public const string StateFileName = ".bimfamilias-downloads.json";

    public static async Task<RemoteLibraryCatalog> LoadCatalogAsync(string manifestUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A fonte da biblioteca ainda não foi configurada com um endereço HTTPS válido.");
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var catalog = await JsonSerializer.DeserializeAsync<RemoteLibraryCatalog>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("O catálogo remoto está vazio ou inválido.");
        if (catalog.Packages.Count == 0) throw new InvalidDataException("O catálogo remoto não contém pacotes de famílias.");
        foreach (var package in catalog.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.Id)) package.Id = package.Name;
            if (string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Name))
                throw new InvalidDataException("O catálogo contém um pacote sem identificação.");
            if (!Uri.TryCreate(package.Url, UriKind.Absolute, out var packageUri) || packageUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException($"O pacote {package.Name} não possui um endereço HTTPS válido.");
        }
        return catalog;
    }

    public static string GetStatePath(string destinationRoot)
    {
        return Path.Combine(Path.GetFullPath(destinationRoot), StateFileName);
    }

    public static LibraryDownloadState LoadState(string destinationRoot)
    {
        try
        {
            var path = GetStatePath(destinationRoot);
            if (!File.Exists(path)) return new LibraryDownloadState();
            var state = JsonSerializer.Deserialize<LibraryDownloadState>(File.ReadAllText(path), JsonOptions);
            if (state == null) return new LibraryDownloadState();
            state.Packages ??= [];
            return state;
        }
        catch
        {
            return new LibraryDownloadState();
        }
    }

    public static bool IsDownloaded(RemoteLibraryPackage package, string destinationRoot)
    {
        var state = LoadState(destinationRoot);
        var id = PackageKey(package);
        var saved = state.Packages.FirstOrDefault(item =>
            string.Equals(PackageKey(item), id, StringComparison.OrdinalIgnoreCase));
        if (saved == null) return false;
        if (!string.IsNullOrWhiteSpace(package.Sha256) && !string.IsNullOrWhiteSpace(saved.Sha256))
            return string.Equals(package.Sha256.Trim(), saved.Sha256.Trim(), StringComparison.OrdinalIgnoreCase);
        return string.Equals(package.Version, saved.Version, StringComparison.OrdinalIgnoreCase)
            && (package.Bytes <= 0 || saved.DownloadedBytes <= 0 || package.Bytes == saved.DownloadedBytes);
    }

    public static void MarkDownloaded(RemoteLibraryPackage package, LibraryInstallReport report, string destinationRoot, string manifestUrl, string catalogVersion)
    {
        Directory.CreateDirectory(destinationRoot);
        var state = LoadState(destinationRoot);
        state.ManifestUrl = manifestUrl;
        state.CatalogVersion = catalogVersion;
        state.UpdatedAtUtc = DateTime.UtcNow;
        var key = PackageKey(package);
        state.Packages.RemoveAll(item => string.Equals(PackageKey(item), key, StringComparison.OrdinalIgnoreCase));
        state.Packages.Add(new DownloadedLibraryPackage
        {
            Id = package.Id,
            Name = package.Name,
            Sha256 = package.Sha256,
            Version = package.Version,
            DownloadedAtUtc = DateTime.UtcNow,
            Added = report.Added,
            Skipped = report.Skipped,
            DownloadedBytes = report.DownloadedBytes
        });
        SaveState(destinationRoot, state);
    }

    public static IReadOnlyList<DownloadedLibraryPackage> GetDownloadedPackages(string destinationRoot)
    {
        return LoadState(destinationRoot).Packages;
    }

    static string PackageKey(RemoteLibraryPackage package) => string.IsNullOrWhiteSpace(package.Id) ? package.Name : package.Id;
    static string PackageKey(DownloadedLibraryPackage package) => string.IsNullOrWhiteSpace(package.Id) ? package.Name : package.Id;

    static void SaveState(string destinationRoot, LibraryDownloadState state)
    {
        var path = GetStatePath(destinationRoot);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporary, path, true);
    }

    public static async Task<LibraryInstallReport> DownloadAndInstallAsync(RemoteLibraryPackage package, string destinationRoot, IProgress<LibraryDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(package.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"O pacote {package.Name} possui um endereço inválido.");
        Directory.CreateDirectory(destinationRoot);
        var workRoot = Path.Combine(Path.GetTempPath(), "BIMFamilias-Downloads");
        Directory.CreateDirectory(workRoot);
        var safeId = string.Concat(package.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        var zipPath = Path.Combine(workRoot, safeId + "-" + Guid.NewGuid().ToString("N") + ".zip");
        var staging = Path.Combine(workRoot, safeId + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? package.Bytes;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                var buffer = new byte[131072];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    var percent = total > 0 ? Math.Clamp((int)Math.Round(received * 100d / total), 0, 100) : 0;
                    progress?.Report(new(package.Name, received, total, percent));
                }
            }
            if (!string.IsNullOrWhiteSpace(package.Sha256))
            {
                await using var hashStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
                if (!string.Equals(actual, package.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"A verificação de segurança falhou para {package.Name}; o arquivo foi descartado.");
            }
            ExtractZipSafely(zipPath, staging, cancellationToken);
            var added = 0;
            var skipped = 0;
            foreach (var file in Directory.EnumerateFiles(staging, "*.rfa", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(staging, file);
                var destination = Path.GetFullPath(Path.Combine(destinationRoot, relative));
                var destinationPrefix = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!destination.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("O pacote contém um caminho inseguro.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination)) { skipped++; continue; }
                File.Move(file, destination);
                added++;
            }
            return new(package.Name, added, skipped, new FileInfo(zipPath).Length);
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    static void ExtractZipSafely(string zipPath, string staging, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(staging);
        var stagingRoot = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(staging, relative));
            if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("O pacote contém um caminho inseguro.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
    }
}
