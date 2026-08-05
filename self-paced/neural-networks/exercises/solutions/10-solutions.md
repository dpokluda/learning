# Solutions — Set 10

Worked answers for [Exercise Set 10](../10-exercises.md).

## Part A — Answers

**1. The three priors.**

Parameter sharing is the one everyone names: the same kernel is applied at every position, so a $3\times3$ filter over 64 input channels costs the same whether the image is $32\times32$ or $2048\times2048$. But sharing is a *consequence* of a modelling assumption, not the assumption itself.

The first structural prior is **locality**. A convolutional unit sees only a small spatial neighbourhood, which assumes that the information needed to detect a feature is spatially concentrated — that an edge, a corner, a texture is determined by nearby pixels and not by pixels on the opposite side of the image. This is true for images and sound and false for, say, a tabular dataset whose columns are in arbitrary order.

The second is **translation equivariance**, which is what sharing actually encodes. Using the same weights everywhere assumes the statistics of the data are stationary: a vertical edge is a vertical edge whether it appears top-left or bottom-right, so the detector for it should not depend on position. Again this is a claim about the data. It is nearly true for natural images, only partly true for faces cropped and aligned to a canonical position, and false for a fixed-layout form where position carries meaning.

The third, which emerges from stacking rather than from any single layer, is **compositional hierarchy**: the assumption that meaningful structure is built from smaller meaningful structure — edges into motifs, motifs into parts, parts into objects. That is why depth in a CNN corresponds to semantic abstraction, and it is a genuine claim about the world rather than a computational convenience.

The cost of these priors is exactly what makes them useful. A fully-connected layer can represent everything a convolution can and much more; the convolution is *worse* at fitting arbitrary functions and better at fitting images, because the hypotheses it has thrown away were mostly wrong ones.

**2. Equivariance versus invariance.**

A function $f$ is **equivariant** to a transformation $T$ if $f(T(x)) = T(f(x))$ — transform the input, and the output transforms the same way. It is **invariant** if $f(T(x)) = f(x)$ — the output does not change at all.

Convolution is equivariant to translation: shift the input by three pixels and every feature map shifts by three pixels, with identical values. Pooling introduces invariance, locally: max-pooling over a $2\times2$ window gives the same answer if the maximum moves within the window, so small shifts are absorbed. Global pooling gives full translation invariance, since the maximum (or mean) over the entire map does not depend on where in the map it occurred.

A classifier needs both, and in that order. It needs equivariance in the feature extractor because *where* a feature is matters for combining features into larger structures — an eye above a nose is a face, an eye below a nose is not, and a translation-invariant early layer would discard the spatial relationships the later layers need. It needs invariance at the output because the label "cat" must not depend on where the cat is in the frame. The standard architecture is therefore a deep equivariant stack that progressively pools, ending in global pooling or a flatten-and-classify head — spatial information is preserved while it is useful and discarded exactly when it stops being.

**3. Output size.**

Consider one spatial dimension. Padding adds $P$ zeros on each side, so the padded length is $H + 2P$. A kernel of width $K$ placed at offset $i$ covers positions $i$ through $i + K - 1$, so the last valid starting offset is $H + 2P - K$. Starting offsets advance by $S$, so the valid ones are $0, S, 2S, \dots$ up to $H + 2P - K$, and the number of them is

$$\left\lfloor \frac{H + 2P - K}{S} \right\rfloor + 1$$

with the $+1$ counting the position at offset zero and the floor discarding a final partial window that does not fit.

With $S = 1$ and $P = (K-1)/2$ for odd $K$, the numerator becomes $H + (K-1) - K = H - 1$, so the output length is exactly $H$. This is **"same" padding**, and it is the reason $3\times3$ with $P=1$, $5\times5$ with $P=2$ and $7\times7$ with $P=3$ appear everywhere: they let you stack arbitrarily many convolutions without the spatial dimensions shrinking, which decouples depth from resolution and lets downsampling happen only where you choose it, at pooling or strided layers. It is also why kernels are almost always odd-sized — an even kernel has no centre pixel and cannot be padded symmetrically.

Verified against PyTorch across five configurations:

| $H$ | $K$ | $S$ | $P$ | PyTorch | formula |
| --- | --- | --- | --- | --- | --- |
| 28 | 5 | 1 | 0 | 24 | 24 |
| 28 | 3 | 1 | 1 | 28 | 28 |
| 32 | 3 | 2 | 1 | 16 | 16 |
| 224 | 11 | 4 | 2 | 55 | 55 |
| 224 | 7 | 2 | 3 | 112 | 112 |

**4. Three $3\times3$ versus one $7\times7$.**

Use the receptive-field recurrence $r \leftarrow r + (k-1)j$ with jump $j = 1$ throughout at stride 1. Starting at $r=1$: after one $3\times3$, $r = 3$; after two, $r = 5$; after three, $r = 7$. One $7\times7$ gives $r = 1 + 6 = 7$. Identical, and the code below confirms it.

The first reason to prefer the stack is **parameters**. For $C$ input and output channels, one $7\times7$ costs $49C^2$ weights while three $3\times3$ cost $27C^2$ — a reduction of 45%, and the same computation applies to FLOPs. At $C = 64$ that is 200,704 versus 110,592.

The second, and the one Simonyan and Zisserman emphasize, is **nonlinearity**. The stack has three ReLUs where the single large kernel has one, so it can express a more discriminative function over the same receptive field. A single $7\times7$ convolution is a linear map over its window; three $3\times3$ convolutions with activations between them are a small three-layer network over that window.

The same argument extends: two $3\times3$ replace one $5\times5$ at a ratio of $25/18 = 1.389$, and three replace a $7\times7$ at $49/27 = 1.815$. This is why the $3\times3$ became the field's default kernel and why VGG's uniform design — only $3\times3$ convolutions and $2\times2$ pools — is still the template most architectures are described against.

**5. The lineage.**

**LeNet-5** (LeCun et al., 1998) established the template: alternating convolution and subsampling layers to extract features, then fully-connected layers to classify. Roughly 61,700 parameters, $\tanh$ activations, average pooling, trained on 60,000 digits. It worked, it was deployed on cheque-reading systems, and then the field moved on — because on harder problems the available data and compute could not support anything larger.

**AlexNet** (Krizhevsky et al., 2012) is the same idea at fifteen hundred times the scale — 60 million parameters — and its changes were the ones that made scale possible. ReLU instead of $\tanh$ removed the saturation that made deep networks untrainable and sped up convergence several-fold. Dropout controlled the overfitting that 60M parameters would otherwise guarantee. Training on two GPUs made the compute feasible at all. And ImageNet supplied 1.2 million labelled images, without which none of it would have generalized. The lesson is that AlexNet's contribution was not a new idea but the confluence of data, hardware and two enabling tricks; the architecture is recognizably LeNet.

**VGG** (2014) replaced ad-hoc kernel sizes with uniform $3\times3$ blocks and went to 16–19 layers, demonstrating that depth itself was the lever and that a simple repeated motif beat hand-tuned heterogeneity. It became necessary because AlexNet's design offered no obvious way to keep growing. Its weakness was cost: 138M parameters, most of them in the first fully-connected layer.

**ResNet** (He et al., 2015) addressed the wall VGG ran into. Beyond about 20 layers, plain networks got *worse on training error* — the degradation problem of [Set 08](./08-solutions.md), which is an optimization failure, not overfitting. Residual connections made the identity the default and the network's job to learn a deviation from it, which made 50, 101 and 152 layers trainable and won ImageNet 2015. Global average pooling replaced the giant FC head, so ResNet-50 has 25M parameters against VGG-16's 138M while being both deeper and more accurate.

Read as a sequence, each step removed the specific obstacle created by the previous one: LeNet needed data and compute, AlexNet needed a principled way to scale depth, VGG needed a way to train past twenty layers, and ResNet needed — well, [Module 12](../../12-attention-and-transformers.md).

**6. The $1\times1$ convolution.**

It has no spatial extent, so it does nothing spatially. What it does is operate **across channels**: at each pixel independently, it applies a learned linear map from $C_{\text{in}}$ channel values to $C_{\text{out}}$ channel values. It is a fully-connected layer applied identically at every spatial position — equivalently, a learned recombination of feature maps, which is why the paper that introduced it called it "network in network."

Two things make it valuable. It changes channel count cheaply, costing $C_{\text{in}}C_{\text{out}}$ parameters against $9C_{\text{in}}C_{\text{out}}$ for a $3\times3$. And it adds a nonlinearity (with its activation) without touching resolution or receptive field.

ResNet's bottleneck block uses both properties. Instead of two $3\times3$ convolutions at 256 channels, it does $1\times1$ down to 64, then $3\times3$ at 64, then $1\times1$ back up to 256. The expensive spatial convolution runs at a quarter of the channel width, and the two cheap $1\times1$s handle the channel arithmetic. Counting: the naive version costs $2 \times 9 \times 256^2 \approx 1.18$M parameters; the bottleneck costs $256{\cdot}64 + 9{\cdot}64^2 + 64{\cdot}256 \approx 70$K — about seventeen times less for a comparable representational job. That saving is what buys the depth in ResNet-50 and beyond.

## Part B — Reference solutions

### Convolution three ways

```python
import torch, torch.nn as nn, torch.nn.functional as F
torch.manual_seed(0)

x = torch.randn(2, 3, 9, 9, dtype=torch.float64)
w = torch.randn(4, 3, 3, 3, dtype=torch.float64)
b = torch.randn(4, dtype=torch.float64)

def manual_conv(x, w, b, stride=1, pad=0):
    N, C, H, W = x.shape
    O, _, KH, KW = w.shape
    xp = F.pad(x, (pad, pad, pad, pad))
    Ho = (H + 2*pad - KH) // stride + 1
    Wo = (W + 2*pad - KW) // stride + 1
    out = torch.zeros(N, O, Ho, Wo, dtype=x.dtype)
    for n in range(N):
        for o in range(O):
            for i in range(Ho):
                for j in range(Wo):
                    patch = xp[n, :, i*stride:i*stride+KH, j*stride:j*stride+KW]
                    out[n, o, i, j] = (patch * w[o]).sum() + b[o]
    return out

def conv_im2col(x, w, b, stride=1, pad=0):
    N, C, H, W = x.shape
    O, _, KH, KW = w.shape
    cols = F.unfold(x, (KH, KW), stride=stride, padding=pad)   # (N, C*KH*KW, L)
    out  = w.view(O, -1) @ cols + b.view(1, -1, 1)             # (N, O, L)
    Ho = (H + 2*pad - KH) // stride + 1
    Wo = (W + 2*pad - KW) // stride + 1
    return out.view(N, O, Ho, Wo)
```
```
stride=1 pad=0  shape (2, 4, 7, 7)  max diff 3.553e-15
stride=1 pad=1  shape (2, 4, 9, 9)  max diff 7.105e-15
stride=2 pad=1  shape (2, 4, 5, 5)  max diff 3.553e-15
im2col max diff: 5.329e-15
```

All three agree to float64 round-off. Two observations worth carrying away.

The naive version is four nested loops and about eight lines, which is the honest size of the idea. Everything else about convolution — dilation, groups, transposed convolution — is index arithmetic layered on this.

The `im2col` version is how it is actually computed. `F.unfold` extracts every $K\times K$ patch into a column, producing a matrix of shape $(C \cdot K^2) \times L$ where $L$ is the number of output positions; the kernel flattens to $O \times (C \cdot K^2)$; and the convolution becomes one matrix multiply. **This wastes memory** — each input pixel is duplicated into up to $K^2$ columns, so a $3\times3$ convolution inflates the activation nine-fold — and it is worth it anyway, because a large dense GEMM is the single most heavily optimized operation in all of computing. Decades of BLAS tuning, and every tensor core on every modern accelerator, exist to make that multiply fast. Trading memory for the ability to call into that machinery is a much better deal than a hand-written spatial loop, and it is why "just use a matrix multiply" keeps winning in this field.

### Equivariance and invariance, measured

```python
xs = torch.zeros(1, 1, 16, 16, dtype=torch.float64); xs[0, 0, 4, 4] = 1.0
conv = nn.Conv2d(1, 1, 3, padding=1, bias=False).double()

o1  = conv(xs)
xs2 = torch.roll(xs, shifts=(3, 2), dims=(2, 3))
o2  = conv(xs2)

print((torch.roll(o1, (3, 2), (2, 3)) - o2).abs().max().item())   # equivariance
print((nn.MaxPool2d(16)(o1) - nn.MaxPool2d(16)(o2)).abs().max().item())   # invariance
lin = nn.Linear(256, 10).double()
print((lin(xs.view(1, -1)) - lin(xs2.view(1, -1))).abs().max().item())
```
```
conv equivariance:  ||shift(conv(x)) - conv(shift(x))||  = 0.0
global max-pool invariance:                              = 0.0
fully-connected:    |f(x) - f(shift(x))|                 = 0.10095
```

Exactly zero, in both cases — not approximately, not to within tolerance. Equivariance and invariance here are algebraic identities about the operations, not empirical tendencies that training encourages, and that is the point. The convolutional network gets these properties for free from its structure, before it has seen a single training example.

The fully-connected row shows what the alternative costs. Moving one bright pixel by a three-by-two shift changes the linear layer's output by 0.10, because as far as `nn.Linear` is concerned position 68 and position 118 are unrelated coordinates with independent weights. To become shift-tolerant, an MLP must *learn* that relationship separately for every feature and every offset, from data. That is the concrete meaning of the claim that architecture is a prior: the CNN is not better at learning, it simply does not need to learn this.

### Receptive fields

```python
def receptive_field(layers):          # layers: [(kernel, stride), ...]
    r, j = 1, 1
    for k, s in layers:
        r = r + (k - 1) * j
        j = j * s
    return r
```
```
three 3x3 stride 1:                    7
one   7x7 stride 1:                    7
conv3 → pool2 → conv3 → pool2 → conv3: 18
```

The recurrence is worth understanding rather than memorizing. $j$ is the *jump*: how many input pixels apart two adjacent units at the current layer are. It starts at 1 and multiplies by the stride at each layer. $r$ is the receptive field, and each new layer of kernel size $k$ extends it by $(k-1)$ units of the current jump — because the kernel reaches $k-1$ neighbours beyond the centre, each of which is $j$ input pixels away.

The third result shows why pooling is the efficient way to grow receptive field. Five layers, all with kernels of size 3 or 2, reach 18 pixels — because each pooling layer doubles $j$, so subsequent convolutions cover twice as much ground per unit. Without the pools, five $3\times3$ layers would reach only 11. This is the mechanism behind the standard pyramid design, and it is why the deepest layers of a CNN "see" the whole image despite every individual kernel being tiny.

### Parameter arithmetic

```
Conv2d(32, 64, 3) params: 18,496  = 3*3*32*64 + 64
one 5x5 CxC: 102,400   two 3x3 CxC: 73,728   ratio 1.389   (C=64)
one 7x7 CxC: 200,704   three 3x3 CxC: 110,592  ratio 1.815
fc 224*224*3 → 1000:   150,528,000
conv 3→64, k=3:              1,792
LeNet-5 output torch.Size([1, 10]), params: 61,706
```

The fully-connected-versus-convolutional line is the one to sit with. A single fully-connected layer mapping one $224\times224$ colour image to 1,000 hidden units needs **150 million parameters** — more than the entirety of VGG-16, for one layer. The convolutional layer that starts a real network needs **1,792**, a factor of 84,000 less, and unlike the FC layer it works at any input resolution. That ratio is why computer vision was not a solved problem before convolution and became one after.

LeNet-5 at 61,706 parameters is the other end of the scale, and worth remembering as a unit of measure: it is roughly 0.0004 of a modern vision model and about $10^{-7}$ of a frontier language model, and it read a substantial fraction of the United States' handwritten cheques.

### Training the CNN

```python
torch.manual_seed(0)
cnn = nn.Sequential(
    nn.Conv2d(1, 32, 3, padding=1),  nn.BatchNorm2d(32), nn.ReLU(), nn.MaxPool2d(2),
    nn.Conv2d(32, 64, 3, padding=1), nn.BatchNorm2d(64), nn.ReLU(), nn.MaxPool2d(2),
    nn.Flatten(), nn.Linear(64*7*7, 128), nn.ReLU(), nn.Dropout(0.25), nn.Linear(128, 10)
).to(device)

opt = torch.optim.AdamW(cnn.parameters(), lr=1e-3, weight_decay=1e-2)
sched = torch.optim.lr_scheduler.CosineAnnealingLR(opt, T_max=5)
# ... standard 5-epoch loop
```

| model | parameters | epochs | test accuracy | time |
| --- | --- | --- | --- | --- |
| MLP (Module 09 recipe) | 203,530 | 15 | 98.28% | 25 s |
| **CNN** | 421,834 | 5 | **99.27%** | **12 s** |

The CNN reaches 99.27% in a third of the epochs and half the wall-clock time. Read the error rate rather than the accuracy to see the size of the win: 0.73% against 1.72%, a **57% reduction in errors**. Getting there with an MLP is not a matter of tuning; 98.3% is approximately the ceiling for fully-connected models on MNIST no matter how large you make them.

Two details are easy to misread. The CNN has *more* parameters than the MLP, so this is not a capacity argument — it is an argument about which functions the parameters can express. And it is faster despite being larger, because convolution's arithmetic is dense, regular and parallel in a way that a big dense matrix multiply on flattened images is not once you account for how much of the MLP's capacity is spent relearning translated copies of the same feature.

The structure of the remaining errors is also informative: the confusions that survive are the genuinely ambiguous digits — 4 against 9, 3 against 5, 7 against 1 — the ones a human annotator would also hesitate over. When your error set stops containing obvious mistakes and starts containing genuinely hard examples, you have reached the point where architecture changes stop helping and data quality takes over.

---

Back to [Set 10](../10-exercises.md) · Next solutions: [Set 11](./11-solutions.md)
