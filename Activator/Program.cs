using BimFamilias.Core;

Console.OutputEncoding=System.Text.Encoding.UTF8;
var moment=DateTime.Now;
if(args.Length>0&&DateTime.TryParseExact(args[0],"dd/MM/yyyy HH:mm",null,System.Globalization.DateTimeStyles.None,out var requested))moment=requested;
Console.WriteLine("BIM · Ativador da Biblioteca de Famílias");
Console.WriteLine($"Data/hora usada: {moment:dd/MM/yyyy HH:mm}");
Console.WriteLine($"Código de ativação: {ActivationCodeService.Generate(moment)}");
Console.WriteLine();
Console.WriteLine("Uso opcional: BimFamilias.Ativador.exe \"dd/MM/yyyy HH:mm\"");
