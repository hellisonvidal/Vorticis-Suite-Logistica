using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Frota.Desktop;
using Vorticis.Erp.Desktop;
using Vorticis.Contabil.Desktop;
using Vorticis.Fiscal.Desktop;

namespace Vorticis.Logistica.Desktop;

public sealed class ModuloLogistica : ModuloCombinado
{
    public ModuloLogistica() : base(
        "Vorticis Suite Logística",
        "Gestão Completa: Frota, Faturamento, Compras, Gestão, Contábil e Fiscal",
        new ModuloFrota(),
        new ModuloFaturamento(),
        new ModuloCompras(),
        new ModuloGestao(),
        new ModuloContabil(),
        new ModuloFiscal()
    ) { }
}