using BimFamilias.Core;
using System.Diagnostics;
using System.Text.Json;

static void Assert(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
if(args.Length>0 && args[0]=="--index")
{
    var root=args.Length>1?args[1]:@"E:\Projeto Em Andamento\#Familia\00 - Biblioteca Organizada";
    var watch=Stopwatch.StartNew();var store=new CatalogStore();
    var previous=store.Load();
    var result=CatalogStore.Scan([root],previous.Entries,null,CancellationToken.None);
    var data=new CatalogData{Roots=[root],Entries=result.Entries,IndexedAt=DateTime.Now};
    store.Save(data);
    Console.WriteLine(JsonSerializer.Serialize(new{count=data.Entries.Count,issues=result.Issues,ms=watch.ElapsedMilliseconds,cache=store.CatalogPath}));
    var examples=data.Entries.Where(e=>Search.Normalize(e.Category).Contains("sala")).Take(20).ToList();
    Console.WriteLine("Embedded previews: "+examples.Count(e=>EmbeddedPreview.Read(e.Path)!=null)+" / "+examples.Count);
    watch.Restart();var matches=Search.Filter(data.Entries,"sofa").ToArray();
    Console.WriteLine($"Search sofa: {matches.Length}; {watch.ElapsedMilliseconds} ms");
    watch.Restart();var warmed=Search.Filter(data.Entries,"cadeira").ToArray();
    Console.WriteLine($"Search cadeira (warm index): {warmed.Length}; {watch.ElapsedMilliseconds} ms");
    return;
}
if(args.Length>0 && args[0]=="--catalog")
{
    var catalog=await LibraryDownloadService.LoadCatalogAsync(LibraryDownloadSettings.ManifestUrl,CancellationToken.None);
    Assert(catalog.Packages.Count==11,"remote catalog package count");
    Assert(catalog.Packages.All(package=>!string.IsNullOrWhiteSpace(package.Id)&&package.Bytes>0&&package.Url.StartsWith("https://")),"remote catalog package fields");
    Console.WriteLine($"Remote catalog OK: {catalog.Packages.Count} packages · {catalog.Packages.Sum(package=>package.Bytes):N0} bytes.");
    return;
}
if(args.Length>0 && args[0]=="--state")
{
    var stateRoot=Path.Combine(Path.GetTempPath(),"BimFamilias-state-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(stateRoot);
    try
    {
        var package=new RemoteLibraryPackage{Id="cozinha-01",Name="cozinha-01.zip",Sha256="abc123",Version="v1",Bytes=123};
        var report=new LibraryInstallReport(package.Name,10,2,123);
        LibraryDownloadService.MarkDownloaded(package,report,stateRoot,"https://example.test/catalog.json","v1");
        Assert(LibraryDownloadService.IsDownloaded(package,stateRoot),"download state recognizes same package");
        Assert(!LibraryDownloadService.IsDownloaded(new RemoteLibraryPackage{Id=package.Id,Name=package.Name,Sha256="changed",Version=package.Version,Bytes=package.Bytes},stateRoot),"download state detects package update");
        Assert(File.Exists(LibraryDownloadService.GetStatePath(stateRoot)),"download state persists file");
    }
    finally { if(Directory.Exists(stateRoot))Directory.Delete(stateRoot,true); }
    return;
}
var temp=Path.Combine(Path.GetTempPath(),"BimFamilias-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try
{
    Assert(new UpdateProgress(5,10,3,1,1,"arquivo.rfa").Percent==50,"update progress percentage");
    Assert(new UpdateReport(10,4,3,1,false,[]).Summary=="80% · 4 atualizados · 3 ignorados · 1 erros","update report summary");
    var activationMoment=new DateTime(2026,10,2,20,15,0,DateTimeKind.Unspecified);
    Assert(ActivationCodeService.Generate(activationMoment)=="880-20-0210","activation code pattern");
    Assert(ActivationCodeService.Validate("880-20-0210",activationMoment),"activation code validation");
    Assert(!ActivationCodeService.Validate("880-19-0210",activationMoment),"activation code hour changes");
    var trialStore=new ActivationStore(Path.Combine(temp,"activation"));
    Assert(trialStore.StartTrial("installation-test",DateTime.Now),"start seven day trial");
    Assert(trialStore.IsUsable("installation-test")&&trialStore.HasUsedTrial("installation-test"),"active seven day trial");
    Assert(trialStore.GetTrialRemaining("installation-test").HasValue&&trialStore.GetTrialRemaining("installation-test")!.Value>TimeSpan.FromDays(6),"trial remaining time");
    Assert(!trialStore.StartTrial("installation-test",DateTime.Now),"trial cannot restart");
    var root=Path.Combine(temp,"library");var category=Path.Combine(root,"01 - Cozinha","02 - Eletrodomesticos");Directory.CreateDirectory(category);
    File.WriteAllText(Path.Combine(category,"Geladeira.rfa"),"one");File.WriteAllText(Path.Combine(category,"Outra.rfa"),"one");
    File.WriteAllText(Path.Combine(category,"Projeto.rvt"),"project");
    var first=CatalogStore.Scan([root],[],null,CancellationToken.None);
    Assert(first.Entries.Count==2,"index excludes RVT");
    Assert(first.Entries.All(e=>e.Category=="Cozinha"&&e.Subcategory=="Eletrodomesticos"),"category tree");
    first.Entries[0].Favorite=true;first.Entries[0].Pinned=true;first.Entries[0].Tags="Aço aprovado";
    Assert(Search.Filter(first.Entries,"aco").Count()==1,"accent insensitive tags");
    Assert(Search.Filter(first.Entries,"fridge").Count()==1,"multilingual search");
    Assert(Search.Filter(first.Entries,"aprovado cozinha").Count()==1,"AND token search");
    Assert(Search.Filter(first.Entries,"",mode:"Favoritas").Count()==1,"favorite filter");
    Assert(Search.Filter(first.Entries,"",category:"Sala").Count()==0,"category isolation");
    first.Entries[0].Version=2027;
    Assert(Search.Filter(first.Entries,"",version:2027).Count()==1,"verified version filter");
    Assert(Search.Filter(first.Entries,"",version:0).Count()==1,"unknown version filter");
    var store=new CatalogStore(Path.Combine(temp,"cache"));var data=new CatalogData{Roots=[root],Entries=first.Entries};store.Save(data);
    Assert(store.Load().Entries[0].Favorite&&store.Load().Entries[0].Pinned,"persistent personal metadata");
    var next=CatalogStore.Scan([root],store.Load().Entries,null,CancellationToken.None);
    Assert(next.Entries[0].Tags=="Aço aprovado"&&next.Entries[0].Pinned,"reindex preserves personal metadata");
    Assert(await CatalogStore.HashDuplicatesAsync(next.Entries,null,CancellationToken.None)==1,"SHA256 duplicate group");
    Assert(Search.Filter(next.Entries,"",mode:"Duplicadas").Count()==2,"duplicates filter");
    var cleanupRoot=Path.Combine(temp,"duplicate-cleanup");Directory.CreateDirectory(cleanupRoot);
    File.WriteAllText(Path.Combine(cleanupRoot,"Keep.rfa"),"same-content");File.WriteAllText(Path.Combine(cleanupRoot,"Delete.rfa"),"same-content");
    var cleanupEntries=CatalogStore.Scan([cleanupRoot],[],null,CancellationToken.None).Entries;
    var cleanupReport=await CatalogStore.DeleteExactDuplicatesAsync(cleanupEntries,null,CancellationToken.None);
    Assert(cleanupReport.Groups==1&&cleanupReport.Deleted==1&&cleanupReport.Skipped==0,"delete exact duplicate");
    Assert(Directory.EnumerateFiles(cleanupRoot,"*.rfa").Count()==1,"keep one duplicate file");
    File.WriteAllText(next.Entries[0].Path,"different-content");
    var changed=CatalogStore.Scan([root],next.Entries,null,CancellationToken.None);
    Assert(changed.Entries.First(e=>e.Path==next.Entries[0].Path).Hash=="","content changes invalidate hash");
    var newFamily=Path.Combine(category,"Nova Familia.rfa");File.WriteAllText(newFamily,"new-family");
    var withNewFamily=CatalogStore.Scan([root],changed.Entries,null,CancellationToken.None);
    Assert(withNewFamily.Entries.Any(e=>e.Path==newFamily),"new family appears after update");
    Assert(await FamilyFileGate.WaitUntilStableAsync(newFamily,TimeSpan.FromMilliseconds(200),TimeSpan.FromSeconds(2),CancellationToken.None),"new family becomes stable before load");
    Assert(EmbeddedPreview.Read(next.Entries[0].Path)==null,"invalid preview is unavailable");
    using var ct=new CancellationTokenSource();ct.Cancel();bool cancelled=false;try{CatalogStore.Scan([root],[],null,ct.Token);}catch(OperationCanceledException){cancelled=true;}
    Assert(cancelled,"scan cancellation");
    var offline=CatalogStore.Scan([root+"-offline"],[],null,CancellationToken.None);
    Assert(offline.Issues.Count==1,"offline library diagnosed");
    Assert(CatalogStore.Scan([root,root],[],null,CancellationToken.None).Entries.Count==3,"overlapping roots not duplicated");
    Assert(!CatalogStore.IsInside(root+"-other\\test.rfa",root),"sibling path rejected");
    Console.WriteLine("All tests passed.");
}
finally {Directory.Delete(temp,true);}

