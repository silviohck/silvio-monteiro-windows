# Orthck — Windows

Portabilidade independente do [Orthck para macOS](https://github.com/silviohck/Silvio-Monteiro),
priorizando **previsibilidade, estabilidade e paridade funcional**.

> **Estado atual:** primeira implementação funcional / versão de desenvolvimento. **Não certificada para uso clínico real** até passar pela compilação do Windows, testes de impressão em Word e homologação com modelos reais. Algumas funcionalidades têm paridade inicial, outras ainda dependem de conclusão; veja [Matriz de paridade](docs/PARIDADE.md).

## O que é

Versão Windows do Orthck, espelhando o app do Mac tela por tela: Gerar documento, Fila de impressão, Pacientes, Locais de atendimento, Modelos (com pasta sincronizada), Histórico e as tabelas CID-10, SIGTAP e TUSS. Veja a [matriz de paridade](docs/PARIDADE.md).

## Tecnologias

- **C# / .NET 10 / WPF** com o tema Fluent do Windows 11 e a paleta "Sereno" do Mac (claro e escuro).
- `src/Orthck.Core`: dados (`dados.json` compatível com o Mac), modelos, sincronização da pasta, campos automáticos, preenchimento DOCX, fila e tabelas embutidas.
- `src/Orthck.Windows`: interface e Microsoft Word por COM (imprimir e converter .doc/.rtf/.odt).
- `tests/Orthck.SmokeTests`: testes de regressão sem Word.

## Baixar e testar

Cada atualização da `main` publica uma versão de teste em **[Releases](https://github.com/silviohck/silvio-monteiro-windows/releases/latest)** como um arquivo único, **Orthck.exe**: baixe e abra com dois cliques, sem instalar nada (o .NET vai dentro). Funciona em PCs com Windows e no Windows 11 de Macs com chip Apple (Parallels, VMware Fusion ou UTM).

Link direto da versão mais recente: https://github.com/silviohck/silvio-monteiro-windows/releases/latest/download/Orthck.exe

No primeiro uso o Windows extrai algumas bibliotecas internas para uma pasta temporária, então a primeira abertura demora alguns segundos a mais. Como o app ainda não tem assinatura digital, o Windows pode mostrar "O Windows protegeu o computador": clique em **Mais informações → Executar assim mesmo**. Para testar a fila sem papel, escolha a impressora **Microsoft Print to PDF**.

## Compilar no Windows

Instale Windows 11, Visual Studio com suporte a WPF e SDK do .NET 10; para impressão direta, tenha o **Microsoft Word desktop** instalado.

```powershell
dotnet build src/Orthck.Windows/Orthck.Windows.csproj -c Release
dotnet run --project src/Orthck.Windows/Orthck.Windows.csproj
```

Testes:

```powershell
dotnet run --project tests/Orthck.SmokeTests/Orthck.SmokeTests.csproj
```

O workflow **Windows — compilar e testar Orthck** também publica uma compilação `Orthck-Windows-x64` nos artifacts do GitHub Actions quando o projeto for publicado.

## Primeiro uso

1. **Modelos › Sincronizar com uma pasta…**: escolha a pasta principal dos modelos (uma subpasta por local e, dentro dela, uma por tipo, como no Mac). Na ⚙️, cada subpasta pode ser **Local**, **Todos os locais** (a `1 - APAC` já vem assim) ou **Ignorar**. Arquivos novos viram modelos, editados são atualizados e apagados saem do app, sempre que você volta para o Orthck. Também dá para importar modelos avulsos (ou arrastar `.docx` para a janela).
2. **Locais de atendimento**: confira os locais criados pela pasta e, se quiser, uma pasta própria para os documentos de cada um.
3. **Gerar documento**: escolha local, subpasta e modelo, digite o nome do paciente (o app sugere os cadastrados), confira os campos e clique em **Gerar documento**. Os documentos ficam em `Documentos\Orthck\<Local>\<Tipo>` (ou na pasta principal escolhida). **Próximo paciente** (Ctrl+Shift+N) recomeça mantendo só o local.
4. **Fila de impressão**: ao fim dos atendimentos, **Imprimir todos** manda um documento por vez para o Word. **Enviado ao Word não garante que o papel saiu**; um envio incerto para a fila e só é reenviado com sua confirmação.
5. Vindo do Mac? **Ajustes › Importar dados do Mac…** com a pasta de dados do Mac copiada (dados.json + pasta Modelos).

### Cadastro e privacidade

Os dados ficam **somente neste computador**, em `%LOCALAPPDATA%\Orthck` (`dados.json`, a pasta `Modelos` e os backups diários em `Backups`). Nunca envie esse conteúdo, documentos de pacientes ou logs com informações reais para um repositório público. Antes de atender pacientes, use uma conta do Windows com senha, disco criptografado (BitLocker) e um backup fora do computador.

## Tabelas CID-10 / SIGTAP / TUSS

Vêm embutidas no app (as mesmas do Mac) e funcionam sem internet. Para atualizar depois que o projeto Mac atualizar as dele:

```powershell
python tools/gerar_tabelas.py C:\caminho\Silvio-Monteiro
```

## Princípios de estabilidade

1. Não apagar modelo original quando falha leitura de pasta.
2. Nunca sobrescrever um DOCX anterior; arquivos consecutivos recebem nomes distintos.
3. CID, conduta e outros campos clínicos não são reaproveitados automaticamente entre pacientes.
4. A fila é serial e persistente: não reimprime em caso de erro ou encerramento inesperado sem confirmação explícita.
5. Histórico e DOCX são preservados quando um item é removido da fila.
6. O projeto macOS **não é modificado** pela portabilidade.

## Compilar fora do Windows

O núcleo e os testes rodam em qualquer sistema com o SDK do .NET 10. O app WPF também compila (sem executar) no Linux/macOS com:

```bash
dotnet build Orthck.Windows.sln -c Release -p:EnableWindowsTargeting=true
```
