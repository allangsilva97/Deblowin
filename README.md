# Deblowin

Aplicativo leve para Windows 10/11 que aplica scripts de debloat e ajustes: remove bloatware, desativa telemetria e aplica ajustes de privacidade e desempenho.

- **Leve:** um único `.exe` de ~240 KB, sem instalador e sem dependências (usa o .NET Framework 4.8 que já vem no Windows).
- **Fácil de usar:** categorias na barra lateral, interruptores com descrição, busca (`Ctrl+F`), presets (Básico, Rigoroso e Extremo) e uma tela de revisão antes de executar.
- **Configurável:** idioma (PT/EN), tema (sistema, claro ou escuro), ponto de restauração automático, confirmação antes de executar, execução na própria janela (com log e barra de progresso) ou no console do PowerShell, ação ao terminar (perguntar se reinicia, reiniciar o Explorer ou não fazer nada), logs salvos, comandos exibidos ao passar o mouse e importar/exportar a seleção.

## Origem dos scripts

O catálogo de ajustes (209 itens em 7 categorias, com os rótulos em PT/EN) é extraído do [WinScript](https://github.com/flick9000/winscript), de flick9000. O WinScript é GPL-3.0, por isso o Deblowin também é distribuído sob a [GPL-3.0](LICENSE).

O "Run MAS" (ativador de terceiros) e o instalador de apps do WinScript ficaram de fora de propósito.

## Compilar

```powershell
.\build.ps1
```

Gera `dist\Deblowin.exe`, que pede permissão de administrador ao abrir. `.\build.ps1 -AsInvoker` gera `dist\Deblowin-user.exe`, que abre sem o prompt do UAC (serve para testar a interface; os ajustes precisam de administrador).

### Atualizar o catálogo a partir do WinScript

Precisa do Node.js:

```powershell
git clone --depth 1 https://github.com/flick9000/winscript.git ..\winscript
.\build.ps1 -UpdateCatalog ..\winscript
```

O `tools/extract-catalog.mjs` lê `app/src/assets/js/scripts.ts` (os comandos e os presets), `app/src/components/MainPage.astro` (a estrutura de categorias e grupos) e `app/src/i18n/locales/{en,pt}.json` (os rótulos), e grava `data/catalog.json`, que fica embutido no `.exe`.

## Linha de comando

```powershell
Deblowin.exe --export-script C:\caminho\script.ps1 --preset basic   # basic | strict | extreme | all
```

Sem `--preset`, o script usa a seleção salva.

## Arquivos

| Caminho | Conteúdo |
|---|---|
| `%LOCALAPPDATA%\Deblowin\settings.json` | Configurações e a última seleção |
| `%LOCALAPPDATA%\Deblowin\runs\` | Scripts gerados em cada execução |
| `%LOCALAPPDATA%\Deblowin\logs\` | Logs das execuções |

## Aviso

Alguns ajustes não podem ser desfeitos automaticamente (por exemplo, remover apps, o Edge ou o OneDrive). Deixe o ponto de restauração ligado e use por sua conta e risco.
