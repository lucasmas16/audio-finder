# ADR 0003 — Instalador próprio e releases pelo GitHub Actions

Status: adotada.

## Contexto

O aplicativo era distribuído como a pasta `dist`. Uma versão instalável precisa de atalho, desinstalação e atualização sem perder configurações. O projeto compila só com o `csc` do .NET Framework, sem NuGet nem ferramentas externas.

## Decisão

Instalador próprio em C# (`installer/Setup.cs`), compilado pelo mesmo `build.ps1`, com o aplicativo embutido como recurso ZIP:

- instala por usuário em `%LOCALAPPDATA%\Programs\AudioFinder`, com manifesto `asInvoker`, sem pedir administrador;
- extrai numa pasta temporária ao lado e troca a pasta inteira, com rollback se a troca falhar;
- grava o marcador `AudioFinder.install` e só substitui ou apaga pastas que o tenham, recusando pastas com outros arquivos;
- valida cada entrada do ZIP contra *path traversal*;
- registra a desinstalação em `HKCU` e cria o atalho via `IShellLinkW`, sem depender do Windows Script Host;
- na desinstalação, remove o valor de inicialização com o Windows apenas se ele apontar para a pasta instalada, e preserva `%LOCALAPPDATA%\AudioFinder`, salvo se o usuário pedir.

A versão vem do arquivo `VERSION`. Releases saem de tags `vX.Y.Z` pela pipeline `release.yml`, que confere a tag, roda a verificação de privacidade, os testes determinísticos e o teste do instalador, gera a atestação de origem e cria um rascunho de release.

## Alternativas

Inno Setup e WiX são maduros, mas acrescentam uma ferramenta externa ao build local e à pipeline. O ZIP portátil sozinho não oferece atalho nem desinstalação; continua publicado como opção.

## Consequências

O código do instalador é nosso para manter e auditar, por isso tem teste de fumaça (`tests/installer-smoke.ps1`) que cobre instalação, reinstalação, recusas de segurança e desinstalação. Sem assinatura de código, o SmartScreen avisa sobre editor desconhecido; a atestação e os hashes permitem conferir a origem.
