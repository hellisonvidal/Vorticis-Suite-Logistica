using Vorticis.Domain.Comum;
using Vorticis.Infra;

namespace Vorticis.Desktop.Comum.Modulos;

/// <summary>O que a importação de planilha entrega a cada linha: o app, a empresa ativa, o usuário e um lugar para guardar o que for lido uma vez só.</summary>
public sealed class ContextoImportacao(VorticisApp app, long empresa, long usuario)
{
    public VorticisApp App { get; } = app;
    public long Empresa { get; } = empresa;
    public long Usuario { get; } = usuario;
    /// <summary>Dados lidos uma vez por importação (ex.: o maior código já usado, a lista de frotas).</summary>
    public Dictionary<string, object> Estado { get; } = [];

    public async Task<T> UmaVezAsync<T>(string chave, Func<Task<T>> carregar) where T : notnull
    {
        if (Estado.TryGetValue(chave, out var v)) return (T)v;
        var novo = await carregar();
        Estado[chave] = novo;
        return novo;
    }

    /// <summary>Próximo código numérico do cadastro (o maior existente + 1, e assim por diante a cada linha).</summary>
    public async Task<int> ProximoCodigoAsync(string cadastro, Func<Task<int>> maiorExistente)
    {
        var atual = await UmaVezAsync("codigo:" + cadastro, async () => (object)await maiorExistente());
        var proximo = (int)atual + 1;
        Estado["codigo:" + cadastro] = proximo;
        return proximo;
    }
}

/// <summary>
/// Cadastro que aceita importação de planilha (Excel .xlsx ou CSV): os campos (com os nomes de coluna que o sistema reconhece sozinho)
/// e como gravar uma linha. A gravação usa o serviço do cadastro, com as mesmas regras e permissões da tela.
/// </summary>
/// <param name="TelaCadastro">Tela do cadastro no menu: a importação aparece no mesmo submenu de Cadastros que ela.</param>
public sealed record DefinicaoImportacao(string Chave, string Titulo, IReadOnlyList<CampoImportacao> Campos,
                                         Func<ContextoImportacao, LinhaImportacao, Task<Resultado>> Gravar, string? TelaCadastro = null)
{
    /// <summary>Chave da tela de importação deste cadastro (menu Cadastros › Importar de planilha).</summary>
    public string Tela => "importar." + Chave;
}
