# Audio Finder

Aplicativo de bandeja para Windows que alterna a saída de áudio padrão por atalhos globais, com até oito posições e histórico de dispositivos Bluetooth.

Projeto pessoal em desenvolvimento. Esta pasta contém o código destinado a um repositório independente.

## Funcionalidades

- Cadastro de saídas de áudio e atalhos globais.
- Persistência de configurações, histórico Bluetooth e recuperação por backup.
- Tema claro/escuro, notificações e início opcional com o Windows.
- Barra horizontal de troca com posições dos atalhos, imagem por dispositivo e destaque ampliado da saída selecionada.
- Ao ativar uma saída Bluetooth desconectada, tenta conectar pelo driver de áudio existente, mostra progresso com o ícone cadastrado e aguarda até 15 segundos pela saída ativa. Falhas usam uma composição iconográfica do computador e do dispositivo.
- Nove modelos de imagem (Fone In-Ear, Fone Earbud, Monitor de Áudio, Fone Premium, Fone Gamer, Fone Bluetooth OverEar, Monitor/Tv, Fone Esportivo e Fone Esportivo de Condução Óssea), além do ícone padrão e PNG próprio no editor.

## Tecnologias

C#, Windows Forms, .NET Framework 4.8 e APIs nativas do Windows.

## Instalar

Baixe `AudioFinder-Setup-<versão>.exe` na página de Releases e execute. A instalação é só para o usuário atual, em `%LOCALAPPDATA%\Programs\AudioFinder`, sem pedir administrador, com atalho no Menu Iniciar e desinstalação em **Configurações › Aplicativos**. Atualizar mantém configurações e atalhos. Também há um `.zip` portátil.

Os executáveis ainda não têm assinatura digital, então o Windows SmartScreen pode avisar "Editor desconhecido". O build já está preparado para assinar ([assinatura de código](docs/ASSINATURA.md)). Para conferir que o arquivo veio da pipeline deste repositório:

```powershell
gh attestation verify .\AudioFinder-Setup-1.0.0.exe --repo lucasmas16/audio-finder
Get-FileHash .\AudioFinder-Setup-1.0.0.exe -Algorithm SHA256   # compare com SHA256SUMS.txt
```

Opções do instalador: `/silent`, `/desktop`, `/nolaunch`, `/dir=<pasta>`. Desinstalação silenciosa: `Uninstall.exe /uninstall /silent` (acrescente `/removedata` para apagar também `%LOCALAPPDATA%\AudioFinder`). Erros no modo silencioso vão para `%TEMP%\AudioFinderSetup.log`.

## Executar a partir do código

Requer Windows 10/11 x64 e .NET Framework 4.8. O script usa o compilador instalado com o Framework; não depende de NuGet.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\dist\AudioFinder.exe
```

O projeto também pode ser aberto pelo `AudioFinder.csproj` no Visual Studio com o targeting pack 4.8.

## Gerar uma versão

`powershell -ExecutionPolicy Bypass -File .\build.ps1 -Release` cria em `release/` o instalador, o `.zip` portátil e `SHA256SUMS.txt`, usando a versão do arquivo `VERSION`. O pacote contém só uma lista fixa de arquivos: executável, config, licença e ícones. `.\tests\installer-smoke.ps1` instala numa pasta temporária, reinstala, testa as recusas de segurança e desinstala.

Para publicar, atualize `VERSION`, faça o commit e envie a tag correspondente (por exemplo `git tag v1.0.0` e `git push origin v1.0.0`). A pipeline [release](.github/workflows/release.yml) compila, roda a verificação de privacidade e os testes determinísticos, testa o instalador, gera a atestação de origem e cria um **rascunho** de release para revisão. Detalhes em [publicação](docs/PUBLICACAO.md). `-SignScript tools\sign.ps1` assina o aplicativo e o instalador durante o build, e `-RequireSigned` falha se algo sair sem assinatura.

## Verificar

`powershell -ExecutionPolicy Bypass -File .\build.ps1 -RunTests`. A suíte existente interage com APIs de áudio e pode trocar temporariamente a saída padrão; execute-a fora de chamadas ou gravações. A compilação simples não executa esses testes. Use `-RunTests -SafeTests` para verificar interface, persistência e atalhos sem trocar a saída de áudio. Os atalhos nativos exigem acesso à sessão interativa do Windows.

Antes de preparar um commit: `node tools/check-privacy.cjs` (Node.js 24).

Testes de identidade sem trocar áudio nem registrar atalhos: `powershell -ExecutionPolicy Bypass -File .\tests\device-identity.ps1`.

Testes determinísticos de conexão, identidade, cancelamento e timeout, sem conectar hardware: `powershell -ExecutionPolicy Bypass -File .\tests\bluetooth-connection.ps1`. A opção `-Probe` acrescenta uma leitura de capacidade do driver (`BASICSUPPORT`), sem enviar a solicitação de reconexão. A suíte de interface injeta uma falha controlada para dispositivos Bluetooth fictícios. A integração entre controlador e avisos tem teste com backend falso para progresso, outro slot, conclusão atrasada, cancelamento, timeout e notificações desligadas: `powershell -ExecutionPolicy Bypass -File .\tests\connection-ui-flow.ps1`.

O cadastro persiste a identidade da interface física e o container do Windows. Se um endpoint for recriado, a associação e o atalho são atualizados somente quando há identidade comprovada, mesmo perfil de áudio e destino único. Nome igual sozinho não identifica hardware; registros antigos sem identidade ou destinos ambíguos permanecem preservados. Identidade é coletada ao reencontrar o endpoint original ou cadastrar uma saída disponível.

## Organização

- `src/Program.cs`: inicialização e ciclo de vida.
- `src/Audio.cs`: enumeração, notificações Core Audio e troca de saída.
- `src/Hotkeys.cs`: atalhos nativos.
- `src/Models.cs`: preferências e persistência.
- `src/Views.cs` e `src/Appearance.cs`: interface e tema.
- `installer/`: instalador por usuário, sem dependências externas.
- `tests/`: suíte existente; `build.ps1`: compilação e pacotes; `.github/workflows/`: CI e releases.

Detalhes: [arquitetura](docs/ARQUITETURA.md), [decisão inicial](docs/decisions/0001-organizacao.md), [privacidade](docs/PRIVACIDADE.md) e [contribuição](CONTRIBUTING.md).

## Limitações

Para diagnóstico local, inicie com `--diagnostics` (encerre a instância anterior primeiro). O arquivo `%LOCALAPPDATA%/AudioFinder/connection-diagnostic.log` registra estágios de conexão, posição do atalho, estado da janela e horários, limitado a aproximadamente 256 KiB. Não registra nomes nem IDs dos dispositivos e não envia dados. Sem a opção, esse registro permanece desligado. A suíte `connection-ui-flow.ps1` também verifica os pixels dos resultados no desktop e o loop real da bandeja com eventos concorrentes; precisa de uma sessão interativa.

A mudança afeta as saídas padrão de reprodução; aplicativos com dispositivo fixado podem ignorá-la. A interface interna `IPolicyConfig` não tem contrato público estável. Validações de hardware, Bluetooth e teclas físicas continuam manuais.

A conexão usa a propriedade pública [KSPROPERTY_ONESHOT_RECONNECT](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect), via Core Audio topology e `IKsControl`. O driver precisa anunciar suporte; aceitar a solicitação não prova conexão, por isso o aplicativo só troca a saída depois de confirmar um endpoint ativo. Não instala serviços nem reinicia drivers. Saídas sem suporte mostram uma falha e orientam conectar pelas configurações do Windows. Cancelar ou escolher outro slot interrompe a espera e impede a troca posterior; uma solicitação já entregue ao driver pode ainda conectar fisicamente o dispositivo. O aplicativo não envia desconexão para desfazer essa solicitação.

## Próximos passos

- Separar testes de lógica dos testes que interagem com hardware.
- Avaliar versões futuras do .NET somente com medição de compatibilidade e consumo.

## Licença

[MIT](LICENSE). Os ícones de `assets/devices` são ilustrações genéricas de categorias de dispositivos, sem vínculo com fabricantes.

## Referências de organização

- [Referência para este tipo de aplicação](https://github.com/dotnet/samples/tree/main/windowsforms).
- [Modelos de exclusão do GitHub](https://github.com/github/gitignore).

As referências orientaram a organização; a estrutura descrita corresponde ao código existente.

Imagens próprias são copiadas para `%LOCALAPPDATA%/AudioFinder/device-images`; mover o PNG original não altera a imagem salva. Arquivo ausente ou inválido usa o modelo escolhido e, se necessário, o desenho padrão. Para distribuir, mantenha `assets/devices` junto ao executável; ambos os fluxos de compilação copiam esses assets.
