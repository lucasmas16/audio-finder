# Publicação no GitHub

Checklist para a primeira publicação e para cada release. A separação física entre `github/` (público) e a pasta irmã `privado/` continua sendo a principal proteção.

## Antes do primeiro commit

1. Inicialize o Git **somente dentro de `github/`**: `git init -b main`. Nunca na pasta acima, que contém `privado/`.
2. Confirme que o autor dos commits usa o e-mail *noreply* do GitHub: `git config user.email` deve terminar em `@users.noreply.github.com`.
3. Rode `node tools/check-privacy.cjs`. Ele precisa terminar com `"issues":0`.
4. Rode `git add .`, depois `git status` e `git status --ignored`. Confira que **não** aparecem como rastreados:
   - `artifacts/`: resultados locais de teste com nomes e IDs reais de dispositivos, cópias de `settings.xml` e executáveis;
   - `dist/`, `release/`, `obj/`, `*.exe`, `*.zip`, `*.log`, `*.bak`.
   O `.gitignore` já exclui todos eles. Mover `artifacts/` para `privado/` elimina o risco de um `git add -f` acidental.
5. Revise o diff completo (`git diff --cached`) antes de `git commit`.

## Criar o repositório

1. Crie o repositório como **privado**, envie `main` e confira os arquivos renderizados no site. Só então torne-o público.
2. Em **Settings**, configure:
   - **Security**: ativar *Private vulnerability reporting*, *Dependabot alerts*, *Dependabot security updates*, *Secret scanning* e *Push protection*.
   - **Actions › General**: *Workflow permissions* = "Read repository contents"; exigir aprovação de workflows de colaboradores externos (*first-time contributors*).
   - **Rules**: ruleset para `main`, exigindo pull request e o check `CI / build`, e bloqueando force push; ruleset de tags `v*` permitindo criação só pelo mantenedor, porque uma tag dispara a release.
3. Substitua `<dono>/<repositório>` no README pelo caminho real.

## Cada release

1. Atualize `VERSION` (`MAJOR.MINOR.PATCH`) e faça o commit em `main`.
2. Crie e envie a tag: `git tag v1.0.0` e `git push origin v1.0.0`.
3. A pipeline `Release` falha se a tag não corresponder a `VERSION`. Se passar, cria um **rascunho** com o instalador, o ZIP portátil, `SHA256SUMS.txt` e as notas geradas.
4. Revise o rascunho e publique.

## Limitações conhecidas

- **Sem assinatura de código**: o SmartScreen mostra "Editor desconhecido". Opções futuras: SignPath Foundation (gratuita para código aberto) ou Azure Trusted Signing (paga). A atestação de origem e os hashes permitem conferir os arquivos.
- **Ícones**: são ilustrações genéricas sem logotipos, mas alguns lembram produtos reais, e os nomes internos dos arquivos (por exemplo `airpods.png`) ficaram iguais para não quebrar configurações salvas. Se quiser eliminar esse risco residual, troque por desenhos mais genéricos e renomeie as chaves com migração em `Settings.Normalize`.
- **Testes de interface**: a suíte completa (`build.ps1 -RunTests`) precisa de dispositivos de áudio reais e de sessão interativa, por isso roda só localmente. A pipeline roda os testes determinísticos e o teste do instalador.
- **Histórico**: comece o repositório sem o histórico anterior; o material antigo fica em `privado/`.
