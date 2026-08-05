# Solutions — Set 02

Worked answers for [Exercise Set 02](../02-exercises.md).

## Part A — Answers

**1. Row-major batches and the transpose.**

They are the same operation written for different memory layouts. The mathematical convention treats $\mathbf{x}$ as a column vector, so $W\mathbf{x}$ with $W$ of shape (out, in) contracts $W$'s second axis against $\mathbf{x}$'s only axis. PyTorch stores a batch as a matrix `X` of shape (batch, in) — one example per *row* — so contracting `in` against `in` requires `X @ W.T`, giving (batch, out), again one example per row. Identical arithmetic; the transpose is bookkeeping to keep the batch axis first.

PyTorch chose rows-as-examples because it makes the batch dimension the *outermost* axis, and because tensors are stored in row-major (C) order, that means each example's features are contiguous in memory. That is what makes slicing a minibatch a cheap view rather than a copy, and it is what lets every operation treat the leading axis as "a bunch of independent things" — a convention that generalizes cleanly to `(batch, time, features)` and `(batch, channels, height, width)`.

If you stored a batch as columns, the batch axis would be innermost, so consecutive elements in memory would belong to *different examples*. Slicing a minibatch would stride across memory, cache behaviour would be poor, and every higher-dimensional layout (sequences, images) would need the batch axis threaded through the middle. Note that `nn.Linear` stores `weight` with shape (out, in) — matching the *mathematical* convention — and does the transpose internally, which is exactly the source of the confusion this question exists to settle.

**2. Derivative, partial, gradient, Jacobian.**

For $f: \mathbb{R}^n \to \mathbb{R}^m$:

A **derivative** in the single-variable sense requires $n = m = 1$ and is a scalar, the local rate of change.

A **partial derivative** $\partial f_i/\partial x_j$ holds all inputs but one fixed and differentiates with respect to that one. It is a scalar for each choice of $i$ and $j$.

A **gradient** requires $m = 1$ (a scalar-valued function). It is the vector of all $n$ partials, $\nabla f \in \mathbb{R}^n$, with the same shape as the input. This is why loss functions are always scalar-valued: it is what makes $\nabla_\theta J$ a well-defined object with the same shape as $\theta$, so you can subtract it from the parameters.

A **Jacobian** is the general case: the $m \times n$ matrix $J_{ij} = \partial f_i/\partial x_j$. The gradient is the special case $m=1$, written as a column rather than a $1\times n$ row.

**3. The chain rule and why order matters.**

For $h = f \circ g$ with $g: \mathbb{R}^n \to \mathbb{R}^m$ and $f: \mathbb{R}^m \to \mathbb{R}$:

$$\nabla_x h = \left(\frac{\partial g}{\partial x}\right)^{\!\top} \nabla_g f$$

where $\partial g/\partial x$ is the $m\times n$ Jacobian and $\nabla_g f$ is an $m$-vector, so the product is $(n\times m)(m\times 1) = n\times 1$ — an $n$-vector, matching $x$. In general, chaining $L$ functions gives a product of $L$ Jacobians.

Associativity means the product has the same *value* in any grouping but wildly different *cost*. Consider $J_1 J_2 J_3 \mathbf{v}$ where the $J_i$ are large matrices and $\mathbf{v}$ is a vector. Multiplying right-to-left, every operation is matrix-times-vector, costing $O(n^2)$ each. Multiplying left-to-right, you form matrix-times-matrix products at $O(n^3)$ each and only touch the vector at the end.

That single observation *is* backpropagation. Reverse-mode differentiation is the right-to-left grouping, and because a loss is a scalar the rightmost object is always a vector (in fact the scalar 1), so every step stays a cheap vector-Jacobian product. Forward-mode is the left-to-right grouping, which is efficient when you have few *inputs* and many outputs — the opposite of a neural network, which has millions of parameters and one loss. Module 05 develops this.

**4. Steepest with respect to what.**

With respect to the **Euclidean norm on the parameter space**. The precise statement is that among all unit-length directions $\mathbf{u}$ with $\lVert\mathbf{u}\rVert_2 = 1$, the one maximizing the directional derivative $\nabla f \cdot \mathbf{u}$ is $\mathbf{u} = \nabla f / \lVert\nabla f\rVert$. Change the norm and you change the answer.

The hidden assumption is that Euclidean distance in parameter space is meaningful — that moving 0.01 in one coordinate is "the same size" step as moving 0.01 in another. It routinely is not. If one input feature is measured in millimetres and another in kilometres, the corresponding weights live on utterly different scales, and the Euclidean gradient will take enormous steps in one and negligible steps in the other. This is exactly why input normalization matters (Module 09) and what adaptive optimizers like Adam are compensating for (Module 06): by dividing each coordinate by its own gradient magnitude, Adam is implicitly using a different, per-coordinate metric.

The second misleading case is that steepest descent is a *local* statement about an infinitesimal step. On an ill-conditioned quadratic — a long narrow valley — the steepest direction points across the valley rather than along it, so gradient descent zigzags. That is the observation momentum exists to fix.

**5. Why logs are everywhere.**

*Products become sums.* The likelihood of $N$ independent examples is a product of $N$ probabilities; its log is a sum. Sums differentiate term by term, parallelize over a batch, and let you average rather than multiply — which is the entire reason maximum likelihood is practical (Module 04).

*Numerical range.* A product of 10,000 probabilities each around 0.1 is $10^{-10000}$, which underflows any float to exactly zero, destroying all information. Its log is $-23026$, perfectly representable. Working in log space converts an impossible dynamic range into an easy one, and this is what `log_softmax` and `logsumexp` exist for.

*Better-behaved gradients.* The log's derivative is $1/x$, which grows without bound as $x \to 0$. Applied to a loss, that means the penalty for confidently assigning near-zero probability to the true answer is enormous, and so is the corrective gradient. Compare squared error, whose gradient *shrinks* as the prediction gets more confidently wrong — the failure demonstrated numerically in [Set 04](./04-solutions.md).

**6. Broadcasting.**

The rule: align shapes from the right; two dimensions are compatible if they are equal or one of them is 1; missing leading dimensions are treated as 1.

`(32, 128) + (128,)` → **`(32, 128)`**. Aligning right, 128 matches 128, and the missing leading dimension is treated as 1 and expanded to 32. This is exactly how a bias vector is added to a batch.

`(32, 128) + (32,)` → **error**. Aligning right compares 128 against 32, which are neither equal nor 1. The error message names "non-singleton dimension 1." To add a per-example scalar you must write `(32, 1)`, making your intent explicit — and the need to write it is the feature, not the annoyance.

`(32, 1) * (1, 128)` → **`(32, 128)`**. Both dimensions expand, producing the outer product. This is the pattern to be careful with, because it silently succeeds and is a common source of a tensor that is far larger than intended.

## Part B — Reference solution

### The hand derivative

With $f(x,y) = (x+y)^2\sin(x)$, the product rule gives

$$\frac{\partial f}{\partial x} = 2(x+y)\sin(x) + (x+y)^2\cos(x), \qquad \frac{\partial f}{\partial y} = 2(x+y)\sin(x)$$

At $x=1, y=2$: $x+y=3$, $\sin 1 = 0.841471$, $\cos 1 = 0.540302$, so $\partial f/\partial x = 6(0.841471) + 9(0.540302) = 5.048826 + 4.862721 = 9.911547$ and $\partial f/\partial y = 5.048826$.

```python
import torch
x = torch.tensor(1.0, requires_grad=True)
y = torch.tensor(2.0, requires_grad=True)
f = (x + y)**2 * torch.sin(x)
f.backward()
print(f.item(), x.grad.item(), y.grad.item())
```
```
7.573238  9.911547  5.048826
```

Agreement to all six printed digits. Note $\partial f/\partial y$ has no $\cos$ term because $y$ appears only inside the squared factor — if your hand answer had one, that is where to look.

### Shape drills

```python
import torch, torch.nn as nn
L = nn.Linear(784, 128)
L(torch.randn(32, 784)).shape          # (32, 128)
L(torch.randn(32, 10, 784)).shape      # (32, 10, 128)
(torch.randn(32, 128) + torch.randn(128)).shape    # (32, 128)
(torch.randn(32, 128) + torch.randn(32)).shape     # RuntimeError
(torch.randn(32, 1) * torch.randn(1, 128)).shape   # (32, 128)
torch.randn(8, 3, 32, 32).mean(dim=(2, 3)).shape   # (8, 3)
torch.randn(2, 3, 4).transpose(0, 1).reshape(3, 8).shape   # (3, 8)
```

The error is `(32, 128) + (32,)`, for the reason given in A6.

Three of these are worth dwelling on. `nn.Linear` applied to `(32, 10, 784)` works and gives `(32, 10, 128)`: it operates on the *last* axis and treats all leading axes as batch. That is what lets the same layer serve as the position-wise feedforward network inside a Transformer (Module 12) with no reshaping. The `mean(dim=(2,3))` reducing `(8, 3, 32, 32)` to `(8, 3)` is global average pooling — collapsing spatial extent while keeping batch and channels (Module 10).

And the last one hides a trap. `transpose(0, 1)` returns a **non-contiguous view** — the data has not moved, only the strides changed. `reshape` succeeds because it silently copies when it must, but `view` on the same tensor raises `view size is not compatible with input tensor's size and stride`. The rule: `view` is free and requires contiguity, `reshape` always works and may copy. When `view` fails, the fix is `.contiguous().view(...)` or simply `reshape`. You will hit this the first time you write multi-head attention.

### Explicit forward pass

```python
import torch, torch.nn as nn

torch.manual_seed(0)
net = nn.Sequential(nn.Linear(784, 128), nn.ReLU(), nn.Linear(128, 10))
X = torch.randn(32, 784)

W1, b1 = net[0].weight, net[0].bias      # shapes (128, 784) and (128,)
W2, b2 = net[2].weight, net[2].bias      # shapes (10, 128)  and (10,)

manual = torch.relu(X @ W1.T + b1) @ W2.T + b2
print((manual - net(X)).abs().max().item())
```
```
2.09e-07
```

That residual is float32 rounding from a different summation order inside the fused kernel, not a discrepancy — rerun in `float64` and it drops to around $10^{-15}$. The point of the exercise is that `nn.Sequential` is not doing anything you cannot write in one line; the shapes above are the *entire* content of a two-layer network's forward pass.

---

Back to [Set 02](../02-exercises.md) · Next solutions: [Set 03](./03-solutions.md)
