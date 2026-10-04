using BimFamilias.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BimFamilias.UI;

public static class ActivationDialog
{
    public static bool EnsureActivated(string installationId)
    {
        var store=new ActivationStore();
        if(store.IsUsable(installationId))return true;
        var window=new ActivationWindow(store,installationId);return window.ShowDialog()==true;
    }
    public static void ShowStatus(string installationId)
    {
        var window=new ActivationStatusWindow(new ActivationStore(),installationId);window.ShowDialog();
    }
}

sealed class ActivationStatusWindow:Window
{
    readonly ActivationStore store;readonly string installationId;readonly TextBlock status;readonly TextBlock detail;readonly Button activate;readonly Button trial;
    public ActivationStatusWindow(ActivationStore activationStore,string id)
    {
        store=activationStore;installationId=id;
        Title="Status da ativação · Biblioteca de Famílias";Width=500;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var title=new TextBlock{Text="Status do ativador",FontSize=22,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(19,37,53)),Margin=new Thickness(0,0,0,14)};
        status=new TextBlock{FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
        detail=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(80,100,119)),Margin=new Thickness(0,0,0,18)};
        activate=new Button{Content="Ativar agora",Width=120,Padding=new Thickness(10,7,10,7)};
        var request=new Button{Content="Solicitar ativação",Width=140,Padding=new Thickness(10,7,10,7),Margin=new Thickness(8,0,0,0)};
        trial=new Button{Content="Teste por 7 dias",Width=125,Padding=new Thickness(10,7,10,7),Margin=new Thickness(8,0,0,0)};
        var close=new Button{Content="Fechar",Width=90,Padding=new Thickness(10,7,10,7),Margin=new Thickness(8,0,0,0),IsCancel=true};
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(activate);actions.Children.Add(request);actions.Children.Add(trial);actions.Children.Add(close);
        var panel=new StackPanel{Margin=new Thickness(28)};panel.Children.Add(title);panel.Children.Add(status);panel.Children.Add(detail);panel.Children.Add(actions);Content=panel;
        activate.Click+=ActivateClick;request.Click+=RequestClick;trial.Click+=TrialClick;Loaded+=(_,_)=>Refresh();
    }
    void Refresh()
    {
        var isActivated=store.IsActivated(installationId);var isTrial=store.IsTrialActive(installationId);var usedTrial=store.HasUsedTrial(installationId);
        if(isActivated){status.Text="Sistema ativado";status.Foreground=new SolidColorBrush(Color.FromRgb(30,130,76));detail.Text="A Biblioteca de Famílias está liberada para esta instalação.";}
        else if(isTrial){var remaining=store.GetTrialRemaining(installationId)??TimeSpan.Zero;var days=Math.Max(0,(int)Math.Ceiling(remaining.TotalDays));status.Text="Teste ativo";status.Foreground=new SolidColorBrush(Color.FromRgb(198,125,18));detail.Text=$"Faltam {days} dias de teste ({remaining.Days} dias, {remaining.Hours} horas e {remaining.Minutes} minutos).";}
        else {status.Text="Sistema não ativado";status.Foreground=new SolidColorBrush(Color.FromRgb(183,52,52));detail.Text=usedTrial?"O teste de 7 dias já foi utilizado. Ative agora para continuar usando a biblioteca.":"Ative agora ou inicie o teste gratuito de 7 dias.";}
        activate.IsEnabled=!isActivated;trial.IsEnabled=!isActivated&&!usedTrial;
    }
    void ActivateClick(object sender,RoutedEventArgs e)
    {
        var window=new ActivationWindow(store,installationId){Owner=this};if(window.ShowDialog()==true)Refresh();
    }
    void RequestClick(object sender,RoutedEventArgs e)
    {
        try{var text=Uri.EscapeDataString("Olá, preciso solicitar a ativação da Biblioteca de Famílias. Instalação: "+installationId);Process.Start(new ProcessStartInfo("https://wa.me/55649996170494?text="+text){UseShellExecute=true});}
        catch(Exception ex){MessageBox.Show(this,"Não foi possível abrir o WhatsApp: "+ex.Message,"Solicitar ativação",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    void TrialClick(object sender,RoutedEventArgs e)
    {
        try{if(store.StartTrial(installationId,DateTime.Now))Refresh();else MessageBox.Show(this,"O período de teste desta instalação já foi utilizado.","Teste por 7 dias",MessageBoxButton.OK,MessageBoxImage.Information);}
        catch(Exception ex){MessageBox.Show(this,"Não foi possível iniciar o teste: "+ex.Message,"Teste por 7 dias",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
}

sealed class ActivationWindow:Window
{
    readonly ActivationStore store;readonly string installationId;readonly TextBox codeBox;readonly TextBlock clock;
    public ActivationWindow(ActivationStore activationStore,string id)
    {
        store=activationStore;installationId=id;
        Title="Ativação · Biblioteca de Famílias";Width=470;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var title=new TextBlock{Text="Ative a Biblioteca de Famílias",FontSize=22,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(19,37,53)),Margin=new Thickness(0,0,0,16)};
        clock=new TextBlock{Foreground=new SolidColorBrush(Color.FromRgb(22,125,141)),Margin=new Thickness(0,0,0,8)};
        codeBox=new TextBox{FontSize=22,Padding=new Thickness(10,7,10,7),MaxLength=11,HorizontalContentAlignment=HorizontalAlignment.Center};
        var activate=new Button{Content="Ativar",Width=110,Padding=new Thickness(12,7,12,7),IsDefault=true,Background=new SolidColorBrush(Color.FromRgb(22,125,141)),Foreground=Brushes.White};
        var request=new Button{Content="Solicitar ativação",Width=140,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,0,8,0)};
        var trial=new Button{Content="Teste por 7 dias",Width=125,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,0,8,0),IsEnabled=!store.HasUsedTrial(installationId)};
        var cancel=new Button{Content="Sair",Width=90,Padding=new Thickness(12,7,12,7),IsCancel=true,Margin=new Thickness(8,0,0,0)};
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(request);actions.Children.Add(trial);actions.Children.Add(activate);actions.Children.Add(cancel);
        var panel=new StackPanel{Margin=new Thickness(28)};panel.Children.Add(title);panel.Children.Add(clock);panel.Children.Add(codeBox);panel.Children.Add(actions);Content=panel;
        activate.Click+=ActivateClick;request.Click+=RequestClick;trial.Click+=TrialClick;Loaded+=(_,_)=>{codeBox.Focus();UpdateClock();};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>UpdateClock();timer.Start();Closed+=(_,_)=>timer.Stop();
    }
    void UpdateClock()=>clock.Text=$"Hora local: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
    void ActivateClick(object sender,RoutedEventArgs e)
    {
        var now=DateTime.Now;
        if(!ActivationCodeService.Validate(codeBox.Text,now)){MessageBox.Show(this,"Código inválido. O código é válido somente por 1 hora.","Ativação não concluída",MessageBoxButton.OK,MessageBoxImage.Warning);codeBox.Focus();codeBox.SelectAll();return;}
        try{store.Save(installationId,codeBox.Text,now);DialogResult=true;Close();}
        catch(Exception ex){MessageBox.Show(this,"Não foi possível salvar a ativação: "+ex.Message,"Ativação",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    void RequestClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var text=Uri.EscapeDataString("Olá, preciso solicitar a ativação da Biblioteca de Famílias. Instalação: "+installationId);
            Process.Start(new ProcessStartInfo("https://wa.me/55649996170494?text="+text){UseShellExecute=true});
        }
        catch(Exception ex){MessageBox.Show(this,"Não foi possível abrir o WhatsApp: "+ex.Message,"Solicitar ativação",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    void TrialClick(object sender,RoutedEventArgs e)
    {
        try
        {
            if(!store.StartTrial(installationId,DateTime.Now)){MessageBox.Show(this,"O período de teste desta instalação já foi utilizado.","Teste por 7 dias",MessageBoxButton.OK,MessageBoxImage.Information);return;}
            DialogResult=true;Close();
        }
        catch(Exception ex){MessageBox.Show(this,"Não foi possível iniciar o teste: "+ex.Message,"Teste por 7 dias",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
}
