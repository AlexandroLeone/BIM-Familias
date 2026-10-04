using System.Windows;
using BimFamilias.UI;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace BimFamilias.Desktop;
public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        var app=new Application();
        app.DispatcherUnhandledException+=(_,e)=>{MessageBox.Show(e.Exception.Message,"Biblioteca de Famílias");e.Handled=true;};
        var view=new LibraryView();
        var window=new Window{Title="BIM · Biblioteca de Famílias",Width=1480,Height=920,MinWidth=460,MinHeight=620,Content=view,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        if(args.Length>1 && args[0]=="--smoke") window.Loaded+=async (_,_)=>{
            var checks=new List<string>();
            try{
                for(int n=0;n<300&&!view.IsReady;n++)await Task.Delay(100);
                if(!view.IsReady)throw new Exception("UI did not initialize");
                var search=(TextBox)view.FindName("SearchBox");var results=(TextBlock)view.FindName("ResultsLabel");
                search.Text="zzzznonexistent012345";await Task.Delay(1500);
                if(!results.Text.StartsWith("0 "))throw new Exception("Empty search failed");checks.Add("PASS empty search");
                search.Text="sofa";await Task.Delay(1500);
                if(results.Text.StartsWith("0 "))throw new Exception("Search sofa failed");checks.Add("PASS real sofa search: "+results.Text);
                search.Clear();var categories=(ListBox)view.FindName("Categories");
                categories.SelectedItem=categories.Items.Cast<CategoryChoice>().First(c=>c.Name=="Sala");await Task.Delay(1800);
                checks.Add("PASS category Sala: "+results.Text);
                search.Text="sofa";await Task.Delay(1600);
                var cards=(ItemsControl)view.FindName("Cards");
                var focus=cards.Items.Cast<CardModel>().FirstOrDefault(c=>c.Preview!=null);
                if(focus!=null){await (Task)view.GetType().GetMethod("FocusAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(view,[focus])!;checks.Add("PASS family details");}
                window.Width=640;await Task.Delay(300);
                if(((FrameworkElement)view.FindName("CompactCategory")).Visibility!=Visibility.Visible)throw new Exception("Narrow layout failed");checks.Add("PASS compact layout");
                window.Width=1480;await Task.Delay(500);window.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(args[1]))encoder.Save(output);
                checks.Add("PASS WPF render");File.WriteAllLines(args[1]+".txt",checks);
            }catch(Exception ex){File.WriteAllText(args[1]+".txt",string.Join("\n",checks)+"\nFAIL "+ex);}
            finally{window.Close();}
        };
        app.Run(window);
    }
}

