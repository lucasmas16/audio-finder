# Privacidade no desenvolvimento

Preferências em `%LOCALAPPDATA%/AudioFinder/settings.xml` e backup ao lado. Não incluir esses arquivos, IDs de dispositivos ou capturas de configurações reais no repositório.

## O que pode entrar

Código, migrações de esquema, configuração sem segredos, documentação genérica, ícones revisados e fixtures sintéticas.

## O que fica fora

Faturas, planilhas de uso, documentos, fotos pessoais, prontuários, cofres, bancos preenchidos, cookies, sessões, logs, capturas com dados reais, prompts e históricos de assistentes. Guarde-os na pasta irmã privada, nunca em `public/` ou assets da aplicação.

## Verificação

Execute `node tools/check-privacy.cjs`. A verificação mostra caminho, linha e categoria, sem imprimir o valor suspeito. Binários e exceções de fixtures exigem hash exato no catálogo `tools/privacy-reviewed.json`; alterá-los exige nova revisão. O catálogo não deve ser atualizado apenas para silenciar um alerta.

A verificação é heurística: não identifica todos os nomes ou dados possíveis e não inspeciona um histórico Git antigo. Revise o diff e os arquivos efetivamente selecionados antes de publicar. `.gitignore` não remove arquivos já rastreados. Não restaure o histórico privado dentro deste diretório.

Os arquivos de teste numéricos convencionais são exemplos sem associação a pessoas; alterações futuras devem manter essa condição.
