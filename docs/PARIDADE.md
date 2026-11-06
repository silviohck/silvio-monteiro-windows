# Orthck macOS ↔ Windows — Matriz objetiva de paridade

Esta matriz diferencia **código implementado**, **paridade completa** e **testes reais**, evitando marcar como pronto algo que apenas parece pronto.

Atualizada em 06/11/2026 (paridade com o Mac de novembro); reescrita a partir do código do Mac no PR #6.

| Funcionalidade (Mac) | Windows | Pendência |
|---|---|---|
| Geração DOCX a partir de `{{CAMPOS}}` (runs divididos, cabeçalho, rodapé, notas, caixas de texto) | Igual; testes automatizados e LibreOffice | Comparar com o Word real e com a saída do Mac |
| Campos automáticos (paciente, local, data, data por extenso, idade) e aliases | Igual (`CamposAutomaticos`) | — |
| Campos do atendimento (CID, conduta…) nunca vão para o cadastro | Igual; trocar nome/homônimo/modelo/subpasta/local recomeça esses campos (regra do CLAUDE.md, mais rígida que o Mac) | — |
| Sugestões de CID no campo `{{CID}}` | Igual | — |
| Modelos copiados para o app, com tipo, local e cópias | Igual | — |
| Pasta sincronizada: locais, "Todos os locais" (1 - APAC), ignorar, subpastas em níveis, novos/alterados/apagados, proteção contra pasta vazia | Igual | Testar com OneDrive/rede |
| Atalhos de pasta (alias do Finder) | Atalhos `.lnk` do Windows | Testar no Windows real |
| Modelos .doc/.rtf/.odt | Convertidos pelo Word (COM) | Testar no Windows real |
| Tipos por local (criar, renomear, excluir, ordem alfabética) | Igual | — |
| Gerar documento: quadro do local, documento pronto, subpasta em níveis, abrir ao gerar, nome copiado | Igual | — |
| Documentos não ficam guardados: pasta temporária do app (Documentos gerados), apagados após 12 h, menos os da fila; "Só abrir" | Igual (06/11/2026) | — |
| Sugestões de pacientes só do local escolhido | Igual (06/11/2026) | — |
| Sugestões de CID só em campos de CID (não em {{CIDADE}}) | Igual (06/11/2026) | — |
| Favoritos de cada local (estrela ao lado do modelo), em grade, reordenáveis (arrastar ou botão direito); favoritos antigos migram por local | Igual (06/11/2026; chave do local em maiúsculas, compatível com o dados.json do Mac) | — |
| Nome do paciente e campos em maiúsculas; clicar num favorito mantém o nome | Igual (06/11/2026) | — |
| "Gerar documento" usa o modelo que está na tela no clique; modelo alterado na pasta é copiado de novo antes de gerar | Igual (06/11/2026) | — |
| Nome do documento nunca repete um do Histórico (o Word imprimiria o que já estivesse aberto) | Igual (06/11/2026) | — |
| Painel lateral: "Imprimir fila (N)" e "Atendidos recentemente" (12, por local e por dia, com documentos) | Igual (06/11/2026) | Conferir visual no Windows real |
| Fila: enviado sai sozinho após 2 min; mesmo arquivo não é reenviado em 20 s; "Parar todas as impressões" | Igual (06/11/2026; cancela trabalhos do spooler pelo System.Printing) | Homologar com impressora real |
| Google Drive (cópia do dados.json e envio da pasta em .zip) | Não implementado | Sincronização em nuvem: prioridade 7 do CLAUDE.md, precisa de projeto de segurança |
| Pacientes: sugestões com locais, homônimos, "é outro paciente", cadastro a partir do Gerar | Igual | Mesclar cadastros (não existe no Mac) |
| Locais: pasta própria, excluir transferindo/desvinculando | Igual (Windows também edita cidade, endereço, telefone, e-mail e CNPJ) | — |
| Histórico: abrir, mostrar na pasta, imprimir, pôr na fila, remover, limpar | Igual + busca | — |
| Fila de impressão: persistente, um por vez, cópias, marcados, reenvio confirmado | Igual; envio incerto interrompe o lote | Homologar com impressora real |
| Impressora escolhida na barra lateral | Lista do Windows; não muda a impressora padrão | Testar impressoras de rede |
| Procurar/instalar impressoras na rede | Abre Configurações › Impressoras do Windows | — |
| CID-10 e SIGTAP embutidos (TUSS removida nos dois), busca sem acentos, filtro por capítulo/grupo, copiar | Igual (mesmas tabelas, `tools/gerar_tabelas.py`) | — |
| Compactar pasta sincronizada (.zip) com backup dos dados | Igual | — |
| Backup diário (30) | Igual + restauração guiada em Ajustes e ao abrir com dados ilegíveis | — |
| Aparência Sereno, claro/escuro, tamanho do texto, atalhos | Igual (Ctrl +, Ctrl −, Ctrl 0, Ctrl+Shift+N) | — |
| Importar dados do Mac | dados.json + pasta Modelos | Testar com dados reais anonimizados |
| Segurança clínica/LGPD | Dados só no computador | Controle de acesso, criptografia |
| Sincronização Mac + Windows | Não implementada | Projeto separado |

## Fluxo mínimo de homologação

1. Importar cópia anônima de dados de teste do Mac, verificar IDs, CPF e nomes repetidos.
2. Gerar 20 DOCX com modelos reais, incluindo cabeçalho/rodapé, tabela e campos divididos entre runs.
3. Gerar pacientes A/B/A seguidamente e verificar que nenhum diagnóstico permanece indevidamente.
4. Gerar 10 documentos na fila com cópias diferentes, imprimir sequencialmente e conferir páginas.
5. Desligar/desconectar impressora durante a fila; verificar estado `incerto`, sem reenvio automático.
6. Encerrar aplicativo durante envio; reabrir e conferir persistência e confirmação de reenvio.
7. Mover a pasta de modelos; garantir que nenhum DOCX ou cadastro seja removido automaticamente.
8. Validar versão Windows x64 e Word desktop real antes de uso assistencial.
