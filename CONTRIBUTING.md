# Como contribuir

Leia o README e a arquitetura antes de alterar o projeto. Abra uma mudança pequena, descrevendo o problema e o comportamento esperado. Use os comandos de verificação indicados no README.

- Reproduza falhas com dados inventados e perfis descartáveis.
- Não anexe documentos de uso, tokens, sessões, cofres ou capturas pessoais.
- Atualize documentação quando mudar comandos, persistência ou arquitetura.
- Preserve os manifestos de dependências e seus lockfiles.
- Execute `node tools/check-privacy.cjs` e revise o diff.
- Mudanças no instalador: rode `build.ps1 -Release` e `tests/installer-smoke.ps1`.

Contribuições são aceitas sob a [licença MIT](LICENSE). Vulnerabilidades seguem o [SECURITY.md](SECURITY.md).
