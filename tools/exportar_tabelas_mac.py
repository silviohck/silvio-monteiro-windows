#!/usr/bin/env python3
"""Exporta CID-10, SIGTAP e TUSS do código Swift do Orthck Mac para TSV.

Uso:
  python tools/exportar_tabelas_mac.py /caminho/Silvio-Monteiro /caminho/saida

Fonte das tabelas continua sendo os arquivos oficiais já mantidos no projeto Mac.
Não modifica o repositório Mac. Não processa dados de pacientes.
"""
from __future__ import annotations
import pathlib
import re
import sys


def literal(text: str, key: str) -> list[str]:
    pattern = r"static let " + re.escape(key) + r'\s*=\s*#"""\s*\n(.*?)\n"""#'
    match = re.search(pattern, text, flags=re.DOTALL)
    if not match:
        raise ValueError(f"Não encontrei o bloco Swift: {key}")
    return [line.strip() for line in match.group(1).splitlines() if "|" in line]


def sigtap_code(raw: str) -> str:
    if len(raw) != 10:
        return ".".join(raw[i:i+2] for i in range(0, len(raw), 2))
    return f"{raw[:2]}.{raw[2:4]}.{raw[4:6]}.{raw[6:9]}-{raw[9]}"


def export(path: pathlib.Path, key: str, output: pathlib.Path, format_code=lambda x: x) -> int:
    src = path.read_text(encoding="utf-8")
    rows = []
    for line in literal(src, key):
        code, description = line.split("|", 1)
        rows.append(f"{format_code(code)}\t{description.replace(chr(9), ' ')}")
    output.write_text("\n".join(rows) + "\n", encoding="utf-8")
    return len(rows)


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit("Uso: exportar_tabelas_mac.py REPOSITORIO_MAC PASTA_DE_SAIDA")
    root = pathlib.Path(sys.argv[1]) / "Sources" / "GeradorDocumentos"
    out = pathlib.Path(sys.argv[2]); out.mkdir(parents=True, exist_ok=True)
    for source, field, dest, converter in [
        ("CID10Dados.swift", "codigos", "cid10.tsv", lambda x: x),
        ("SIGTAPDados.swift", "procedimentos", "sigtap.tsv", sigtap_code),
        ("TUSSDados.swift", "procedimentos", "tuss.tsv", lambda x: x),
    ]:
        count = export(root / source, field, out / dest, converter)
        print(f"{dest}: {count} entradas")


if __name__ == "__main__":
    main()
