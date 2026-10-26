using Avalonia.Data.Converters;
using Avalonia.Media;
using Vorticis.Application.Cadastros;

namespace Vorticis.Desktop.Comum.Views;

public static class Conversores
{
    public static readonly IValueConverter OkOuErro =
        new FuncValueConverter<bool, IBrush>(ok => ok ? new SolidColorBrush(Color.Parse("#1B6E3A")) : new SolidColorBrush(Color.Parse("#B3261E")));

    public static readonly IValueConverter NomeRegime = new FuncValueConverter<RegimeTributario, string>(r => r switch
    {
        RegimeTributario.MEI => "MEI",
        RegimeTributario.SimplesNacional => "Simples Nacional",
        RegimeTributario.LucroPresumido => "Lucro Presumido",
        RegimeTributario.LucroReal => "Lucro Real",
        RegimeTributario.LucroArbitrado => "Lucro Arbitrado",
        RegimeTributario.ImuneIsenta => "Imune / Isenta",
        _ => r.ToString(),
    });
}
