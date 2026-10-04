using BimFamilias.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace BimFamilias.UI;

public interface IRevitBridge
{
    Task<string> LoadAsync(IReadOnlyList<FamilyEntry> files,bool hideAfterLoad);
    Task<string> InspectAsync(IReadOnlyList<FamilyEntry> files);
    Task HideAsync();
    Task<UpdateReport> UpdateAsync(IReadOnlyList<UpdateCandidate> files,IProgress<UpdateProgress>? progress,CancellationToken cancel);
}
public sealed class CardModel : INotifyPropertyChanged
{
    public required FamilyEntry Entry {get;init;}
    bool selected; BitmapSource? preview;
    public bool Selected {get=>selected;set { selected=value; Changed(nameof(Selected)); }}
    public BitmapSource? Preview {get=>preview;set {preview=value;Changed(nameof(Preview));Changed(nameof(Placeholder));}}
    public Visibility Placeholder => Preview == null ? Visibility.Visible : Visibility.Collapsed;
    public string Star=>Entry.Favorite ? "★" : "☆";
    public string Pin=>Entry.Pinned ? "⚑" : "⚐";
    public string VersionLabel=>Entry.Version == 0 ? "RFA · versão a verificar" : $"Revit {Entry.Version}";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Changed(string name)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
}
public sealed record CategoryChoice(string Name, int Count, string Value);
public partial class LibraryView : UserControl
{
    readonly CatalogStore store;
    CatalogData data = new();
    readonly DispatcherTimer debounce = new() {Interval=TimeSpan.FromMilliseconds(220)};
    List<FamilyEntry> filtered=[];
    List<CardModel> pageCards=[];
    readonly HashSet<string> selection = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, BitmapSource?> previews = new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim previewGate=new(3);
    CancellationTokenSource? operation;
    IRevitBridge? bridge;
    string installationId="";
    int page, generation;
    const int PageSize=48;
    string mode="Todas", category="";
    bool initialized, updating, listMode, deleteDuplicatesAfterUpdate;
    CardModel? active;
    public CatalogData Data=>data;
    public bool IsReady {get;private set;}
    public LibraryView(CatalogStore? catalogStore = null)
    {
        store=catalogStore??new CatalogStore();
        InitializeComponent();
        debounce.Tick+=(_,_)=>{debounce.Stop(); ApplyFilters();};
        Loaded+=async (_,_)=>{if(initialized)return;initialized=true;await InitializeAsync();};
        SizeChanged+=(_,_)=>Resize();
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.F && Keyboard.Modifiers==ModifierKeys.Control){SearchBox.Focus();SearchBox.SelectAll();e.Handled=true;}};
        Sort.ItemsSource=new[]{"Nome A–Z","Menor tamanho","Maior tamanho","Mais recentes","Mais usadas"};Sort.SelectedIndex=0;
        Subcategories.ItemsSource=new[]{"Todas as subcategorias"};Subcategories.SelectedIndex=0;
        Versions.ItemsSource=new[]{"Todas as versões","A verificar"};Versions.SelectedIndex=0;
    }
    public void AttachBridge(IRevitBridge value,string id)
    {
        installationId=id;
        bridge=value;HostLabel.Text="Revit 2027 · carregamento disponível com um projeto aberto";UpdateSelection();
    }
    async Task InitializeAsync()
    {
        try
        {
            data=await Task.Run(store.Load);RefreshChoices();
            if(data.Entries.Count==0)await ScanAsync();
            else {Status.Text=$"Índice local · {data.IndexedAt:dd/MM/yyyy HH:mm} · Atualizar verifica alterações nas pastas.";ApplyFilters();}
            var savedRoot=data.Roots.FirstOrDefault(Directory.Exists);
            if(!string.IsNullOrWhiteSpace(savedRoot))
            {
                var savedPackages=LibraryDownloadService.GetDownloadedPackages(savedRoot);
                if(savedPackages.Count>0)
                    LibraryDownloadDetails.Text=$"Biblioteca online: {savedPackages.Count:N0} pacote(s) ja registrados nesta pasta. Clique em Baixar biblioteca para verificar atualizacoes.";
            }
            IsReady=true;
        }
        catch(Exception ex){Error(ex);}
    }
    void Resize()
    {
        bool wide=ActualWidth>=900, inspect=ActualWidth>=1200;
        NavColumn.Width=new GridLength(wide?205:0);Navigation.Visibility=wide?Visibility.Visible:Visibility.Collapsed;
        CompactCategory.Visibility=wide?Visibility.Collapsed:Visibility.Visible;
        InspectorColumn.Width=new GridLength(inspect?280:0);Inspector.Visibility=inspect?Visibility.Visible:Visibility.Collapsed;
    }
    void RefreshChoices()
    {
        updating=true;
        var choices=new List<CategoryChoice>{new("Todas as famílias", data.Entries.Count,"")};
        choices.AddRange(data.Entries.GroupBy(e=>e.Category).OrderBy(g=>g.Key).Select(g=>new CategoryChoice(g.Key,g.Count(),g.Key)));
        Categories.ItemsSource=choices; CompactCategory.ItemsSource=choices;
        var selected=choices.FirstOrDefault(c=>c.Value==category)??choices[0];
        Categories.SelectedItem=selected;CompactCategory.SelectedItem=selected;category=selected.Value;
        var prior=Versions.SelectedItem as string;
        Versions.ItemsSource=new[]{"Todas as versões","A verificar"}.Concat(data.Entries.Where(e=>e.Version>0).Select(e=>e.Version).Distinct().OrderDescending().Select(v=>$"Revit {v}")).ToArray();
        Versions.SelectedItem=prior??"Todas as versões";if(Versions.SelectedIndex<0)Versions.SelectedIndex=0;
        LibraryCount.Text=$"{data.Entries.Count:N0} famílias";
        RefreshSubcategories();updating=false;ApplyFilters();
    }
    void RefreshSubcategories()
    {
        Subcategories.ItemsSource=new[]{"Todas as subcategorias"}.Concat(data.Entries.Where(e=>category==""||e.Category==category).Select(e=>e.Subcategory).Distinct().Order()).ToArray();
        Subcategories.SelectedIndex=0;
    }
    void CategoryChanged(object sender,SelectionChangedEventArgs e)
    {
        if(updating||Categories.SelectedItem is not CategoryChoice c)return;
        category=c.Value;updating=true;CompactCategory.SelectedItem=c;RefreshSubcategories();updating=false;ApplyFilters();
    }
    void CompactCategoryChanged(object sender,SelectionChangedEventArgs e)
    {
        if(updating||CompactCategory.SelectedItem is not CategoryChoice c)return;
        Categories.SelectedItem=c;
    }
    void SearchChanged(object sender,TextChangedEventArgs e){if(!initialized)return;debounce.Stop();debounce.Start();}
    void FilterChanged(object sender,SelectionChangedEventArgs e){if(initialized&&!updating)ApplyFilters();}
    void ApplyFilters()
    {
        if(!initialized)return;
        int version=Versions.SelectedIndex<=0?-1:Versions.SelectedIndex==1?0:int.Parse(((string)Versions.SelectedItem).Split(' ')[1]);
        string sub=Subcategories.SelectedIndex<=0?"":Subcategories.SelectedItem as string??"";
        var items=Search.Filter(data.Entries,SearchBox.Text,category,sub,version,mode);
        var sorted=Sort.SelectedIndex switch {1=>items.OrderBy(e=>e.Bytes),2=>items.OrderByDescending(e=>e.Bytes),3=>items.OrderByDescending(e=>mode=="Recentes"?e.LastUsed:e.Modified),4=>items.OrderByDescending(e=>e.Uses),_=>mode=="Recentes"?items.OrderByDescending(e=>e.LastUsed):items.OrderBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase)};
        filtered=sorted.OrderByDescending(e=>e.Pinned).ToList();
        page=0;RenderPage();
    }
    async void RenderPage()
    {
        int current=++generation;
        pageCards=filtered.Skip(page*PageSize).Take(PageSize).Select(e=>new CardModel{Entry=e,Selected=selection.Contains(e.Path)}).ToList();
        Cards.ItemsSource=pageCards;Rows.ItemsSource=pageCards;CardScroll.ScrollToTop();
        EmptyState.Visibility=filtered.Count==0?Visibility.Visible:Visibility.Collapsed;
        EmptyText.Text=mode=="Duplicadas"?"Use ⋯ → Verificar duplicatas para comparar os arquivos pelo conteúdo.":"Experimente outra palavra, selecione outra categoria ou limpe os filtros.";
        ResultsLabel.Text=$"{filtered.Count:N0} resultados · {mode}"+(category==""?"":" / "+category);
        PageLabel.Text=$"{page+1} / {Math.Max(1,(int)Math.Ceiling(filtered.Count/(double)PageSize))}";
        Previous.IsEnabled=page>0;Next.IsEnabled=(page+1)*PageSize<filtered.Count;
        try
        {
            await Task.WhenAll(pageCards.Select(async card=>{
                var image=await PreviewAsync(card.Entry);
                if(current==generation)card.Preview=image;
            }));
        }
        catch(Exception ex){Status.Text="Algumas prévias não puderam ser lidas: "+ex.Message;}
    }
    async Task<BitmapSource?> PreviewAsync(FamilyEntry entry)
    {
        string key=entry.Path+"|"+entry.Modified;
        if(previews.TryGetValue(key,out var cached))return cached;
        await previewGate.WaitAsync();
        try
        {
            if(previews.TryGetValue(key,out cached))return cached;
            var image=await Task.Run(()=>{
                try {
                    byte[]? bytes=EmbeddedPreview.Read(entry.Path);if(bytes==null)return null;
                    using var ms=new MemoryStream(bytes);
                    var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=380;bitmap.StreamSource=ms;bitmap.EndInit();bitmap.Freeze();return bitmap;
                }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException){return null;}
            });
            if(previews.Count>800)previews.Clear();
            previews[key]=image;return image;
        }finally{previewGate.Release();}
    }
    void ModeClick(object sender,RoutedEventArgs e)
    {
        mode=(string)((Button)sender).Tag;
        foreach(var b in ((Panel)((Button)sender).Parent).Children.OfType<Button>()){b.Background=Brushes.White;b.Foreground=new SolidColorBrush(Color.FromRgb(36,54,75));}
        ((Button)sender).Background=new SolidColorBrush(Color.FromRgb(22,125,141));((Button)sender).Foreground=Brushes.White;
        ApplyFilters();
    }
    void ClearClick(object sender,RoutedEventArgs e){SearchBox.Clear();Categories.SelectedIndex=0;Subcategories.SelectedIndex=0;Versions.SelectedIndex=0;Sort.SelectedIndex=0;mode="Todas";ModeClick(AllButton,new RoutedEventArgs());}
    void GridClick(object sender,RoutedEventArgs e){listMode=false;CardsVisibility();}
    void ListClick(object sender,RoutedEventArgs e){listMode=true;CardsVisibility();}
    void CardsVisibility(){Rows.Visibility=listMode?Visibility.Visible:Visibility.Collapsed;CardScroll.Visibility=listMode?Visibility.Collapsed:Visibility.Visible;}
    void PreviousClick(object sender,RoutedEventArgs e){if(page>0){page--;RenderPage();}}
    void NextClick(object sender,RoutedEventArgs e){if((page+1)*PageSize<filtered.Count){page++;RenderPage();}}
    void FavoriteClick(object sender,RoutedEventArgs e)
    {
        e.Handled=true;if(((FrameworkElement)sender).DataContext is not CardModel c)return;
        c.Entry.Favorite=!c.Entry.Favorite;c.Changed(nameof(c.Star));Persist();if(mode=="Favoritas")ApplyFilters();
    }
    void PinClick(object sender,RoutedEventArgs e)
    {
        e.Handled=true;if(((FrameworkElement)sender).DataContext is not CardModel c)return;
        c.Entry.Pinned=!c.Entry.Pinned;c.Changed(nameof(c.Pin));Persist();ApplyFilters();
    }
    void SelectionClick(object sender,RoutedEventArgs e)
    {
        e.Handled=true;if(((FrameworkElement)sender).DataContext is CardModel c){if(c.Selected)selection.Add(c.Entry.Path);else selection.Remove(c.Entry.Path);}UpdateSelection();
    }
    void SelectPageClick(object sender,RoutedEventArgs e){foreach(var c in pageCards){c.Selected=true;selection.Add(c.Entry.Path);}UpdateSelection();}
    void ClearSelectionClick(object sender,RoutedEventArgs e){selection.Clear();foreach(var c in pageCards)c.Selected=false;UpdateSelection();}
    void UpdateSelection(){LoadSelected.Content=$"Carregar seleção ({selection.Count})";RenameSelected.Content=$"Renomear ({selection.Count})";DeleteSelected.Content=$"Excluir do HD ({selection.Count})";LoadSelected.IsEnabled=bridge!=null&&selection.Count>0&&operation==null;RenameSelected.IsEnabled=selection.Count==1&&operation==null;DeleteSelected.IsEnabled=selection.Count>0&&operation==null;DetailLoad.IsEnabled=bridge!=null&&active!=null&&operation==null;DetailRename.IsEnabled=active!=null&&operation==null;DetailPin.IsEnabled=active!=null&&operation==null;}
    async void CardMouseDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ClickCount<2||e.OriginalSource is DependencyObject d && Ancestor<Button>(d)!=null||e.OriginalSource is DependencyObject cb && Ancestor<CheckBox>(cb)!=null)return;
        if(((FrameworkElement)sender).DataContext is CardModel c){e.Handled=true;await LoadAsync([c.Entry]);}
    }
    async void CardClicked(object sender,MouseButtonEventArgs e)
    {
        if(e.ClickCount>=2)return;
        if(e.OriginalSource is DependencyObject d && Ancestor<Button>(d)!=null || e.OriginalSource is DependencyObject cb && Ancestor<CheckBox>(cb)!=null)return;
        if(((FrameworkElement)sender).DataContext is CardModel c)
        {
            await FocusAsync(c);if(Inspector.Visibility!=Visibility.Visible)ShowDetails(c);
        }
    }
    static T? Ancestor<T>(DependencyObject? d) where T:DependencyObject{while(d!=null){if(d is T found)return found;d=d is FrameworkContentElement fc?fc.Parent:VisualTreeHelper.GetParent(d);}return null;}
    async void RowSelected(object sender,SelectionChangedEventArgs e){if(Rows.SelectedItem is CardModel c){await FocusAsync(c);if(Inspector.Visibility!=Visibility.Visible)ShowDetails(c);}}
    async Task FocusAsync(CardModel c)
    {
        active=c;DetailName.Text=c.Entry.Name;DetailCategory.Text=c.Entry.Category+" / "+c.Entry.Subcategory;DetailPath.Text=c.Entry.Path;TagsBox.Text=c.Entry.Tags;
        DetailMeta.Text=$"{c.Entry.Size} · {c.VersionLabel}\nModificado em {new DateTime(c.Entry.Modified):dd/MM/yyyy}";DetailPin.Content=c.Entry.Pinned?"⚑ Desafixar do topo":"⚐ Fixar no topo";
        var preview=await PreviewAsync(c.Entry);if(active==c)DetailImage.Source=preview;UpdateSelection();
    }
    void PreviewClick(object sender,RoutedEventArgs e){if(active!=null)ShowDetails(active);}
    void ShowDetails(CardModel card)
    {
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(new Image{Source=card.Preview??DetailImage.Source,Height=270,Stretch=Stretch.Uniform});
        panel.Children.Add(new TextBlock{Text=card.Entry.Name,FontSize=21,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,10)});
        panel.Children.Add(new TextBlock{Text=card.Entry.Category+" / "+card.Entry.Subcategory+"\n"+card.VersionLabel+" · "+card.Entry.Size,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text=card.Entry.Path,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,12,0,12)});
        var tags=new TextBox{Text=card.Entry.Tags,ToolTip="Etiquetas pessoais",Margin=new Thickness(0,5,0,12)};
        panel.Children.Add(tags);
        var buttons=new WrapPanel();
        var save=new Button{Content="Salvar etiquetas"};save.Click+=(_,_)=>{card.Entry.Tags=tags.Text;Persist();ApplyFilters();};
        var pin=new Button{Content=card.Entry.Pinned?"Desafixar do topo":"Fixar no topo"};
        var folder=new Button{Content="Abrir pasta"};folder.Click+=(_,_)=>OpenFolder(card.Entry);
        var rename=new Button{Content="Renomear arquivo"};
        var load=new Button{Content="Carregar no projeto",IsEnabled=bridge!=null,Style=(Style)FindResource("Primary")};
        buttons.Children.Add(save);buttons.Children.Add(pin);buttons.Children.Add(folder);buttons.Children.Add(rename);buttons.Children.Add(load);panel.Children.Add(buttons);
        var window=new Window{Title="Família · "+card.Entry.Name,Width=620,Height=650,MinWidth=470,MinHeight=400,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},WindowStartupLocation=WindowStartupLocation.CenterScreen};
        if(Window.GetWindow(this) is Window owner)window.Owner=owner;
        pin.Click+=(_,_)=>{card.Entry.Pinned=!card.Entry.Pinned;Persist();ApplyFilters();window.Close();};
        rename.Click+=(_,_)=>{window.Close();RenameEntry(card.Entry);};
        load.Click+=async (_,_)=>{window.Close();await LoadAsync([card.Entry]);};
        window.ShowDialog();
    }
    void SaveTagsClick(object sender,RoutedEventArgs e){if(active==null)return;active.Entry.Tags=TagsBox.Text;Persist();ApplyFilters();Status.Text="Etiquetas salvas no catálogo pessoal.";}
    void PinActiveClick(object sender,RoutedEventArgs e){if(active==null)return;active.Entry.Pinned=!active.Entry.Pinned;DetailPin.Content=active.Entry.Pinned?"⚑ Desafixar do topo":"⚐ Fixar no topo";Persist();ApplyFilters();}
    void CopyClick(object sender,RoutedEventArgs e){try{if(active!=null)Clipboard.SetText(active.Entry.Path);}catch(Exception ex){Error(ex);}}
    void FolderClick(object sender,RoutedEventArgs e){if(active!=null)OpenFolder(active.Entry);}
    void RenameActiveClick(object sender,RoutedEventArgs e){if(active!=null)RenameEntry(active.Entry);}
    void RenameSelectedClick(object sender,RoutedEventArgs e)
    {
        if(operation!=null)return;
        if(selection.Count!=1){Status.Text="Selecione exatamente uma família para renomear.";return;}
        var entry=data.Entries.FirstOrDefault(x=>selection.Contains(x.Path));if(entry!=null)RenameEntry(entry);
    }
    void RenameEntry(FamilyEntry entry)
    {
        if(!File.Exists(entry.Path)){Status.Text="O arquivo não está disponível para renomear.";return;}
        var proposed=PromptRename(entry.Name);if(proposed==null)return;
        proposed=proposed.Trim();if(proposed.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase))proposed=proposed[..^4];
        if(proposed.Length==0||proposed=="."||proposed==".."||Path.GetFileName(proposed)!=proposed||proposed.Any(c=>Path.GetInvalidFileNameChars().Contains(c))){MessageBox.Show("Digite um nome de arquivo válido, sem pastas ou caracteres proibidos.","Renomear família",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        var oldPath=entry.Path;var folder=Path.GetDirectoryName(oldPath);if(string.IsNullOrWhiteSpace(folder))return;
        var newPath=Path.Combine(folder,proposed+Path.GetExtension(oldPath));
        if(string.Equals(oldPath,newPath,StringComparison.OrdinalIgnoreCase)){Status.Text="O nome informado é igual ao atual.";return;}
        if(File.Exists(newPath)){MessageBox.Show("Já existe uma família com esse nome na mesma pasta.","Renomear família",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        try
        {
            File.Move(oldPath,newPath);entry.Path=newPath;entry.Name=proposed;
            if(selection.Remove(oldPath))selection.Add(newPath);
            if(active?.Entry==entry){DetailName.Text=entry.Name;DetailPath.Text=entry.Path;}
            Persist();RefreshChoices();ApplyFilters();Status.Text=$"Família renomeada para {entry.Name}.";
        }
        catch(Exception ex){Error(ex);}
    }
    string? PromptRename(string current)
    {
        var input=new TextBox{Text=current,MinWidth=360,Margin=new Thickness(0,8,0,16)};
        var ok=new Button{Content="Renomear",Width=100,IsDefault=true,Margin=new Thickness(0,0,8,0)};
        var cancel=new Button{Content="Cancelar",Width=100,IsCancel=true};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(ok);buttons.Children.Add(cancel);
        var panel=new StackPanel{Margin=new Thickness(20)};panel.Children.Add(new TextBlock{Text="Novo nome da família (sem .rfa):",FontWeight=FontWeights.SemiBold});panel.Children.Add(input);panel.Children.Add(buttons);
        var window=new Window{Title="Renomear família",Width=470,SizeToContent=SizeToContent.Height,Content=panel,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        if(Window.GetWindow(this) is Window owner)window.Owner=owner;
        ok.Click+=(_,_)=>{window.DialogResult=true;window.Close();};window.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};
        return window.ShowDialog()==true?input.Text:null;
    }
    void OpenFolder(FamilyEntry entry)
    {
        try{if(!File.Exists(entry.Path))throw new FileNotFoundException("Arquivo indisponível. Conecte o disco e atualize a biblioteca.");Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,Arguments="/select,\""+entry.Path+"\""});}catch(Exception ex){Error(ex);}
    }
    async void LoadOneClick(object sender,RoutedEventArgs e){if(active!=null)await LoadAsync([active.Entry]);}
    async void LoadSelectedClick(object sender,RoutedEventArgs e){await LoadAsync(data.Entries.Where(x=>selection.Contains(x.Path)).ToList());}
    async void DeleteSelectedClick(object sender,RoutedEventArgs e)
    {
        if(operation!=null||selection.Count==0)return;
        var entries=data.Entries.Where(x=>selection.Contains(x.Path)&&x.Path.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase)).ToList();
        if(entries.Count==0){Status.Text="Nenhuma família .rfa válida foi selecionada.";return;}
        var names=string.Join(Environment.NewLine,entries.Take(12).Select(x=>"• "+x.Name));
        if(entries.Count>12)names+=Environment.NewLine+$"… e mais {entries.Count-12:N0} arquivo(s)";
        var answer=MessageBox.Show($"Enviar {entries.Count:N0} família(s) para a Lixeira do Windows?\n\n{names}\n\nOs arquivos sairão da pasta da biblioteca, mas poderão ser restaurados pela Lixeira.","Excluir famílias selecionadas",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No);
        if(answer!=MessageBoxResult.Yes)return;
        BeginOperation(true);var removed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var skipped=0;
        try
        {
            for(var i=0;i<entries.Count;i++)
            {
                operation!.Token.ThrowIfCancellationRequested();var entry=entries[i];Status.Text=$"Enviando para a Lixeira: {i+1:N0}/{entries.Count:N0} · {entry.Name}";
                if(!File.Exists(entry.Path)){skipped++;continue;}
                if(SendToRecycleBin(entry.Path)){removed.Add(entry.Path);selection.Remove(entry.Path);}else skipped++;
                await Task.Yield();
            }
            if(removed.Count>0){data.Entries.RemoveAll(x=>removed.Contains(x.Path));data.IndexedAt=DateTime.Now;Persist();RefreshChoices();ApplyFilters();}
            Status.Text=$"{removed.Count:N0} arquivo(s) enviado(s) para a Lixeira"+(skipped>0?$" · {skipped:N0} preservado(s) por erro, bloqueio ou indisponibilidade.":".");
        }
        catch(OperationCanceledException){Status.Text=$"Exclusão cancelada. {removed.Count:N0} arquivo(s) já foram enviados para a Lixeira.";}
        catch(Exception ex){Error(ex);}
        finally{EndOperation();UpdateSelection();}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct ShellFileOperation
    {
        public IntPtr WindowHandle;public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)]public string From;
        [MarshalAs(UnmanagedType.LPWStr)]public string To;
        public ushort Flags;public IntPtr AnyOperationsAborted;public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]public string ProgressTitle;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    static extern int SHFileOperation(ref ShellFileOperation operation);
    static bool SendToRecycleBin(string path)
    {
        var operation=new ShellFileOperation{Function=3,From=path+"\0\0",To="",Flags=0x0040|0x0010|0x0004|0x0400,ProgressTitle=""};
        return SHFileOperation(ref operation)==0;
    }
    async void MinimizeClick(object sender,RoutedEventArgs e)
    {
        if(bridge==null||operation!=null)return;
        try{await bridge.HideAsync();}catch(Exception ex){Error(ex);}
    }
    async void DownloadLibraryClickOld(object sender,RoutedEventArgs e)
    {
        if(operation!=null)return;
        if(!new ActivationStore().IsActivated(installationId))
        {
            MessageBox.Show("O download da biblioteca está disponível somente para instalações ativadas. Abra o botão Ativador e ative o sistema.","Biblioteca protegida",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        if(!LibraryDownloadSettings.IsConfigured)
        {
            MessageBox.Show("A fonte online da biblioteca ainda não foi configurada no instalador.","Download da biblioteca",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        var root=data.Roots.FirstOrDefault(Directory.Exists);
        if(string.IsNullOrWhiteSpace(root))
        {
            var folder=new OpenFolderDialog{Title="Escolha a pasta onde a biblioteca será instalada"};
            if(folder.ShowDialog()!=true)return;
            root=folder.FolderName;
            if(!data.Roots.Contains(root,StringComparer.OrdinalIgnoreCase))data.Roots.Insert(0,root);
            Persist();
        }
        BeginOperation(true);
        var added=0;var skipped=0;var packages=0;
        try
        {
            Status.Text="Consultando a biblioteca online…";
            var catalog=await LibraryDownloadService.LoadCatalogAsync(LibraryDownloadSettings.ManifestUrl,operation!.Token);
            var size=catalog.Packages.Sum(p=>Math.Max(0,p.Bytes));
            var answer=MessageBox.Show($"A biblioteca {catalog.Name} possui {catalog.Packages.Count:N0} pacotes e aproximadamente {FormatBytes(size)}.\n\nDeseja iniciar o download?","Baixar biblioteca",MessageBoxButton.YesNo,MessageBoxImage.Question);
            if(answer!=MessageBoxResult.Yes)return;
            foreach(var package in catalog.Packages)
            {
                operation!.Token.ThrowIfCancellationRequested();
                var report=await LibraryDownloadService.DownloadAndInstallAsync(package,root,new Progress<LibraryDownloadProgress>(p=>Status.Text=$"Baixando {p.PackageName}: {p.Percent}% · pacote {packages+1:N0}/{catalog.Packages.Count:N0}"),operation.Token);
                added+=report.Added;skipped+=report.Skipped;packages++;
            }
            Status.Text=$"Download concluído: {added:N0} novas famílias · {skipped:N0} já existentes. Atualizando a lista…";
        }
        catch(OperationCanceledException){Status.Text=$"Download cancelado. {added:N0} famílias já instaladas foram preservadas.";return;}
        catch(Exception ex){Error(ex);return;}
        finally{EndOperation();}
        await ScanAsync();
        Status.Text=$"Biblioteca atualizada: {added:N0} novas famílias · {skipped:N0} já existentes.";
    }
    async void DownloadLibraryClick(object sender,RoutedEventArgs e)
    {
        if(operation!=null)return;
        if(!new ActivationStore().IsActivated(installationId))
        {
            MessageBox.Show("O download da biblioteca esta disponivel somente para instalacoes ativadas. Abra o botao Ativador e ative o sistema.","Biblioteca protegida",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        if(!LibraryDownloadSettings.IsConfigured)
        {
            MessageBox.Show("A fonte online da biblioteca ainda nao foi configurada no instalador.","Download da biblioteca",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        var root=data.Roots.FirstOrDefault(Directory.Exists);
        if(string.IsNullOrWhiteSpace(root))
        {
            var folder=new OpenFolderDialog{Title="Escolha a pasta onde a biblioteca sera instalada"};
            if(folder.ShowDialog()!=true)return;
            root=folder.FolderName;
            if(!data.Roots.Contains(root,StringComparer.OrdinalIgnoreCase))data.Roots.Insert(0,root);
            Persist();
        }

        BeginOperation(true);
        DownloadButton.Content="Consultando biblioteca...";
        LibraryDownloadProgressBar.Visibility=Visibility.Visible;
        LibraryDownloadProgressBar.Value=0;
        LibraryDownloadDetails.Text="Consultando o catalogo online...";
        var added=0;var skipped=0;var packages=0;var scanAfter=false;var completed=false;
        try
        {
            Status.Text="Consultando a biblioteca online...";
            var catalog=await LibraryDownloadService.LoadCatalogAsync(LibraryDownloadSettings.ManifestUrl,operation!.Token);
            var already=catalog.Packages.Where(package=>LibraryDownloadService.IsDownloaded(package,root!)).ToList();
            var pending=catalog.Packages.Where(package=>!LibraryDownloadService.IsDownloaded(package,root!)).ToList();
            var downloadedAt=LibraryDownloadService.GetDownloadedPackages(root!).Select(item=>item.DownloadedAtUtc).OrderByDescending(value=>value).FirstOrDefault();
            if(pending.Count==0)
            {
                LibraryDownloadProgressBar.Value=100;
                DownloadButton.Content="Verificar atualizacoes";
                Status.Text=$"Biblioteca ja baixada: {already.Count:N0}/{catalog.Packages.Count:N0} pacotes.";
                LibraryDownloadDetails.Text=downloadedAt==default
                    ? "Nenhum pacote novo para baixar."
                    : $"Ultimo download: {downloadedAt.ToLocalTime():dd/MM/yyyy HH:mm}. Nenhum pacote novo.";
                MessageBox.Show($"Esta biblioteca ja foi baixada nesta pasta.\n\nPacotes registrados: {already.Count:N0}/{catalog.Packages.Count:N0}.\nNao sera feito outro download dos mesmos arquivos.","Biblioteca ja baixada",MessageBoxButton.OK,MessageBoxImage.Information);
            }
            else
            {
                var size=pending.Sum(package=>Math.Max(0,package.Bytes));
                var summary=already.Count==0
                    ? $"A biblioteca {catalog.Name} possui {pending.Count:N0} pacotes pendentes e aproximadamente {FormatBytes(size)}."
                    : $"{already.Count:N0} pacote(s) ja baixado(s) serao preservado(s).\n\nHa {pending.Count:N0} pacote(s) novo(s) ou alterado(s), aproximadamente {FormatBytes(size)}.";
                var answer=MessageBox.Show(summary+"\n\nDeseja iniciar o download?","Baixar biblioteca",MessageBoxButton.YesNo,MessageBoxImage.Question);
                if(answer==MessageBoxResult.Yes)
                {
                    DownloadButton.Content="Baixando biblioteca...";
                    var totalBytes=pending.Sum(package=>Math.Max(0,package.Bytes));
                    long completedBytes=0;
                    for(var index=0;index<pending.Count;index++)
                    {
                        operation!.Token.ThrowIfCancellationRequested();
                        var package=pending[index];
                        var packageNumber=index+1;
                        var progress=new Progress<LibraryDownloadProgress>(value=>
                        {
                            var currentTotal=value.TotalBytes>0?value.TotalBytes:Math.Max(0,package.Bytes);
                            var received=completedBytes+value.ReceivedBytes;
                            var overall=totalBytes>0?Math.Clamp((int)Math.Round(received*100d/totalBytes),0,100):Math.Clamp((int)Math.Round((index+value.Percent/100d)*100d/pending.Count),0,100);
                            LibraryDownloadProgressBar.Value=overall;
                            Status.Text=$"Baixando biblioteca: {overall}%";
                            LibraryDownloadDetails.Text=$"{value.PackageName} · {value.Percent}% · {FormatBytes(value.ReceivedBytes)} / {FormatBytes(currentTotal)} · pacote {packageNumber:N0}/{pending.Count:N0}";
                        });
                        var report=await LibraryDownloadService.DownloadAndInstallAsync(package,root!,progress,operation.Token);
                        LibraryDownloadService.MarkDownloaded(package,report,root!,LibraryDownloadSettings.ManifestUrl,catalog.Version);
                        added+=report.Added;skipped+=report.Skipped;packages++;scanAfter=true;
                        completedBytes+=package.Bytes>0?package.Bytes:report.DownloadedBytes;
                        LibraryDownloadProgressBar.Value=totalBytes>0?Math.Clamp((int)Math.Round(completedBytes*100d/totalBytes),0,100):Math.Clamp((int)Math.Round(packageNumber*100d/pending.Count),0,100);
                    }
                    completed=true;
                    DownloadButton.Content="Verificar atualizacoes";
                    Status.Text=$"Download concluido: {added:N0} novas familias · {skipped:N0} ja existentes.";
                    LibraryDownloadDetails.Text=$"{packages:N0} pacote(s) registrados. Clique novamente para verificar atualizacoes.";
                }
                else
                {
                    DownloadButton.Content="Baixar biblioteca";
                    Status.Text=$"Download nao iniciado. {already.Count:N0} pacote(s) ja registrados e {pending.Count:N0} pendente(s).";
                    LibraryDownloadDetails.Text="Os pacotes ja baixados nao serao repetidos.";
                }
            }
        }
        catch(OperationCanceledException)
        {
            DownloadButton.Content="Retomar download";
            Status.Text=$"Download cancelado. {added:N0} familias ja instaladas foram preservadas.";
            LibraryDownloadDetails.Text="Os pacotes concluidos foram registrados; o proximo download continuara pelos pendentes.";
        }
        catch(Exception ex)
        {
            DownloadButton.Content="Retomar download";
            LibraryDownloadDetails.Text="O download foi interrompido. Tente novamente para continuar pelos pacotes pendentes.";
            Error(ex);
        }
        finally{EndOperation();}
        if(scanAfter)await ScanAsync();
        if(completed)
        {
            ShowDownloadedLibrary();
            Status.Text=$"Biblioteca atualizada: {added:N0} novas familias · {skipped:N0} ja existentes.";
            LibraryDownloadDetails.Text=$"Nova biblioteca aberta no plugin · {data.Entries.Count:N0} familias indexadas · {packages:N0} pacote(s) registrados.";
        }
    }
    void ShowDownloadedLibrary()
    {
        updating=true;
        mode="Todas";
        category="";
        SearchBox.Clear();
        Categories.SelectedIndex=0;
        CompactCategory.SelectedIndex=0;
        Subcategories.SelectedIndex=0;
        Versions.SelectedIndex=0;
        Sort.SelectedIndex=0;
        updating=false;
        RefreshSubcategories();
        ModeClick(AllButton,new RoutedEventArgs());
        CardScroll.ScrollToTop();
    }
    static string FormatBytes(long bytes)
    {
        if(bytes>=1L<<30)return $"{bytes/(double)(1L<<30):N2} GB";
        if(bytes>=1L<<20)return $"{bytes/(double)(1L<<20):N1} MB";
        return $"{bytes/(double)(1L<<10):N0} KB";
    }
    async void UpdateVersionsClick(object sender,RoutedEventArgs e)
    {
        if(bridge==null||operation!=null)return;
        var dialog=new OpenFolderDialog{Title="Selecionar pasta para atualizar famílias .rfa para o Revit atual"};
        if(dialog.ShowDialog()!=true)return;
        List<UpdateCandidate> candidates;
        try
        {
            candidates=Directory.EnumerateFiles(dialog.FolderName,"*.rfa",SearchOption.AllDirectories)
                .Where(path=>path.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase))
                .Where(path=>!Path.GetFileName(path).StartsWith("~$",StringComparison.OrdinalIgnoreCase)&&!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(path),@"\.\d{4}$"))
                .Select(path=>new UpdateCandidate(path,Path.GetFileNameWithoutExtension(path),Path.GetExtension(path).TrimStart('.').ToUpperInvariant()))
                .ToList();
        }
        catch(Exception ex){Error(ex);return;}
        if(candidates.Count==0){Status.Text="Nenhum arquivo .rfa encontrado nessa pasta.";return;}
        var deleteDuplicates=deleteDuplicatesAfterUpdate;
        Status.Text=$"Atualização automática iniciada · {candidates.Count:N0} famílias encontradas."+(deleteDuplicates?" · duplicatas exatas serão excluídas":"");
        UpdateProgressBar.Visibility=Visibility.Visible;UpdateProgressBar.Value=0;
        UpdateReport? report=null;DuplicateCleanupReport? cleanup=null;string? reportPath=null;
        BeginOperation(true);
        try
        {
            report=await bridge.UpdateAsync(candidates,new Progress<UpdateProgress>(p=>{
                UpdateProgressBar.Value=p.Percent;
                Status.Text=$"{p.Percent}% · {p.Updated} atualizados · {p.Skipped} ignorados · {p.Errors} erros · {Path.GetFileName(p.Current)}";
            }),operation!.Token);
            diagnostic=string.Join(Environment.NewLine,report.Results.Select(r=>$"{r.Status} | {r.Kind} | Revit {r.SourceVersion} → {r.TargetVersion} | {r.Name} | {r.Message}"));
            reportPath=Path.Combine(store.DirectoryPath,"atualizacoes-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".jsonl");
            Directory.CreateDirectory(store.DirectoryPath);File.WriteAllLines(reportPath,report.Results.Select(r=>JsonSerializer.Serialize(r)));
        }
        catch(OperationCanceledException){Status.Text="Atualização cancelada; os arquivos já concluídos foram preservados.";}
        catch(Exception ex){Error(ex);}
        finally{EndOperation();UpdateProgressBar.Visibility=Visibility.Collapsed;}
        if(report==null)return;
        if(report.Updated>0||deleteDuplicates)await ScanAsync();
        if(deleteDuplicates)
        {
            BeginOperation(true);
            try
            {
                var cleanupEntries=candidates.Select(c=>new FamilyEntry{Path=c.Path,Name=c.Name}).ToList();
                cleanup=await CatalogStore.DeleteExactDuplicatesAsync(cleanupEntries,new Progress<string>(s=>Status.Text=s),operation!.Token);
                Persist();
                if(reportPath!=null)File.AppendAllLines(reportPath,cleanup.Results.Select(r=>JsonSerializer.Serialize(new{Tipo="Duplicata",r.Path,r.KeptPath,r.Status,r.Message})));
                diagnostic+=Environment.NewLine+string.Join(Environment.NewLine,cleanup.Results.Select(r=>$"{r.Status} | DUPLICATA | {r.Path} | {r.Message}"));
            }
            catch(OperationCanceledException){Status.Text="Limpeza de duplicatas cancelada; os arquivos já concluídos foram preservados.";}
            catch(Exception ex){Error(ex);}
            finally{EndOperation();}
            if(cleanup?.Deleted>0)await ScanAsync();
        }
        var cleanupSummary=cleanup==null?"":$" · {cleanup.Summary}";
        Status.Text=$"Atualização concluída · {report.Summary}{cleanupSummary}. Relatório salvo em %LOCALAPPDATA%\\BIMFamilias.";
    }
    async Task LoadAsync(IReadOnlyList<FamilyEntry> entries)
    {
        if(bridge==null||operation!=null)return;
        if(entries.Count>50){Status.Text="Selecione até 50 famílias por carregamento.";return;}
        BeginOperation(false);
        try
        {
            var token=operation!.Token;
            Status.Text="Aguardando os arquivos terminarem de ser atualizados…";
            var checks=await Task.WhenAll(entries.Select(async entry=>new {entry,Ready=await FamilyFileGate.WaitUntilStableAsync(entry.Path,TimeSpan.FromMilliseconds(350),TimeSpan.FromSeconds(15),token)}));
            var unavailable=checks.FirstOrDefault(x=>!x.Ready);
            if(unavailable!=null)throw new IOException($"A família não ficou disponível/estável a tempo: {unavailable.entry.Name}.");
            foreach(var entry in entries)
            {
                var info=new FileInfo(entry.Path);
                if(entry.Bytes!=info.Length||entry.Modified!=info.LastWriteTimeUtc.Ticks){entry.Bytes=info.Length;entry.Modified=info.LastWriteTimeUtc.Ticks;entry.Hash="";entry.Version=0;}
            }
            Status.Text="Arquivos atualizados e estáveis. Aguardando o Revit carregar a seleção…";
            Status.Text=await bridge.LoadAsync(entries,HideAfterLoadCheck.IsChecked==true);Persist();
        }
        catch(Exception ex){Error(ex);}finally{EndOperation();UpdateSelection();}
    }
    async void RefreshClick(object sender,RoutedEventArgs e)=>await ScanAsync();
    async Task ScanAsync()
    {
        if(operation!=null)return;
        BeginOperation(true);
        try{
            var token=operation!.Token;
            var result=await Task.Run(()=>CatalogStore.Scan(data.Roots,data.Entries,new Progress<string>(s=>Dispatcher.InvokeAsync(()=>Status.Text=s)),token));
            data.Entries=result.Entries;data.IndexedAt=DateTime.Now;Persist();RefreshChoices();
            Status.Text=$"{data.Entries.Count:N0} famílias indexadas · "+(result.Issues.Count==0?"Biblioteca atualizada.":$"{result.Issues.Count} pastas com avisos. Consulte ⋯ → Último diagnóstico.");
            diagnostic=string.Join("\n",result.Issues);
        }catch(OperationCanceledException){Status.Text="Atualização cancelada; índice anterior preservado.";}catch(Exception ex){Error(ex);}finally{EndOperation();}
    }
    string diagnostic="";
    void BeginOperation(bool cancel){operation=new();RefreshButton.IsEnabled=false;DownloadButton.IsEnabled=false;CancelButton.Visibility=cancel?Visibility.Visible:Visibility.Collapsed;UpdateSelection();}
    void EndOperation(){operation?.Dispose();operation=null;RefreshButton.IsEnabled=true;DownloadButton.IsEnabled=true;CancelButton.Visibility=Visibility.Collapsed;UpdateSelection();}
    void CancelClick(object sender,RoutedEventArgs e)=>operation?.Cancel();
    void Persist(){try{store.Save(data);}catch(Exception ex){Error(ex);}}
    void Error(Exception ex){diagnostic=ex.ToString();Status.Text=ex.Message;}
    void MenuClick(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        void Add(string title,RoutedEventHandler action,bool enabled=true){var item=new MenuItem{Header=title,IsEnabled=enabled};item.Click+=action;menu.Items.Add(item);}
        Add("Adicionar pasta de famílias…",AddFolderClick,operation==null);
        Add("Pastas cadastradas…",RootsClick,operation==null);
        Add("Verificar duplicatas por conteúdo",DuplicatesClick,operation==null);
        Add("Verificar versões desta página no Revit",InspectClick,bridge!=null&&operation==null);
        var duplicateOption=new MenuItem{Header="Excluir duplicatas exatas na próxima atualização (.rfa)",IsCheckable=true,IsChecked=deleteDuplicatesAfterUpdate,IsEnabled=operation==null,ToolTip="Quando marcado, mantém uma cópia e exclui somente arquivos .rfa com conteúdo SHA-256 idêntico."};
        duplicateOption.Click+=(_,_)=>{deleteDuplicatesAfterUpdate=duplicateOption.IsChecked;Status.Text=deleteDuplicatesAfterUpdate?"Opção ativada: duplicatas exatas serão excluídas após a próxima atualização.":"Opção desativada: nenhum arquivo duplicado será excluído.";};
        menu.Items.Add(duplicateOption);
        Add("Atualizar famílias .rfa para o Revit atual…",UpdateVersionsClick,bridge!=null&&operation==null);
        Add("Exportar resultados para CSV…",ExportClick);
        Add("Último diagnóstico",(_,_)=>MessageBox.Show(diagnostic==""?"Nenhum erro registrado.":diagnostic,"Biblioteca de Famílias"));
        menu.PlacementTarget=(Button)sender;menu.IsOpen=true;
    }
    async void AddFolderClick(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog{Title="Selecionar pasta de famílias (.rfa) — disco D:, E: ou rede"};
        if(dialog.ShowDialog()!=true)return;
        if(!data.Roots.Contains(dialog.FolderName,StringComparer.OrdinalIgnoreCase))data.Roots.Add(dialog.FolderName);
        await ScanAsync();
    }
    void RootsClick(object sender,RoutedEventArgs e)
    {
        var stack=new StackPanel{Margin=new Thickness(20)};
        stack.Children.Add(new TextBlock{Text="Pastas consultadas pelo catálogo",FontSize=18,Margin=new Thickness(0,0,0,12)});
        foreach(var root in data.Roots)stack.Children.Add(new TextBlock{Text=root,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
        stack.Children.Add(new TextBlock{Text="Para incluir outro disco, use Adicionar pasta. A indexação lê apenas famílias .rfa.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,16,0,0)});
        new Window{Title="Fontes da biblioteca",Width=650,Height=350,Content=new ScrollViewer{Content=stack},WindowStartupLocation=WindowStartupLocation.CenterScreen}.ShowDialog();
    }
    async void DuplicatesClick(object sender,RoutedEventArgs e)
    {
        BeginOperation(true);
        try{int groups=await CatalogStore.HashDuplicatesAsync(data.Entries,new Progress<string>(s=>Status.Text=s),operation!.Token);Persist();mode="Duplicadas";ApplyFilters();Status.Text=$"{groups:N0} grupos com conteúdo idêntico. Nenhum arquivo removido.";}
        catch(OperationCanceledException){Persist();Status.Text="Comparação cancelada. Resultados parciais preservados.";}
        catch(Exception ex){Persist();Error(ex);}finally{EndOperation();}
    }
    async void InspectClick(object sender,RoutedEventArgs e)
    {
        if(bridge==null)return;BeginOperation(false);
        try{Status.Text=await bridge.InspectAsync(pageCards.Select(c=>c.Entry).ToList());Persist();RefreshChoices();}
        catch(Exception ex){Error(ex);}finally{EndOperation();}
    }
    void ExportClick(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{Filter="CSV|*.csv",FileName="familias-filtradas.csv"};if(dialog.ShowDialog()!=true)return;
        try {
            static string Q(string t)=>"\""+(t.Length>0&&"=+-@".Contains(t[0])?"'":"")+t.Replace("\"","\"\"")+"\"";
            File.WriteAllLines(dialog.FileName,new[]{"Nome;Categoria;Subcategoria;Versao;Bytes;Caminho"}.Concat(filtered.Select(f=>string.Join(";",Q(f.Name),Q(f.Category),Q(f.Subcategory),f.Version==0?"":f.Version.ToString(),f.Bytes.ToString(),Q(f.Path)))),new UTF8Encoding(true));Status.Text=$"{filtered.Count:N0} registros exportados.";
        }catch(Exception ex){Error(ex);}
    }
}

