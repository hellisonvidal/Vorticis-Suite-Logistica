using Vorticis.Application.Obrigacoes;
using Vorticis.Application.Conciliacao;
using Vorticis.Application.Imobilizado;
using Npgsql;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Application.Relatorios;
using Vorticis.Application.Seguranca;
using Vorticis.Application.Sistema;
using Vorticis.Application.Sped;
using Vorticis.Infra.Sistema;
using Vorticis.Infra.Cnpj;
using Vorticis.Infra.Migracoes;
using Vorticis.Infra.Postgres;
using Vorticis.Infra.Relatorios;
using Vorticis.Infra.Sped;

namespace Vorticis.Infra;

/// <summary>Composição dos serviços da aplicação sobre um banco PostgreSQL (raiz de composição).</summary>
public sealed class VorticisApp : IAsyncDisposable
{
    private readonly NpgsqlDataSource _ds;

    private VorticisApp(NpgsqlDataSource ds, string connectionString, IConsultaCnpj consultaCnpj, IConsultaInscricaoEstadual consultaIe)
    {
        _ds = ds;
        var repoPerfis = new RepositorioPerfis(ds);
        var autorizador = new Autorizador(repoPerfis);
        Autorizador = autorizador;
        Usuarios = new ServicoUsuarios(new RepositorioUsuarios(ds));
        Perfis = new ServicoPerfis(repoPerfis);
        var repoContabil = new RepositorioContabil(ds);
        Lancamentos = new ServicoLancamento(repoContabil, autorizador);
        Periodos = new ServicoPeriodo(repoContabil, autorizador);
        var repoPlano = new RepositorioPlanoContas(ds);
        PlanoContas = new ServicoPlanoContas(repoPlano, autorizador, new PlanoModeloEmbutido());
        Conciliacao = new ServicoConciliacao(new RepositorioConciliacao(ds), repoPlano, autorizador);
        Historicos = new ServicoHistoricos(new RepositorioHistoricos(ds), autorizador);
        var repoParticipantes = new RepositorioParticipantes(ds);
        Participantes = new ServicoParticipantes(repoParticipantes, repoPlano, autorizador);
        Imobilizado = new ServicoImobilizado(new RepositorioImobilizado(ds), repoPlano, repoParticipantes, Lancamentos, autorizador);
        Importacao = new ServicoImportacao(new RepositorioImportacao(ds), Lancamentos, autorizador);
        Consultas = new ConsultasContabeis(ds);
        Relatorios = new ServicoRelatorios(Consultas, autorizador);
        Gerenciais = new ServicoGerenciais(Consultas, new RepositorioOrcamento(ds), autorizador);
        DiferencasSaldos = new ServicoDiferencasSaldos(Relatorios, repoContabil, autorizador);
        RepositorioEmpresas = new RepositorioEmpresa(ds);
        Empresas = new ServicoEmpresa(RepositorioEmpresas, new PlanoModeloEmbutido(), consultaCnpj, consultaIe);
        var repoConfiguracao = new RepositorioConfiguracao(ds);
        Licenca = new ServicoLicenca(repoConfiguracao);
        ConfiguracoesGerais = new ServicoConfiguracoesGerais(repoConfiguracao);
        Reorganizacao = new ServicoReorganizacao(new RepositorioReorganizacao(ds), ConfiguracoesGerais, autorizador);
        Certificados = new ServicoCertificados(new RepositorioCertificados(ds), null, autorizador);
        Encerramento = new ServicoEncerramento(repoContabil, Consultas, autorizador);
        PlanoReferencial = new ServicoPlanoReferencial(new RepositorioPlanoReferencial(ds), autorizador);
        Responsaveis = new ServicoResponsaveis(new RepositorioResponsaveis(ds), RepositorioEmpresas, autorizador, consultaCnpj);
        Demonstracoes = new ServicoDemonstracoes(Consultas, autorizador);
        Assinaturas = new ServicoAssinaturas(Responsaveis);
        Livros = new ServicoLivros(Relatorios, Demonstracoes, Consultas, RepositorioEmpresas, autorizador, new GeradorPdf(), Assinaturas);
        AssinadorSped = new AssinadorSpedCms();
        var consultasEcd = new ConsultasEcd(ds);
        Ecd = new ServicoEcd(consultasEcd, repoContabil, Responsaveis, autorizador, AssinadorSped);
        Ecf = new ServicoEcf(new ConsultasEcf(ds, consultasEcd), Ecd, autorizador, AssinadorSped);
        Backup = new ServicoBackup(new BackupPostgres(connectionString));
        Obrigacoes = new ServicoObrigacoes(new RepositorioObrigacoes(ds), autorizador);
        Migrador = new Migrador(ds);
        Pendencias = new ServicoPendencias(new RepositorioPendencias(ds), autorizador);
        PreferenciasMenu = new ServicoPreferenciasMenu(new RepositorioPreferenciasMenu(ds));
    }

    /// <summary>Fonte de dados do PostgreSQL, para módulos (Folha, Fiscal) comporem seus próprios repositórios.</summary>
    public NpgsqlDataSource DataSource => _ds;
    public Autorizador Autorizador { get; }
    public ServicoUsuarios Usuarios { get; }
    public ServicoPerfis Perfis { get; }
    public ServicoLicenca Licenca { get; }
    public ServicoLancamento Lancamentos { get; }
    public ServicoPeriodo Periodos { get; }
    public ServicoPlanoContas PlanoContas { get; }
    public ServicoHistoricos Historicos { get; }
    public ServicoConciliacao Conciliacao { get; }
    public ServicoImobilizado Imobilizado { get; }
    public ServicoParticipantes Participantes { get; }
    public ServicoConfiguracoesGerais ConfiguracoesGerais { get; }
    public ServicoReorganizacao Reorganizacao { get; }
    public ServicoCertificados Certificados { get; }
    public ServicoImportacao Importacao { get; }
    public ServicoRelatorios Relatorios { get; }
    public ServicoGerenciais Gerenciais { get; }
    public ServicoDiferencasSaldos DiferencasSaldos { get; }
    public ServicoEmpresa Empresas { get; }
    public ServicoEncerramento Encerramento { get; }
    public ServicoPlanoReferencial PlanoReferencial { get; }
    public ServicoResponsaveis Responsaveis { get; }
    public ServicoDemonstracoes Demonstracoes { get; }
    public ServicoAssinaturas Assinaturas { get; }
    public ServicoLivros Livros { get; }
    /// <summary>Geração e assinatura do arquivo da ECD (SPED Contábil).</summary>
    public ServicoEcd Ecd { get; }
    /// <summary>Geração e assinatura do arquivo da ECF (Escrituração Contábil Fiscal).</summary>
    public ServicoEcf Ecf { get; }
    public IAssinadorSped AssinadorSped { get; }
    public ServicoBackup Backup { get; }
    public ServicoObrigacoes Obrigacoes { get; }
    public IRepositorioEmpresa RepositorioEmpresas { get; }
    public IConsultasContabeis Consultas { get; }
    public Migrador Migrador { get; }
    /// <summary>Tipo de contabilização (on-line ou em lote) e pendências de contabilização dos módulos.</summary>
    public ServicoPendencias Pendencias { get; }
    /// <summary>Itens de menu que cada usuário (ou todos) escondeu.</summary>
    public ServicoPreferenciasMenu PreferenciasMenu { get; }

    /// <summary>Conecta e aplica migrações pendentes.</summary>
    /// <param name="consultaCnpj">Fonte de dados de CNPJ; padrão: base pública da RFB via internet.</param>
    public static async Task<VorticisApp> IniciarAsync(string connectionString, IConsultaCnpj? consultaCnpj = null, CancellationToken ct = default,
                                                      IConsultaInscricaoEstadual? consultaIe = null)
    {
        var ds = NpgsqlDataSource.Create(connectionString);
        var app = new VorticisApp(ds, connectionString, consultaCnpj ?? new ConsultaCnpjPublica(ConsultaCnpjPublica.CriarHttpClient()),
                                 consultaIe ?? new ConsultaInscricaoEstadualPublica(ConsultaCnpjPublica.CriarHttpClient()));
        try
        {
            await app.Migrador.AplicarAsync(ct);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _ds.DisposeAsync();
}
