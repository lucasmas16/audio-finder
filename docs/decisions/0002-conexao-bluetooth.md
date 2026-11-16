# Conexão de saída Bluetooth pelo driver de áudio

Uma saída render Bluetooth desconectada deve receber uma tentativa real de conexão antes de se tornar a saída padrão. `AudioPlaybackConnection` trata a direção telefone→computador, e `BluetoothSetServiceState` instala/remove serviços; essas APIs não atendem este fluxo.

Usar Core Audio `IDeviceTopology` e `IConnector.GetDeviceIdConnectedTo` para localizar exclusivamente o filtro Bluetooth conectado ao endpoint identificado. Ativar `IKsControl` nesse filtro e verificar `KSPROPERTY_TYPE_BASICSUPPORT`, com quatro bytes para os flags de acesso. Se suportar GET, solicitar `KSPROPSETID_BtAudio` / `KSPROPERTY_ONESHOT_RECONNECT` (ID 0), GET e dados NULL. O resultado somente confirma que o driver tentou conectar; aguardar a enumeração de endpoint render ativo, com identidade física e perfil comprovados, antes de selecionar.

O fluxo tem timeout de 15 segundos, cancelamento e serialização das solicitações nativas. Objetos COM nascem e são liberados no mesmo worker. Cancelamento e troca de slot impedem uma resposta tardia de alterar a saída; não cancelam fisicamente uma tentativa que o driver já recebeu. Não enviar DISCONNECT, modificar emparelhamento, alternar serviço nem instalar componentes como alternativa.

## Referências primárias

- [Semântica de KSPROPERTY_ONESHOT_RECONNECT](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect).
- [Conjunto KSPROPSETID_BtAudio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/kspropsetid-btaudio).
- [Enumeração KSPROPERTY_BTAUDIO](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ksmedia/ne-ksmedia-ksproperty_btaudio).
- [Windows SDK ksmedia.h, GUID e IDs](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/ksmedia.h).
- [Windows SDK ksproxy.h, IID e métodos IKsControl](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/ksproxy.h).
- [Windows SDK devicetopology.h, ABI de topology/connector](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/devicetopology.h).

O projeto [ToothTray](https://github.com/m2jean/ToothTray) foi usado para investigar o caminho Core Audio→filtro KS; implementação própria neste projeto, baseada nos contratos públicos e sem incorporar código externo.

## Verificação local em 2026-10-09

14 verificações determinísticas passaram. Sondagem somente leitura fora do sandbox: três endpoints Bluetooth, um ativo e dois desconectados, todos com `IKsControl` e GET de reconexão anunciado em `BASICSUPPORT` (HRESULT 0, quatro bytes). Dentro do sandbox o acesso KS foi negado (0x80070005). O probe externo removeu a restrição do sandbox; o aplicativo não solicita elevação administrativa. Nenhuma solicitação GET de reconexão foi enviada e nenhuma saída foi trocada nesse QA. Conexão física e comportamento específico de rádio/driver continuam exigindo validação manual.
