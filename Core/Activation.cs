using System.Text.Json;

namespace BimFamilias.Core;

public static class ActivationCodeService
{
    public const string Prefix="880";
    public static string Generate(DateTime localTime)=>$"{Prefix}-{localTime:HH}-{localTime:ddMM}";
    public static bool Validate(string? value,DateTime localTime)
    {
        var digits=new string((value??"").Where(char.IsDigit).ToArray());
        var expected=new string(Generate(localTime).Where(char.IsDigit).ToArray());
        return digits.Length==9&&digits==expected;
    }
}

public sealed record ActivationRecord(string InstallationId,string Code,DateTime? ActivatedAtUtc,DateTime? TrialStartedAtUtc=null,DateTime? TrialEndsAtUtc=null);
public sealed class ActivationStore
{
    public string DirectoryPath {get;}
    public string ActivationPath=>Path.Combine(DirectoryPath,"ativacao.json");
    public ActivationStore(string? directory=null)
    {
        DirectoryPath=directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BIMFamilias");
    }
    public bool IsActivated(string installationId)
    {
        try
        {
            if(!File.Exists(ActivationPath))return false;
            var record=JsonSerializer.Deserialize<ActivationRecord>(File.ReadAllText(ActivationPath));
            return record!=null&&record.InstallationId==installationId&&record.ActivatedAtUtc.HasValue&&ActivationCodeService.Validate(record.Code,record.ActivatedAtUtc.Value.ToLocalTime());
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return false;}
    }
    public bool IsTrialActive(string installationId)
    {
        try
        {
            if(!File.Exists(ActivationPath))return false;
            var record=JsonSerializer.Deserialize<ActivationRecord>(File.ReadAllText(ActivationPath));
            return record!=null&&record.InstallationId==installationId&&record.TrialEndsAtUtc.HasValue&&record.TrialEndsAtUtc.Value>DateTime.UtcNow;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return false;}
    }
    public bool HasUsedTrial(string installationId)
    {
        try
        {
            if(!File.Exists(ActivationPath))return false;
            var record=JsonSerializer.Deserialize<ActivationRecord>(File.ReadAllText(ActivationPath));
            return record!=null&&record.InstallationId==installationId&&record.TrialStartedAtUtc.HasValue;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return false;}
    }
    public TimeSpan? GetTrialRemaining(string installationId)
    {
        try
        {
            if(!File.Exists(ActivationPath))return null;
            var record=JsonSerializer.Deserialize<ActivationRecord>(File.ReadAllText(ActivationPath));
            if(record==null||record.InstallationId!=installationId||!record.TrialEndsAtUtc.HasValue)return null;
            var remaining=record.TrialEndsAtUtc.Value-DateTime.UtcNow;
            return remaining>TimeSpan.Zero?remaining:TimeSpan.Zero;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
    public bool IsUsable(string installationId)=>IsActivated(installationId)||IsTrialActive(installationId);
    public void Save(string installationId,string code,DateTime localTime)
    {
        Directory.CreateDirectory(DirectoryPath);
        var tmp=ActivationPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var record=new ActivationRecord(installationId,code,localTime.ToUniversalTime());
            File.WriteAllText(tmp,JsonSerializer.Serialize(record));
            if(File.Exists(ActivationPath))File.Replace(tmp,ActivationPath,ActivationPath+".bak");else File.Move(tmp,ActivationPath);
        }
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    public bool StartTrial(string installationId,DateTime localTime)
    {
        if(HasUsedTrial(installationId))return false;
        Directory.CreateDirectory(DirectoryPath);
        var tmp=ActivationPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var start=localTime.ToUniversalTime();var record=new ActivationRecord(installationId,"",null,start,start.AddDays(7));
            File.WriteAllText(tmp,JsonSerializer.Serialize(record));
            if(File.Exists(ActivationPath))File.Replace(tmp,ActivationPath,ActivationPath+".bak");else File.Move(tmp,ActivationPath);
            return true;
        }
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
}
