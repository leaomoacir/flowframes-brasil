# Como compilar e publicar uma atualização

Notas internas do projeto (fora do README, que é voltado para quem usa o programa).

## Compilar e gerar o instalador

Requer .NET SDK (ou Visual Studio) e o [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

```powershell
powershell -ExecutionPolicy Bypass -File Build\build.ps1
# saída: Build\out\FlowFramesBrasil-Setup-<versão>.exe
```

Use `-SkipInstaller` para só compilar e montar a pasta `Build\stage`.

## Publicar uma atualização

1. Atualize `version.txt` e `changelog.txt` e faça o commit.
2. Crie e envie a tag: `git tag v1.1.0 && git push origin v1.1.0`.
3. O GitHub Actions (`.github/workflows/release.yml`) compila, gera o instalador e cria a Release.
   Quem já tem o programa instalado recebe o aviso de atualização automaticamente.

> A tag precisa ser igual à versão (`v1.1.0` ↔ `1.1.0`): o app compara o número da tag com o `ver.ini` instalado.

## Trocar o repositório de atualizações

Altere `RepoOwner` e `RepoName` em `CodeLegacy/Data/Branding.cs` e o endereço em `Installer/FlowFramesBrasil.iss`.

## Assinatura digital (SmartScreen)

O instalador ainda não é assinado. A opção gratuita para código aberto é o SignPath Foundation (signpath.org/foundation).
