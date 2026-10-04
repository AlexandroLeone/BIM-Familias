using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace BimFamilias.Core;

public sealed class FamilyEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subcategory { get; set; } = "";
    public string Root { get; set; } = "";
    public long Bytes { get; set; }
    public long Modified { get; set; }
    public string Hash { get; set; } = "";
    public int Version { get; set; }
    public string Tags { get; set; } = "";
    public bool Favorite { get; set; }
    public bool Pinned { get; set; }
    public long LastUsed { get; set; }
    public int Uses { get; set; }
    string searchKey = "", searchValue = "";
    [JsonIgnore] public string SearchText { get { var key = Name + " " + Category + " " + Subcategory + " " + Tags; if(key != searchKey) { searchKey = key; searchValue = Search.Normalize(key); } return searchValue; } }
    [JsonIgnore] public bool Review => Search.Normalize(Category + " " + Subcategory).Contains("revisar");
    [JsonIgnore] public string Size => Bytes < 1048576 ? $"{Bytes / 1024d:N0} KB" : $"{Bytes / 1048576d:N1} MB";
}
public sealed class CatalogData
{
    public List<string> Roots { get; set; } = [@"E:\Projeto Em Andamento\#Familia\00 - Biblioteca Organizada"];
    public List<FamilyEntry> Entries { get; set; } = [];
    public DateTime? IndexedAt { get; set; }
}
public sealed class ScanResult
{
    public required List<FamilyEntry> Entries { get; init; }
    public List<string> Issues { get; init; } = [];
}
public sealed class CatalogStore
{
    public string DirectoryPath { get; }
    public string CatalogPath => System.IO.Path.Combine(DirectoryPath, "catalogo.json");
    public CatalogStore(string? directory = null)
    {
        DirectoryPath = directory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMFamilias");
    }
    public CatalogData Load()
    {
        if (!File.Exists(CatalogPath)) return new();
        return JsonSerializer.Deserialize<CatalogData>(File.ReadAllText(CatalogPath)) ?? throw new IOException("Índice vazio ou inválido.");
    }
    public void Save(CatalogData data)
    {
        Directory.CreateDirectory(DirectoryPath);
        var tmp = CatalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(data), new UTF8Encoding(false));
            if (File.Exists(CatalogPath)) File.Replace(tmp, CatalogPath, CatalogPath + ".bak");
            else File.Move(tmp, CatalogPath);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static ScanResult Scan(IReadOnlyList<string> roots, IReadOnlyList<FamilyEntry> previous, IProgress<string>? progress, CancellationToken cancel)
    {
        var old = previous.GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var results = new List<FamilyEntry>();
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) { issues.Add("Pasta indisponível: " + root); results.AddRange(previous.Where(e => e.Root.Equals(root, StringComparison.OrdinalIgnoreCase) && seen.Add(e.Path))); continue; }
            var stack = new Stack<string>(); stack.Push(root);
            var inaccessible = new List<string>();
            while (stack.TryPop(out var dir))
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    foreach (var child in Directory.EnumerateDirectories(dir))
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0 && !System.IO.Path.GetFileName(child).StartsWith("00 - Relator")) stack.Push(child);
                    foreach (var path in Directory.EnumerateFiles(dir, "*.rfa"))
                    {
                        cancel.ThrowIfCancellationRequested();
                        if (!seen.Add(path)) continue;
                        var info = new FileInfo(path);
                        var parts = System.IO.Path.GetRelativePath(root, path).Split(System.IO.Path.DirectorySeparatorChar);
                        old.TryGetValue(path, out var before);
                        var unchanged = before != null && before.Modified == info.LastWriteTimeUtc.Ticks && before.Bytes == info.Length;
                        results.Add(new FamilyEntry {
                            Path = path, Root = root, Name = System.IO.Path.GetFileNameWithoutExtension(path),
                            Category = parts.Length > 1 ? CleanLabel(parts[0]) : "Sem categoria",
                            Subcategory = parts.Length > 2 ? string.Join(" / ", parts.Skip(1).SkipLast(1).Select(CleanLabel)) : "Geral",
                            Bytes = info.Length, Modified = info.LastWriteTimeUtc.Ticks,
                            Hash = unchanged ? before!.Hash : "", Version = unchanged ? before!.Version : 0,
                            Tags = before?.Tags ?? "", Favorite = before?.Favorite ?? false, Pinned = before?.Pinned ?? false, LastUsed = before?.LastUsed ?? 0, Uses = before?.Uses ?? 0
                        });
                        if (results.Count % 500 == 0) progress?.Report($"{results.Count:N0} famílias encontradas…");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { issues.Add(dir + ": " + ex.Message); inaccessible.Add(dir); }
            }
            foreach(var item in previous.Where(e => inaccessible.Any(d => IsInside(e.Path, d)) && seen.Add(e.Path))) results.Add(item);
        }
        return new ScanResult { Entries = results, Issues = issues };
    }
    public static bool IsInside(string file, string root) => System.IO.Path.GetFullPath(file).StartsWith(System.IO.Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    public static string CleanLabel(string value) => Regex.Replace(value, @"^\d+\s*-\s*", "").Replace('_', ' ');
    public static async Task<int> HashDuplicatesAsync(List<FamilyEntry> entries, IProgress<string>? progress, CancellationToken ct)
    {
        var candidates = entries.GroupBy(e => e.Bytes).Where(g => g.Count() > 1).SelectMany(g => g).ToArray();
        int count = 0;
        foreach (var entry in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(entry.Path))
            {
                var info = new FileInfo(entry.Path);
                if(info.Length != entry.Bytes || info.LastWriteTimeUtc.Ticks != entry.Modified) { entry.Hash = ""; continue; }
                if (entry.Hash.Length == 0)
                {
                    using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 131072, true);
                    entry.Hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                }
            }
            if (++count % 25 == 0) progress?.Report($"Comparando conteúdo: {count:N0} / {candidates.Length:N0}");
        }
        return entries.Where(e => e.Hash.Length > 0).GroupBy(e => e.Hash).Count(g => g.Count() > 1);
    }
    public static async Task<DuplicateCleanupReport> DeleteExactDuplicatesAsync(IReadOnlyList<FamilyEntry> entries, IProgress<string>? progress, CancellationToken ct)
    {
        var bySize = new Dictionary<long,List<FamilyEntry>>();
        foreach(var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info=new FileInfo(entry.Path);
                if(!info.Exists)continue;
                if(!bySize.TryGetValue(info.Length,out var sameSize))bySize[info.Length]=sameSize=[];
                sameSize.Add(entry);
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
        var hashGroups=new Dictionary<string,List<FamilyEntry>>(StringComparer.OrdinalIgnoreCase);
        var candidates=bySize.Values.Where(g=>g.Count>1).SelectMany(g=>g).ToArray();
        var results=new List<DuplicateFileResult>();
        var processed=0;
        foreach(var entry in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var stream=new FileStream(entry.Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,131072,true);
                entry.Hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));
                if(!hashGroups.TryGetValue(entry.Hash,out var sameHash))hashGroups[entry.Hash]=sameHash=[];
                sameHash.Add(entry);
            }
            catch(IOException ex){results.Add(new(entry.Path,"","Ignorado",$"Não foi possível ler para comparar: {ex.Message}"));}
            catch(UnauthorizedAccessException ex){results.Add(new(entry.Path,"","Ignorado",$"Acesso negado ao comparar: {ex.Message}"));}
            progress?.Report($"Comparando duplicatas: {++processed:N0} / {candidates.Length:N0}");
        }
        var groups=hashGroups.Values.Where(g=>g.Count>1).ToList();
        var deleted=0;var skipped=results.Count(r=>r.Status=="Ignorado");
        foreach(var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var ordered=group.OrderBy(e=>e.Path.Length).ThenBy(e=>e.Path,StringComparer.OrdinalIgnoreCase).ToArray();
            var keeper=ordered[0];
            foreach(var duplicate in ordered.Skip(1))
            {
                try
                {
                    if((File.GetAttributes(duplicate.Path)&FileAttributes.ReadOnly)!=0)
                    {
                        skipped++;results.Add(new(duplicate.Path,keeper.Path,"Ignorado","Duplicata idêntica somente leitura; arquivo preservado."));continue;
                    }
                    File.Delete(duplicate.Path);deleted++;
                    results.Add(new(duplicate.Path,keeper.Path,"Excluído","Conteúdo idêntico; arquivo mantido: "+keeper.Path));
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;results.Add(new(duplicate.Path,keeper.Path,"Ignorado","Não foi possível excluir a duplicata: "+ex.Message));
                }
            }
        }
        return new(groups.Count,deleted,skipped,results);
    }
}
public static class FamilyFileGate
{
    readonly record struct FileStamp(long Bytes,long Modified);
    public static async Task<bool> WaitUntilStableAsync(string path,TimeSpan stableFor,TimeSpan timeout,CancellationToken cancel)
    {
        var deadline=DateTime.UtcNow+timeout;
        FileStamp? previous=null;
        var stableSince=DateTime.UtcNow;
        while(DateTime.UtcNow<deadline)
        {
            cancel.ThrowIfCancellationRequested();
            if(TryReadStamp(path,out var current))
            {
                if(previous==null||previous.Value!=current){previous=current;stableSince=DateTime.UtcNow;}
                else if(DateTime.UtcNow-stableSince>=stableFor)return true;
            }
            else {previous=null;stableSince=DateTime.UtcNow;}
            await Task.Delay(TimeSpan.FromMilliseconds(100),cancel).ConfigureAwait(false);
        }
        return false;
    }
    static bool TryReadStamp(string path,out FileStamp stamp)
    {
        stamp=default;
        try
        {
            var info=new FileInfo(path);if(!info.Exists)return false;
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            stamp=new(stream.Length,info.LastWriteTimeUtc.Ticks);return true;
        }
        catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
    }
}
public sealed record UpdateCandidate(string Path,string Name,string Kind);
public sealed record UpdateProgress(int Processed,int Total,int Updated,int Skipped,int Errors,string Current)
{
    public int Percent=>Total==0?100:(int)Math.Round(Processed*100d/Total,MidpointRounding.AwayFromZero);
}
public sealed record UpdateResult(string Path,string Name,string Kind,int SourceVersion,int TargetVersion,string Status,string Message);
public sealed record UpdateReport(int Total,int Updated,int Skipped,int Errors,bool Cancelled,IReadOnlyList<UpdateResult> Results)
{
    public int Processed=>Updated+Skipped+Errors;
    public int Percent=>Total==0?100:(int)Math.Round(Processed*100d/Total,MidpointRounding.AwayFromZero);
    public string Summary=>$"{Percent}% · {Updated} atualizados · {Skipped} ignorados · {Errors} erros"+(Cancelled?" · cancelado":"");
}
public sealed record DuplicateFileResult(string Path,string KeptPath,string Status,string Message);
public sealed record DuplicateCleanupReport(int Groups,int Deleted,int Skipped,IReadOnlyList<DuplicateFileResult> Results)
{
    public string Summary=>$"{Groups} grupos idênticos · {Deleted} excluídos · {Skipped} preservados";
}
public static class Search
{
    public static string Normalize(string text) => Regex.Replace(new string(text.ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()), @"[^\p{L}\p{N}]+", " ").Trim();
    static readonly string[][] Synonyms = [
        ["sofa", "sofas", "couch", "settee"], ["cadeira", "cadeiras", "chair", "chairs", "silla"],
        ["mesa", "mesas", "table", "desk"], ["cama", "camas", "bed", "beds"],
        ["porta", "portas", "door", "doors"], ["janela", "janelas", "window", "windows"],
        ["geladeira", "refrigerador", "refrigerator", "fridge"], ["torneira", "faucet", "tap"],
        ["pia", "sink"], ["armario", "cabinet", "wardrobe"], ["chuveiro", "shower"],
        ["luminaria", "lamp", "light"], ["banheiro", "bathroom"], ["cozinha", "kitchen", "cocina"]
    ];
    public static IEnumerable<FamilyEntry> Filter(IEnumerable<FamilyEntry> source, string query, string category = "", string subcategory = "", int version = -1, string mode = "Todas")
    {
        var tokens = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => Synonyms.FirstOrDefault(g => g.Contains(t)) ?? [t]).ToArray();
        var duplicates = mode == "Duplicadas" ? source.Where(e => e.Hash != "").GroupBy(e => e.Hash).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet() : [];
        return source.Where(e => (category == "" || e.Category == category) && (subcategory == "" || e.Subcategory == subcategory)
            && (version == -1 || e.Version == version)
            && (mode != "Favoritas" || e.Favorite) && (mode != "Recentes" || e.LastUsed > 0)
            && (mode != "A revisar" || e.Review) && (mode != "Duplicadas" || duplicates.Contains(e.Hash))
            && tokens.All(alternatives => alternatives.Any(t => e.SearchText.Contains(t))));
    }
}

