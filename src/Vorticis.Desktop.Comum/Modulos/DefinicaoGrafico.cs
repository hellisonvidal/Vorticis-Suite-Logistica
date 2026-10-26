using Vorticis.Application.Relatorios;
using Vorticis.Infra;

namespace Vorticis.Desktop.Comum.Modulos;

/// <summary>Período e quem pede o gráfico; os dados vêm dos serviços de cada módulo (com as permissões deles).</summary>
public sealed record ContextoGrafico(VorticisApp App, long Empresa, long Usuario, DateOnly Inicio, DateOnly Fim);

/// <summary>Gráfico de uma função do sistema (Relatórios › Gráficos). <see cref="Area"/> agrupa no menu (Frota, Transporte, Financeiro, Contábil).</summary>
public sealed record DefinicaoGrafico(string Chave, string Titulo, string Area, Func<ContextoGrafico, Task<DadosGrafico>> Gerar)
{
    public string Tela => "grafico." + Chave;
}
