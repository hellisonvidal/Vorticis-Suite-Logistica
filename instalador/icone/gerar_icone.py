"""Gera os ícones da suíte Vorticis a partir da logo (logo-vorticis.jpg):

  Um ícone por módulo (VorticisContabil, VorticisFiscal, VorticisFolha, VorticisEscritorio, VorticisGerencial,
  VorticisCondominio, VorticisFrota, VorticisLogistica — .ico, -256.png e -512.png): a espiral da logo inteira na cor do módulo (o verde vira a
  cor de destaque e o azul-marinho, a cor da barra) com um selo redondo na mesma cor e a sigla do módulo
  (C, F, FP, E, G, CD, FR, VL), para não confundir os sistemas na barra de tarefas.
  VorticisInstalador.ico — o ícone do instalador (e da Vorticis Suite), diferente de todos: a espiral branca sobre um quadrado
  azul-marinho arredondado, com uma faixa nas cores dos módulos e uma seta de instalação.
  VorticisLogisticaInstalador.ico — o instalador do Vorticis Suite Logística: caminhão branco sobre uma estrada em faixas com as cores dos
  sete módulos (CORES_LOGISTICA), fundo azul-noite e selo dourado de instalação.

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


# Cores dos sete módulos do Vorticis Suite Logística (ModuloLogistica.CorDestaque), na ordem da navegação
CORES_LOGISTICA = ["#0284C7", "#0D9488", "#EA580C", "#7C3AED", "#16A34A", "#DB2777", "#4F46E5"]


def instalador_logistica() -> Image.Image:
    """
    Ícone do instalador do Vorticis Suite Logística: fundo azul-noite, uma estrada em faixas com as cores dos sete módulos,
    um caminhão branco (com a mesma faixa colorida no baú) e o selo dourado de instalação. Desenhado em 2048 e reduzido.
    """
    S = 2048
    k = S / 1024                                   # coordenadas abaixo na grade de 1024
    def P(x, y): return (x * k, y * k)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # fundo: degradê vertical azul-noite com um brilho azul no alto à esquerda
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32) / S
    topo, base = np.array(hex_rgb("#15284A")), np.array(hex_rgb("#060B16"))
    fundo = topo[None, None, :] * (1 - yy[..., None]) + base[None, None, :] * yy[..., None]
    brilho = np.exp(-(((xx - 0.18) ** 2 + (yy - 0.12) ** 2) / 0.09))[..., None]
    fundo = np.clip(fundo + brilho * np.array(hex_rgb("#38BDF8"))[None, None, :] * 0.35, 0, 1)
    camada = Image.fromarray((np.dstack([fundo, np.ones((S, S, 1), np.float32)]) * 255).astype(np.uint8), "RGBA")
    mascara = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mascara).rounded_rectangle((16 * k, 16 * k, S - 16 * k, S - 16 * k), radius=210 * k, fill=255)
    img.paste(camada, (0, 0), mascara)

    desenho = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(desenho)
    # estrada: curva de Bézier com uma faixa por módulo, de baixo à esquerda para a direita
    def bezier(t, p0, p1, p2, p3):
        u = 1 - t
        return (u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1])
    ctrl = [P(-60, 840), P(300, 700), P(700, 820), P(1084, 690)]
    pontos = [bezier(i / 200, *ctrl) for i in range(201)]
    largura, n = 24 * k, len(CORES_LOGISTICA)
    for faixa, cor in enumerate(CORES_LOGISTICA):
        desloc = (faixa - (n - 1) / 2) * largura
        linha = []
        for i, (x, y) in enumerate(pontos):
            x2, y2 = pontos[min(i + 1, len(pontos) - 1)]
            x1, y1 = pontos[max(i - 1, 0)]
            dx, dy = x2 - x1, y2 - y1
            norma = max((dx * dx + dy * dy) ** 0.5, 1e-6)
            linha.append((x - dy / norma * desloc, y + dx / norma * desloc))
        d.line(linha, fill=cor, width=round(largura) + 2, joint="curve")
    # linhas de velocidade atrás do caminhão
    for y, x0, x1 in [(420, 70, 200), (500, 40, 200), (580, 100, 200)]:
        d.rounded_rectangle((*P(x0, y - 11), *P(x1, y + 11)), radius=11 * k, fill=(255, 255, 255, 150))
    # caminhão: baú, cabine com janela, para-choque, rodas
    branco, noite = (255, 255, 255, 255), "#0B1424"
    d.rounded_rectangle((*P(230, 330), *P(610, 620)), radius=34 * k, fill=branco)
    # faixa com as cores dos módulos no baú
    larg_seg = (560 - 280) / n
    for i, cor in enumerate(CORES_LOGISTICA):
        d.rectangle((*P(280 + i * larg_seg, 450), *P(280 + (i + 1) * larg_seg + 1, 492)), fill=cor)
    d.polygon([P(628, 410), P(748, 410), P(840, 520), P(840, 620), P(628, 620)], fill=branco)
    d.rounded_rectangle((*P(628, 410), *P(700, 470)), radius=18 * k, fill=branco)
    d.polygon([P(660, 440), P(736, 440), P(800, 516), P(660, 516)], fill=noite)          # janela
    d.rounded_rectangle((*P(214, 612), *P(856, 652)), radius=16 * k, fill=branco)       # chassi
    d.rounded_rectangle((*P(812, 560), *P(860, 590)), radius=10 * k, fill="#FBBF24")    # farol
    for cx in (350, 720):
        d.ellipse((*P(cx - 74, 646 - 74), *P(cx + 74, 646 + 74)), fill=noite)
        d.ellipse((*P(cx - 58, 646 - 58), *P(cx + 58, 646 + 58)), fill=branco)
        d.ellipse((*P(cx - 32, 646 - 32), *P(cx + 32, 646 + 32)), fill="#CBD5E1")
        d.ellipse((*P(cx - 12, 646 - 12), *P(cx + 12, 646 + 12)), fill=noite)
    # selo de instalação: círculo dourado com seta para baixo, contornado na cor do fundo
    cx, cy, r = 826, 214, 132
    d.ellipse((*P(cx - r - 18, cy - r - 18), *P(cx + r + 18, cy + r + 18)), fill="#0E1B33")
    d.ellipse((*P(cx - r, cy - r), *P(cx + r, cy + r)), fill="#F59E0B")
    d.ellipse((*P(cx - r + 10, cy - r + 6), *P(cx + r - 10, cy + r - 26)), fill="#FBBF24")
    d.rounded_rectangle((*P(cx - 22, cy - 82), *P(cx + 22, cy + 22)), radius=10 * k, fill=branco)
    d.polygon([P(cx - 66, cy + 4), P(cx + 66, cy + 4), P(cx, cy + 74)], fill=branco)
    d.rounded_rectangle((*P(cx - 70, cy + 86), *P(cx + 70, cy + 108)), radius=10 * k, fill=branco)
    img.alpha_composite(Image.composite(desenho, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mascara))
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
    salvar("VorticisLogisticaInstalador", instalador_logistica())


if __name__ == "__main__":
    import sys
    # "python3 -I gerar_icone.py VorticisLogisticaInstalador" refaz só o ícone do instalador da Suite Logística
    if sys.argv[1:] == ["VorticisLogisticaInstalador"]:
        salvar("VorticisLogisticaInstalador", instalador_logistica())
    else:
        main()
