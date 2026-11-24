# Deblowin

Aplicativo leve para Windows 10/11 que aplica scripts de debloat e ajustes: remove bloatware, desativa telemetria e aplica ajustes de privacidade e desempenho.

- **Leve:** cerca de 260 KB, sem dependências (usa o .NET Framework 4.8 que já vem no Windows).
- **Fácil de usar:** categorias na barra lateral, interruptores com descrição, busca (`Ctrl+F`), presets (Básico, Rigoroso e Extremo) e uma tela de revisão antes de executar.
- **Instalar apps:** aba com 38 apps comuns (navegadores, mensagens, utilitários, desenvolvimento, jogos), instalados pelo `winget`. Só IDs do repositório winget são aceitos.
- **Configurável:** idioma (PT/EN), tema (sistema, claro ou escuro), ponto de restauração automático, confirmação antes de executar, execução na própria janela (com log e barra de progresso) ou no console do PowerShell, ação ao terminar (perguntar se reinicia, reiniciar o Explorer ou não fazer nada), logs salvos, comandos exibidos ao passar o mouse e importar/exportar a seleção.

## Instalação

Baixe na página de [Releases](https://github.com/allangsilva97/Deblowin/releases):

- **`Deblowin-Setup-<versão>.exe`:** instalador. Coloca o app em `C:\Program Files\Deblowin`, cria atalhos no Menu Iniciar (e, se você quiser, na área de trabalho) e registra o desinstalador em *Configurações › Aplicativos › Aplicativos instalados*.
- **`Deblowin.exe`:** versão portátil. Basta abrir, sem instalar.

O Windows SmartScreen pode avisar que o app não é reconhecido, porque o executável não tem assinatura digital. Para continuar, clique em **Mais informações** e depois em **Executar assim mesmo**.

O instalador também aceita linha de comando:

```powershell
Deblowin-Setup-1.0.0.exe /S                      # instalação silenciosa
Deblowin-Setup-1.0.0.exe /S /D=D:\Apps\Deblowin  # instalação silenciosa em outra pasta
"C:\Program Files\Deblowin\uninstall.exe" /uninstall /S   # desinstalação silenciosa
```

## Compilar

```powershell
.\build.ps1
```

Gera `dist\Deblowin.exe` (portátil) e `dist\Deblowin-Setup-<versão>.exe` (instalador). Os dois pedem permissão de administrador. Com `-AsInvoker`, os executáveis abrem sem o prompt do UAC: serve para testar a interface, mas os ajustes precisam de administrador.

## Linha de comando do app

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

## Licença

GPL-3.0. Veja [LICENSE](LICENSE) e [NOTICE](NOTICE).

## Aviso

Alguns ajustes não podem ser desfeitos automaticamente (por exemplo, remover apps, o Edge ou o OneDrive). Deixe o ponto de restauração ligado e use por sua conta e risco.
