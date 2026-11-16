# Arquitetura — Audio Finder

## Contexto e decisão

Aplicação desktop monolítica pequena, orientada a eventos. A interface chama serviços nativos de áudio e atalhos; os modelos concentram o estado persistente. Essa separação acompanha as responsabilidades reais sem introduzir serviços ou camadas artificiais.

O .NET Framework 4.8 foi mantido porque já corresponde ao código e ao processo de distribuição. Os wrappers das APIs nativas ficam isolados para facilitar adaptação a mudanças do Windows.

## Fluxo principal

```text
Interface / bandeja → modelos e atalhos → Core Audio / Windows
Configurações ↔ XML local
```

## Mapa do código

- `src/Program.cs`: inicialização e ciclo de vida.
- `src/Audio.cs`: enumeração, notificações Core Audio e troca de saída.
- `src/Hotkeys.cs`: atalhos nativos.
- `src/Models.cs`: preferências e persistência.
- `src/Views.cs` e `src/Appearance.cs`: interface e tema.
- `installer/`: instalador por usuário ([ADR 0003](decisions/0003-instalador-e-releases.md)).
- `tests/`: suíte existente; `build.ps1`: compilação e pacotes; `.github/workflows/`: CI e releases.

## Estado e arquivos

Preferências em `%LOCALAPPDATA%/AudioFinder/settings.xml` e backup ao lado. Não incluir esses arquivos, IDs de dispositivos ou capturas de configurações reais no repositório.

## Fronteiras de manutenção

Mudanças de interface devem preservar os contratos da camada de dados e das integrações nativas. Novos formatos externos pertencem aos adaptadores; dados reais nunca pertencem às fixtures. Alterações estruturais futuras devem registrar contexto, alternativas, decisão e consequência em `docs/decisions/`.

## Evolução planejada

- Separar testes de lógica dos testes que interagem com hardware.
- Avaliar versões futuras do .NET somente com medição de compatibilidade e consumo.

Não há proposta de microserviços: o tamanho atual e a execução local favorecem um único aplicativo com responsabilidades delimitadas.
