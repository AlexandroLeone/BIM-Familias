using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace BimFamilias.Instalador;

public static class Program
{
    [STAThread]
    public static void Main(){var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};app.Run(new InstallerWindow());}
}

sealed record VersionOption(string Year,string? Source,bool Available,string Message);

sealed class InstallerWindow:Window
{
    const string AddInId="2E766DF8-CFC7-4713-B0B9-B7E26E533DA8";
    const string EmbeddedSource="[build embutida no EXE]";
    const string EmbeddedPrefix="BimFamilias.Instalador.Embedded.";
    readonly string packageRoot;
    readonly StackPanel versionsPanel=new();
    readonly TextBlock status=new();
    readonly Button installButton=new();
    readonly List<(VersionOption Option,CheckBox CheckBox)> options=[];
    public InstallerWindow()
    {
        packageRoot=FindPackageRoot();Title="Instalador · Biblioteca de Famílias";Width=700;Height=540;MinWidth=620;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var title=new TextBlock{Text="Biblioteca de Famílias",FontSize=25,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(19,37,53)),Margin=new Thickness(0,0,0,5)};
        var intro=new TextBlock{Text="Selecione as versões do Revit para instalar. O instalador usa somente uma build própria para cada versão.",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(80,100,119)),Margin=new Thickness(0,0,0,16)};
        var package=new TextBlock{Text="Pacote: "+(HasEmbeddedBuild()?EmbeddedSource:packageRoot),FontSize=11,Foreground=Brushes.SlateGray,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,0,0,14)};
        versionsPanel.Margin=new Thickness(0,0,0,12);
        var scroll=new ScrollViewer{Content=versionsPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderBrush=new SolidColorBrush(Color.FromRgb(220,227,234)),BorderThickness=new Thickness(1),Padding=new Thickness(10),MinHeight=150};
        status.TextWrapping=TextWrapping.Wrap;status.Foreground=new SolidColorBrush(Color.FromRgb(80,100,119));status.Margin=new Thickness(0,8,0,10);status.Text="Pronto para detectar versões.";
        installButton.Content="Instalar selecionadas";installButton.Padding=new Thickness(16,8,16,8);installButton.Background=new SolidColorBrush(Color.FromRgb(22,125,141));installButton.Foreground=Brushes.White;installButton.Click+=InstallClick;
        var close=new Button{Content="Fechar",Padding=new Thickness(16,8,16,8),Margin=new Thickness(8,0,0,0),IsCancel=true};close.Click+=(_,_)=>Close();
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(installButton);actions.Children.Add(close);
        var panel=new StackPanel{Margin=new Thickness(28)};panel.Children.Add(title);panel.Children.Add(intro);panel.Children.Add(package);panel.Children.Add(scroll);panel.Children.Add(status);panel.Children.Add(actions);Content=panel;
        Loaded+=(_,_)=>LoadOptions();
    }
    string FindPackageRoot()
    {
        var current=AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        for(var i=0;i<4&&current.Length>0;i++)
        {
            if(Directory.Exists(Path.Combine(current,"dist")))return current;
            var parent=Directory.GetParent(current);if(parent==null)break;current=parent.FullName;
        }
        return AppContext.BaseDirectory;
    }
    void LoadOptions()
    {
        versionsPanel.Children.Clear();options.Clear();
        var years=DetectYears();
        foreach(var year in years)
        {
            var source=FindBuild(year);var available=source!=null;
            var option=new VersionOption(year,source,available,available?"Build compatível disponível.":"Build própria ausente; não será instalado.");
            var check=new CheckBox{Content=$"Revit {year} — {option.Message}",IsChecked=available,IsEnabled=available,Margin=new Thickness(3,5,3,5),FontSize=14};
            versionsPanel.Children.Add(check);options.Add((option,check));
        }
        installButton.IsEnabled=options.Any(x=>x.Option.Available);
        status.Text=years.Count==0?"Nenhuma versão do Revit foi detectada.":"Selecione as versões compatíveis e clique em Instalar selecionadas.";
    }
    List<string> DetectYears()
    {
        var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots=new[]{Environment.GetEnvironmentVariable("ProgramFiles"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","Revit","Addins")}.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!);
        foreach(var root in roots)
        {
            try
            {
                if(!Directory.Exists(root))continue;
                foreach(var dir in Directory.GetDirectories(root))
                {
                    var name=Path.GetFileName(dir);var match=Regex.Match(name??"",@"(?:Revit\s+)?(20\d{2})$",RegexOptions.IgnoreCase);if(match.Success)found.Add(match.Groups[1].Value);
                }
            }
            catch(UnauthorizedAccessException){}
        }
        if(found.Count==0&&FindBuild("2027")!=null)found.Add("2027");
        return found.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
    }
    string? FindBuild(string year)
    {
        var specific=Path.Combine(packageRoot,"dist","revit-"+year);if(File.Exists(Path.Combine(specific,"BimFamilias.Revit.dll")))return specific;
        if(year=="2027")
        {
            var current=Path.Combine(packageRoot,"dist","revit-final");if(File.Exists(Path.Combine(current,"BimFamilias.Revit.dll")))return current;
            if(HasEmbeddedBuild())return EmbeddedSource;
        }
        return null;
    }
    async void InstallClick(object sender,RoutedEventArgs e)
    {
        var selected=options.Where(x=>x.CheckBox.IsChecked==true&&x.Option.Source!=null).Select(x=>x.Option).ToList();if(selected.Count==0)return;
        installButton.IsEnabled=false;var messages=new List<string>();
        try{foreach(var option in selected){status.Text=$"Instalando Revit {option.Year}…";messages.Add(await Task.Run(()=>InstallBuild(option.Year,option.Source!)));}status.Text=string.Join(Environment.NewLine,messages)+Environment.NewLine+"Concluído. Reinicie o Revit.";}
        catch(Exception ex){status.Text="Instalação interrompida: "+ex.Message;}
        finally{installButton.IsEnabled=options.Any(x=>x.Option.Available);}
    }
    string InstallBuild(string year,string source)
    {
        var files=ReadBuild(source);if(files.Count==0)throw new IOException("Nenhuma DLL do complemento foi encontrada em "+source);
        var hashInput=string.Join("|",files.Select(file=>file.Name+":"+Convert.ToHexString(SHA256.HashData(file.Data))));var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        var addinRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","Revit","Addins",year);var target=Path.Combine(addinRoot,"BimFamilias","1.0-"+hash[..12]);var manifest=Path.Combine(addinRoot,"BimFamilias.addin");
        if(File.Exists(manifest)){var old=XDocument.Load(manifest);var oldId=old.Descendants("AddInId").FirstOrDefault()?.Value;if(oldId!=AddInId)throw new InvalidOperationException("O manifesto existente pertence a outro complemento.");}
        Directory.CreateDirectory(target);
        foreach(var file in files){var destination=Path.Combine(target,file.Name);if(File.Exists(destination)&&!HashesEqual(file.Data,destination))throw new IOException("Arquivo instalado diferente: "+destination);if(!File.Exists(destination))File.WriteAllBytes(destination,file.Data);}
        var installation=new{InstallationId=Guid.NewGuid().ToString("N"),InstalledAt=DateTime.UtcNow,PackageHash=hash,RevitYear=year};File.WriteAllText(Path.Combine(target,"installation.json"),JsonSerializer.Serialize(installation));
        var xml=new XDocument(new XElement("RevitAddIns",new XElement("AddIn",new XAttribute("Type","Application"),new XElement("Name","Biblioteca de Familias"),new XElement("Assembly",Path.Combine(target,"BimFamilias.Revit.dll")),new XElement("AddInId",AddInId),new XElement("FullClassName","BimFamilias.Revit.App"),new XElement("VendorId","DGMX"),new XElement("VendorDescription","Biblioteca local de familias BIM"),new XElement("AllowLoadingIntoExistingSession","Yes"))));Directory.CreateDirectory(addinRoot);xml.Save(manifest);
        return $"Revit {year}: instalado.";
    }
    static bool HasEmbeddedBuild()=>Assembly.GetExecutingAssembly().GetManifestResourceNames().Any(x=>x.StartsWith(EmbeddedPrefix,StringComparison.Ordinal));
    static List<(string Name,byte[] Data)> ReadBuild(string source)
    {
        if(source==EmbeddedSource)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(x=>x.StartsWith(EmbeddedPrefix,StringComparison.Ordinal)).Select(x=>(Name:x[EmbeddedPrefix.Length..],Data:ReadResource(x))).OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToList();
        }
        return Directory.GetFiles(source,"BimFamilias.*").OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Select(x=>(Name:Path.GetFileName(x),Data:File.ReadAllBytes(x))).ToList();
    }
    static byte[] ReadResource(string name)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)??throw new IOException("Recurso embutido não encontrado: "+name);
        using var memory=new MemoryStream();stream.CopyTo(memory);return memory.ToArray();
    }
    static bool HashesEqual(byte[] first,string second)
    {
        var a=SHA256.HashData(first);var b=SHA256.HashData(File.ReadAllBytes(second));return a.SequenceEqual(b);
    }
}
