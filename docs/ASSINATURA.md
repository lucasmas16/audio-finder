# Assinatura de código

Os pacotes saem sem assinatura enquanto nenhum provedor estiver configurado. O build e a pipeline já têm os pontos de assinatura. A ordem importa:

1. `AudioFinder.exe` é assinado **antes** de entrar no instalador e no ZIP portátil.
2. `AudioFinder-Setup.exe` é assinado depois de compilado.
3. `SHA256SUMS.txt` e a atestação de origem são gerados **depois** da assinatura, porque assinar muda o hash.

O modo é escolhido pela variável de repositório `SIGNING_MODE` em **Settings › Secrets and variables › Actions › Variables**:

| `SIGNING_MODE` | Quem assina | Atestação de origem |
| --- | --- | --- |
| (não definida) | ninguém: pacotes sem assinatura | sim |
| `signpath` | SignPath, dentro da pipeline | sim, dos arquivos assinados |
| `local` | o mantenedor, com `tools/sign-release.ps1` | não, porque os arquivos finais são gerados fora da pipeline |

Uma assinatura nova não remove o aviso do SmartScreen na hora. A reputação se acumula no certificado conforme os downloads; até lá o aviso mostra o editor identificado.

## Opção 1 — SignPath Foundation (gratuita para open source)

Requisitos do programa: licença aprovada pela OSI (MIT), código público, binários gerados pela pipeline, autenticação em dois fatores e aprovação manual de cada assinatura. A aprovação não é garantida, e o editor exibido é "SignPath Foundation".

**Situação:** o primeiro pedido foi recusado em 2026-11-16 por falta de sinais públicos de adoção (estrelas, forks, contribuidores, artigos ou discussões externas). Dá para pedir de novo quando o projeto tiver mais visibilidade. Só publique a frase de atribuição abaixo depois de aprovado.

1. Peça em [signpath.org](https://signpath.org/), informando este repositório.
2. Depois de aprovado, configure no SignPath o projeto, a política de assinatura e duas configurações de artefato:
   - `app`:
     ```xml
     <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
       <zip-file>
         <pe-file path="AudioFinder.exe"><authenticode-sign /></pe-file>
       </zip-file>
     </artifact-configuration>
     ```
   - `setup`: igual, com `path="AudioFinder-Setup.exe"`. Ajuste conforme o editor do SignPath validar.
3. No GitHub, cadastre o secret `SIGNPATH_API_TOKEN` e as variáveis `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG` e `SIGNING_MODE=signpath`.
4. Acrescente à seção "Code signing policy" do README (e cite-a nas notas de cada release) a atribuição exigida pelo programa:

   > Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

A cada tag, a pipeline envia o aplicativo, espera a sua aprovação no SignPath, empacota com o executável assinado, envia o instalador e recalcula os checksums. Por fim, `build.ps1 -ChecksumsOnly -RequireSigned` falha se algo não estiver assinado.

## Opção 2 — Certum Open Source (local)

Certificado para pessoa física e software open source, com chave na nuvem SimplySign ou em cartão. **O seu nome civil aparece como editor em todas as assinaturas.**

1. Compre e ative o certificado. Instale o SimplySign Desktop e conecte-se; o certificado passa a aparecer no repositório do usuário.
2. Descubra a impressão digital (thumbprint):
   ```powershell
   Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object Subject, Thumbprint, NotAfter
   ```
3. Defina `SIGNING_MODE=local` no GitHub.
4. A cada versão:
   ```powershell
   git tag -a v1.0.1 -m "Audio Finder 1.0.1"; git push origin v1.0.1   # a pipeline testa e cria o rascunho sem arquivos
   git checkout v1.0.1
   $env:AUDIOFINDER_SIGN_THUMBPRINT = '<thumbprint>'
   .\tools\sign-release.ps1 -Tag v1.0.1                                  # compila, assina, testa e anexa ao rascunho
   git checkout main
   ```
   O script recusa checkouts com alterações, commits diferentes da tag e tags sem rascunho. Feche o Audio Finder antes, porque o build grava em `dist`.

O carimbo de tempo padrão é o da Certum (`http://time.certum.pl`); outro pode ser definido em `AUDIOFINDER_SIGN_TIMESTAMP`.

## Opção 3 — Azure Artifact Signing

Hoje, para pessoa física, só está disponível nos EUA e no Canadá. Se ficar disponível, use `tools/sign.ps1` com `AUDIOFINDER_SIGN_ARGS="/dlib <Azure.CodeSigning.Dlib.dll> /dmdf <metadata.json>"`.

## Conferir uma assinatura

```powershell
Get-AuthenticodeSignature .\AudioFinder-Setup.exe | Format-List Status, SignerCertificate, TimeStamperCertificate
```

## Segurança

- Chaves e certificados nunca entram no repositório; `.gitignore` e `tools/check-privacy.cjs` bloqueiam `*.pfx`, `*.p12`, `*.key` e `*.pem`.
- O token do SignPath fica só em *Actions secrets*, usado pelo job de release, que roda apenas em tags `v*` protegidas por ruleset.
- Cada assinatura no SignPath exige aprovação manual, então um commit indevido não é assinado sem você ver.
