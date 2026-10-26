using System.Runtime.CompilerServices;
using Vorticis.Folha.Dados;
using Vorticis.Folha.Servicos;
using Vorticis.Infra;
using Vorticis.Infra.Relatorios;

namespace Vorticis.Folha;

/// <summary>
/// Raiz de composição do módulo Folha sobre o banco do <see cref="VorticisApp"/> (mesmo login, empresas e permissões).
/// Uma instância por conexão: <see cref="De"/> a reaproveita enquanto o <see cref="VorticisApp"/> existir.
/// </summary>
public sealed class ServicosFolha
{
    private static readonly ConditionalWeakTable<VorticisApp, ServicosFolha> Instancias = new();

    public ServicoCadastrosFolha Cadastros { get; }
    public ServicoFolha Folha { get; }
    public ServicoContabilizacaoFolha Contabilizacao { get; }
    public ServicoRelatoriosFolha Relatorios { get; }
    public ServicoESocial ESocial { get; }
    public AssistenteEsocial AssistenteEsocial { get; }
    public ServicoTransmissaoEsocial Transmissao { get; }
    public ServicoProLabore ProLabore { get; }

    public ServicosFolha(VorticisApp app)
    {
        var ds = app.DataSource;
        var cadastros = new RepositorioCadastrosFolha(ds);
        var movimento = new RepositorioMovimentoFolha(ds);
        var contab = new RepositorioContabilizacaoFolha(ds);
        Cadastros = new ServicoCadastrosFolha(cadastros, app.Autorizador);
        Folha = new ServicoFolha(cadastros, movimento, contab, app.Autorizador);
        Contabilizacao = new ServicoContabilizacaoFolha(contab, movimento, app.Lancamentos, app.Autorizador, app.Pendencias);
        app.Pendencias.RegistrarProcessador(Contabilizacao.ProcessadorPendencias);
        Relatorios = new ServicoRelatoriosFolha(cadastros, movimento, contab, new GeradorPdf(), app.Autorizador);
        var repoEsocial = new RepositorioESocial(ds);
        ESocial = new ServicoESocial(cadastros, movimento, repoEsocial, app.Autorizador);
        AssistenteEsocial = new AssistenteEsocial(cadastros, movimento, repoEsocial, app.Autorizador);
        ProLabore = new ServicoProLabore(new RepositorioProLabore(ds), cadastros, movimento, app.Autorizador);
        Transmissao = new ServicoTransmissaoEsocial(cadastros, repoEsocial, new RepositorioTransmissaoEsocial(ds), app.Responsaveis, app.Autorizador);
    }

    public static ServicosFolha De(VorticisApp app) => Instancias.GetValue(app, a => new ServicosFolha(a));
}
