using System.Text.RegularExpressions;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

public sealed record ContaDetalhe(
    long Id, string Codigo, string Descricao, bool Analitica, short Nivel, long? ContaPaiId, string CodNat,
    IndicadorDC NaturezaSaldo, bool ExigeCentroCusto, bool ExigeParticipante, DateOnly DataInclusao, bool Ativa,
    bool TemMovimento, bool TemFilhas, decimal Saldo, int? CodigoReduzido = null, long? GrupoParticipantesId = null);

/// <param name="GrupoParticipantesId">Conta sintética onde os participantes são alocados; obrigatório quando a conta exige participante.</param>
public sealed record NovaConta(string Codigo, string Descricao, string CodNat, IndicadorDC NaturezaSaldo,
                               bool ExigeCentroCusto, bool ExigeParticipante, DateOnly DataInclusao, int? CodigoReduzido = null,
                               long? GrupoParticipantesId = null);

public sealed record CentroCustoDetalhe(long Id, string Codigo, string Descricao, long? PaiId, bool Ativo, bool TemMovimento,
                                        bool Analitico = true, short Nivel = 1, bool TemFilhos = false, long? FrotaId = null);

/// <summary>Frota que um centro de custo pode representar (cadastro do módulo Frota).</summary>
public sealed record FrotaDoCentro(long Id, string Texto)
{
    public override string ToString() => Texto;
}

public interface IRepositorioPlanoContas
{
    Task<IReadOnlyList<ContaDetalhe>> ListarAsync(long empresaId, CancellationToken ct);

    /// <summary>Insere a conta; se <paramref name="paiParaSintetica"/> for informado, converte o pai em sintético na mesma transação.</summary>
    Task<long> IncluirAsync(long empresaId, NovaConta conta, long? contaPaiId, short nivel, bool analitica, long? paiParaSintetica,
                            long usuarioId, CancellationToken ct);
    Task AlterarAsync(long contaId, string descricao, IndicadorDC naturezaSaldo, bool exigeCentroCusto, bool exigeParticipante, bool ativa,
                      long? grupoParticipantesId, long usuarioId, CancellationToken ct);
    Task ExcluirAsync(long contaId, long usuarioId, CancellationToken ct);
    Task AlterarReduzidoAsync(long contaId, int reduzido, long usuarioId, CancellationToken ct);
    /// <summary>
    /// Apaga o plano da empresa (sem lançamentos) e grava o novo, numa transação. Com <paramref name="modelo"/>, a empresa fica registrada
    /// com ele (e com as contas de encerramento dele); sem, o plano passa a ser próprio (importado).
    /// </summary>
    Task SubstituirAsync(long empresaId, IReadOnlyList<Cadastros.ContaModelo> plano, long usuarioId, CancellationToken ct,
                         Cadastros.ModeloPlano? modelo = null);
    Task<Cadastros.ConfiguracaoContabil> ObterConfiguracaoAsync(long empresaId, CancellationToken ct);
    Task SalvarConfiguracaoAsync(long empresaId, Cadastros.ConfiguracaoContabil configuracao, CancellationToken ct);
    /// <summary>Troca os códigos das contas (mesma hierarquia), numa transação.</summary>
    Task RecodificarAsync(long empresaId, IReadOnlyDictionary<long, string> codigos, long usuarioId, CancellationToken ct);

    Task<IReadOnlyList<CentroCustoDetalhe>> ListarCentrosAsync(long empresaId, CancellationToken ct);
    /// <summary>Insere o centro; se <paramref name="paiParaSintetico"/> for informado, converte o pai em sintético na mesma transação.</summary>
    Task<long> IncluirCentroAsync(long empresaId, string codigo, string descricao, long? paiId, DateOnly dataInclusao, short nivel, bool analitico,
                                  long? paiParaSintetico, CancellationToken ct);
    Task AlterarCentroAsync(long centroId, string descricao, bool ativo, bool analitico, CancellationToken ct);
    Task<IReadOnlyList<FrotaDoCentro>> ListarFrotasAsync(long empresaId, CancellationToken ct);
    Task DefinirFrotaCentroAsync(long centroId, long? frotaId, CancellationToken ct);
    Task<string?> ObterMascaraCentrosAsync(long empresaId, CancellationToken ct);
    /// <summary>Grava a máscara e, se informado, troca os códigos dos centros (id → novo código; nível recalculado), numa transação.</summary>
    Task DefinirMascaraCentrosAsync(long empresaId, string? mascara, IReadOnlyDictionary<long, string>? codigos, long usuarioId, CancellationToken ct);
    Task ExcluirCentroAsync(long centroId, CancellationToken ct);
}

/// <summary>
/// Manutenção do plano de contas e dos centros de custo (ECD I050/I100). Regras:
/// código hierárquico (pai = código sem o último segmento); nível 1 sempre sintético; só analíticas recebem
/// lançamentos; uma analítica com movimento não pode virar sintética; conta com movimento ou filhas não é
/// excluída (inativa-se); só se inativa conta com saldo zero.
/// </summary>
public sealed partial class ServicoPlanoContas(IRepositorioPlanoContas repositorio, Autorizador autorizador, Cadastros.IFontePlanoModelo? modelos = null)
{
    [GeneratedRegex(@"^\d+(\.\d+)*$")]
    private static partial Regex FormatoCodigo();

    private static readonly string[] NaturezasValidas = ["01", "02", "03", "04", "05", "09"];

    public async Task<IReadOnlyList<ContaDetalhe>> ListarAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso ? await repositorio.ListarAsync(empresaId, ct) : [];

    public async Task<Resultado<long>> IncluirAsync(long empresaId, NovaConta c, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.PlanoContasManter, ct) is { } negado) return negado;
        var codigo = c.Codigo.Trim();
        var erros = new List<string>();
        if (!FormatoCodigo().IsMatch(codigo) || codigo.Length > 30) erros.Add("Código inválido: use números separados por ponto (ex.: 1.1.01.004), até 30 caracteres.");
        if (string.IsNullOrWhiteSpace(c.Descricao)) erros.Add("Informe a descrição.");
        if (!NaturezasValidas.Contains(c.CodNat)) erros.Add("Natureza (COD_NAT) inválida.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);

        var plano = await repositorio.ListarAsync(empresaId, ct);
        if (plano.Any(x => x.Codigo == codigo)) return Resultado<long>.Falha($"Já existe a conta {codigo}.");
        if (c.CodigoReduzido is { } red && (red <= 0 || plano.FirstOrDefault(x => x.CodigoReduzido == red) is not null))
            return Resultado<long>.Falha(red <= 0 ? "O código reduzido deve ser maior que zero." : $"O código reduzido {red} já está em uso.");
        if (ValidarGrupoParticipantes(plano, c.ExigeParticipante, c.GrupoParticipantesId, c.CodNat) is { } erroGrupo) return Resultado<long>.Falha(erroGrupo);
        if (!c.ExigeParticipante) c = c with { GrupoParticipantesId = null };

        var segmentos = codigo.Split('.');
        if (segmentos.Length == 1)
            return Resultado<long>.Ok(await repositorio.IncluirAsync(empresaId, c with { Codigo = codigo, Descricao = c.Descricao.Trim() },
                null, 1, analitica: false, null, usuarioId, ct));

        var codigoPai = string.Join('.', segmentos[..^1]);
        var pai = plano.FirstOrDefault(x => x.Codigo == codigoPai);
        if (pai is null) return Resultado<long>.Falha($"A conta superior {codigoPai} não existe. Cadastre-a primeiro.");
        if (pai.Analitica && pai.TemMovimento)
            return Resultado<long>.Falha($"A conta {codigoPai} é analítica e já tem lançamentos; não pode receber subcontas. Crie a nova conta em outro grupo.");
        if (!pai.Ativa) return Resultado<long>.Falha($"A conta superior {codigoPai} está inativa.");
        if (c.CodNat != pai.CodNat && !(pai.CodNat == "02" && c.CodNat == "03"))   // PL pode ficar sob o grupo do Passivo
            return Resultado<long>.Falha($"A natureza da conta deve ser a mesma da conta superior ({pai.CodNat}).");
        if (c.DataInclusao < pai.DataInclusao)
            return Resultado<long>.Falha($"A data de inclusão não pode ser anterior à da conta superior ({pai.DataInclusao:dd/MM/yyyy}).");

        return Resultado<long>.Ok(await repositorio.IncluirAsync(empresaId, c with { Codigo = codigo, Descricao = c.Descricao.Trim() },
            pai.Id, (short)segmentos.Length, analitica: true, pai.Analitica ? pai.Id : null, usuarioId, ct));
    }

    /// <summary>
    /// Conta que exige participante (cliente/fornecedor) precisa indicar o grupo — conta sintética, da mesma natureza — em que as
    /// contas dos participantes são alocadas. Retorna o erro, ou null se estiver correto.
    /// </summary>
    private static string? ValidarGrupoParticipantes(IReadOnlyList<ContaDetalhe> plano, bool exige, long? grupoId, string codNat)
    {
        if (!exige) return null;
        if (grupoId is null) return "Informe o grupo (conta sintética) onde as contas dos participantes serão alocadas.";
        var grupo = plano.FirstOrDefault(x => x.Id == grupoId);
        if (grupo is null) return "Grupo dos participantes não encontrado neste plano.";
        if (grupo.Analitica) return $"O grupo dos participantes deve ser uma conta sintética; {grupo.Codigo} é analítica.";
        if (!grupo.Ativa) return $"O grupo dos participantes {grupo.Codigo} está inativo.";
        if (grupo.CodNat != codNat) return $"O grupo dos participantes {grupo.Codigo} é de outra natureza (COD_NAT {grupo.CodNat}).";
        return null;
    }

    public async Task<Resultado> AlterarAsync(long empresaId, long contaId, string descricao, IndicadorDC naturezaSaldo, bool exigeCentroCusto,
                                             bool exigeParticipante, bool ativa, long usuarioId, CancellationToken ct = default,
                                             long? grupoParticipantesId = null)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.PlanoContasManter, ct) is { } negado) return Resultado.Falha(negado);
        if (string.IsNullOrWhiteSpace(descricao)) return Resultado.Falha("Informe a descrição.");
        var plano = await repositorio.ListarAsync(empresaId, ct);
        var conta = plano.FirstOrDefault(x => x.Id == contaId);
        if (conta is null) return Resultado.Falha("Conta não encontrada.");
        if (conta.Ativa && !ativa)
        {
            var comSaldo = plano.Where(x => x.Codigo == conta.Codigo || x.Codigo.StartsWith(conta.Codigo + ".", StringComparison.Ordinal))
                                .FirstOrDefault(x => x.Analitica && x.Saldo != 0);
            if (comSaldo is not null)
                return Resultado.Falha($"Não é possível inativar: a conta {comSaldo.Codigo} tem saldo {LinhaBalancete.Formatar(comSaldo.Saldo)}. Transfira o saldo antes.");
        }
        if (exigeParticipante && !conta.Analitica) return Resultado.Falha("Só conta analítica (que recebe lançamentos) exige participante.");
        if (ValidarGrupoParticipantes(plano, exigeParticipante, grupoParticipantesId, conta.CodNat) is { } erroGrupo) return Resultado.Falha(erroGrupo);
        await repositorio.AlterarAsync(contaId, descricao.Trim(), naturezaSaldo, exigeCentroCusto, exigeParticipante, ativa,
                                       exigeParticipante ? grupoParticipantesId : null, usuarioId, ct);
        return Resultado.Ok();
    }

    /// <summary>Troca o código reduzido (atalho de digitação) de uma conta analítica; deve ser único na empresa.</summary>
    public async Task<Resultado> AlterarReduzidoAsync(long empresaId, long contaId, int reduzido, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.PlanoContasManter, ct) is { } negado) return Resultado.Falha(negado);
        if (reduzido <= 0) return Resultado.Falha("O código reduzido deve ser um número maior que zero.");
        var plano = await repositorio.ListarAsync(empresaId, ct);
        var conta = plano.FirstOrDefault(x => x.Id == contaId);
        if (conta is null) return Resultado.Falha("Conta não encontrada.");
        if (!conta.Analitica) return Resultado.Falha("Só contas analíticas têm código reduzido.");
        if (conta.CodigoReduzido == reduzido) return Resultado.Ok();
        if (plano.FirstOrDefault(x => x.CodigoReduzido == reduzido) is { } outra)
            return Resultado.Falha($"O reduzido {reduzido} já é da conta {outra.Codigo} – {outra.Descricao}.");
        await repositorio.AlterarReduzidoAsync(contaId, reduzido, usuarioId, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ExcluirAsync(long empresaId, long contaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.PlanoContasManter, ct) is { } negado) return Resultado.Falha(negado);
        var conta = (await repositorio.ListarAsync(empresaId, ct)).FirstOrDefault(x => x.Id == contaId);
        if (conta is null) return Resultado.Falha("Conta não encontrada.");
        if (conta.TemFilhas) return Resultado.Falha($"A conta {conta.Codigo} tem subcontas; exclua-as antes.");
        if (conta.TemMovimento) return Resultado.Falha($"A conta {conta.Codigo} tem lançamentos e não pode ser excluída; inative-a.");
        try
        {
            await repositorio.ExcluirAsync(contaId, usuarioId, ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado.Falha($"Não foi possível excluir: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ modelos de plano e configuração contábil

    /// <summary>Modelos de plano de contas embutidos (o mesmo catálogo do cadastro da empresa).</summary>
    public IReadOnlyList<Cadastros.ModeloPlano> ListarModelos() => modelos?.Listar() ?? [];

    /// <summary>
    /// Aplica um modelo de plano de contas à empresa, substituindo o plano atual. Só é permitido enquanto nenhuma conta tem
    /// lançamentos (inclusive rascunhos e saldos iniciais). Registra o modelo e as contas padrão do encerramento na empresa.
    /// </summary>
    public async Task<Resultado<int>> AplicarModeloAsync(long empresaId, string modeloId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.PlanoContasManter, ct) is { } negado) return negado;
        var modelo = modelos?.Listar().FirstOrDefault(m => m.Id == modeloId);
        if (modelo is null) return Resultado<int>.Falha("Escolha um modelo de plano de contas.");
        var atual = await repositorio.ListarAsync(empresaId, ct);
        if (atual.FirstOrDefault(c => c.TemMovimento) is { } comMovimento)
            return Resultado<int>.Falha($"O plano atual já tem lançamentos (ex.: conta {comMovimento.Codigo}); trocar o modelo substitui o plano inteiro " +
                                        "e só é permitido antes do primeiro lançamento.");
        var contas = modelos!.Obter(modelo.Id);
        try
        {
            await repositorio.SubstituirAsync(empresaId, contas, usuarioId, ct, modelo);
            return Resultado<int>.Ok(contas.Count);
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<int>.Falha($"Não foi possível aplicar o modelo: {ex.Message}");
        }
    }

    public async Task<Cadastros.ConfiguracaoContabil> ObterConfiguracaoAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso ? await repositorio.ObterConfiguracaoAsync(empresaId, ct) : new();

    /// <summary>
    /// Grava as configurações contábeis da empresa. As contas de apuração e de lucros/prejuízos devem existir no plano
    /// (analíticas); o modelo registrado não muda aqui (ver <see cref="AplicarModeloAsync"/>). Se a máscara dos centros de custo
    /// mudou, é aplicada antes pelas regras de <see cref="DefinirMascaraCentrosAsync"/> (exige a permissão de centros de custo;
    /// <paramref name="converterCentros"/> converte os códigos existentes para a nova máscara).
    /// </summary>
    public async Task<Resultado> SalvarConfiguracaoAsync(long empresaId, Cadastros.ConfiguracaoContabil cfg, DateOnly? inicioAtividades, long usuarioId,
                                                        CancellationToken ct = default, bool converterCentros = false)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.PlanoContasManter, ct) is { } negado) return Resultado.Falha(negado);
        cfg = cfg.Normalizar();
        var erros = cfg.Validar(inicioAtividades).ToList();
        if (erros.Count == 0)
        {
            var plano = await repositorio.ListarAsync(empresaId, ct);
            if (plano.Count > 0)
                foreach (var (rotulo, codigo) in new[] { ("apuração do resultado", cfg.ContaApuracao), ("lucros ou superávit acumulados", cfg.ContaLucros),
                                                         ("prejuízos ou déficit acumulados", cfg.ContaPrejuizos) })
                    if (codigo is not null && !plano.Any(c => c.Codigo == codigo && c.Analitica))
                        erros.Add($"A conta de {rotulo} ({codigo}) não existe no plano ou não é analítica.");
        }
        if (erros.Count > 0) return Resultado.Falha(erros);
        var atual = await repositorio.ObterConfiguracaoAsync(empresaId, ct);
        if ((cfg.MascaraCentroCusto ?? "") != (atual.MascaraCentroCusto ?? ""))
        {
            var m = await DefinirMascaraCentrosAsync(empresaId, cfg.MascaraCentroCusto, usuarioId, converterCentros, ct);
            if (!m.Sucesso) return m;
        }
        await repositorio.SalvarConfiguracaoAsync(empresaId, cfg with { ModeloPlano = atual.ModeloPlano }, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ importação e máscara

    /// <summary>
    /// Substitui o plano da empresa pelo lido da planilha. Só é permitido enquanto nenhuma conta tem lançamentos
    /// (inclusive rascunhos e saldos iniciais); os vínculos com o plano referencial do plano anterior são descartados.
    /// </summary>
    public async Task<Resultado<int>> ImportarAsync(long empresaId, PlanoImportado plano, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.PlanoContasManter, ct) is { } negado) return negado;
        if (plano.Erros.Count > 0) return Resultado<int>.Falha(plano.Erros);
        if (plano.Contas.Count == 0) return Resultado<int>.Falha("A planilha não tem contas.");
        if (plano.Contas.FirstOrDefault(c => !NaturezasValidas.Contains(c.CodNat)) is { } invalida)
            return Resultado<int>.Falha($"Conta {invalida.Codigo}: natureza (COD_NAT) inválida.");
        var atual = await repositorio.ListarAsync(empresaId, ct);
        if (atual.FirstOrDefault(c => c.TemMovimento) is { } comMovimento)
            return Resultado<int>.Falha($"O plano atual já tem lançamentos (ex.: conta {comMovimento.Codigo}); a importação substitui o plano inteiro e só é " +
                                        "permitida antes do primeiro lançamento. Para ajustar só os códigos, use a conversão de máscara.");
        try
        {
            await repositorio.SubstituirAsync(empresaId, plano.Contas.Select(c =>
                new Cadastros.ContaModelo(c.Codigo, c.Descricao, c.CodNat, c.NaturezaSaldo, c.Analitica)).ToList(), usuarioId, ct);
            return Resultado<int>.Ok(plano.Contas.Count);
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<int>.Falha($"Não foi possível importar: {ex.Message}");
        }
    }

    /// <summary>Códigos atuais convertidos para a máscara (De → Para), ou os erros que impedem a conversão.</summary>
    public async Task<Resultado<IReadOnlyList<(ContaDetalhe Conta, string Novo)>>> SimularMascaraAsync(long empresaId, MascaraPlano mascara, long usuarioId,
                                                                                                       CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<(ContaDetalhe, string)>>(usuarioId, empresaId, Permissao.PlanoContasManter, ct) is { } negado) return negado;
        var plano = await repositorio.ListarAsync(empresaId, ct);
        if (plano.Count == 0) return Resultado<IReadOnlyList<(ContaDetalhe, string)>>.Falha("A empresa não tem plano de contas.");
        var erros = new List<string>();
        var res = new List<(ContaDetalhe, string)>();
        foreach (var c in plano)
        {
            var r = mascara.Converter(c.Codigo);
            if (!r.Sucesso) erros.Add($"Conta {c.Codigo}: {r.Erros[0]}.");
            else if (r.Valor.Count(ch => ch == '.') != c.Codigo.Count(ch => ch == '.'))
                erros.Add($"Conta {c.Codigo}: a conversão mudaria o nível da conta.");
            else res.Add((c, r.Valor));
        }
        foreach (var g in res.GroupBy(x => x.Item2).Where(g => g.Count() > 1).Take(10))
            erros.Add($"As contas {string.Join(", ", g.Select(x => x.Item1.Codigo))} ficariam com o mesmo código {g.Key}.");
        return erros.Count > 0 ? Resultado<IReadOnlyList<(ContaDetalhe, string)>>.Falha(erros.Take(30))
                               : Resultado<IReadOnlyList<(ContaDetalhe, string)>>.Ok(res);
    }

    /// <summary>
    /// Converte os códigos do plano atual para a máscara. A hierarquia e os lançamentos ficam como estão
    /// (os lançamentos apontam para a conta, não para o código). Retorna quantas contas mudaram de código.
    /// </summary>
    public async Task<Resultado<int>> AplicarMascaraAsync(long empresaId, MascaraPlano mascara, long usuarioId, CancellationToken ct = default)
    {
        var sim = await SimularMascaraAsync(empresaId, mascara, usuarioId, ct);
        if (!sim.Sucesso) return Resultado<int>.Falha(sim.Erros);
        var mudancas = sim.Valor.Where(x => x.Conta.Codigo != x.Novo).ToDictionary(x => x.Conta.Id, x => x.Novo);
        if (mudancas.Count == 0) return Resultado<int>.Ok(0);
        try
        {
            await repositorio.RecodificarAsync(empresaId, mudancas, usuarioId, ct);
            return Resultado<int>.Ok(mudancas.Count);
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<int>.Falha($"Não foi possível converter: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ centros de custo

    /// <summary>Máscara do plano de centros de custo da empresa (null = cadastro livre, sem máscara).</summary>
    public async Task<string?> ObterMascaraCentrosAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso ? await repositorio.ObterMascaraCentrosAsync(empresaId, ct) : null;

    private async Task<MascaraPlano?> MascaraAsync(long empresaId, CancellationToken ct)
        => await repositorio.ObterMascaraCentrosAsync(empresaId, ct) is { Length: > 0 } t && MascaraPlano.Ler(t) is { Sucesso: true } m ? m.Valor : null;

    /// <summary>
    /// Define a máscara dos códigos dos centros (ex.: "9.99.999"). Os centros existentes precisam caber na máscara; com
    /// <paramref name="converterExistentes"/> os códigos são convertidos (1.1 → 1.01), mantendo a hierarquia. Máscara vazia remove a regra.
    /// </summary>
    public async Task<Resultado> DefinirMascaraCentrosAsync(long empresaId, string? mascara, long usuarioId, bool converterExistentes = false,
                                                           CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.CentroCustoManter, ct) is { } negado) return Resultado.Falha(negado);
        if (string.IsNullOrWhiteSpace(mascara))
        {
            await repositorio.DefinirMascaraCentrosAsync(empresaId, null, null, usuarioId, ct);
            return Resultado.Ok();
        }
        var m = MascaraPlano.Ler(mascara);
        if (!m.Sucesso) return Resultado.Falha(m.Erros);
        var centros = await repositorio.ListarCentrosAsync(empresaId, ct);
        var porId = centros.ToDictionary(c => c.Id);
        var novos = new Dictionary<long, string>();
        var erros = new List<string>();
        foreach (var c in centros)
        {
            var codigo = c.Codigo;
            if (!PlanoCentrosCusto.Validar(codigo, m.Valor).Sucesso)
            {
                if (!converterExistentes) { erros.Add($"O centro {c.Codigo} não cabe na máscara {m.Valor.Texto}."); continue; }
                var conv = m.Valor.Converter(codigo);
                if (!conv.Sucesso) { erros.Add($"Centro {c.Codigo}: {conv.Erros[0]}."); continue; }
                codigo = conv.Valor;
            }
            novos[c.Id] = codigo;
        }
        if (erros.Count == 0)
        {
            foreach (var c in centros)
            {
                var cod = novos[c.Id];
                var esperado = PlanoCentrosCusto.CodigoPaiDe(cod);
                var atual = c.PaiId is long p ? novos[p] : null;
                if (esperado != atual)
                    erros.Add(esperado is null ? $"O centro {c.Codigo} tem superior {atual}, mas o código {cod} é de nível 1."
                                               : $"O superior do centro {c.Codigo} deveria ser {esperado} (pelo código {cod}).");
            }
            foreach (var g in novos.GroupBy(x => x.Value).Where(g => g.Count() > 1))
                erros.Add($"Códigos duplicados após a conversão: {g.Key}.");
        }
        if (erros.Count > 0)
        {
            var lista = erros.Take(10).ToList();
            if (erros.Count > 10) lista.Add($"... e mais {erros.Count - 10} problema(s).");
            return Resultado.Falha(lista);
        }
        var mudancas = novos.Where(x => porId[x.Key].Codigo != x.Value).ToDictionary(x => x.Key, x => x.Value);
        await repositorio.DefinirMascaraCentrosAsync(empresaId, m.Valor.Texto, mudancas, usuarioId, ct);
        return Resultado.Ok();
    }

    public async Task<IReadOnlyList<CentroCustoDetalhe>> ListarCentrosAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso ? await repositorio.ListarCentrosAsync(empresaId, ct) : [];

    /// <summary>
    /// Inclui um centro no plano. Com máscara: código validado por ela, nível 1 sempre grupo e superior (código sem o último nível)
    /// obrigatório nos demais. O superior analítico sem lançamentos vira sintético; com lançamentos, não recebe subcentros.
    /// </summary>
    public async Task<Resultado<long>> IncluirCentroAsync(long empresaId, string codigo, string descricao, string? codigoPai, long usuarioId,
                                                         DateOnly? dataInclusao = null, CancellationToken ct = default, bool sintetico = false)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.CentroCustoManter, ct) is { } negado) return negado;
        codigo = codigo.Trim();
        var mascara = await MascaraAsync(empresaId, ct);
        var pos = PlanoCentrosCusto.Validar(codigo, mascara);
        if (!pos.Sucesso) return Resultado<long>.Falha(pos.Erros);
        if (string.IsNullOrWhiteSpace(descricao)) return Resultado<long>.Falha("Informe a descrição.");
        var centros = await repositorio.ListarCentrosAsync(empresaId, ct);
        if (centros.Any(c => c.Codigo == codigo)) return Resultado<long>.Falha($"Já existe o centro de custo {codigo}.");

        var informado = string.IsNullOrWhiteSpace(codigoPai) ? null : codigoPai.Trim();
        if (mascara is not null && informado is not null && informado != pos.Valor.CodigoPai)
            return Resultado<long>.Falha($"O superior do centro {codigo} deve ser {pos.Valor.CodigoPai ?? "(nenhum: nível 1)"}, conforme a máscara.");
        var codPai = mascara is not null ? pos.Valor.CodigoPai
            : informado ?? (pos.Valor.CodigoPai is { } d && centros.Any(c => c.Codigo == d) ? d : null);

        CentroCustoDetalhe? pai = null;
        if (codPai is not null)
        {
            pai = centros.FirstOrDefault(c => c.Codigo == codPai);
            if (pai is null) return Resultado<long>.Falha($"Centro de custo superior {codPai} não encontrado. Cadastre-o primeiro.");
            if (!pai.Ativo) return Resultado<long>.Falha($"O centro de custo superior {codPai} está inativo.");
            if (pai.Analitico && pai.TemMovimento)
                return Resultado<long>.Falha($"O centro {codPai} é analítico e já tem lançamentos; não pode receber subcentros. Crie o novo centro em outro grupo.");
        }

        var nivel = mascara is not null ? pos.Valor.Nivel : (short)(pai is null ? 1 : pai.Nivel + 1);
        var tipo = PlanoCentrosCusto.DefinirAnalitico(nivel, sintetico, mascara);
        if (!tipo.Sucesso) return Resultado<long>.Falha(tipo.Erros);
        return Resultado<long>.Ok(await repositorio.IncluirCentroAsync(empresaId, codigo, descricao.Trim(), pai?.Id,
            dataInclusao ?? DateOnly.FromDateTime(DateTime.Today), nivel, tipo.Valor, pai is { Analitico: true } ? pai.Id : null, ct));
    }

    /// <summary>Altera descrição/situação; <paramref name="analitico"/> troca o tipo (analítico→sintético exige não ter lançamentos; sintético→analítico, não ter subcentros).</summary>
    public async Task<Resultado> AlterarCentroAsync(long empresaId, long centroId, string descricao, bool ativo, long usuarioId,
                                                   CancellationToken ct = default, bool? analitico = null)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.CentroCustoManter, ct) is { } negado) return Resultado.Falha(negado);
        if (string.IsNullOrWhiteSpace(descricao)) return Resultado.Falha("Informe a descrição.");
        var centro = (await repositorio.ListarCentrosAsync(empresaId, ct)).FirstOrDefault(c => c.Id == centroId);
        if (centro is null) return Resultado.Falha("Centro de custo não encontrado.");
        var tipo = analitico ?? centro.Analitico;
        if (tipo != centro.Analitico)
        {
            if (!tipo && centro.TemMovimento) return Resultado.Falha($"O centro {centro.Codigo} tem lançamentos; não pode virar sintético.");
            if (tipo && centro.TemFilhos) return Resultado.Falha($"O centro {centro.Codigo} tem subcentros; não pode ser analítico.");
            var mascara = await MascaraAsync(empresaId, ct);
            if (mascara is not null && !tipo && centro.Nivel == mascara.Segmentos.Count)
                return Resultado.Falha($"O último nível da máscara ({mascara.Texto}) só admite centros analíticos.");
            if (mascara is { Segmentos.Count: > 1 } && tipo && centro.Nivel == 1)
                return Resultado.Falha("Os centros de nível 1 são sempre grupos (sintéticos).");
        }
        await repositorio.AlterarCentroAsync(centroId, descricao.Trim(), ativo, tipo, ct);
        return Resultado.Ok();
    }

    /// <summary>Frotas ativas da empresa (módulo Frota) para o campo opcional "Frota" do centro de custo.</summary>
    public async Task<IReadOnlyList<FrotaDoCentro>> ListarFrotasAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso ? await repositorio.ListarFrotasAsync(empresaId, ct) : [];

    /// <summary>Liga (ou desliga, com null) o centro de custo a uma frota: o centro passa a representar a frota nos relatórios de custo por frota.</summary>
    public async Task<Resultado> DefinirFrotaDoCentroAsync(long empresaId, long centroId, long? frotaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.CentroCustoManter, ct) is { } negado) return Resultado.Falha(negado);
        var centro = (await repositorio.ListarCentrosAsync(empresaId, ct)).FirstOrDefault(c => c.Id == centroId);
        if (centro is null) return Resultado.Falha("Centro de custo não encontrado.");
        if (frotaId is { } f && (await repositorio.ListarFrotasAsync(empresaId, ct)).All(x => x.Id != f)) return Resultado.Falha("Frota não encontrada.");
        await repositorio.DefinirFrotaCentroAsync(centroId, frotaId, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ExcluirCentroAsync(long empresaId, long centroId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.CentroCustoManter, ct) is { } negado) return Resultado.Falha(negado);
        var centros = await repositorio.ListarCentrosAsync(empresaId, ct);
        var centro = centros.FirstOrDefault(c => c.Id == centroId);
        if (centro is null) return Resultado.Falha("Centro de custo não encontrado.");
        if (centro.TemMovimento) return Resultado.Falha($"O centro de custo {centro.Codigo} tem lançamentos; inative-o.");
        if (centros.Any(c => c.PaiId == centroId)) return Resultado.Falha($"O centro de custo {centro.Codigo} tem subcentros; exclua-os antes.");
        try
        {
            await repositorio.ExcluirCentroAsync(centroId, ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado.Falha($"Não foi possível excluir: {ex.Message}");
        }
    }
}
