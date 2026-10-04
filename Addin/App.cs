using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BimFamilias.Core;
using BimFamilias.UI;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace BimFamilias.Revit;

public sealed class App : IExternalApplication
{
    public static readonly DockablePaneId PaneId=new(new Guid("4D61006D-9E8C-4F69-B5EE-D8BAA7BAE142"));
    internal static LibraryView? View;
    internal static RevitBridge? Bridge;
    internal static string InstallationId
    {
        get
        {
            try
            {
                var path=Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location)??"","installation.json");
                if(File.Exists(path))
                {
                    using var document=JsonDocument.Parse(File.ReadAllText(path));
                    if(document.RootElement.TryGetProperty("InstallationId",out var value)&&!string.IsNullOrWhiteSpace(value.GetString()))return value.GetString()!;
                }
            }
            catch{}
            return "dev:"+(Path.GetDirectoryName(typeof(App).Assembly.Location)??typeof(App).Assembly.FullName??"BimFamilias");
        }
    }
    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            Bridge=new RevitBridge();View=new LibraryView();View.AttachBridge(Bridge,InstallationId);
            application.RegisterDockablePane(PaneId,"Biblioteca de Famílias",new Provider(View));
            try{application.CreateRibbonTab("Familia");}catch(Autodesk.Revit.Exceptions.ArgumentException){}
            var panel=application.CreateRibbonPanel("Familia","Biblioteca de Famílias");
            var button=new PushButtonData("BimFamilias.Open","Biblioteca de\nFamílias",Assembly.GetExecutingAssembly().Location,typeof(OpenLibrary).FullName!);
            button.ToolTip="Pesquisar, visualizar e carregar famílias da sua biblioteca local.";
            button.LongDescription="Categorias e subcategorias, pesquisa por palavras-chave, favoritos, recentes, prévias reais e carregamento em lote.";
            button.AvailabilityClassName=typeof(AlwaysAvailable).FullName;
            button.LargeImage=CreateIcon();
            panel.AddItem(button);
            var activator=new PushButtonData("BimFamilias.Activator","Ativador",Assembly.GetExecutingAssembly().Location,typeof(OpenActivator).FullName!);
            activator.ToolTip="Ver se o sistema está ativado, consultar os dias restantes do teste e ativar agora.";
            activator.LongDescription="Mostra o status da ativação e oferece ativação imediata ou teste de 7 dias.";
            activator.AvailabilityClassName=typeof(AlwaysAvailable).FullName;
            activator.LargeImage=CreateActivatorIcon();
            panel.AddItem(activator);
            return Result.Succeeded;
        }
        catch(Exception ex){TaskDialog.Show("Biblioteca de Famílias",ex.ToString());return Result.Failed;}
    }
    static DrawingImage CreateIcon()
    {
        var group=new DrawingGroup();
        using(var context=group.Open()){
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(22,125,141)),null,new Rect(0,0,32,32),6,6);
            var pen=new Pen(Brushes.White,1.6);
            for(int x=8;x<=24;x+=8)context.DrawLine(pen,new Point(x,8),new Point(x,24));
            context.DrawLine(pen,new Point(7,24),new Point(25,24));
            context.DrawLine(pen,new Point(8,9),new Point(22,6));
        }
        var image=new DrawingImage(group);image.Freeze();return image;
    }
    static DrawingImage CreateActivatorIcon()
    {
        var group=new DrawingGroup();
        using(var context=group.Open())
        {
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(19,92,110)),null,new Rect(0,0,32,32),6,6);
            var pen=new Pen(Brushes.White,2);
            context.DrawEllipse(null,pen,new Point(12,12),5,5);
            context.DrawLine(pen,new Point(16,16),new Point(25,25));
            context.DrawLine(pen,new Point(22,22),new Point(25,19));
            context.DrawLine(pen,new Point(25,25),new Point(28,22));
        }
        var image=new DrawingImage(group);image.Freeze();return image;
    }
    public Result OnShutdown(UIControlledApplication application){Bridge?.Dispose();return Result.Succeeded;}
    sealed class Provider(LibraryView view):IDockablePaneProvider
    {
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement=view;
            data.InitialState=new DockablePaneState{DockPosition=DockPosition.Right};
        }
    }
}
public sealed class AlwaysAvailable:IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData,CategorySet selectedCategories)=>true;
}
[Transaction(TransactionMode.Manual)]
public sealed class OpenActivator:IExternalCommand
{
    public Result Execute(ExternalCommandData commandData,ref string message,ElementSet elements){ActivationDialog.ShowStatus(App.InstallationId);return Result.Succeeded;}
}
[Transaction(TransactionMode.Manual)]
public sealed class OpenLibrary:IExternalCommand
{
    public Result Execute(ExternalCommandData commandData,ref string message,ElementSet elements)
    {
        if(!ActivationDialog.EnsureActivated(App.InstallationId)){message="A Biblioteca de Famílias precisa ser ativada antes do primeiro uso.";return Result.Cancelled;}
        commandData.Application.GetDockablePane(App.PaneId).Show();return Result.Succeeded;
    }
}

public sealed class RevitBridge:IExternalEventHandler,IRevitBridge,IDisposable
{
    readonly ExternalEvent external;
    readonly object gate=new();
    object? pending;
    sealed record Request(string Kind,IReadOnlyList<FamilyEntry> Entries,TaskCompletionSource<string> Completion,bool HideAfterLoad=false);
    sealed record UpdateRequest(IReadOnlyList<UpdateCandidate> Entries,TaskCompletionSource<UpdateReport> Completion,IProgress<UpdateProgress>? Progress,CancellationToken Cancellation);
    sealed class UpdateJob
    {
        public required UIApplication App {get;init;}
        public required UpdateRequest Request {get;init;}
        public int TargetYear {get;init;}
        public int Index;
        public int Updated;
        public int Skipped;
        public int Errors;
        public bool Busy;
        public List<UpdateResult> Results {get;}=[];
    }
    UpdateJob? updateJob;
    public RevitBridge(){external=ExternalEvent.Create(this);}
    public Task<string> LoadAsync(IReadOnlyList<FamilyEntry> files,bool hideAfterLoad)=>Queue("load",files,hideAfterLoad);
    public Task<string> InspectAsync(IReadOnlyList<FamilyEntry> files)=>Queue("inspect",files);
    public Task HideAsync()=>Queue("hide",[]);
    public Task<UpdateReport> UpdateAsync(IReadOnlyList<UpdateCandidate> files,IProgress<UpdateProgress>? progress,CancellationToken cancel)
    {
        lock(gate)
        {
            if(pending!=null)throw new InvalidOperationException("O Revit já está processando outra solicitação.");
            var completion=new TaskCompletionSource<UpdateReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending=new UpdateRequest(files.ToArray(),completion,progress,cancel);
            var result=external.Raise();
            if(result!=ExternalEventRequest.Accepted){pending=null;completion.TrySetException(new InvalidOperationException("Revit ocupado. Finalize o comando atual e tente novamente."));}
            return completion.Task;
        }
    }
    Task<string> Queue(string kind,IReadOnlyList<FamilyEntry> files,bool hideAfterLoad=false)
    {
        lock(gate)
        {
            if(pending!=null)throw new InvalidOperationException("O Revit já está processando outra solicitação.");
            var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending=new Request(kind,files.ToArray(),completion,hideAfterLoad);
            var result=external.Raise();
            if(result!=ExternalEventRequest.Accepted){pending=null;completion.TrySetException(new InvalidOperationException("Revit ocupado. Finalize o comando atual e tente novamente."));}
            return completion.Task;
        }
    }
    public string GetName()=>"Biblioteca de Famílias · operações locais";
    public void Execute(UIApplication app)
    {
        object? queued;lock(gate){queued=pending;pending=null;}
        if(queued is UpdateRequest update){StartUpdate(app,update);return;}
        if(queued is not Request request)return;
        try
        {
            if(request.Kind=="hide")
            {
                app.GetDockablePane(App.PaneId).Hide();
                request.Completion.TrySetResult("Biblioteca ocultada. Reabra em Familia → Biblioteca de Famílias.");return;
            }
            if(request.Kind=="inspect"){
                int verified=0;
                foreach(var entry in request.Entries){
                    try{using var info=BasicFileInfo.Extract(entry.Path);var match=Regex.Match(info.Format,@"20\d{2}");entry.Version=match.Success?int.Parse(match.Value):0;if(entry.Version>0)verified++;}
                    catch{entry.Version=0;}
                }
                request.Completion.TrySetResult($"Versão verificada em {verified} de {request.Entries.Count} arquivos desta página.");return;
            }
            var doc=app.ActiveUIDocument?.Document;
            if(doc==null||doc.IsFamilyDocument||doc.IsReadOnly||doc.IsModifiable)throw new InvalidOperationException("Abra um projeto editável e finalize o comando atual para carregar famílias.");
            var results=LoadInto(doc,request.Entries,int.Parse(app.Application.VersionNumber));
            string summary=$"{results.Count(r=>r.Success)} carregadas · {results.Count(r=>!r.Success)} não carregadas.";
            var placement=results.FirstOrDefault(r=>r.Success&&r.SymbolId>=0);
            if(placement!=null&&app.ActiveUIDocument!=null)
            {
                var symbol=doc.GetElement(new ElementId(placement.SymbolId)) as FamilySymbol;
                if(symbol!=null)
                {
                    try
                    {
                        if(request.HideAfterLoad){try{app.GetDockablePane(App.PaneId).Hide();}catch{}}
                        EnsureFloorPlan(app.ActiveUIDocument);
                        app.ActiveUIDocument.PromptForFamilyInstancePlacement(symbol);
                        summary+=$" Inserção concluída para {placement.Name}.";
                    }
                    catch(Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        summary+=" Inserção cancelada pelo usuário.";
                    }
                    catch(Exception ex)
                    {
                        summary+=$" Carregamento concluído, mas o modo Inserir Componente não pôde ser iniciado: {ex.Message}";
                    }
                }
            }
            request.Completion.TrySetResult(summary);
            Directory.CreateDirectory(new CatalogStore().DirectoryPath);
            File.AppendAllLines(Path.Combine(new CatalogStore().DirectoryPath,"carregamentos.jsonl"),results.Select(r=>JsonSerializer.Serialize(r)));
            if(results.Any(r=>!r.Success||r.Warning!=""))TaskDialog.Show("Resultado do carregamento",summary+"\n\n"+string.Join("\n",results.Where(r=>!r.Success||r.Warning!="").Select(r=>r.Name+": "+r.Message+" "+r.Warning).Take(12)));
        }
        catch(Exception ex){request.Completion.TrySetException(ex);}
    }
    static bool EnsureFloorPlan(UIDocument uiDoc)
    {
        if(uiDoc.ActiveView is ViewPlan activePlan && !activePlan.IsTemplate && activePlan.ViewType==ViewType.FloorPlan)return true;
        var floorPlan=new FilteredElementCollector(uiDoc.Document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(view=>!view.IsTemplate&&view.ViewType==ViewType.FloorPlan)
            .OrderBy(view=>view.Name)
            .FirstOrDefault();
        if(floorPlan==null)return false;
        try{uiDoc.ActiveView=floorPlan;return true;}catch{return false;}
    }
    void StartUpdate(UIApplication app,UpdateRequest request)
    {
        if(updateJob!=null){request.Completion.TrySetException(new InvalidOperationException("Outra atualização já está em andamento."));return;}
        updateJob=new UpdateJob{App=app,Request=request,TargetYear=int.Parse(app.Application.VersionNumber)};
        app.Idling+=UpdateOnIdling;
        ProcessNextUpdate(updateJob);
    }
    void UpdateOnIdling(object? sender,IdlingEventArgs e)
    {
        e.SetRaiseWithoutDelay();
        if(updateJob!=null)ProcessNextUpdate(updateJob);
    }
    void ProcessNextUpdate(UpdateJob job)
    {
        if(job.Busy)return;
        job.Busy=true;
        try
        {
            if(job.Request.Cancellation.IsCancellationRequested||job.Index>=job.Request.Entries.Count){FinishUpdate(job);return;}
            var candidate=job.Request.Entries[job.Index++];
            var result=UpdateOneFile(job.App,candidate,job.TargetYear);
            job.Results.Add(result);
            if(result.Status=="Atualizado")job.Updated++;else if(result.Status=="Ignorado")job.Skipped++;else job.Errors++;
            job.Request.Progress?.Report(new(job.Index,job.Request.Entries.Count,job.Updated,job.Skipped,job.Errors,candidate.Path));
            if(job.Index>=job.Request.Entries.Count)FinishUpdate(job);
        }
        catch(Exception ex)
        {
            job.Request.Completion.TrySetException(ex);
            job.App.Idling-=UpdateOnIdling;updateJob=null;
        }
        finally{job.Busy=false;}
    }
    void FinishUpdate(UpdateJob job)
    {
        job.App.Idling-=UpdateOnIdling;
        updateJob=null;
        job.Request.Completion.TrySetResult(new(job.Request.Entries.Count,job.Updated,job.Skipped,job.Errors,job.Request.Cancellation.IsCancellationRequested,job.Results));
    }
    static UpdateResult UpdateOneFile(UIApplication app,UpdateCandidate candidate,int targetYear)
    {
        var sourceYear=0;var versionMessage="";
        try
        {
            if(!File.Exists(candidate.Path))return new(candidate.Path,candidate.Name,candidate.Kind,0,targetYear,"Erro","Arquivo não encontrado.");
            if((File.GetAttributes(candidate.Path)&FileAttributes.ReadOnly)!=0)return new(candidate.Path,candidate.Name,candidate.Kind,0,targetYear,"Ignorado","Arquivo somente leitura.");
            try{sourceYear=ReadVersion(candidate.Path);}catch(Exception ex){versionMessage=ex.Message;}
            if(sourceYear>targetYear)return new(candidate.Path,candidate.Name,candidate.Kind,sourceYear,targetYear,"Ignorado",$"Arquivo mais novo que o Revit {targetYear}.");
            if(sourceYear==targetYear)return new(candidate.Path,candidate.Name,candidate.Kind,sourceYear,targetYear,"Ignorado",$"Já está no Revit {targetYear}.");
            var active=app.ActiveUIDocument?.Document;
            bool isActiveModel=candidate.Kind=="RVT"&&active!=null&&string.Equals(Path.GetFullPath(active.PathName),Path.GetFullPath(candidate.Path),StringComparison.OrdinalIgnoreCase);
            if(isActiveModel)
            {
                if(active!.IsReadOnly||active.IsWorkshared)return new(candidate.Path,candidate.Name,candidate.Kind,sourceYear,targetYear,"Ignorado",active.IsWorkshared?"Modelo compartilhado; atualização automática bloqueada para preservar o vínculo central.":"Documento somente leitura.");
                active.Save();return new(candidate.Path,candidate.Name,candidate.Kind,sourceYear,targetYear,"Atualizado",$"Salvo no mesmo arquivo como Revit {targetYear}.");
            }
            Document? opened=null;
            try
            {
                opened=app.Application.OpenDocumentFile(candidate.Path);
                if(opened.IsWorkshared)return new(candidate.Path,candidate.Name,candidate.Kind,sourceYear,targetYear,"Ignorado","Modelo compartilhado; atualização automática bloqueada para preservar o vínculo central.");
                opened.Save();return new(candidate.Path,candidate.Name,candidate.Kind,sourceYear,targetYear,"Atualizado",sourceYear==0?$"Revit abriu e salvou a família como {targetYear}; a versão não pôde ser lida pelo BasicFileInfo.":$"Salvo no mesmo arquivo como Revit {targetYear}.");
            }
            finally{try{opened?.Close(false);}catch{}}
        }
        catch(Exception ex)
        {
            var message=ex.Message;
            if(IsVersionCompatibilityMessage(versionMessage)||IsVersionCompatibilityMessage(message))
                return new(candidate.Path,candidate.Name,candidate.Kind,sourceYear,targetYear,"Ignorado",$"Formato/versão não aceito pelo Revit {targetYear}; arquivo preservado. {message}");
            return new(candidate.Path,candidate.Name,candidate.Kind,sourceYear,targetYear,"Erro",message);
        }
    }
    static bool IsVersionCompatibilityMessage(string value)
    {
        var text=value.ToLowerInvariant();
        return text.Contains("newer format")||text.Contains("basicfileinfo")||text.Contains("newer version of revit")||text.Contains("older version of revit");
    }
    static int ReadVersion(string path)
    {
        using var info=BasicFileInfo.Extract(path);
        var match=Regex.Match(info.Format, @"20\d{2}");
        return match.Success?int.Parse(match.Value):0;
    }
    public sealed record LoadResult(string Name,bool Success,string Message,string Warning,long SymbolId=-1);
    // Also used by integration tests with a temporary, unsaved project.
    public static List<LoadResult> LoadInto(Document doc,IReadOnlyList<FamilyEntry> entries,int currentYear)
    {
        var results=new List<LoadResult>();
        foreach(var entry in entries)
        {
            if(!entry.Path.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase)||!File.Exists(entry.Path)){results.Add(new(entry.Name,false,"Arquivo .rfa indisponível.",""));continue;}
            try
            {
                try{using var info=BasicFileInfo.Extract(entry.Path);var match=Regex.Match(info.Format,@"20\d{2}");if(match.Success)entry.Version=int.Parse(match.Value);}
                catch{ /* Revit's loader performs the final validation for old files. */ }
                if(entry.Version>currentYear){results.Add(new(entry.Name,false,$"Requer Revit {entry.Version}.",""));continue;}
                using var tx=new Transaction(doc,"Ativar tipo · "+entry.Name);
                tx.Start();
                var failures=new LoadFailures();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                bool loaded=doc.LoadFamily(entry.Path,new PreserveExisting(),out var family);
                family??=FindExistingFamily(doc,entry);
                if(family==null){tx.RollBack();results.Add(new(entry.Name,false,loaded?"A família foi carregada, mas nenhum tipo foi encontrado.":"Já existente ou carga recusada pelo Revit; conteúdo preservado.",""));continue;}
                var symbolId=family.GetFamilySymbolIds().Select(id=>doc.GetElement(id)).OfType<FamilySymbol>().Select(symbol=>{if(!symbol.IsActive)symbol.Activate();return symbol.Id.Value;}).FirstOrDefault(-1L);
                if(symbolId<0){tx.RollBack();results.Add(new(entry.Name,false,"Nenhum tipo utilizável foi encontrado na família.",""));continue;}
                var status=tx.Commit();
                if(status!=TransactionStatus.Committed){results.Add(new(entry.Name,false,"Transação revertida pelo Revit.",string.Join("; ",failures.Messages)));continue;}
                entry.LastUsed=DateTime.UtcNow.Ticks;entry.Uses++;
                results.Add(new(entry.Name,true,"Carregada.",string.Join("; ",failures.Messages),symbolId));
            }
            catch(Exception ex){results.Add(new(entry.Name,false,ex.Message,""));}
        }
        return results;
    }
    static Family? FindExistingFamily(Document doc,FamilyEntry entry)
    {
        string fileName=Path.GetFileNameWithoutExtension(entry.Path);
        return new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f=>string.Equals(f.Name,fileName,StringComparison.OrdinalIgnoreCase)||string.Equals(f.Name,entry.Name,StringComparison.OrdinalIgnoreCase));
    }
    sealed class PreserveExisting:IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool inUse,out bool overwrite){overwrite=false;return true;}
        public bool OnSharedFamilyFound(Family shared,bool inUse,out FamilySource source,out bool overwrite){source=FamilySource.Project;overwrite=false;return true;}
    }
    sealed class LoadFailures:IFailuresPreprocessor
    {
        public List<string> Messages {get;}=[];
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            bool error=false;
            foreach(var f in a.GetFailureMessages()){
                Messages.Add(f.GetDescriptionText());
                if(f.GetSeverity()==FailureSeverity.Warning)a.DeleteWarning(f);else error=true;
            }
            return error?FailureProcessingResult.ProceedWithRollBack:FailureProcessingResult.Continue;
        }
    }
    public void Dispose(){lock(gate){if(pending is Request r)r.Completion.TrySetCanceled();if(pending is UpdateRequest u)u.Completion.TrySetCanceled();pending=null;}if(updateJob!=null){updateJob.App.Idling-=UpdateOnIdling;updateJob.Request.Completion.TrySetCanceled();updateJob=null;}external.Dispose();}
}

