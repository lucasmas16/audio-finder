# Segurança

## Como relatar uma vulnerabilidade

Use **Security › Report a vulnerability** neste repositório (relato privado do GitHub). Não abra issue pública para vulnerabilidades. Descreva o impacto e os passos para reproduzir com dados sintéticos; não inclua configurações reais, IDs de dispositivos, credenciais ou capturas pessoais.

## Superfície do aplicativo

- Roda sem privilégios de administrador (`asInvoker`), tanto o aplicativo quanto o instalador.
- Não acessa a rede e não envia dados. O log de diagnóstico só existe com `--diagnostics` e não registra nomes nem IDs de dispositivos.
- Grava apenas no perfil do usuário: `%LOCALAPPDATA%\AudioFinder` (configurações, imagens próprias e log opcional), `%LOCALAPPDATA%\Programs\AudioFinder` (instalação) e chaves em `HKCU` (inicialização com o Windows e entrada de desinstalação).
- O instalador só substitui ou apaga pastas que tenham o marcador `AudioFinder.install`, valida os caminhos do pacote antes de extrair e troca a pasta inteira de uma vez, para não deixar instalação pela metade.

## Releases

Os pacotes são gerados apenas pela pipeline do GitHub Actions a partir de uma tag, com atestação de origem (`gh attestation verify`) e `SHA256SUMS.txt`. As actions usadas são fixadas por SHA e atualizadas pelo Dependabot. Os executáveis ainda não têm assinatura digital de código.
