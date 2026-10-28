#!/usr/bin/env python3
"""Gera as tabelas CID-10, SIGTAP e TUSS embutidas no Orthck Windows a partir do projeto Mac.

Uso:
  python tools/gerar_tabelas.py CAMINHO/DO/Silvio-Monteiro

Lê Sources/GeradorDocumentos/{CID10,SIGTAP,TUSS}Dados.swift (as mesmas tabelas oficiais do app Mac)
e grava src/Orthck.Core/Tabelas/*.txt, no formato "campo|campo". Não modifica o projeto Mac.
"""
from __future__ import annotations
import pathlib
import re
import sys

SAIDA = pathlib.Path(__file__).resolve().parent.parent / "src" / "Orthck.Core" / "Tabelas"


def bloco(texto: str, nome: str) -> str:
    achado = re.search(r"static let " + re.escape(nome) + r'\s*=\s*#"""\s*\n(.*?)\n"""#', texto, flags=re.DOTALL)
    if not achado:
        raise SystemExit(f"Não encontrei o bloco {nome}")
    linhas = [linha.strip() for linha in achado.group(1).splitlines() if "|" in linha]
    return "\n".join(linhas) + "\n"


def valor(texto: str, nome: str) -> str:
    achado = re.search(r'static let ' + re.escape(nome) + r'\s*=\s*"([^"]*)"', texto)
    return achado.group(1) if achado else ""


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    fontes = pathlib.Path(sys.argv[1]) / "Sources" / "GeradorDocumentos"
    cid = (fontes / "CID10Dados.swift").read_text(encoding="utf-8")
    sigtap = (fontes / "SIGTAPDados.swift").read_text(encoding="utf-8")
    tuss = (fontes / "TUSSDados.swift").read_text(encoding="utf-8")
    SAIDA.mkdir(parents=True, exist_ok=True)
    arquivos = {
        "cid10_capitulos.txt": bloco(cid, "capitulos"),
        "cid10_codigos.txt": bloco(cid, "codigos"),
        "sigtap_grupos.txt": bloco(sigtap, "grupos"),
        "sigtap_subgrupos.txt": bloco(sigtap, "subgrupos"),
        "sigtap_procedimentos.txt": bloco(sigtap, "procedimentos"),
        "tuss_procedimentos.txt": bloco(tuss, "procedimentos"),
        "versoes.txt": f"sigtap|{valor(sigtap, 'competencia')}\ntuss|{valor(tuss, 'versao')}\n",
    }
    for nome, conteudo in arquivos.items():
        (SAIDA / nome).write_text(conteudo, encoding="utf-8", newline="\n")
        print(f"{nome}: {conteudo.count(chr(10))} linhas")


if __name__ == "__main__":
    main()
