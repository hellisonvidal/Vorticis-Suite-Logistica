using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Esocial;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Servicos;

/// <summary>Tipos de evento gerados nesta etapa.</summary>
public static class EventosEsocial
{
    public static readonly IReadOnlyList<(string Tipo, string Descricao, bool Periodico)> Todos =
    [
        ("S-1000", "Informações do empregador", false), ("S-1005", "Tabela de estabelecimentos", false), ("S-1010", "Tabela de rubricas", false),
        ("S-1020", "Tabela de lotações tributárias", false), ("S-2200", "Admissão do trabalhador", false), ("S-2206", "Alteração contratual", false),
        ("S-2230", "Afastamento temporário", false), ("S-2299", "Desligamento", false), ("S-1200", "Remuneração do trabalhador", true),
        ("S-1210", "Pagamentos de rendimentos do trabalho", true), ("S-1299", "Fechamento dos eventos periódicos", true),
    ];
}

public sealed record ResumoGeracaoEsocial(int Gerados, int Ignorados, IReadOnlyList<string> Avisos);

/// <summary>
/// Geração e exportação dos XMLs do eSocial por competência. Os eventos ficam gravados com o status GERADO e passam a EXPORTADO
/// ao serem gravados em arquivo; eventos já exportados, enviados ou processados não são regerados (gerados, assinados e rejeitados são
/// substituídos). Assinatura e transmissão: <see cref="ServicoTransmissaoEsocial"/>.
/// </summary>
public sealed class ServicoESocial(IRepositorioCadastrosFolha cadastros, IRepositorioMovimentoFolha movimento, IRepositorioESocial repositorio, Autorizador autorizador)
{
    public int Ambiente { get; init; } = 2;   // 2 = produção restrita (homologação), 1 = produção

    public async Task<IReadOnlyList<EventoEsocial>> ListarAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
        => await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaESocial, ct) is null
            ? await repositorio.ListarAsync(empresaId, new DateOnly(competencia.Year, competencia.Month, 1), ct)
            : [];

    public async Task<Resultado<ResumoGeracaoEsocial>> GerarAsync(long empresaId, DateOnly competencia, IReadOnlyCollection<string> tipos, long usuarioId,
                                                                  CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResumoGeracaoEsocial>(usuarioId, empresaId, Permissao.FolhaESocial, ct) is { } negado) return negado;
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var fimMes = competencia.AddMonths(1).AddDays(-1);
        if (tipos.Count == 0) return Resultado<ResumoGeracaoEsocial>.Falha("Escolha ao menos um evento.");
        var empresa = await cadastros.ObterEmpresaAsync(empresaId, ct);
        if (empresa is null) return Resultado<ResumoGeracaoEsocial>.Falha("Empresa não encontrada.");
        var p = await cadastros.ObterParametrosAsync(empresaId, ct);
        var agora = DateTime.Now;
        var prefixo = GeradoresEsocial.PrefixoId(empresa.Cnpj, agora);
        var seq = await repositorio.SequencialDoDiaAsync(empresaId, prefixo, ct);
        var existentes = await repositorio.ListarAsync(empresaId, null, ct);
        int gerados = 0, ignorados = 0;
        var avisos = new List<string>();

        async Task Gravar(string tipo, DateOnly? comp, string referencia, Func<string, string> xml)
        {
            if (existentes.Any(x => x.Tipo == tipo && x.Referencia == referencia && x.Competencia == comp && x.Status is "EXPORTADO" or "ENVIADO" or "PROCESSADO"))
            { ignorados++; return; }
            await repositorio.ExcluirNaoExportadosAsync(empresaId, tipo, comp, referencia, ct);
            var id = GeradoresEsocial.GerarId(empresa.Cnpj, agora, ++seq);
            var conteudo = xml(id);
            // Confere contra o XSD oficial já na geração; o evento é gravado mesmo assim, para o usuário corrigir o cadastro e regerar.
            if (ValidadorXsdEsocial.ValidarEvento(conteudo, exigirAssinatura: false) is { Count: > 0 } erros)
                avisos.Add($"{tipo} {referencia}: {ResumirErroXsd(erros[0])}");
            await repositorio.GravarAsync(empresaId, tipo, comp, referencia, id, conteudo, usuarioId, ct);
            gerados++;
        }

        var funcionarios = (await cadastros.ListarFuncionariosAsync(empresaId, ct)).ToDictionary(f => f.Id);
        var rubricas = await cadastros.ListarRubricasAsync(empresaId, ct);
        var inicioTabelas = competencia;

        if (tipos.Contains("S-1000")) await Gravar("S-1000", null, empresa.Cnpj, id => GeradoresEsocial.S1000(id, empresa, p, inicioTabelas, Ambiente));
        if (tipos.Contains("S-1005")) await Gravar("S-1005", null, empresa.Cnpj, id => GeradoresEsocial.S1005(id, empresa, p, inicioTabelas, Ambiente));
        if (tipos.Contains("S-1010"))
        {
            if (rubricas.Count == 0) avisos.Add("S-1010: não há rubricas cadastradas (abra Folha › Rubricas para criar as padrão).");
            foreach (var r in rubricas.Where(r => r.Ativa)) await Gravar("S-1010", null, r.Codigo, id => GeradoresEsocial.S1010(id, empresa, r, inicioTabelas, Ambiente));
        }
        if (tipos.Contains("S-1020"))
        {
            var codigos = (await cadastros.ListarDepartamentosAsync(empresaId, ct)).Where(d => d.Ativo).Select(d => d.Codigo).Append("GERAL").Distinct();
            foreach (var cod in codigos) await Gravar("S-1020", null, cod, id => GeradoresEsocial.S1020(id, empresa, cod, p, inicioTabelas, Ambiente));
        }
        if (tipos.Contains("S-2200"))
            foreach (var f in funcionarios.Values.Where(f => f.Admissao <= fimMes).OrderBy(f => f.Matricula))
                await Gravar("S-2200", null, f.Matricula, id => GeradoresEsocial.S2200(id, empresa, f, Ambiente));
        if (tipos.Contains("S-2206"))
            foreach (var a in (await cadastros.ListarAlteracoesAsync(empresaId, ct)).Where(a => a.Data >= competencia && a.Data <= fimMes))
                if (funcionarios.TryGetValue(a.FuncionarioId, out var f))
                    await Gravar("S-2206", competencia, $"{f.Matricula}/{a.Id}", id => GeradoresEsocial.S2206(id, empresa, f, a, Ambiente));
        if (tipos.Contains("S-2230"))
            foreach (var a in await cadastros.ListarAfastamentosAsync(empresaId, ct))
            {
                if (!funcionarios.TryGetValue(a.FuncionarioId, out var f)) continue;
                if (a.Inicio >= competencia && a.Inicio <= fimMes) await Gravar("S-2230", competencia, $"{f.Matricula}/{a.Id}/ini", id => GeradoresEsocial.S2230(id, empresa, f, a, false, Ambiente));
                if (a.Fim is { } fim && fim >= competencia && fim <= fimMes) await Gravar("S-2230", competencia, $"{f.Matricula}/{a.Id}/fim", id => GeradoresEsocial.S2230(id, empresa, f, a, true, Ambiente));
            }
        if (tipos.Contains("S-2299"))
            foreach (var r in (await movimento.ListarRescisoesAsync(empresaId, ct)).Where(r => r.Status == "CALCULADA" && r.Desligamento >= competencia && r.Desligamento <= fimMes))
                if (funcionarios.TryGetValue(r.FuncionarioId, out var f))
                {
                    DateOnly? proj = r.Aviso == TipoAviso.Indenizado ? r.Desligamento.AddDays(r.DiasAviso) : null;
                    await Gravar("S-2299", competencia, f.Matricula, id => GeradoresEsocial.S2299(id, empresa, f, r, proj, Ambiente));
                }

        var calculos = await movimento.ListarCalculosAsync(empresaId, competencia, null, true, ct);
        var mensais = calculos.Where(c => c.Tipo is TipoFolha.Mensal or TipoFolha.Ferias or TipoFolha.Rescisao).GroupBy(c => c.FuncionarioId).ToList();
        var decimos = calculos.Where(c => c.Tipo == TipoFolha.Decimo2).GroupBy(c => c.FuncionarioId).ToList();
        var departamentos = (await cadastros.ListarDepartamentosAsync(empresaId, ct)).ToDictionary(d => d.Id, d => d.Codigo);
        string Lotacao(Funcionario f) => f.DepartamentoId is { } d && departamentos.TryGetValue(d, out var c) ? c : "GERAL";

        if (tipos.Contains("S-1200") || tipos.Contains("S-1210"))
        {
            if (mensais.Count == 0 && decimos.Count == 0) avisos.Add($"S-1200/S-1210: não há folha calculada em {competencia:MM/yyyy}.");
            foreach (var (grupos, ind) in new[] { (mensais, 1), (decimos, 2) })
                foreach (var g in grupos)
                {
                    var f = funcionarios[g.Key];
                    var lista = g.OrderBy(c => c.Tipo).ThenBy(c => c.OrigemId).ToList();
                    if (tipos.Contains("S-1200"))
                        await Gravar("S-1200", competencia, ind == 1 ? f.Matricula : f.Matricula + "/13", id => GeradoresEsocial.S1200(id, empresa, f, competencia, ind, lista, Lotacao(f), Ambiente));
                }
            // S-1210: um evento por trabalhador no mês, com todos os pagamentos (folha, férias, rescisão e 13º).
            if (tipos.Contains("S-1210"))
                foreach (var g in calculos.GroupBy(c => c.FuncionarioId))
                {
                    var f = funcionarios[g.Key];
                    var lista = g.OrderBy(c => c.Tipo).ThenBy(c => c.OrigemId).ToList();
                    var dataPg = lista.Select(c => c.DataPagamento).FirstOrDefault(d => d is not null) ?? competencia.AddMonths(1).AddDays(4);
                    await Gravar("S-1210", competencia, f.Matricula, id => GeradoresEsocial.S1210(id, empresa, f, competencia, lista, dataPg, Ambiente));
                }
        }
        if (tipos.Contains("S-1299"))
        {
            if (string.IsNullOrWhiteSpace(p.ResponsavelNome) || string.IsNullOrWhiteSpace(p.ResponsavelCpf))
                avisos.Add("S-1299: informe o responsável pelas informações (nome e CPF) em Folha › Parâmetros.");
            await Gravar("S-1299", competencia, empresa.Cnpj, id => GeradoresEsocial.S1299(id, empresa, p, competencia, mensais.Count + decimos.Count > 0, mensais.Count + decimos.Count > 0, Ambiente));
        }
        return Resultado<ResumoGeracaoEsocial>.Ok(new ResumoGeracaoEsocial(gerados, ignorados, avisos));
    }

    /// <summary>Traduz as mensagens mais comuns do validador para orientar o preenchimento do cadastro.</summary>
    internal static string ResumirErroXsd(string msg)
    {
        if (msg.Contains("cnpjSindCategProf")) return "informe o CNPJ do sindicato da categoria no cadastro do funcionário.";
        var i = msg.IndexOf("The element", StringComparison.Ordinal);
        return "fora do leiaute S-1.3 – " + System.Text.RegularExpressions.Regex.Replace(msg, @"in namespace '[^']*'|'?http://www\.esocial\.gov\.br/schema/evt/[^:']*:?", "").Trim();
    }

    /// <summary>Grava os eventos escolhidos como arquivos XML em <paramref name="pasta"/> e os marca como exportados.</summary>
    public async Task<Resultado<IReadOnlyList<string>>> ExportarAsync(long empresaId, DateOnly competencia, IReadOnlyCollection<long> ids, string pasta, long usuarioId,
                                                                      CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<string>>(usuarioId, empresaId, Permissao.FolhaESocial, ct) is { } negado) return negado;
        var eventos = (await repositorio.ListarAsync(empresaId, new DateOnly(competencia.Year, competencia.Month, 1), ct)).Where(e => ids.Contains(e.Id)).ToList();
        if (eventos.Count == 0) return Resultado<IReadOnlyList<string>>.Falha("Selecione ao menos um evento gerado.");
        Directory.CreateDirectory(pasta);
        var arquivos = new List<string>();
        foreach (var e in eventos)
        {
            var arquivo = Path.Combine(pasta, $"{e.Tipo}_{e.IdEvento}.xml");
            await File.WriteAllTextAsync(arquivo, e.Xml, new System.Text.UTF8Encoding(false), ct);
            arquivos.Add(arquivo);
        }
        await repositorio.MarcarExportadosAsync(empresaId, eventos.Select(e => e.Id).ToArray(), ct);
        return Resultado<IReadOnlyList<string>>.Ok(arquivos);
    }
}
