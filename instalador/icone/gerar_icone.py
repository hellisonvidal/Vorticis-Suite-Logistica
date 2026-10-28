"""Gera os ícones da suíte Vorticis a partir da logo (logo-vorticis.jpg):

  Um ícone por módulo (VorticisContabil, VorticisFiscal, VorticisFolha, VorticisEscritorio, VorticisGerencial,
  VorticisCondominio, VorticisFrota, VorticisLogistica — .ico, -256.png e -512.png): a espiral da logo inteira na cor do módulo (o verde vira a
  cor de destaque e o azul-marinho, a cor da barra) com um selo redondo na mesma cor e a sigla do módulo
  (C, F, FP, E, G, CD, FR, VL), para não confundir os sistemas na barra de tarefas.
  VorticisInstalador.ico — o ícone do instalador (e da Vorticis Suite), diferente de todos: a espiral branca sobre um quadrado
  azul-marinho arredondado, com uma faixa nas cores dos módulos e uma seta de instalação.
  VorticisLogistica.ico (janela, executável e atalhos) e VorticisLogisticaInstalador.ico (instalador) do Vorticis Suite Logística: um caminhão
  branco sobre azul-marinho, dentro de um anel com as cores dos seis módulos; o do instalador tem ainda um selo dourado de instalação.

Usa só o símbolo (a espiral à esquerda da logo): o nome VORTICIS fica ilegível nos tamanhos de ícone.
O fundo branco da logo vira transparente. As cores devem ser as mesmas de IModuloDesktop.CorDestaque e
CorBarra de cada módulo (o teste MenusTests confere que cada executável tem o seu ícone).
Uso: python3 -I instalador/icone/gerar_icone.py   (requer Pillow e numpy)
"""
import colorsys
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFont

PASTA = Path(__file__).resolve().parent
LOGO = PASTA / "logo-vorticis.jpg"
SIMBOLO = (100, 355, 409, 669)   # caixa do símbolo na logo de 1024 x 1024 (esq, topo, dir, base)
MARGEM = 0.04                    # folga em volta do símbolo, proporcional ao lado
TAMANHOS = [16, 20, 24, 32, 40, 48, 64, 128, 256]
VERDE_LOGO = "#1B7F5A"           # verde de referência da espiral
MARINHO_LOGO = "#14305A"         # azul-marinho de referência do contorno
FONTE = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

# Módulo -> (cor de destaque, cor da barra, sigla do selo) — as mesmas de IModuloDesktop
MODULOS = {
    "VorticisContabil": ("#1B7F5A", "#14532D", "C"),
    "VorticisFiscal": ("#D97706", "#78350F", "F"),
    "VorticisFolha": ("#7E22CE", "#3B0764", "FP"),
    "VorticisEscritorio": ("#1D4ED8", "#1E3A8A", "E"),
    "VorticisGerencial": ("#0891B2", "#164E63", "G"),
    "VorticisCondominio": ("#BE123C", "#881337", "CD"),
    "VorticisFrota": ("#475569", "#1E293B", "FR"),
    "VorticisLogistica": ("#0F766E", "#134E4A", "VL"),
}


def simbolo() -> Image.Image:
    rgb = Image.open(LOGO).convert("RGB").crop(SIMBOLO)
    # transparência pela distância ao branco (bordas suaves, sem halo claro)
    escuro = ImageChops.invert(rgb.convert("L"))
    alfa = escuro.point(lambda v: 0 if v < 12 else min(255, round((v - 12) * 255 / 120)))
    img = rgb.convert("RGBA")
    img.putalpha(alfa)
    lado = round(max(img.size) * (1 + 2 * MARGEM))
    quadro = Image.new("RGBA", (lado, lado), (0, 0, 0, 0))
    quadro.paste(img, ((lado - img.width) // 2, (lado - img.height) // 2), img)
    return quadro


def hex_rgb(cor: str) -> tuple[float, float, float]:
    return tuple(int(cor[i:i + 2], 16) / 255 for i in (1, 3, 5))  # type: ignore[return-value]


def recolorir(base: Image.Image, cor: str, marinho: str | None = None) -> Image.Image:
    """Troca o verde da espiral pela cor do módulo (e o azul-marinho pela cor da barra), mantendo o tom de cada pixel."""
    a = np.asarray(base).astype(np.float32) / 255.0
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    maximo, minimo = a[..., :3].max(axis=-1), a[..., :3].min(axis=-1)
    delta = maximo - minimo
    # matiz em graus (0-360); verde da logo ~ 140-170, azul-marinho ~ 210-225
    h = np.zeros_like(maximo)
    com_cor = delta > 1e-6
    mr = (maximo == r) & com_cor
    mg = (maximo == g) & com_cor & ~mr
    mb = com_cor & ~mr & ~mg
    h[mr] = ((g - b)[mr] / delta[mr]) % 6
    h[mg] = (b - r)[mg] / delta[mg] + 2
    h[mb] = (r - g)[mb] / delta[mb] + 4
    h *= 60
    sat = np.where(maximo > 0, delta / np.maximum(maximo, 1e-6), 0)
    # peso do "verde": 1 até 175 graus, cai a 0 em 195 (transição suave para o azul-marinho)
    peso = np.clip((195 - h) / 20, 0, 1) * np.clip(sat / 0.25, 0, 1)
    # brilho relativo ao verde de referência da logo
    ref = colorsys.rgb_to_hsv(*hex_rgb(VERDE_LOGO))[2]
    fator = np.clip(maximo / ref, 0, 1.25)
    ch, cs, cv = colorsys.rgb_to_hsv(*hex_rgb(cor))
    alvo = np.array(colorsys.hsv_to_rgb(ch, cs, 1.0), dtype=np.float32)
    pleno = alvo[None, None, :] * np.clip(fator * cv, 0, 1)[..., None]
    # pixels pouco saturados (borda suave, perto do branco) mantêm a cor original
    claro = 1 - np.clip(sat / 0.9, 0, 1)[..., None]
    novo = pleno * (1 - claro) + a[..., :3] * claro
    a[..., :3] = np.clip(a[..., :3] * (1 - peso[..., None]) + novo * peso[..., None], 0, 1)
    if marinho is not None:
        # contorno azul-marinho (matiz 195-260) vira a cor escura do módulo
        peso_m = np.clip((h - 195) / 15, 0, 1) * np.clip((265 - h) / 15, 0, 1) * np.clip(sat / 0.2, 0, 1)
        ref_m = colorsys.rgb_to_hsv(*hex_rgb(MARINHO_LOGO))[2]
        fator_m = np.clip(maximo / ref_m, 0, 1.6)
        mh, ms, mv = colorsys.rgb_to_hsv(*hex_rgb(marinho))
        alvo_m = np.array(colorsys.hsv_to_rgb(mh, ms, 1.0), dtype=np.float32)
        novo_m = alvo_m[None, None, :] * np.clip(fator_m * mv, 0, 1)[..., None]
        a[..., :3] = np.clip(a[..., :3] * (1 - peso_m[..., None]) + novo_m * peso_m[..., None], 0, 1)
    return Image.fromarray((a * 255).round().astype(np.uint8), "RGBA")


def selo(img: Image.Image, cor: str, sigla: str) -> Image.Image:
    """Selo redondo no canto inferior direito, na cor do módulo, com a sigla em branco (borda branca para destacar)."""
    lado = img.width
    saida = img.copy()
    d = ImageDraw.Draw(saida)
    diam = round(lado * 0.46)
    x0, y0 = lado - diam - round(lado * 0.01), lado - diam - round(lado * 0.01)
    borda = max(2, round(lado * 0.025))
    d.ellipse((x0 - borda, y0 - borda, x0 + diam + borda, y0 + diam + borda), fill=(255, 255, 255, 255))
    d.ellipse((x0, y0, x0 + diam, y0 + diam), fill=cor)
    tam = round(diam * {1: 0.62, 2: 0.46, 3: 0.34}[len(sigla)])
    fonte = ImageFont.truetype(FONTE, tam)
    caixa = d.textbbox((0, 0), sigla, font=fonte)
    tx = x0 + (diam - (caixa[2] - caixa[0])) / 2 - caixa[0]
    ty = y0 + (diam - (caixa[3] - caixa[1])) / 2 - caixa[1]
    d.text((tx, ty), sigla, font=fonte, fill=(255, 255, 255, 255))
    return saida


# Produtos com instalador próprio -> (fundo, módulos da faixa de cores)
INSTALADORES = {
    "VorticisInstalador": (MARINHO_LOGO, ["VorticisContabil", "VorticisFiscal", "VorticisFolha", "VorticisEscritorio", "VorticisGerencial",
                                          "VorticisCondominio"]),
}


def instalador(base: Image.Image, fundo: str, modulos: list[str]) -> Image.Image:
    """Ícone do instalador (e da Suite): espiral branca sobre quadrado escuro, faixa com as cores dos módulos e seta de instalação."""
    lado = 512
    img = Image.new("RGBA", (lado, lado), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((8, 8, lado - 8, lado - 8), radius=96, fill=fundo)
    # faixa inferior com as cores dos módulos do produto
    cores = [MODULOS[m][0] for m in modulos]
    larg = (lado - 16) / len(cores)
    faixa = Image.new("RGBA", (lado, lado), (0, 0, 0, 0))
    fd = ImageDraw.Draw(faixa)
    for i, c in enumerate(cores):
        fd.rectangle((8 + i * larg, lado - 92, 8 + (i + 1) * larg, lado - 8), fill=c)
    mascara = Image.new("L", (lado, lado), 0)
    ImageDraw.Draw(mascara).rounded_rectangle((8, 8, lado - 8, lado - 8), radius=96, fill=255)
    img.paste(faixa, (0, 0), ImageChops.multiply(mascara, faixa.getchannel("A")))
    # espiral em branco (só o desenho, pela transparência da logo)
    esp = base.resize((330, 330), Image.LANCZOS)
    branca = Image.new("RGBA", esp.size, (255, 255, 255, 255))
    branca.putalpha(esp.getchannel("A"))
    img.alpha_composite(branca, (40, 40))
    # seta de instalação (para baixo, sobre uma bandeja) no canto superior direito
    cx, topo = 395, 70
    d.rectangle((cx - 22, topo, cx + 22, topo + 120), fill="#FBBF24")
    d.polygon([(cx - 70, topo + 110), (cx + 70, topo + 110), (cx, topo + 190)], fill="#FBBF24")
    d.rectangle((cx - 80, topo + 215, cx + 80, topo + 245), fill="#FBBF24")
    return img


# Cores dos seis módulos do Vorticis Suite Logística, na ordem da navegação (Frotas, Transporte, Manutenção, Financeiro, Gerencial, Contabilidade)
CORES_MODULOS_LOGISTICA = ["#0284C7", "#0D9488", "#EA580C", "#16A34A", "#DB2777", "#4F46E5"]


def icone_logistica(selo_instalacao: bool = False) -> Image.Image:
    """
    Ícone do programa (janela, executável e atalhos) e do instalador do Vorticis Suite Logística: um caminhão branco sobre fundo azul-marinho,
    dentro de um anel de seis arcos nas cores dos módulos (Frotas, Transporte, Manutenção, Financeiro, Gerencial e Contabilidade).
    O do instalador leva ainda um selo dourado de instalação no canto. Desenhado em 2048 e reduzido.
    """
    S = 2048
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32) / S
    t = np.clip((xx + yy) / 2, 0, 1)[..., None]
    a, b = np.array(hex_rgb("#1E3A8A")), np.array(hex_rgb("#0F172A"))
    cor = a[None, None, :] * (1 - t) + b[None, None, :] * t
    brilho = np.exp(-(((xx - 0.3) ** 2 + (yy - 0.22) ** 2) / 0.06))[..., None] * 0.16
    cor = np.clip(cor + brilho, 0, 1)
    fundo = Image.fromarray((np.dstack([cor, np.ones((S, S, 1), np.float32)]) * 255).astype(np.uint8), "RGBA")
    m = round(S * 0.03)
    mascara = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mascara).rounded_rectangle((m, m, S - m, S - m), radius=round(S * 0.24), fill=255)
    img.paste(fundo, (0, 0), mascara)
    d = ImageDraw.Draw(img)

    # anel dos módulos: seis arcos, a partir do alto, com um pequeno vão entre eles
    cx, cy, r, w = S / 2, S / 2, S * 0.395, S * 0.075
    for k, c in enumerate(CORES_MODULOS_LOGISTICA):
        ini = -90 + k * 60 + 4
        d.arc((cx - r, cy - r, cx + r, cy + r), ini, ini + 52, fill=c, width=round(w))

    # caminhão (vista de lado, para a direita): baú, cabine, para-brisa, chassi e rodas
    branco = (255, 255, 255, 255)
    marinho = "#14254F"
    u = S / 100                                     # unidade: 1% do lado
    bx0, by0, bx1, by1 = 27 * u, 37 * u, 58 * u, 61 * u                 # baú
    d.rounded_rectangle((bx0, by0, bx1, by1), radius=2.2 * u, fill=branco)
    cab = [(59.5 * u, 44 * u), (66 * u, 44 * u), (73 * u, 52 * u), (73 * u, 61 * u), (59.5 * u, 61 * u)]   # cabine com o bico inclinado
    d.polygon(cab, fill=branco)
    d.polygon([(61.5 * u, 46.2 * u), (65.2 * u, 46.2 * u), (69.6 * u, 51.6 * u), (61.5 * u, 51.6 * u)], fill=marinho)   # para-brisa
    d.rounded_rectangle((26 * u, 60 * u, 74 * u, 64 * u), radius=1.5 * u, fill=branco)                                 # chassi
    for x in (35 * u, 47 * u, 66 * u):                                                                                  # rodas
        d.ellipse((x - 5.4 * u, 63.5 * u - 5.4 * u, x + 5.4 * u, 63.5 * u + 5.4 * u), fill=marinho)
        d.ellipse((x - 4 * u, 63.5 * u - 4 * u, x + 4 * u, 63.5 * u + 4 * u), fill=branco)
        d.ellipse((x - 1.7 * u, 63.5 * u - 1.7 * u, x + 1.7 * u, 63.5 * u + 1.7 * u), fill=marinho)
    # faixas de velocidade atrás do caminhão
    for k, y in enumerate((41.5, 48.5, 55.5)):
        d.rounded_rectangle(((17 + k * 2) * u, y * u, (24 + k * 0) * u, (y + 2.2) * u), radius=1.1 * u, fill=(255, 255, 255, 170))

    if selo_instalacao:
        sx, sy, sr = S * 0.80, S * 0.80, S * 0.15
        d.ellipse((sx - sr - S * 0.02, sy - sr - S * 0.02, sx + sr + S * 0.02, sy + sr + S * 0.02), fill=branco)
        d.ellipse((sx - sr, sy - sr, sx + sr, sy + sr), fill="#F59E0B")
        hw, hh = sr * 0.24, sr * 0.55
        d.rounded_rectangle((sx - hw / 2, sy - hh, sx + hw / 2, sy + hh * 0.15), radius=hw / 3, fill=branco)
        d.polygon([(sx - sr * 0.5, sy), (sx + sr * 0.5, sy), (sx, sy + sr * 0.5)], fill=branco)
    return img.resize((512, 512), Image.LANCZOS)


def salvar(nome: str, grande: Image.Image) -> None:
    grande.save(PASTA / f"{nome}-512.png")
    grande.resize((256, 256), Image.LANCZOS).save(PASTA / f"{nome}-256.png")
    grande.save(PASTA / f"{nome}.ico", format="ICO", sizes=[(t, t) for t in TAMANHOS])


def main() -> None:
    base = simbolo()
    for nome, (cor, barra, sigla) in MODULOS.items():
        img = recolorir(base, cor, barra).resize((512, 512), Image.LANCZOS)
        salvar(nome, selo(img, cor, sigla))
    for nome, (fundo, modulos) in INSTALADORES.items():
        salvar(nome, instalador(base, fundo, modulos))
    salvar("VorticisLogistica", icone_logistica())
    salvar("VorticisLogisticaInstalador", icone_logistica(selo_instalacao=True))


if __name__ == "__main__":
    import sys
    # "python3 -I gerar_icone.py VorticisLogisticaInstalador" refaz só os ícones do programa e do instalador da Suite Logística
    if sys.argv[1:] == ["VorticisLogisticaInstalador"]:
        salvar("VorticisLogistica", icone_logistica())
        salvar("VorticisLogisticaInstalador", icone_logistica(selo_instalacao=True))
    else:
        main()
