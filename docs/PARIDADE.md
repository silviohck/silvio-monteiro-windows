# Orthck macOS ↔ Windows — Matriz objetiva de paridade

Esta matriz diferencia **código implementado**, **paridade completa** e **testes reais**, evitando marcar como pronto algo que apenas parece pronto.

Atualizada em 18/11/2026 (todas as alterações do Mac até 17/11/2026: Prontuário, negrito, Google Drive, Limpar fila); reescrita a partir do código do Mac no PR #6.

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
| Atendidos recentemente: do atendimento mais recente para o mais antigo | Igual | — |
| Gerar documento: quadro do local, documento pronto, subpasta em níveis, abrir ao gerar, nome copiado; trocar de subpasta não escolhe sozinho o primeiro modelo (fica "Escolha…", exceto com modelo único); gera com Ctrl+Enter (⌘↩ no Mac), não com Enter sozinho; o botão mostra o modelo (Gerar “nome”) e não gera se o modelo mudou | Igual | — |
| Documentos não ficam guardados: pasta temporária do app (Documentos gerados), apagados após 12 h, menos os da fila; "Só abrir" | Igual (06/11/2026) | — |
| Sugestões de pacientes só do local escolhido | Igual (06/11/2026) | — |
| Sugestões de CID só em campos de CID (não em {{CIDADE}}) | Igual (06/11/2026) | — |
| Favoritos de cada local (estrela ao lado do modelo), em grade, reordenáveis (arrastar ou botão direito); favoritos antigos migram por local | Igual (06/11/2026; chave do local em maiúsculas, compatível com o dados.json do Mac) | — |
| Nome do paciente e campos em maiúsculas; clicar num favorito mantém o nome | Igual (06/11/2026) | — |
| "Gerar documento" usa o modelo que está na tela no clique; modelo alterado na pasta é copiado de novo antes de gerar | Igual (06/11/2026) | — |
| Nome do documento nunca repete um do Histórico (o Word imprimiria o que já estivesse aberto) | Igual (06/11/2026) | — |
| Painel lateral: "Imprimir fila (N)" e "Atendidos recentemente" (12, por local e por dia, com documentos) | Igual (06/11/2026) | Conferir visual no Windows real |
| Fila: enviado sai sozinho após 2 min; mesmo arquivo não é reenviado em 20 s; "Parar todas as impressões" | Igual (06/11/2026; cancela trabalhos do spooler pelo System.Printing) | Homologar com impressora real |
| Google Drive: cópia automática do dados.json (envia até 30 s depois de cada mudança e ao fechar; junta a cada 30 s o que mudou em outro computador) e "Enviar pasta ao Google Drive" (.zip) | Igual (18/11/2026), a pedido do proprietário; só depois de "Conectar ao Google Drive…"; credencial pelos Secrets `GOOGLE_CLIENT_ID`/`GOOGLE_CLIENT_SECRET` ou pelo arquivo client_secret….json | Testar com a conta real e com o Mac ao mesmo tempo; a mesma credencial (projeto do Google Cloud) do Mac é necessária para os dois verem o mesmo arquivo. Avaliar LGPD (dados clínicos no Drive da conta do médico) |
| Prontuário: fichas prontas (Araruna CER II, Alagoa Grande), importar ficha .docx, identificação do cadastro, linhas da ficha (o que não cabe desce, Enter quebra a linha), exames complementares junto do exame físico | Igual (18/11/2026) | Conferir a ficha impressa no Windows real |
| Prontuário: 1.050 modelos de atendimento, busca por doença ou CID, lado (Direito/Esquerdo/Bilateral) com concordância e troca no texto já escrito, favoritos por local (reorganizar), "Meus modelos" | Igual (18/11/2026) | — |
| Prontuário: Tab pula para o próximo ___, "Tirar os ___ não preenchidos", Limpar por campo, Limpar ficha, Próximo paciente, Ctrl+Enter gera | Igual (18/11/2026; Ctrl no lugar de ⌘) | — |
| Negrito: Ctrl+B (⌘B no Mac) em qualquer campo; `**trecho**` sai em negrito no Word | Igual (18/11/2026) | Conferir no Word real |
| Paciente compartilhado entre Gerar documento e Prontuário (só na memória) | Igual (18/11/2026) | — |
| Cadastro: número, bairro, cidade, UF, CEP e CNS ({{NUMERO}}, {{BAIRRO}}, {{CIDADE_PACIENTE}}, {{UF}}, {{CEP}}, {{CNS}}) | Igual (18/11/2026) | — |
| Importar dados (.json): Juntar ou Substituir | Igual (18/11/2026; Juntar acrescenta sem trocar os cadastros abertos nas telas) | — |
| Fila: "Limpar fila"; Atendidos recentemente: "Limpar" e "Tirar desta lista" | Igual (18/11/2026) | — |
| Impressão: só imprime se o Word abriu exatamente o arquivo certo (não um de mesmo nome de outra pasta) e se ele está à frente | Igual (18/11/2026) | Homologar com Word e impressora reais |
| Favorito no editor do modelo (aba Modelos) | Igual (18/11/2026) | — |
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
| Sincronização Mac + Windows | Pela cópia no Google Drive (juntar; apagar não se propaga) | Testar os dois ao mesmo tempo |

## Fluxo mínimo de homologação

1. Importar cópia anônima de dados de teste do Mac, verificar IDs, CPF e nomes repetidos.
2. Gerar 20 DOCX com modelos reais, incluindo cabeçalho/rodapé, tabela e campos divididos entre runs.
3. Gerar pacientes A/B/A seguidamente e verificar que nenhum diagnóstico permanece indevidamente.
4. Gerar 10 documentos na fila com cópias diferentes, imprimir sequencialmente e conferir páginas.
5. Desligar/desconectar impressora durante a fila; verificar estado `incerto`, sem reenvio automático.
6. Encerrar aplicativo durante envio; reabrir e conferir persistência e confirmação de reenvio.
7. Mover a pasta de modelos; garantir que nenhum DOCX ou cadastro seja removido automaticamente.
8. Validar versão Windows x64 e Word desktop real antes de uso assistencial.
