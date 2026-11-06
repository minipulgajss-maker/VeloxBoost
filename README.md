# VeloxBoost

Otimizador automático para Windows 10/11 — 381 otimizações reversíveis em 11 categorias (Jogos & FPS, Rede & Ping, Privacidade, Desempenho, Interface, Anúncios, Energia, Serviços, Tarefas, Apps, Limpeza), Game Booster com prioridade automática, teste de DNS, pontos de restauro, 4 línguas e atualizações automáticas como o Discord.

## Instalar
Transfere o `VeloxBoost-win-Setup.exe` da última [release](https://github.com/minipulgajss-maker/VeloxBoost/releases/latest).

## Publicar uma nova versão
```
git tag v1.0.1
git push origin v1.0.1
```
O GitHub Actions compila, cria o instalador e publica a release. Todas as apps instaladas transferem a atualização em segundo plano e instalam-na ao reiniciar.

## Segurança
Cada alteração guarda o valor original (`%LOCALAPPDATA%\VeloxBoostData\backup.json`) e pode ser revertida. Defender, UAC, Firewall, SmartScreen e Windows Update nunca são desligados.
