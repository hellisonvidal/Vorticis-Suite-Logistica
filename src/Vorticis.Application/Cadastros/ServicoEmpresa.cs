using System.Text.RegularExpressions;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Application.Cadastros;

/// <summary>Módulos da suíte em que a empresa está ativa (o banco e os cadastros comuns são únicos).</summary>
[Flags]
public enum ModulosEmpresa : byte
{
    Nenhum = 0, Contabil = 1, Fiscal = 2, Folha = 4, Escritorio = 8, Erp = 16, Condominio = 32, Frota = 64, Todos = 127,
}

public static class ModulosEmpresaExtensoes
{
    public static string Nome(this ModulosEmpresa m) => m switch
    {
        ModulosEmpresa.Contabil => "Contábil", ModulosEmpresa.Fiscal => "Fiscal", ModulosEmpresa.Folha => "Folha de Pagamento",
        ModulosEmpresa.Escritorio => "Escritório", ModulosEmpresa.Erp => "Gerencial", ModulosEmpresa.Condominio => "Condomínio",
        ModulosEmpresa.Frota => "Frota", _ => string.Join(", ", Individuais.Where(x => m.HasFlag(x)).Select(Nome)),
    };

    /// <summary>Cada módulo da suíte, na ordem do menu de escolha.</summary>
    public static readonly ModulosEmpresa[] Individuais =
        [ModulosEmpresa.Contabil, ModulosEmpresa.Fiscal, ModulosEmpresa.Folha, ModulosEmpresa.Escritorio, ModulosEmpresa.Erp, ModulosEmpresa.Condominio,
         ModulosEmpresa.Frota];

    /// <summary>Coluna de core.empresa que indica o módulo.</summary>
    public static string Coluna(this ModulosEmpresa m) => m switch
    {
        ModulosEmpresa.Contabil => "modulo_contabil", ModulosEmpresa.Fiscal => "modulo_fiscal", ModulosEmpresa.Folha => "modulo_folha",
        ModulosEmpresa.Escritorio => "modulo_escritorio", ModulosEmpresa.Erp => "modulo_erp", ModulosEmpresa.Condominio => "modulo_condominio",
        ModulosEmpresa.Frota => "modulo_frota", _ => throw new ArgumentException("Informe um único módulo.", nameof(m)),
    };
}

public enum RegimeTributario { MEI, SimplesNacional, LucroPresumido, LucroReal, LucroArbitrado, ImuneIsenta }

public sealed record NovaEmpresa(
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    string NaturezaJuridica,
    string CnaePrincipal,
    string Uf,
    string CodMunicipioIbge,
    DateOnly DataInicioAtividades,
    RegimeTributario Regime,
    int AnoPrimeiroExercicio,
    bool ImportarPlanoModelo = true,
    ComplementoEmpresa? Complemento = null,
    string? InscricaoEstadual = null,
    string? Nire = null,
    string? ModeloPlano = null,
    ConfiguracaoContabil? Contabil = null,
    ModulosEmpresa Modulos = ModulosEmpresa.Todos);

/// <summary>Alteração do cadastro (o CNPJ não muda; o regime tem histórico próprio).</summary>
public sealed record AlteracaoEmpresa(
    long Id,
    string RazaoSocial,
    string? NomeFantasia,
    string NaturezaJuridica,
    string CnaePrincipal,
    string Uf,
    string CodMunicipioIbge,
    DateOnly DataInicioAtividades,
    string? InscricaoEstadual,
    ComplementoEmpresa? Complemento = null,
    string? Nire = null,
    ModulosEmpresa? Modulos = null);

/// <summary>Endereço, contato e rastreabilidade da origem dos dados cadastrais.</summary>
public sealed record ComplementoEmpresa(
    string? Cep = null,
    string? Logradouro = null,
    string? Numero = null,
    string? Complemento = null,
    string? Bairro = null,
    string? Municipio = null,
    string? Email = null,
    string? Telefone = null,
    string? SituacaoCadastral = null,
    string? FonteDados = null,
    DateTimeOffset? DadosConsultadosEm = null);

public sealed record EmpresaResumo(long Id, string Cnpj, string RazaoSocial);

public sealed record EmpresaDetalhe(long Id, string Cnpj, string RazaoSocial, string? Nire, string Uf, string CodMunicipioIbge, string? Municipio,
                                    string? Cep, string? Logradouro, string? Numero, string? Complemento, string? Bairro,
                                    string? NomeFantasia = null, string NaturezaJuridica = "", string CnaePrincipal = "",
                                    DateOnly? DataInicioAtividades = null, string? InscricaoEstadual = null, string? Email = null,
                                    string? Telefone = null, RegimeTributario? Regime = null, string? SituacaoCadastral = null,
                                    string? FonteDados = null, ModulosEmpresa Modulos = ModulosEmpresa.Todos);

/// <summary>Conta do plano modelo. Nível e pai derivam do código; tipo também, salvo quando informado (sintética sem subcontas).</summary>
public sealed record ContaModelo(string Codigo, string Descricao, string CodNat, IndicadorDC NaturezaSaldo, bool? Analitica = null);

/// <summary>
/// Modelo de plano de contas embutido (ex.: comércio no Lucro Presumido, ITG 1000, ITG 2002). <paramref name="Contas"/> são as de
/// apuração e de lucros/prejuízos acumulados do modelo, usadas como padrão no encerramento do exercício.
/// </summary>
public sealed record ModeloPlano(string Id, string Nome, string Descricao, string Base, IReadOnlyList<RegimeTributario> Regimes,
                                 ContasEncerramento Contas, int QuantidadeContas)
{
    public override string ToString() => Nome;
}

/// <summary>
/// Configurações contábeis da empresa (aba Contabilidade). <see cref="ModeloPlano"/> é só informativo: muda ao aplicar um modelo.
/// A forma de apuração do IRPJ (Lucro Real) e o regime de caixa ficam no histórico de regime da empresa.
/// </summary>
public sealed partial record ConfiguracaoContabil(
    string? ModeloPlano = null,
    string? MascaraPlano = null,
    DateOnly? InicioEscrituracao = null,
    bool UsaCentroCusto = false,
    bool GrandePorte = false,
    string? ContaApuracao = null,
    string? ContaLucros = null,
    string? ContaPrejuizos = null,
    string? ApuracaoIrpj = null,
    bool RegimeCaixa = false,
    string? MascaraCentroCusto = null,
    bool ExigeDocumento = false,
    DateOnly? DataBloqueio = null,
    bool UsaEstorno = true,
    bool UsaGerencial = false)
{
    public const string Trimestral = "TRIMESTRAL";
    public const string AnualEstimativa = "ANUAL_ESTIMATIVA";

    [GeneratedRegex(@"^\d+(\.\d+)*$")]
    private static partial Regex FormatoCodigo();

    /// <summary>Conferência do que o usuário informou, sem consultar o plano (ver <c>ServicoPlanoContas.SalvarConfiguracaoAsync</c>).</summary>
    public IReadOnlyList<string> Validar(DateOnly? inicioAtividades)
    {
        var erros = new List<string>();
        if (!string.IsNullOrWhiteSpace(MascaraPlano) && !Domain.Contabil.MascaraPlano.Ler(MascaraPlano).Sucesso)
            erros.Add("Máscara do plano inválida: use, por exemplo, 9.9.99.999 (até 10 níveis e 30 caracteres).");
        if (InicioEscrituracao is { } ini && inicioAtividades is { } ativ && ini < ativ)
            erros.Add($"O início da escrituração não pode ser anterior ao início das atividades ({ativ:dd/MM/yyyy}).");
        foreach (var (rotulo, codigo) in new[] { ("apuração do resultado", ContaApuracao), ("lucros ou superávit acumulados", ContaLucros),
                                                 ("prejuízos ou déficit acumulados", ContaPrejuizos) })
            if (!string.IsNullOrWhiteSpace(codigo) && (!FormatoCodigo().IsMatch(codigo.Trim()) || codigo.Trim().Length > 30))
                erros.Add($"Código da conta de {rotulo} inválido: use números separados por ponto (ex.: 2.3.03.001).");
        if (ApuracaoIrpj is not (null or Trimestral or AnualEstimativa)) erros.Add("Forma de apuração do IRPJ inválida.");
        if (!string.IsNullOrWhiteSpace(MascaraCentroCusto) && !Domain.Contabil.MascaraPlano.Ler(MascaraCentroCusto).Sucesso)
            erros.Add("Máscara do plano de centros de custo inválida: use, por exemplo, 9.99.999.");
        if (DataBloqueio is { } b && inicioAtividades is { } ini2 && b < ini2)
            erros.Add($"A data de bloqueio dos lançamentos não pode ser anterior ao início das atividades ({ini2:dd/MM/yyyy}).");
        return erros;
    }

    /// <summary>Textos aparados e vazios como nulos; máscara no formato canônico.</summary>
    public ConfiguracaoContabil Normalizar()
    {
        static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var mascara = N(MascaraPlano) is { } m && Domain.Contabil.MascaraPlano.Ler(m) is { Sucesso: true } r ? r.Valor.Texto : N(MascaraPlano);
        var mascaraCc = N(MascaraCentroCusto) is { } mc && Domain.Contabil.MascaraPlano.Ler(mc) is { Sucesso: true } rc ? rc.Valor.Texto : N(MascaraCentroCusto);
        return this with { MascaraPlano = mascara, ContaApuracao = N(ContaApuracao), ContaLucros = N(ContaLucros), ContaPrejuizos = N(ContaPrejuizos),
                           ApuracaoIrpj = N(ApuracaoIrpj), MascaraCentroCusto = mascaraCc };
    }
}

public interface IRepositorioEmpresa
{
    Task<bool> CnpjExisteAsync(string cnpj, CancellationToken ct);
    /// <summary>Cria empresa, regime, exercício com 12 períodos e (opcional) o plano de contas, numa transação.</summary>
    Task<long> CriarAsync(NovaEmpresa empresa, IReadOnlyList<ContaModelo> plano, CancellationToken ct);
    Task CriarExercicioAsync(long empresaId, int ano, CancellationToken ct);
    Task<IReadOnlyList<EmpresaResumo>> ListarAsync(CancellationToken ct);
    Task<EmpresaDetalhe?> ObterAsync(long id, CancellationToken ct);
    /// <summary>Ativa a empresa também nos módulos indicados (não desativa nenhum).</summary>
    Task AtivarModulosAsync(long id, ModulosEmpresa modulos, CancellationToken ct);
    Task AtualizarAsync(AlteracaoEmpresa e, CancellationToken ct);
    Task AtualizarInscricaoEstadualAsync(long id, string? inscricao, CancellationToken ct);
    /// <summary>Exclui a empresa do banco com tudo o que pertence a ela. Retorna quantos lançamentos foram apagados.</summary>
    Task<int> ExcluirAsync(long id, CancellationToken ct);
}

public interface IFontePlanoModelo
{
    /// <summary>Id do modelo usado quando nenhum é escolhido (o plano básico original).</summary>
    string ModeloPadrao { get; }
    IReadOnlyList<ModeloPlano> Listar();
    /// <summary>Contas do modelo (o padrão, se o id for nulo); vazio se o modelo não existir.</summary>
    IReadOnlyList<ContaModelo> Obter(string? modeloId = null);
}

public sealed class ServicoEmpresa(IRepositorioEmpresa repositorio, IFontePlanoModelo planoModelo, IConsultaCnpj? consultaCnpj = null,
                                   IConsultaInscricaoEstadual? consultaIe = null)
{
    /// <summary>Consulta os dados cadastrais do CNPJ na fonte externa configurada (para pré-preencher o cadastro).</summary>
    public async Task<Resultado<DadosCnpj>> ConsultarCnpjAsync(string cnpj, CancellationToken ct = default)
    {
        var c = new string(cnpj.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (!Cnpj.Valido(c)) return Resultado<DadosCnpj>.Falha("CNPJ inválido. Confira os dígitos antes de consultar.");
        if (consultaCnpj is null) return Resultado<DadosCnpj>.Falha("Consulta de CNPJ não configurada.");
        return await consultaCnpj.ConsultarAsync(c, ct);
    }

    public async Task<Resultado<long>> CadastrarAsync(NovaEmpresa e, CancellationToken ct = default)
    {
        var erros = new List<string>();
        var cnpj = new string(e.Cnpj.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (!Cnpj.Valido(cnpj)) erros.Add("CNPJ inválido.");
        if (string.IsNullOrWhiteSpace(e.RazaoSocial)) erros.Add("Razão social obrigatória.");
        if (e.NaturezaJuridica.Length != 4 || !e.NaturezaJuridica.All(char.IsAsciiDigit)) erros.Add("Natureza jurídica deve ter 4 dígitos (tabela CONCLA).");
        var cnae = Cnpj.SomenteDigitos(e.CnaePrincipal);
        if (cnae.Length != 7) erros.Add("CNAE deve ter 7 dígitos.");
        if (e.Uf.Length != 2) erros.Add("UF inválida.");
        if (e.CodMunicipioIbge.Length != 7 || !e.CodMunicipioIbge.All(char.IsAsciiDigit)) erros.Add("Código de município IBGE deve ter 7 dígitos.");
        if (e.AnoPrimeiroExercicio < e.DataInicioAtividades.Year) erros.Add("O primeiro exercício não pode ser anterior ao início das atividades.");
        var cep = Cnpj.SomenteDigitos(e.Complemento?.Cep);
        if (cep.Length is not (0 or 8)) erros.Add("CEP deve ter 8 dígitos.");
        if (!Domain.Cadastros.InscricaoEstadual.Valida(e.InscricaoEstadual)) erros.Add("Inscrição estadual deve ter de 2 a 14 dígitos ou ISENTO.");
        if (!Nire.Valido(e.Nire)) erros.Add("NIRE deve ter 11 dígitos (número do registro na Junta Comercial).");
        var modelo = e.ImportarPlanoModelo ? planoModelo.Listar().FirstOrDefault(m => m.Id == (e.ModeloPlano ?? planoModelo.ModeloPadrao)) : null;
        if (e.ImportarPlanoModelo && modelo is null) erros.Add("Modelo de plano de contas não encontrado.");
        if (e.Modulos == ModulosEmpresa.Nenhum || (e.Modulos & ~ModulosEmpresa.Todos) != 0) erros.Add("Escolha ao menos um módulo para a empresa.");
        var contabil = (e.Contabil ?? new ConfiguracaoContabil()).Normalizar();
        erros.AddRange(contabil.Validar(e.DataInicioAtividades));
        if (erros.Count > 0) return Resultado<long>.Falha(erros);

        if (await repositorio.CnpjExisteAsync(cnpj, ct)) return Resultado<long>.Falha("Já existe empresa com este CNPJ.");

        var normalizada = e with
        {
            Cnpj = cnpj, CnaePrincipal = cnae, Uf = e.Uf.ToUpperInvariant(), RazaoSocial = e.RazaoSocial.Trim(),
            InscricaoEstadual = Domain.Cadastros.InscricaoEstadual.Normalizar(e.InscricaoEstadual),
            Complemento = e.Complemento is null ? null : e.Complemento with { Cep = cep.Length == 0 ? null : cep },
            Nire = Nire.Normalizar(e.Nire),
            ModeloPlano = modelo?.Id,
            // contas de encerramento: as informadas, senão as do modelo escolhido
            Contabil = contabil with
            {
                ModeloPlano = modelo?.Id,
                ContaApuracao = contabil.ContaApuracao ?? modelo?.Contas.Apuracao,
                ContaLucros = contabil.ContaLucros ?? modelo?.Contas.LucrosAcumulados,
                ContaPrejuizos = contabil.ContaPrejuizos ?? modelo?.Contas.PrejuizosAcumulados,
            },
        };
        var plano = modelo is null ? [] : planoModelo.Obter(modelo.Id);
        return Resultado<long>.Ok(await repositorio.CriarAsync(normalizada, plano, ct));
    }

    public Task<EmpresaDetalhe?> ObterAsync(long id, CancellationToken ct = default) => repositorio.ObterAsync(id, ct);

    /// <summary>Modelos de plano de contas disponíveis (cadastro da empresa e tela do plano de contas).</summary>
    public IReadOnlyList<ModeloPlano> ListarModelosPlano() => planoModelo.Listar();

    public async Task<Resultado> AlterarAsync(AlteracaoEmpresa e, CancellationToken ct = default)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(e.RazaoSocial)) erros.Add("Razão social obrigatória.");
        if (e.NaturezaJuridica.Length != 4 || !e.NaturezaJuridica.All(char.IsAsciiDigit)) erros.Add("Natureza jurídica deve ter 4 dígitos (tabela CONCLA).");
        var cnae = Cnpj.SomenteDigitos(e.CnaePrincipal);
        if (cnae.Length != 7) erros.Add("CNAE deve ter 7 dígitos.");
        if (!Uf.Valida(e.Uf)) erros.Add("UF inválida.");
        if (e.CodMunicipioIbge.Length != 7 || !e.CodMunicipioIbge.All(char.IsAsciiDigit)) erros.Add("Código de município IBGE deve ter 7 dígitos.");
        else if (Uf.DeCodigoIbge(e.CodMunicipioIbge) is { } ufIbge && !string.Equals(ufIbge, e.Uf.Trim(), StringComparison.OrdinalIgnoreCase))
            erros.Add($"O município IBGE {e.CodMunicipioIbge} é de {ufIbge}, não de {e.Uf.ToUpperInvariant()}.");
        var cep = Cnpj.SomenteDigitos(e.Complemento?.Cep);
        if (cep.Length is not (0 or 8)) erros.Add("CEP deve ter 8 dígitos.");
        if (!Domain.Cadastros.InscricaoEstadual.Valida(e.InscricaoEstadual)) erros.Add("Inscrição estadual deve ter de 2 a 14 dígitos ou ISENTO.");
        if (!Nire.Valido(e.Nire)) erros.Add("NIRE deve ter 11 dígitos (número do registro na Junta Comercial).");
        if (e.Modulos is { } mods && (mods == ModulosEmpresa.Nenhum || (mods & ~ModulosEmpresa.Todos) != 0)) erros.Add("Escolha ao menos um módulo para a empresa.");
        if (erros.Count > 0) return Resultado.Falha(erros);
        if (await repositorio.ObterAsync(e.Id, ct) is null) return Resultado.Falha("Empresa não encontrada.");

        await repositorio.AtualizarAsync(e with
        {
            RazaoSocial = e.RazaoSocial.Trim(), CnaePrincipal = cnae, Uf = e.Uf.Trim().ToUpperInvariant(),
            InscricaoEstadual = Domain.Cadastros.InscricaoEstadual.Normalizar(e.InscricaoEstadual),
            Complemento = e.Complemento is null ? null : e.Complemento with { Cep = cep.Length == 0 ? null : cep },
            Nire = Nire.Normalizar(e.Nire),
        }, ct);
        return Resultado.Ok();
    }

    /// <summary>Ativa a empresa no módulo (ou módulos) indicado, sem alterar os demais.</summary>
    public async Task<Resultado> AtivarNoModuloAsync(long id, ModulosEmpresa modulos, CancellationToken ct = default)
    {
        if (modulos == ModulosEmpresa.Nenhum || (modulos & ~ModulosEmpresa.Todos) != 0) return Resultado.Falha("Módulo inválido.");
        if (await repositorio.ObterAsync(id, ct) is null) return Resultado.Falha("Empresa não encontrada.");
        await repositorio.AtivarModulosAsync(id, modulos, ct);
        return Resultado.Ok();
    }

    /// <summary>Grava só a inscrição estadual (após o usuário confirmar o número encontrado na consulta).</summary>
    public async Task<Resultado> SalvarInscricaoEstadualAsync(long id, string? inscricao, CancellationToken ct = default)
    {
        if (!Domain.Cadastros.InscricaoEstadual.Valida(inscricao)) return Resultado.Falha("Inscrição estadual deve ter de 2 a 14 dígitos ou ISENTO.");
        await repositorio.AtualizarInscricaoEstadualAsync(id, Domain.Cadastros.InscricaoEstadual.Normalizar(inscricao), ct);
        return Resultado.Ok();
    }

    /// <summary>
    /// Exclusão física: apaga a empresa e todos os seus dados (lançamentos, plano de contas, períodos, sócios, acessos),
    /// liberando o CNPJ para um novo cadastro. Retorna o número de lançamentos apagados.
    /// </summary>
    public async Task<Resultado<int>> ExcluirAsync(long id, CancellationToken ct = default)
    {
        if (await repositorio.ObterAsync(id, ct) is null) return Resultado<int>.Falha("Empresa não encontrada.");
        try
        {
            return Resultado<int>.Ok(await repositorio.ExcluirAsync(id, ct));
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado<int>.Falha(ex.Message);
        }
    }

    /// <summary>
    /// Inscrições estaduais do CNPJ na UF da empresa (UF informada ou deduzida do código IBGE do município).
    /// A gravação é decisão do usuário (ver <see cref="SalvarInscricaoEstadualAsync"/>).
    /// </summary>
    public async Task<Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>> ConsultarInscricaoEstadualAsync(
        string cnpj, string? uf, string? codMunicipioIbge, CancellationToken ct = default)
    {
        var c = new string(cnpj.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (!Cnpj.Valido(c)) return Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Falha("CNPJ inválido. Confira os dígitos antes de consultar.");
        var estado = Uf.Valida(uf) ? uf!.Trim().ToUpperInvariant() : Uf.DeCodigoIbge(codMunicipioIbge);
        if (estado is null) return Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Falha("Informe a UF ou o código IBGE do município para consultar a inscrição estadual.");
        if (consultaIe is null) return Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Falha("Consulta de inscrição estadual não configurada.");
        var r = await consultaIe.ConsultarAsync(c, ct);
        return r.Sucesso
            ? Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Ok(r.Valor.Where(i => i.Uf == estado).ToList())
            : r;
    }
}
