namespace Vorticis.Desktop.Comum.Modulos;

/// <summary>
/// Ícones da Suite desenhados como caminhos vetoriais numa grade de 24×24 (sintaxe de Path do Avalonia; "F0" = recortes por par/ímpar,
/// "F1" = união). Ficam nítidos em qualquer tamanho e pegam a cor de quem os desenha (branco sobre a cor do módulo, ou a cor do módulo).
/// </summary>
public static class IconesModulos
{
    /// <summary>Caminhão: baú, cabine com janela e duas rodas com cubo.</summary>
    public const string Frota =
        "F0 M2,5.5 H13.5 V15 H2 Z M14.5,8.5 H18.2 L21.5,12.2 V15 H14.5 Z M15.8,9.8 V12 H19.4 L17.6,9.8 Z " +
        "M6.2,15.5 A2.1,2.1 0 1 0 6.2,19.7 A2.1,2.1 0 1 0 6.2,15.5 Z M6.2,16.7 A0.9,0.9 0 1 1 6.2,18.5 A0.9,0.9 0 1 1 6.2,16.7 Z " +
        "M17.6,15.5 A2.1,2.1 0 1 0 17.6,19.7 A2.1,2.1 0 1 0 17.6,15.5 Z M17.6,16.7 A0.9,0.9 0 1 1 17.6,18.5 A0.9,0.9 0 1 1 17.6,16.7 Z";

    /// <summary>Rota: ponto de partida, trajeto tracejado e o marcador do destino.</summary>
    public const string Transporte =
        "F0 M17,1.8 C14.6,1.8 12.7,3.7 12.7,6.1 C12.7,9.3 17,13.9 17,13.9 C17,13.9 21.3,9.3 21.3,6.1 C21.3,3.7 19.4,1.8 17,1.8 Z " +
        "M17,4.4 A1.7,1.7 0 1 0 17,7.8 A1.7,1.7 0 1 0 17,4.4 Z " +
        "M5,15.9 A2.6,2.6 0 1 0 5,21.1 A2.6,2.6 0 1 0 5,15.9 Z M5,17.4 A1.1,1.1 0 1 0 5,19.6 A1.1,1.1 0 1 0 5,17.4 Z " +
        "M8.9,17.7 H11.1 V19.3 H8.9 Z M12.5,17.7 H14.7 V19.3 H12.5 Z M16.2,15.5 H17.8 V19.3 H16.2 Z";

    /// <summary>Chave de boca: cabeça com abertura e cabo arredondado com furo.</summary>
    public const string Manutencao =
        "F1 M4.88,2.48 A5,5 0 1 1 2.48,4.88 L5.38,7.78 L7.78,5.38 Z " +
        "M10.13,7.87 L20.63,18.37 A1.6,1.6 0 0 1 18.37,20.63 L7.87,10.13 Z " +
        "M17.6,16.8 A0.8,0.8 0 1 0 17.6,18.4 A0.8,0.8 0 1 0 17.6,16.8 Z";

    /// <summary>Nota fiscal: folha com borda serrilhada e linhas de itens.</summary>
    public const string Faturamento =
        "F0 M5,2 H19 V21 L17.25,19.6 L15.5,21 L13.75,19.6 L12,21 L10.25,19.6 L8.5,21 L6.75,19.6 L5,21 Z " +
        "M8,5.8 H16 V7.4 H8 Z M8,9.4 H16 V11 H8 Z M8,13 H13 V14.6 H8 Z";

    /// <summary>Carteira: corpo, aba de cima e o fecho com botão.</summary>
    public const string Financeiro =
        "F0 M3,7 H19 A2,2 0 0 1 21,9 V18 A2,2 0 0 1 19,20 H5 A2,2 0 0 1 3,18 Z M5,5.6 L15.2,2.5 L16.4,5.6 Z " +
        "M14.6,11.2 H19.6 V15.8 H14.6 Z M16.7,12.5 A1,1 0 1 0 16.7,14.5 A1,1 0 1 0 16.7,12.5 Z";

    /// <summary>Gráfico de barras crescentes sobre a linha de base.</summary>
    public const string Gerencial =
        "F1 M2,20.4 H22 V22 H2 Z " +
        "M3.5,19 V13.8 A1.2,1.2 0 0 1 4.7,12.6 H6.1 A1.2,1.2 0 0 1 7.3,13.8 V19 Z " +
        "M10.1,19 V9.8 A1.2,1.2 0 0 1 11.3,8.6 H12.7 A1.2,1.2 0 0 1 13.9,9.8 V19 Z " +
        "M16.7,19 V5.2 A1.2,1.2 0 0 1 17.9,4 H19.3 A1.2,1.2 0 0 1 20.5,5.2 V19 Z";

    /// <summary>Calculadora: visor e teclado 3×3.</summary>
    public const string Contabilidade =
        "F0 M6,2 H18 A2,2 0 0 1 20,4 V20 A2,2 0 0 1 18,22 H6 A2,2 0 0 1 4,20 V4 A2,2 0 0 1 6,2 Z M6.6,4.6 H17.4 V8.6 H6.6 Z " +
        "M6.6,10.6 H9 V12.8 H6.6 Z M10.8,10.6 H13.2 V12.8 H10.8 Z M15,10.6 H17.4 V12.8 H15 Z " +
        "M6.6,14.2 H9 V16.4 H6.6 Z M10.8,14.2 H13.2 V16.4 H10.8 Z M15,14.2 H17.4 V16.4 H15 Z " +
        "M6.6,17.8 H9 V20 H6.6 Z M10.8,17.8 H13.2 V20 H10.8 Z M15,17.8 H17.4 V20 H15 Z";

    /// <summary>Marca da Suite: o "V" do Vorticis.</summary>
    public const string Marca = "F1 M2.5,4 H7.2 L12,15.3 L16.8,4 H21.5 L14.2,20 H9.8 Z";

    /// <summary>Layouts: quatro quadros.</summary>
    public const string Layout = "F1 M3,3 H10 V10 H3 Z M14,3 H21 V10 H14 Z M3,14 H10 V21 H3 Z M14,14 H21 V21 H14 Z";

    /// <summary>Alfinete (fixar a navegação).</summary>
    public const string Fixar = "F1 M9,2 H15 V4 H14 V9.2 L17,12 V14 H13 V21 L12,22 L11,21 V14 H7 V12 L10,9.2 V4 H9 Z";

    /// <summary>Casa (início).</summary>
    public const string Inicio = "F1 M12,3 L21,11 H18.5 V20 H14 V14.5 H10 V20 H5.5 V11 H3 Z";

    /// <summary>Lupa (busca).</summary>
    public const string Busca =
        "F0 M10.5,3 A7.5,7.5 0 1 0 10.5,18 A7.5,7.5 0 1 0 10.5,3 Z M10.5,5.4 A5.1,5.1 0 1 1 10.5,15.6 A5.1,5.1 0 1 1 10.5,5.4 Z " +
        "M16.92,15.08 L21.72,19.88 L19.88,21.72 L15.08,16.92 Z";

    /// <summary>Ícone de um módulo conhecido pelo executável (os da Suite Logística trazem o seu em <see cref="IModuloDesktop.Icone"/>).</summary>
    public static string? DoExecutavel(string executavel) => executavel switch
    {
        "VorticisFrota" => Frota,
        "VorticisGerencial" => Gerencial,
        "VorticisContabil" => Contabilidade,
        "VorticisFiscal" => Faturamento,
        _ => null,
    };
}
