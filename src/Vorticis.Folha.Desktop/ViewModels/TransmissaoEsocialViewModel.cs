using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Sistema;
using Vorticis.Desktop.Comum;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Esocial;
using Vorticis.Folha.Servicos;

namespace Vorticis.Folha.Desktop.ViewModels;

public sealed record OpcaoAmbiente(AmbienteEsocial Valor, string Texto);
public sealed record OpcaoTransmissor(PapelTransmissor Valor, PapelAssinatura Papel, string Texto);

public sealed record EventoTransmissaoLinha(EventoEsocial E)
{
    public string Tipo => E.Tipo;
    public string Referencia => E.Referencia;
    public string Id => E.IdEvento;
    public string Status => E.Status switch
    {
        "GERADO" => "Gerado", "ASSINADO" => "Assinado", "ENVIADO" => "Enviado (aguardando retorno)", "PROCESSADO" => "Processado",
        "ERRO" => E.CodigoResposta is null ? "Falha no envio" : "Rejeitado", "EXPORTADO" => "Exportado em arquivo", _ => E.Status,
    };
    public string Ambiente => E.Ambiente switch { 1 => "Produção", 2 => "Prod. restrita", _ => "" };
    public string Recibo => E.Recibo ?? "";
    public string Resumo => ServicoTransmissaoEsocial.LerOcorrencias(E.OcorrenciasJson) is { Count: > 0 } o ? o[0].Descricao : "";
}

public sealed record LoteTransmissaoLinha(LoteEsocial L)
{
    public string Enviado => L.EnviadoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string Grupo => MontadorLoteEsocial.NomeGrupo(L.Grupo);
    public string Ambiente => L.Ambiente == 1 ? "Produção" : "Prod. restrita";
    public string Transmissor => L.TransmissorPapel switch { "EMPREGADOR" => "Empregador", "PROCURADOR" => "Procurador", _ => "Contador" } + $" ({L.TransmissorNrInsc})";
    public string Protocolo => L.Protocolo ?? "";
    public string Status => L.Status switch { "ENVIADO" => "Aguardando retorno", "PROCESSADO" => "Processado", _ => "Recusado" };
    public string Resposta => L.CodigoResposta is { } c ? $"{c} – {L.DescricaoResposta}" : "";
    public int Eventos => L.QtdEventos;
}

/// <summary>
/// Folha › Obrigações › eSocial › Transmissão: assina os eventos gerados com o certificado do empregador, do procurador ou do contador
/// e os envia em lotes ao eSocial (produção restrita por padrão), consulta o retorno e mostra recibos e ocorrências.
/// </summary>
public partial class TransmissaoEsocialViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<OpcaoAmbiente> Ambientes { get; } =
        [new(AmbienteEsocial.ProducaoRestrita, "Produção restrita (testes)"), new(AmbienteEsocial.Producao, "PRODUÇÃO (valendo)")];
    public static IReadOnlyList<OpcaoTransmissor> Transmissores { get; } =
    [
        new(PapelTransmissor.Empregador, PapelAssinatura.Responsavel, "Empregador (e-CNPJ da empresa)"),
        new(PapelTransmissor.Procurador, PapelAssinatura.Procurador, "Contador procurador (procuração eletrônica)"),
        new(PapelTransmissor.Contador, PapelAssinatura.Contador, "Contador / escritório (procuração eletrônica)"),
    ];

    public const string Checklist =
        "Antes de transmitir:\n" +
        "1. Cadastros completos: empresa (CNPJ, CNAE, município), parâmetros da Folha (classificação tributária, FPAS, RAT/FAP), funcionários (CPF, CBO, sindicato, endereço) e rubricas.\n" +
        "2. Certificado ICP-Brasil A1/A3 válido em Configurações › Certificados digitais, definido como padrão do papel (responsável, procurador ou contador).\n" +
        "3. Transmitindo por procurador/contador: procuração eletrônica no e-CAC / módulo de procurações do eSocial, com o perfil dos eventos, e o contador procurador com vigência e abrangência eSocial cadastradas em Cadastros › Contadores (Procuração eletrônica).\n" +
        "4. Teste primeiro na PRODUÇÃO RESTRITA. Ordem: S-1000 → tabelas (S-1005, S-1010, S-1020) → não periódicos (S-2200, S-2206, S-2230, S-2299) → periódicos (S-1200, S-1210) → S-1299.\n" +
        "5. Envie, aguarde e use \"Consultar retorno\" até o lote ser processado; só então envie o grupo seguinte.\n" +
        "Guia completo: docs/07-esocial.md";

    public ObservableCollection<EventoTransmissaoLinha> Eventos { get; } = [];
    public ObservableCollection<LoteTransmissaoLinha> Lotes { get; } = [];
    public ObservableCollection<string> Ocorrencias { get; } = [];

    [ObservableProperty] private DateTime? _competencia = FolhaExtensoes.Mes(contexto.PeriodoTrabalho);
    [ObservableProperty] private OpcaoAmbiente _ambiente = Ambientes[0];
    [ObservableProperty] private OpcaoTransmissor _transmissor = Transmissores[0];
    [ObservableProperty] private EventoTransmissaoLinha? _selecionado;
    [ObservableProperty] private string _xml = "";
    [ObservableProperty] private string _situacaoTransmissor = "";
    [ObservableProperty] private bool _mostrarChecklist = true;

    public bool PodeUsar => Contexto.Pode(Permissao.FolhaESocial);
    public bool EhProducao => Ambiente.Valor == AmbienteEsocial.Producao;
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeUsar));

    private DateOnly Comp => FolhaExtensoes.InicioMes(Competencia);

    partial void OnCompetenciaChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnAmbienteChanged(OpcaoAmbiente value) => OnPropertyChanged(nameof(EhProducao));
    partial void OnTransmissorChanged(OpcaoTransmissor value) => _ = AtualizarSituacaoAsync();
    partial void OnSelecionadoChanged(EventoTransmissaoLinha? value)
    {
        Xml = value is null ? "" : value.E.XmlAssinado ?? value.E.Xml;
        Ocorrencias.Clear();
        if (value is null) return;
        foreach (var o in ServicoTransmissaoEsocial.LerOcorrencias(value.E.OcorrenciasJson))
            Ocorrencias.Add($"{o.TipoTexto} {o.Codigo}: {o.Descricao}" + (o.Localizacao is { Length: > 0 } l ? $" [{l}]" : ""));
        if (value.E.Recibo is { } r) Ocorrencias.Insert(0, $"Recibo: {r}");
    }

    private bool _padroesAplicados;

    public void Limpar() { _padroesAplicados = false; Eventos.Clear(); Lotes.Clear(); Ocorrencias.Clear(); Xml = ""; }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var folha = app.Folha();
        if (!_padroesAplicados)
        {
            // Padrões de Cadastros › Parâmetros (ambiente e transmissor); depois disso a escolha do usuário na tela prevalece.
            var cfg = await folha.Cadastros.ObterConfiguracaoAsync(empresa, sessao.Id);
            if (!EhAtual(g)) return;
            _padroesAplicados = true;
            Ambiente = Ambientes.FirstOrDefault(a => (int)a.Valor == cfg.AmbienteEsocial) ?? Ambientes[0];
            Transmissor = Transmissores.FirstOrDefault(t => t.Valor.ToString().Equals(cfg.TransmissorEsocial, StringComparison.OrdinalIgnoreCase)) ?? Transmissores[0];
        }
        var eventos = await folha.ESocial.ListarAsync(empresa, Comp, sessao.Id);
        var lotes = await folha.Transmissao.ListarLotesAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        var id = Selecionado?.E.Id;
        Eventos.Clear(); foreach (var e in eventos) Eventos.Add(new EventoTransmissaoLinha(e));
        Lotes.Clear(); foreach (var l in lotes) Lotes.Add(new LoteTransmissaoLinha(l));
        Selecionado = Eventos.FirstOrDefault(e => e.E.Id == id);
        await AtualizarSituacaoAsync();
    }

    /// <summary>Mostra, para o transmissor escolhido, se há procuração vigente cadastrada.</summary>
    private async Task AtualizarSituacaoAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa) { SituacaoTransmissor = ""; return; }
        var papel = Transmissor.Valor;
        if (papel == PapelTransmissor.Procurador)
        {
            var p = await app.Responsaveis.ProcuradorVigenteAsync(empresa, DateOnly.FromDateTime(DateTime.Today), AbrangenciaProcuracao.Esocial);
            SituacaoTransmissor = p is null
                ? "Nenhum contador procurador com procuração vigente para o eSocial (Cadastros › Contadores, seção Procuração eletrônica). Cadastre também a procuração eletrônica no e-CAC / eSocial."
                : $"Procurador vigente: {p.Nome} (CPF {p.Cpf}) desde {p.VigenciaInicio:dd/MM/yyyy}{(p.VigenciaFim is { } f ? $" até {f:dd/MM/yyyy}" : "")}. A procuração no e-CAC/eSocial precisa cobrir o perfil dos eventos.";
        }
        else if (papel == PapelTransmissor.Contador)
            SituacaoTransmissor = "O certificado padrão do contador/escritório deve ser de quem tem vínculo vigente com a empresa e procuração eletrônica no e-CAC / eSocial.";
        else
            SituacaoTransmissor = "Usa o certificado padrão da empresa/responsável (e-CNPJ com a mesma raiz do CNPJ do empregador).";
    }

    private static bool Pendente(EventoEsocial e) => e.Status is "GERADO" or "ASSINADO" or "EXPORTADO" || (e.Status == "ERRO" && e.CodigoResposta is null);

    [RelayCommand] private Task AtualizarAsync() => CarregarAsync();

    [RelayCommand] private Task EnviarPendentesAsync() => EnviarAsync(Eventos.Where(e => Pendente(e.E)).Select(e => e.E.Id).ToArray());

    [RelayCommand]
    private Task EnviarSelecionadoAsync()
        => Selecionado is { } s ? EnviarAsync([s.E.Id]) : ExecutarAsync((_, _, _) => { Erros.Add("Selecione um evento."); return Task.CompletedTask; });

    private async Task EnviarAsync(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) { Erros.Clear(); Erros.Add("Não há eventos pendentes de envio. Gere os XMLs em Obrigações › eSocial."); return; }
        var producao = Ambiente.Valor == AmbienteEsocial.Producao;
        if (producao && !await Contexto.ConfirmarAsync("Transmitir em PRODUÇÃO",
                "Os eventos serão enviados ao eSocial de PRODUÇÃO e passam a valer perante a Receita Federal, o FGTS e os trabalhadores. " +
                "Já testou na produção restrita?\n\nConfirma a transmissão em produção?", "Transmitir em produção", "Cancelar")) return;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var cert = await CertificadosPadrao.AbrirParaEsocialAsync(Contexto, Transmissor.Papel);
            if (!cert.Sucesso) { MostrarResultado(cert, ""); return; }
            using var certificado = cert.Valor;
            var r = await app.Folha().Transmissao.AssinarEEnviarAsync(empresa, Comp, ids, Ambiente.Valor, Transmissor.Valor, certificado, usuario, producao);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            await CarregarAsync();
            foreach (var a in r.Valor.Avisos) Erros.Add(a);
            Mensagem = $"{r.Valor.Assinados} evento(s) assinado(s); {r.Valor.Enviados} enviado(s) em {r.Valor.Lotes} lote(s). Use \"Consultar retorno\" para acompanhar o processamento.";
        }, Permissao.FolhaESocial);
    }

    [RelayCommand]
    private Task ConsultarRetornoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var cert = await CertificadosPadrao.AbrirParaEsocialAsync(Contexto, Transmissor.Papel);
        if (!cert.Sucesso) { MostrarResultado(cert, ""); return; }
        using var certificado = cert.Valor;
        var r = await app.Folha().Transmissao.ConsultarRetornoAsync(empresa, certificado, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        foreach (var a in r.Valor.Avisos) Erros.Add(a);
        Mensagem = $"{r.Valor.Consultados} lote(s) consultado(s): {r.Valor.Aceitos} evento(s) aceito(s), {r.Valor.Rejeitados} rejeitado(s), {r.Valor.Aguardando} lote(s) ainda em processamento.";
    }, Permissao.FolhaESocial);
}
