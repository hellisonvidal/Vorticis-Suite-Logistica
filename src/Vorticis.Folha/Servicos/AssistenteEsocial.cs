using Vorticis.Application.Seguranca;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Esocial;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Servicos;

public enum SituacaoEtapaEsocial { Pronta, Atencao, Pendente }

/// <summary>Onde o usuário corrige a pendência de uma etapa (a tela correspondente do módulo).</summary>
public enum DestinoEtapaEsocial
{
    Nenhum, Empresas, ParametrosFolha, ParametrosModulo, Rubricas, Departamentos, Funcionarios, FolhaMensal, Certificados, Geracao, Transmissao,
}

/// <summary>Uma etapa do assistente: o que é conferido, como está e o que falta.</summary>
public sealed record EtapaEsocial(string Titulo, string Explicacao, SituacaoEtapaEsocial Situacao, IReadOnlyList<string> Pendencias,
                                  DestinoEtapaEsocial Destino, string? TextoDestino)
{
    public EtapaEsocial ComPendencias(IReadOnlyList<string> pendencias, SituacaoEtapaEsocial situacaoSeHouver = SituacaoEtapaEsocial.Pendente) =>
        this with { Pendencias = pendencias, Situacao = pendencias.Count == 0 ? SituacaoEtapaEsocial.Pronta : situacaoSeHouver };
}

/// <summary>
/// Assistente de configuração do eSocial: confere, etapa por etapa, o que a empresa precisa ter antes de gerar e transmitir
/// os eventos (empregador, responsável, ambiente, rubricas, lotações, trabalhadores e a folha da competência). Monta os
/// XMLs em memória e confere contra o XSD oficial (S-1.3), sem gravar nada, para apontar exatamente o cadastro a corrigir.
/// </summary>
public sealed class AssistenteEsocial(IRepositorioCadastrosFolha cadastros, IRepositorioMovimentoFolha movimento, IRepositorioESocial eventos, Autorizador autorizador)
{
    private const int MaxPorEtapa = 15;

    public async Task<IReadOnlyList<EtapaEsocial>> DiagnosticarAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaESocial, ct) is { } negado)
            return [new EtapaEsocial("Permissão", negado, SituacaoEtapaEsocial.Pendente, [negado], DestinoEtapaEsocial.Nenhum, null)];
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var fimMes = competencia.AddMonths(1).AddDays(-1);
        var empresa = await cadastros.ObterEmpresaAsync(empresaId, ct);
        if (empresa is null) return [new EtapaEsocial("Empresa", "Escolha a empresa no topo da tela.", SituacaoEtapaEsocial.Pendente, ["Empresa não encontrada."], DestinoEtapaEsocial.Empresas, "Abrir Cadastros › Empresas")];
        var p = await cadastros.ObterParametrosAsync(empresaId, ct);
        var cfg = await cadastros.ObterConfiguracaoAsync(empresaId, ct);
        var etapas = new List<EtapaEsocial>();
        var idTeste = GeradoresEsocial.GerarId(empresa.Cnpj, DateTime.Now, 1);   // só para montar os XMLs em memória

        // 1. Empregador (S-1000 e S-1005)
        var erros1 = new List<string>();
        if (string.IsNullOrWhiteSpace(p.ClassTrib)) erros1.Add("Classificação tributária não informada (Folha › Parâmetros e tabelas legais).");
        erros1.AddRange(Validar("S-1000", GeradoresEsocial.S1000(idTeste, empresa, p, competencia, cfg.AmbienteEsocial)));
        erros1.AddRange(Validar("S-1005", GeradoresEsocial.S1005(idTeste, empresa, p, competencia, cfg.AmbienteEsocial)));
        etapas.Add(new EtapaEsocial("1. Dados do empregador (S-1000 e S-1005)",
            "O primeiro evento enviado ao eSocial identifica a empresa: CNPJ, classificação tributária, CNAE, município, alíquotas de RAT, FAP e terceiros. " +
            "Confira os dados da empresa e os parâmetros da folha.", default, [], DestinoEtapaEsocial.ParametrosFolha, "Abrir Parâmetros e tabelas legais")
            .ComPendencias(Limitar(erros1)));

        // 2. Responsável pelas informações (S-1299 e contato)
        var erros2 = new List<string>();
        if (string.IsNullOrWhiteSpace(p.ResponsavelNome)) erros2.Add("Informe o nome do responsável pelas informações.");
        if (string.IsNullOrWhiteSpace(p.ResponsavelCpf)) erros2.Add("Informe o CPF do responsável pelas informações.");
        else if (!Domain.Cadastros.Cpf.Valido(p.ResponsavelCpf)) erros2.Add("O CPF do responsável é inválido.");
        if (string.IsNullOrWhiteSpace(p.ResponsavelEmail) && string.IsNullOrWhiteSpace(p.ResponsavelTelefone))
            erros2.Add("Informe o e-mail ou o telefone do responsável (contato do eSocial).");
        etapas.Add(new EtapaEsocial("2. Responsável pelas informações",
            "Pessoa que responde pelas informações enviadas (vai no fechamento dos eventos periódicos, S-1299). Fica nos parâmetros da folha.",
            default, [], DestinoEtapaEsocial.ParametrosFolha, "Abrir Parâmetros e tabelas legais").ComPendencias(erros2));

        // 3. Ambiente e transmissor
        var producao = cfg.AmbienteEsocial == 1;
        etapas.Add(new EtapaEsocial("3. Ambiente e quem transmite",
            "Comece pela produção restrita (ambiente de testes do governo, sem efeito legal). Só passe para a produção depois que os eventos forem aceitos. " +
            "Escolha também quem assina e transmite: o próprio empregador, um procurador ou o escritório contábil.",
            producao ? SituacaoEtapaEsocial.Atencao : SituacaoEtapaEsocial.Pronta,
            [$"Ambiente padrão: {(producao ? "PRODUÇÃO (envios definitivos)" : "produção restrita (testes)")}.", $"Transmite: {Transmissor(cfg.TransmissorEsocial)}."],
            DestinoEtapaEsocial.ParametrosModulo, "Abrir Cadastros › Parâmetros"));

        // 4. Rubricas (S-1010)
        var rubricas = (await cadastros.ListarRubricasAsync(empresaId, ct)).Where(r => r.Ativa).ToList();
        var erros4 = new List<string>();
        if (rubricas.Count == 0) erros4.Add("Não há rubricas cadastradas: abra Rubricas para criar as padrão.");
        foreach (var r in rubricas)
            erros4.AddRange(Validar($"S-1010 rubrica {r.Codigo}", GeradoresEsocial.S1010(idTeste, empresa, r, competencia, cfg.AmbienteEsocial)));
        etapas.Add(new EtapaEsocial("4. Rubricas da folha (S-1010)",
            "Cada provento e desconto usado na folha precisa estar na tabela de rubricas com a natureza do eSocial e as incidências de INSS, IRRF e FGTS.",
            default, [], DestinoEtapaEsocial.Rubricas, "Abrir Rubricas").ComPendencias(Limitar(erros4)));

        // 5. Lotações (S-1020)
        var departamentos = (await cadastros.ListarDepartamentosAsync(empresaId, ct)).Where(d => d.Ativo).Select(d => d.Codigo).Append("GERAL").Distinct().ToList();
        var erros5 = departamentos.SelectMany(cod => Validar($"S-1020 lotação {cod}", GeradoresEsocial.S1020(idTeste, empresa, cod, p, competencia, cfg.AmbienteEsocial))).ToList();
        etapas.Add(new EtapaEsocial("5. Lotações tributárias (S-1020)",
            "Cada departamento vira uma lotação (onde o trabalhador está alocado, com FPAS e terceiros). Sem departamento, o funcionário fica na lotação GERAL.",
            default, [], DestinoEtapaEsocial.Departamentos, "Abrir Departamentos").ComPendencias(Limitar(erros5)));

        // 6. Trabalhadores (S-2200)
        var funcionarios = (await cadastros.ListarFuncionariosAsync(empresaId, ct)).Where(f => f.Admissao <= fimMes && (f.Desligamento is null || f.Desligamento >= competencia)).ToList();
        var erros6 = new List<string>();
        foreach (var f in funcionarios.OrderBy(f => f.Matricula))
        {
            if (!Domain.Cadastros.Cpf.Valido(f.Cpf)) erros6.Add($"Funcionário {f.Matricula} – {f.Nome}: CPF inválido.");
            erros6.AddRange(Validar($"Funcionário {f.Matricula} – {f.Nome}", GeradoresEsocial.S2200(idTeste, empresa, f, cfg.AmbienteEsocial)));
        }
        etapas.Add(new EtapaEsocial("6. Trabalhadores (S-2200)",
            "A admissão de cada funcionário vai com dados pessoais, endereço, contrato, cargo (CBO), jornada e sindicato. Corrija o cadastro de quem aparecer abaixo.",
            funcionarios.Count == 0 ? SituacaoEtapaEsocial.Atencao : default, funcionarios.Count == 0 ? [$"Nenhum funcionário ativo em {competencia:MM/yyyy}."] : [],
            DestinoEtapaEsocial.Funcionarios, "Abrir Cadastro de funcionários") is var e6 && funcionarios.Count == 0 ? e6 : e6.ComPendencias(Limitar(erros6)));

        // 7. Folha da competência (S-1200, S-1210 e S-1299)
        var calculos = await movimento.ListarCalculosAsync(empresaId, competencia, null, true, ct);
        var status = await movimento.ObterCompetenciaAsync(empresaId, competencia, ct);
        var erros7 = new List<string>();
        var situacao7 = SituacaoEtapaEsocial.Pronta;
        if (calculos.Count == 0 && funcionarios.Count > 0) { erros7.Add($"A folha de {competencia:MM/yyyy} ainda não foi calculada."); situacao7 = SituacaoEtapaEsocial.Pendente; }
        else if (!status.Fechada) { erros7.Add($"A competência {competencia:MM/yyyy} está aberta: feche-a depois de conferir, para os valores não mudarem após o envio."); situacao7 = SituacaoEtapaEsocial.Atencao; }
        else erros7.Add($"Folha calculada ({calculos.Select(c => c.FuncionarioId).Distinct().Count()} trabalhador(es)) e competência fechada.");
        etapas.Add(new EtapaEsocial("7. Folha da competência (S-1200, S-1210 e S-1299)",
            "Os eventos periódicos (remuneração, pagamentos e fechamento) saem da folha calculada no mês. Calcule, confira e feche a competência.",
            situacao7, erros7, DestinoEtapaEsocial.FolhaMensal, "Abrir Cálculo da folha mensal"));

        // 8. Eventos já gerados/transmitidos na competência
        var gerados = await eventos.ListarAsync(empresaId, competencia, ct);
        var resumo = gerados.GroupBy(e => e.Status).OrderBy(g => g.Key).Select(g => $"{g.Count()} evento(s) {g.Key.ToLowerInvariant()}").ToList();
        var rejeitados = gerados.Count(e => e.Status == "REJEITADO");
        etapas.Add(new EtapaEsocial("9. Gerar e transmitir",
            "Com as etapas acima prontas, gere os eventos (as tabelas S-1000 a S-1020 e as admissões só na primeira vez ou quando mudarem) e transmita, " +
            "começando pela produção restrita. Depois, consulte o retorno do governo na tela de Transmissão.",
            rejeitados > 0 ? SituacaoEtapaEsocial.Pendente : gerados.Count == 0 ? SituacaoEtapaEsocial.Atencao : SituacaoEtapaEsocial.Pronta,
            gerados.Count == 0 ? ["Nenhum evento gerado nesta competência ainda."] : [.. resumo, .. rejeitados > 0 ? new[] { "Há eventos rejeitados: veja o motivo na Transmissão, corrija o cadastro e gere de novo." } : []],
            DestinoEtapaEsocial.Geracao, "Abrir Geração dos eventos"));
        return etapas;
    }

    private static IEnumerable<string> Validar(string rotulo, string xml) =>
        ValidadorXsdEsocial.ValidarEvento(xml, exigirAssinatura: false).Take(3).Select(e => $"{rotulo}: {ServicoESocial.ResumirErroXsd(e)}");

    private static IReadOnlyList<string> Limitar(List<string> erros) =>
        erros.Count <= MaxPorEtapa ? erros : [.. erros.Take(MaxPorEtapa), $"… e mais {erros.Count - MaxPorEtapa} pendência(s)."];

    private static string Transmissor(string t) => t switch { "PROCURADOR" => "procurador", "CONTADOR" => "escritório contábil (contador)", _ => "o próprio empregador" };
}
