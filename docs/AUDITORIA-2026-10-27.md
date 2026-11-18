# Auditoria de segurança funcional — Orthck Windows (27/10/2026)

> **Estado:** correções propostas para revisão. A passagem no GitHub Actions confirma compilação e testes automatizados, **não** homologação clínica, impressão real nem paridade total com o Mac.

## Base integrada do Claude

Branch: `claude/eloquent-gates-s1hfgt`. Inclui compatibilidade de datas ISO-8601 com `dados.json` do Mac, pastas compartilhadas, prevenção de nomes reservados do Windows, higienização de campos DOCX, testes correspondentes, melhor controle do Word e execução única do aplicativo.

## Falhas adicionais encontradas e corrigidas

| Severidade | Defeito | Correção |
|---|---|---|
| Crítica | Edição do nome ou troca de homônimo preservava CID, diagnóstico e conduta manuais do paciente anterior (`RefreshFields(keepTyped: true)`). | Campos específicos de atendimento são redefinidos ao trocar identidade/modelo; dados permanentes vêm apenas do cadastro selecionado. |
| Alta | Timeout ou impressão com estado `incerto` podia deixar o restante do lote seguir automaticamente. | Lote para imediatamente no estado incerto; demais arquivos permanecem pendentes. |
| Alta | Status `enviado` era divulgado antes de terminar a limpeza da instância COM do Word. | O próximo envio é liberado só após fechar documento, restaurar impressora e concluir limpeza. |
| Alta | Após timeout, outra tarefa podia iniciar uma segunda automação Word enquanto a primeira permanecia travada. | Semaphore global de impressão Word; só liberado quando a tarefa COM realmente terminar, não quando expirar o timeout. |
| Alta | Importar arquivo JSON ausente tratava a origem como cadastro vazio. | Exige existência da origem antes da importação. |
| Alta | Falha na gravação do JSON importado podia deixar memória diferente dos dados em disco. | Troca da base ativa somente depois de gravar a importação com sucesso. |
| Média | Recuperação de itens `enviando` para `incerto` acontecia apenas em memória. | Grava imediatamente o estado de recuperação em disco. |
| Média | Campos de exames, justificativas e relatos clínicos extensos tinham edição limitada a linha curta. | Entradas multilinha para campos pertinentes, com rolagem. |

## Testes automatizados adicionados

- DOCX modelo intacto e arquivo de saída existente não sobrescrito.
- Campo clínico (CID e conduta) não incorporado aos dados permanentes do paciente.
- Importação inválida e importação de arquivo inexistente preservam cadastros.
- Estado de impressão interrompida é salvo como `incerto`, inclusive após outra inicialização.
- Lote interrompido por envio incerto deixa os próximos pacientes pendentes.
- Reenvio de documento incerto exige confirmação explícita.

## Riscos remanescentes e critérios de homologação

1. **Microsoft Word COM e drivers:** testar em Windows 11 com Word desktop real, modelos complexos, impressora USB/rede, papel especial, 1–20 cópias, Word aberto/fechado, impressora offline, PDF e fila bloqueada. `Enviado` significa comando ao Word, não papel impresso.
2. **Compatibilidade DOCX:** comparar visualmente geração Mac/Windows para cabeçalhos, rodapés, tabelas, caixas de texto e formatação de parágrafos; os testes automatizados não garantem igualdade visual.
3. **Paridade de modelos/cadastros:** implementação Windows ainda não reproduz todas as configurações de local, exclusão e sincronização do aplicativo Mac; consultar `docs/PARIDADE.md`.
4. **Privacidade e LGPD:** os dados locais ainda não estão protegidos por criptografia específica do aplicativo; avaliar permissões do perfil Windows, política de backup, disco criptografado e acesso por usuário antes de usar informações clínicas reais.
5. **Histórico migrado:** caminhos absolutos do macOS não são caminhos Windows; arquivos históricos precisam ser copiados/remapeados.
6. **Erros irreversíveis após gravar DOCX:** um arquivo Word salvo e um erro posterior no JSON podem deixar um documento fora do histórico. Planejar operação de reconciliação/verificação de arquivos não registrados.

**Não usar o projeto como sistema clínico de produção antes da homologação real.**

## 28/10/2026 — Reescrita espelhando o Mac (PR #6)

O proprietário relatou que a versão Windows estava "praticamente não utilizável". O núcleo e a interface foram reescritos a partir do código do Mac, mantendo os invariantes deste repositório:
- Fila: incerto interrompe o lote, uma sessão do Word por vez, nada é reenviado sem confirmação.
- Atendimento: campos do atendimento recomeçam ao trocar nome, homônimo, modelo, subpasta ou local.
- Dados: gravação atômica, backup diário, `dados.json` ilegível nunca é sobrescrito, importação não zera dados.
- Pasta sincronizada: nunca remove modelos quando a pasta está indisponível ou vazia.
- 46 testes de regressão; o CI abre todas as telas e falha se alguma der erro.
- Riscos remanescentes: impressão e conversão pelo Word real, atalhos `.lnk` e pastas de rede/OneDrive ainda sem teste no Windows físico.

## 28/10/2026 — Revisão de estabilidade

| Risco | Correção | Teste |
|---|---|---|
| `dados.json` aberto por um instante por antivírus/OneDrive fazia a gravação falhar de primeira | Até 8 tentativas (~2 s) antes de mostrar o erro | Simulado (arquivo bloqueado) |
| Falha ao gravar "enviando" no meio da fila deixava o item preso | Nada é enviado ao Word e o item volta a "aguardando" | Automatizado |
| Erros em gerar/imprimir/sincronizar sumiam sem aviso | Mostrados na tela e registrados em `erros.log` (Ajustes › Registro de erros) | — |
| Documento gerado, mas histórico não gravado | Aviso com o caminho do documento | — |
| Word ocupado (conversão de modelo) marcava a impressão como "Conferir" e parava a fila | Espera até 2 min; se não liberar, "Erro" (nada foi enviado) | — |
| Sincronização ao voltar para o app refazia a tela Gerar e tirava o cursor da caixa | Só refaz se algum modelo mudou; erro automático não abre janela (evita repetição) | — |
| Lista de Pacientes lenta com muito histórico | Locais por paciente calculados uma vez | Automatizado |
| Caminho acima de 259 caracteres (o Word não abre) | Nome do documento encurtado, mantendo modelo, paciente e data | Automatizado |


## 18/11/2026 — Alterações do Mac portadas (até 17/11/2026)

Pedido do proprietário: "porte todas as alterações que fizemos no app do Mac para o Windows". Parte já tinha sido portada em 06/11 (PR #9: favoritos, painel de atendidos, documentos temporários, parar impressões). Agora entram as demais:

- **Prontuário** (aba nova): fichas prontas de Araruna (CER II) e Alagoa Grande, importar ficha, linhas da ficha, 1.050 modelos de atendimento com lado e concordância, favoritos por local, "Meus modelos", Tab nos `___`, "Tirar os ___ não preenchidos", Limpar, Próximo paciente. As regras ficam em `Orthck.Core` (`Prontuario`, `Lacunas`, `ModelosDeAtendimento`) e têm testes.
- **Negrito** (Ctrl+B) em qualquer campo; o DOCX sai com o trecho num run em negrito com a mesma fonte.
- **Google Drive**: cópia automática do `dados.json` e envio da pasta em .zip. Fica desligado até o médico conectar; o login é pelo navegador e o app só vê os arquivos que ele mesmo cria (`drive.file`). Decisão do proprietário (o CLAUDE.md punha a sincronização em nuvem por último): risco de LGPD a avaliar, porque os dados clínicos passam a ficar também no Google Drive da conta do médico.
- **Cadastro**: número, bairro, cidade, UF, CEP e CNS.
- **Importar dados**: Juntar ou Substituir. Juntar acrescenta nos dados abertos (as telas guardam referências) e desfaz se a gravação falhar.
- **Fila**: "Limpar fila". **Atendidos recentemente**: "Limpar" e "Tirar desta lista".
- **Impressão**: antes de imprimir, confere que o Word abriu exatamente o arquivo (não um de mesmo nome de outra pasta) e que ele está à frente; senão, "Erro" sem enviar nada.
- Tabela CID-10: correção do Q72.4 (igual ao Mac).

Invariantes mantidos: A → B → A no Prontuário (trocar de paciente limpa o atendimento; só a identificação vai para o cadastro, testado); a junção nunca apaga nada; a fila continua sem reenvio automático. 91 testes de regressão.

Riscos remanescentes: Word e impressora reais; Google Drive com a conta real e com o Mac ao mesmo tempo (a mesma credencial do Google Cloud precisa estar nos Secrets dos dois repositórios); aparência da ficha impressa no Windows.
