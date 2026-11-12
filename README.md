# Flow Frames Brasil - Interpolação de vídeo para Windows

Programa gratuito e de código aberto para **aumentar a taxa de quadros (FPS) de vídeos com inteligência artificial** - deixe vídeos de 24/30 FPS em 60 FPS ou mais, ou crie câmera lenta suave. Suporta RIFE (PyTorch e NCNN), DAIN (NCNN) e FLAVR (PyTorch).

> **Flow Frames Brasil** é uma versão em português, com instalador e atualização automática, baseada no
> [Flowframes](https://github.com/n00mkrad/flowframes) criado por **N00MKRAD** (licença GPLv3).

## Instalação

1. Baixe o instalador mais recente em **[Releases](../../releases/latest)** (`FlowFramesBrasil-Setup-x.y.z.exe`).
2. Execute e siga o assistente (não precisa de administrador).
3. Abra o **Flow Frames Brasil** pelo menu Iniciar.

> O Windows pode exibir o aviso do SmartScreen por o instalador ainda não ser assinado digitalmente.
> Clique em **Mais informações → Executar assim mesmo**.

### Atualizações

O programa verifica sozinho se há uma versão nova ao abrir. Quando houver, aparece um aviso e você atualiza com um clique
(também dá para conferir em **Atualizar** na tela principal). O instalador fecha o programa, atualiza e abre novamente.

## Requisitos

**Mínimo:** Windows 10/11 64 bits e uma GPU com suporte a Vulkan (Nvidia Kepler ou mais nova, AMD GCN 2 ou mais nova).

**Recomendado:** GPU Nvidia (Maxwell ou mais nova) com 6 GB de VRAM ou mais, 16 GB de RAM e processador moderno.

## Usando uma implementação PyTorch (CUDA)

O Flow Frames Brasil inclui o RIFE-NCNN, que roda em qualquer GPU com Vulkan. O RIFE original em PyTorch costuma ser mais rápido, mas exige:

* Uma **GPU Nvidia** moderna (750 Ti, séries 900/1000/1600/2000/3000/4000).
* **Python** com PyTorch (1.5 ou mais novo) e os pacotes `opencv-python`, `sk-video` e `imageio`
  (o programa pode baixar tudo automaticamente). [Mais detalhes](PythonDependencies.md)

## Principais configurações

Todas têm valores padrão adequados; não é preciso mexer em nada para começar.

* **Estilo de processamento:** fazer tudo de uma vez ou executar cada etapa separadamente (útil para editar quadros).
* **Tamanho máximo do vídeo de entrada:** reduz vídeos maiores que essa altura; resoluções menores aceleram muito.
* **Remoção de quadros duplicados:** para animações 2D. Desative em vídeos de câmera ou renderizações 3D.
* **Corrigir mudanças de cena:** evita o efeito de "derretimento" nos cortes.
* **Codificação automática:** codifica o vídeo durante a interpolação.
* **IDs da GPU:** `0` para uma GPU; `0,1` para duas, e assim por diante.
* **Modo de meia precisão (CUDA):** mais rápido e usa menos VRAM, mas pode ser instável.

## Perguntas frequentes

**Qual a diferença entre RIFE CUDA e RIFE NCNN?**
Os resultados são equivalentes. O NCNN também roda em placas AMD/Intel; o CUDA só em Nvidia, mas é mais rápido.

**Meu vídeo ficou "travando", principalmente em cenas escuras.**
Desative a remoção de quadros duplicados ou reduza o limite dela.

**Apareceu "Value must be a finite float" ou "FPS não encontrado".**
Corrigido na versão 1.0.0. Se acontecer com algum arquivo, informe o FPS manualmente e abra uma [issue](../../issues) anexando o log (Ferramentas de Depuração → Abrir pasta de logs).

## Para desenvolvedores

Compilar e gerar o instalador (requer .NET SDK ou Visual Studio, e o [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
powershell -ExecutionPolicy Bypass -File Build\build.ps1
# saída: Build\out\FlowFramesBrasil-Setup-<versão>.exe
```

### Publicar uma atualização

1. Atualize `version.txt` e o `changelog.txt` e faça o commit.
2. Crie e envie a tag: `git tag v1.1.0 && git push origin v1.1.0`.
3. O GitHub Actions (`.github/workflows/release.yml`) compila, gera o instalador e cria a Release.
   Os usuários recebem o aviso de atualização automaticamente.

Para usar outro repositório de atualizações, altere `RepoOwner`/`RepoName` em `CodeLegacy/Data/Branding.cs`.

## Licença e créditos

GPLv3 - veja [LICENSE](LICENSE). Baseado no [Flowframes](https://github.com/n00mkrad/flowframes) de N00MKRAD.
Os modelos de IA e ferramentas incluídas (RIFE, DAIN, FLAVR, FFmpeg, NCNN etc.) pertencem aos seus respectivos autores;
veja as licenças em `Pkgs/licenses`.
