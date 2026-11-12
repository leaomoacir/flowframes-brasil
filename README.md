<div align="center">

# 🎞️ Flow Frames Brasil

### Deixe seus vídeos mais fluidos com inteligência artificial

Transforme vídeos de **24/30 FPS em 60 FPS ou mais**, ou crie **câmera lenta suave** — de graça, em português, direto no Windows.

[![Baixar](https://img.shields.io/badge/⬇️%20Baixar%20para%20Windows-instalador-2ea44f?style=for-the-badge)](../../releases/latest)
&nbsp;
[![Versão](https://img.shields.io/github/v/release/leaomoacir/flowframes-brasil?style=for-the-badge&label=versão)](../../releases/latest)

</div>

---

## ✨ Destaques

| | |
|---|---|
| 🇧🇷 **Tudo em português** | Interface, mensagens e erros explicados em português do Brasil |
| 🖥️ **GPU ou CPU** | Usa sua placa de vídeo e, se não houver, **funciona na CPU** (mais lento) |
| 🔄 **Atualização automática** | O programa avisa quando há versão nova e atualiza com um clique |
| 🧠 **Várias IAs** | RIFE (NCNN e PyTorch), DAIN (NCNN) e FLAVR (PyTorch) |
| 🔍 **Erros claros** | Quando algo falha, mostra o dispositivo usado e a **causa provável** |

---

## 📥 Instalação

1. Baixe o instalador na página de **[Releases](../../releases/latest)** — arquivo `FlowFramesBrasil-Setup-x.y.z.exe`.
2. Execute e siga o assistente. **Não precisa de administrador.**
3. Abra o **Flow Frames Brasil** pelo menu Iniciar.

> [!NOTE]
> O Windows pode mostrar o aviso azul do **SmartScreen**, porque o instalador ainda não é assinado digitalmente.
> Clique em **Mais informações → Executar assim mesmo**.

### 🔄 Atualizações

Ao abrir, o programa confere sozinho se existe uma versão nova. Se houver, aparece um aviso — é só confirmar.
O instalador fecha o programa, atualiza e **abre de novo**. Você também pode conferir pelo botão **Atualizar** na tela principal.

---

## 💻 Requisitos

| | Mínimo | Recomendado |
|---|---|---|
| **Sistema** | Windows 10 ou 11 (64 bits) | Windows 11 |
| **Placa de vídeo** | Qualquer GPU com **Vulkan** (Nvidia, AMD ou Intel) — ou nenhuma, usando a CPU | Nvidia com 6 GB de VRAM ou mais |
| **Memória** | 8 GB de RAM | 16 GB de RAM |
| **Armazenamento** | SSD com espaço livre (os quadros temporários ocupam bastante) | SSD |

---

## 🚀 Como usar

1. **Arraste o vídeo** para a janela (ou use *Procurar*).
2. Escolha a **IA** e o **fator** (2x dobra os FPS; 4x quadruplica).
3. Clique em **Interpolar!** e aguarde.
4. O vídeo novo é salvo na pasta de saída escolhida.

### ⚙️ Escolhendo GPU ou CPU

Em **Configurações → Dispositivo NCNN** escolha o número da GPU, ou **`-1` para usar a CPU**.
Ao lado do seletor, o programa mostra em verde ou laranja **qual dispositivo vai ser usado**. Em cada execução, o log também informa:

```
Dispositivo: GPU [0] NVIDIA GeForce RTX 3050 (dedicada) (Vulkan)
```

> [!TIP]
> Em testes com o mesmo vídeo, a GPU Nvidia levou **5 s**, a GPU integrada Intel **12 s** e a CPU **23 s**.
> A CPU funciona bem para vídeos curtos; para vídeos longos, prefira uma GPU.

### 🔧 Principais configurações

Todas têm valores padrão adequados — você não precisa mexer em nada para começar.

| Configuração | Para que serve |
|---|---|
| **Tamanho máximo do vídeo de entrada** | Reduz vídeos maiores que essa altura. Resoluções menores aceleram muito. |
| **Remoção de quadros duplicados** | Use em **animações 2D**. Desative em vídeos de câmera ou renderizações 3D. |
| **Corrigir mudanças de cena** | Evita o efeito de "derretimento" nos cortes do vídeo. |
| **Codificação automática** | Gera o vídeo enquanto interpola, poupando tempo e espaço. |
| **Modo de meia precisão (CUDA)** | Mais rápido e usa menos VRAM, mas pode ser instável. |

---

## 🩺 Problemas comuns

> **❌ Problema:** O vídeo ficou **"travando"**, principalmente em cenas escuras.
>
> **✅ Solução:** Desative a *remoção de quadros duplicados* (ou reduza o limite dela). Ela é feita para animações 2D e prejudica vídeos de câmera.

<br>

> **❌ Problema:** Apareceu **"Video FPS: Not Found"** ou **"Value must be a finite float"**.
>
> **✅ Solução:** Isso foi corrigido na versão 1.0.0 — atualize o programa. Se acontecer com algum arquivo, informe o FPS manualmente na tela principal.

<br>

> **❌ Problema:** **"Memória de vídeo (VRAM) insuficiente"** ou a GPU parou de responder.
>
> **✅ Solução:** Reduza o *Tamanho máximo do vídeo de entrada*, diminua as threads da GPU nas Configurações, feche jogos e navegadores, ou use a **CPU** (`-1`).

<br>

> **❌ Problema:** O programa diz que **não há GPU compatível** / erro de **Vulkan**.
>
> **✅ Solução:** Atualize o driver da placa de vídeo (Nvidia, AMD ou Intel). Sem GPU, o programa continua funcionando na **CPU**, só que mais devagar.

<br>

> **❌ Problema:** O **modelo de IA não baixa** ou aparece "arquivos do modelo ausentes".
>
> **✅ Solução:** Confira a internet e abra **Configurações → Abrir baixador de modelos**. Se persistir, use **Limpar cache de modelos** e baixe de novo.

<br>

> **❌ Problema:** O Windows mostra **"O Windows protegeu seu computador"** ao instalar.
>
> **✅ Solução:** É o SmartScreen avisando sobre um instalador novo e sem assinatura digital. Clique em **Mais informações → Executar assim mesmo**.

<br>

> **❌ Problema:** Um erro que **não está aqui** aparece durante o uso.
>
> **✅ Solução:** Abra uma [issue](../../issues) descrevendo o que aconteceu e anexe o log (**Ferramentas de Depuração → Abrir pasta de logs**).

---

## ❓ Dúvidas frequentes

<details>
<summary><b>Qual a diferença entre RIFE CUDA e RIFE NCNN? Qual usar?</b></summary>

<br>

Os resultados são equivalentes. O **NCNN** roda em qualquer GPU com Vulkan (Nvidia, AMD, Intel) e também na CPU. O **CUDA** só roda em placas Nvidia, mas costuma ser mais rápido. Se você tem Nvidia e Python/PyTorch instalados, use CUDA; caso contrário, use NCNN.
</details>

<details>
<summary><b>O que é a remoção de quadros duplicados?</b></summary>

<br>

É para **animações 2D**, que repetem quadros sem mudança. Eles precisam ser removidos antes da interpolação, senão o resultado fica "picotado". Desative em vídeos de câmera e renderizações 3D.
</details>

<details>
<summary><b>O que é a codificação automática? Devo ativar?</b></summary>

<br>

Ela cria o vídeo **durante** a interpolação, em vez de depois. Deixe ativada, a menos que seu processador seja muito fraco.
</details>

<details>
<summary><b>Preciso instalar Python?</b></summary>

<br>

Não, para as versões **NCNN** (as padrão). Python com PyTorch só é necessário para as versões CUDA. Veja os detalhes em [PythonDependencies.md](PythonDependencies.md).
</details>

---

<div align="center">

<sub>Licença <a href="LICENSE">GPLv3</a> · Baseado no <a href="https://github.com/n00mkrad/flowframes">Flowframes</a>, de N00MKRAD · Os modelos de IA e ferramentas incluídos (RIFE, DAIN, FLAVR, FFmpeg, NCNN) pertencem aos seus autores — veja <code>Pkgs/licenses</code>.</sub>

</div>
