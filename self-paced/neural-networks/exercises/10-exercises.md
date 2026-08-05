# Exercise Set 10 — Convolutional Networks

Companion to [Module 10](../10-convolutional-networks.md).

## Part A — Questionnaire

1. Convolution is often justified by "parameter sharing." Give the *other* two justifications — the structural priors it encodes — and explain what each one assumes about the data.

2. Distinguish equivariance from invariance precisely. Which does convolution give you, which does pooling give you, and why does a classifier need both?

3. Derive the output size formula $\lfloor (H + 2P - K)/S \rfloor + 1$ and explain what padding $P = (K-1)/2$ achieves for odd $K$ at stride 1.

4. VGG replaced large kernels with stacks of $3\times3$. Show that three $3\times3$ convolutions have the same receptive field as one $7\times7$, then give the two reasons the stack is preferable.

5. Trace the architectural lineage LeNet → AlexNet → VGG → ResNet, naming for each what changed and *why it became possible or necessary* at that moment.

6. A $1\times1$ convolution has a receptive field of one pixel and appears to do nothing. Explain what it actually computes and why ResNet's bottleneck block is built around it.

## Part B — Coding

**The goal, in prose.** Convolution is a small idea buried under a lot of index arithmetic. Implement it three ways — naive loops, `im2col` matrix multiply, and PyTorch's — and prove they agree. Then demonstrate the structural properties directly, and finally train a real CNN and see what the architectural prior is worth against the MLP ceiling.

**Specifics.**

*Implement 2-D convolution with explicit loops* over batch, output channel, and spatial positions, supporting stride and padding. Verify against `F.conv2d` in `float64` for three configurations: stride 1 no padding, stride 1 padding 1, stride 2 padding 1. You want agreement around $10^{-15}$.

*Reimplement it as a single matrix multiplication* using `F.unfold` (this is `im2col`, and it is essentially how convolution is really executed on hardware). Verify agreement again, and explain why turning convolution into a GEMM is worth the memory it wastes.

*Demonstrate equivariance and invariance numerically.* Take an image with a single bright pixel, convolve it, then shift the input and convolve again. Show that shifting the input shifts the output identically. Then apply global max-pooling to both and show the results are equal. Do the same shift through an `nn.Linear` and report how much its output changes.

*Compute receptive fields.* Write the recurrence $r \leftarrow r + (k-1)j$, $j \leftarrow js$ and use it to confirm the VGG claim, then compute the receptive field of `conv3 → pool2 → conv3 → pool2 → conv3`.

*Count parameters and check the shape formula* for LeNet-5, for `Conv2d(32, 64, 3)`, and for the fully-connected-versus-convolutional comparison on a $224\times224\times3$ input.

*Train a CNN on MNIST* — two conv-BN-ReLU-pool blocks into a small classifier head — with AdamW and cosine annealing for 5 epochs. Compare accuracy, parameter count and wall-clock time against the [Module 09](../09-practical-training-and-debugging.md) MLP.

**Starter stub.**

```python
def manual_conv(x, w, b, stride=1, pad=0):
    N, C, H, W = x.shape
    O, _, KH, KW = w.shape
    xp = F.pad(x, (pad, pad, pad, pad))
    Ho = (H + 2*pad - KH) // stride + 1
    Wo = (W + 2*pad - KW) // stride + 1
    out = torch.zeros(N, O, Ho, Wo, dtype=x.dtype)
    ...        # four nested loops; correctness first, speed never
    return out
```

---

Solutions: [`solutions/10-solutions.md`](./solutions/10-solutions.md) · Next: [Set 11](./11-exercises.md)
