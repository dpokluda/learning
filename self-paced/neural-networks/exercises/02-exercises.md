# Exercise Set 02 — Just enough mathematics

Companion to [Module 02](../02-mathematical-foundations.md). This set is drills. It is the least glamorous in the book and the one whose absence causes the most confusion later, because almost every bug you will write for the next ten modules is a shape bug.

## Part A — Questionnaire

1. The book writes a layer as $\mathbf{z} = W\mathbf{x} + \mathbf{b}$ with $W$ of shape (out, in), but PyTorch computes `x @ W.T + b` with `x` of shape (batch, in). Explain why these are the same operation and why PyTorch chose its layout. What would go wrong if you stored a batch as columns instead of rows?

2. What is the difference between a *derivative*, a *partial derivative*, a *gradient*, and a *Jacobian*? Give the shape of each for a function $f: \mathbb{R}^n \to \mathbb{R}^m$.

3. State the chain rule for $h(x) = f(g(x))$ where $g: \mathbb{R}^n \to \mathbb{R}^m$ and $f: \mathbb{R}^m \to \mathbb{R}$. Which shapes are being multiplied, and in which order? Why does the order matter for efficiency even though matrix multiplication is associative?

4. The gradient $\nabla_\theta J$ "points in the direction of steepest ascent." Steepest with respect to what? Name the assumption hiding in that sentence, and describe a situation where it is misleading.

5. Why is $\log$ used so pervasively in this field — in losses, in probabilities, in numerical stability tricks? Give three distinct reasons.

6. Broadcasting: given tensors of shape `(32, 128)` and `(128,)`, what does addition produce and why? What about `(32, 128)` and `(32,)`? What about `(32, 1)` and `(1, 128)`? For each, state the result shape or the error.

## Part B — Coding

**The goal, in prose.** Get fluent enough with tensor shapes and autograd that you stop guessing. You should be able to predict, before running anything, the shape of every intermediate in a forward pass, and you should be able to compute a small gradient by hand on paper and have the machine confirm it to the last digit.

**Specifics.**

First, the hand derivative. Take $f(x, y) = (x+y)^2 \sin(x)$. Differentiate it on paper with respect to both variables. Then evaluate at $x = 1, y = 2$ and check against `autograd`. Do not run the code until you have written the two derivative expressions down.

Second, the shape drills. For each of the following, write the output shape *before* running it, then verify: `nn.Linear(784, 128)` applied to a tensor of shape `(32, 784)`; the same applied to `(32, 10, 784)`; `torch.randn(32, 128) + torch.randn(128)`; `torch.randn(32, 128) + torch.randn(32)`; `torch.randn(32, 1) * torch.randn(1, 128)`; `torch.randn(8, 3, 32, 32).mean(dim=(2, 3))`; `torch.randn(2, 3, 4).transpose(0, 1).reshape(3, 8)`. At least one of these raises an error — identify which and say exactly why.

Third, implement a two-layer forward pass with explicit tensor operations only (no `nn.Module`, no `nn.Linear`), verify it against the equivalent `nn.Sequential`, and confirm agreement to floating-point precision.

**Starter stub.**

```python
import torch, torch.nn as nn, math

# 1. hand derivative
x = torch.tensor(1.0, requires_grad=True)
y = torch.tensor(2.0, requires_grad=True)
f = (x + y)**2 * torch.sin(x)
f.backward()
print(x.grad.item(), y.grad.item())
# now compare against your paper answer

# 3. explicit forward pass
def forward(X, W1, b1, W2, b2):
    ...   # z1 = X @ W1.T + b1 ; a1 = relu(z1) ; z2 = a1 @ W2.T + b2
```

---

Solutions: [`solutions/02-solutions.md`](./solutions/02-solutions.md) · Next: [Set 03](./03-exercises.md)
