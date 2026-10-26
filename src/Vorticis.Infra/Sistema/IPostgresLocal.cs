namespace Vorticis.Infra.Sistema;

/// <summary>O que a configuração automática do banco precisa do PostgreSQL embutido (separado para poder ser simulado em testes).</summary>
public interface IPostgresLocal
{
    string PastaDados { get; }
    string ArquivoLog { get; }
    bool Inicializado { get; }
    bool IniciadoPorEsteProcesso { get; }
    Task InicializarAsync(string senha, CancellationToken ct = default);
    bool LimparPidOrfao(Func<int, string?>? nomeDoProcesso = null);
    Task<int> IniciarAsync(int portaPreferida = ServidorPostgresEmbutido.PortaPreferida, CancellationToken ct = default);
    void Parar();
}
