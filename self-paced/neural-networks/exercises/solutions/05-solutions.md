# Solutions — Set 05

Worked answers for [Exercise Set 05](../05-exercises.md). These scripts are also the reference implementations cited by [Module 05](../../05-backpropagation-and-autodiff.md).

## Part A — Answers

**1. What backpropagation actually is.**

Backpropagation computes **gradients**. That is all it does. It is reverse-mode automatic differentiation applied to the computational graph of a neural network, and its output is $\partial\mathcal{L}/\partial\theta$ for every parameter $\theta$.

The training is done by the **optimizer** — gradient descent or one of its variants — which consumes those gradients and decides how to change the parameters. The two are cleanly separable, which is exactly why PyTorch separates them: `loss.backward()` fills in `.grad`, and `optimizer.step()` reads `.grad` and updates. You could keep backpropagation and swap gradient descent for any other gradient-based method; you could keep gradient descent and obtain gradients by finite differences instead (absurdly slowly, but validly).

Conflating them causes real confusion, because the failure modes are different. A vanishing gradient is a backpropagation problem, fixable with architecture. A learning rate that overshoots is an optimizer problem, fixable with a schedule. Knowing which you have is most of debugging.

**2. Reverse-mode versus forward-mode.**

Differentiating a composition $f_L \circ \cdots \circ f_1$ produces a product of Jacobians $J_L J_{L-1}\cdots J_1$. Matrix multiplication is associative, so the *value* is fixed but the *cost* depends entirely on the grouping.

**Forward mode** groups left from the input: it propagates $\partial(\text{everything})/\partial x_i$ forward for one chosen input $x_i$. One pass gives you the derivative of all outputs with respect to *one input*, so covering $n$ inputs costs $n$ passes.

**Reverse mode** groups right from the output: it propagates $\partial\mathcal{L}/\partial(\text{everything})$ backward from one chosen output. One pass gives the derivative of *one output* with respect to all inputs, so covering $m$ outputs costs $m$ passes.

A neural network has $n \sim 10^6$ to $10^{11}$ parameters and $m = 1$ (a scalar loss). Reverse mode needs one backward pass, total; forward mode would need one pass per parameter. The cost difference is a factor of $n$, which is the difference between minutes and geological time. The mechanical reason is that in reverse mode the rightmost factor is always a *vector*, so every step is a cheap vector-Jacobian product at $O(n^2)$ rather than a matrix-matrix product at $O(n^3)$.

Forward mode is preferable when the shape is reversed: few inputs, many outputs. Computing the full Jacobian of a function $\mathbb{R}^2 \to \mathbb{R}^{1000}$ takes 2 forward passes and 1000 reverse ones. It also uses no memory for stored activations, which is why it appears in Jacobian-vector-product applications and in some scientific computing. PyTorch provides both (`torch.func.jvp` and `torch.func.vjp`); the default is reverse for the reason above.

**3. The four equations.**

$$
\begin{aligned}
\delta^{(L)} &= \nabla_{\mathbf{a}^{(L)}}\mathcal{L} \odot \phi'(\mathbf{z}^{(L)}) \\
\delta^{(\ell)} &= \left(W^{(\ell+1)\top}\delta^{(\ell+1)}\right)\odot\phi'(\mathbf{z}^{(\ell)}) \\
\frac{\partial\mathcal{L}}{\partial W^{(\ell)}} &= \delta^{(\ell)}\mathbf{a}^{(\ell-1)\top} \\
\frac{\partial\mathcal{L}}{\partial\mathbf{b}^{(\ell)}} &= \delta^{(\ell)}
\end{aligned}
$$

Shapes, with layer $\ell$ having $n_\ell$ units. $\delta^{(\ell)}$ is $n_\ell \times 1$ — one number per unit, the sensitivity of the loss to that unit's pre-activation. $W^{(\ell+1)}$ is $n_{\ell+1}\times n_\ell$, so $W^{(\ell+1)\top}$ is $n_\ell \times n_{\ell+1}$ and multiplying the $n_{\ell+1}$-vector $\delta^{(\ell+1)}$ gives an $n_\ell$-vector — the transpose is what routes error backward along the same connections the forward pass used. $\phi'(\mathbf{z}^{(\ell)})$ is $n_\ell\times1$, so the $\odot$ is elementwise and shape-preserving. The weight gradient is an outer product: $(n_\ell\times1)(1\times n_{\ell-1}) = n_\ell\times n_{\ell-1}$, matching $W^{(\ell)}$, and its interpretation is that the gradient for the weight connecting unit $j$ to unit $i$ is (error at $i$) × (activation at $j$). The bias gradient equals $\delta^{(\ell)}$ because $\partial\mathbf{z}/\partial\mathbf{b} = I$.

**4. Why gradients accumulate.**

Because a node's contribution to the loss is the *sum* over all paths from it to the output — the multivariable chain rule. Assignment would keep only the last path visited.

Two concrete cases. First, a **branching value**: if `d = a * b` and `e = a + c`, then `a` feeds two downstream nodes, and $\partial\mathcal{L}/\partial a$ is the sum of what arrives through `d` and through `e`. With assignment, whichever backward closure ran second would overwrite the first and you would silently lose half the gradient. Second, a **shared parameter**: an RNN applies the same $W_{hh}$ at every timestep (Module 11), and a convolutional kernel is applied at every spatial position (Module 10). The correct gradient sums the contributions from all uses, and accumulation is exactly what produces that sum without any special-case code.

Accumulation is also why PyTorch requires `optimizer.zero_grad()`. Since `.grad` accumulates by design, it would otherwise keep summing across iterations, and your update would be based on the running total of every gradient since the program started. Forgetting it produces a model that trains oddly and then diverges, and it is the most common bug in the beginner's training loop. It is a deliberate trade: the same behaviour that makes shared parameters and branching work for free is the one that requires you to reset explicitly. It also lets you *deliberately* accumulate over several minibatches to simulate a larger batch on limited memory.

**5. Topological order.**

Because a node's gradient is not complete until *every* consumer of that node has pushed its contribution. Running a node's `_backward` before all of its consumers have run means propagating a partial gradient downstream, and the error contaminates everything below.

A minimal counterexample: let `b = a * 2`, `c = a + b`, `L = c`. The graph is `a → b → c` and also `a → c` directly. A breadth-first walk from `L` visits `c`, then its children `a` and `b` at the same level. If it processes `a` first, `a.grad` has received only the direct contribution from `c` (which is 1), and `a._backward` would propagate that. Only afterwards does `b` run and add its contribution ($2$), so the true $\partial L/\partial a = 3$ is assembled too late — any node below `a` already received the wrong value. Reverse topological order guarantees `b` is fully processed before `a`, because the topological sort places `a` before `b` in forward order and therefore after it in reverse.

For a network that is a simple chain, layer-by-layer reverse order *is* the topological order, which is why the textbook presentation never mentions this. The moment you have a skip connection (Module 10) or a shared weight, it matters.

**6. Gradient checking.**

Use the **central difference**, $\partial\mathcal{L}/\partial\theta_i \approx (\mathcal{L}(\theta + \epsilon\mathbf{e}_i) - \mathcal{L}(\theta - \epsilon\mathbf{e}_i))/2\epsilon$, whose truncation error is $O(\epsilon^2)$ rather than the forward difference's $O(\epsilon)$ — a free order of accuracy for one extra function evaluation.

Compare with **relative** error, $|g_{\text{num}} - g_{\text{ana}}| / \max(|g_{\text{num}}|, |g_{\text{ana}}|, 10^{-8})$, not absolute, because gradient magnitudes vary over many orders. In `float64` with $\epsilon = 10^{-6}$, accept anything below about $10^{-7}$; the reference run below achieves $1.9\times10^{-10}$. In `float32` you cannot do better than roughly $10^{-3}$, which is why gradient checks are always run in double precision. Check a random sample of coordinates rather than all of them, since each costs two forward passes.

Two false alarms to know about. **Kinks**: at a non-differentiable point such as ReLU's origin, the two perturbed evaluations can straddle the kink and land on different branches, making the numerical estimate meaningless while the analytic gradient is a perfectly valid subgradient. Skip coordinates where $|z|$ is within $\epsilon$ of a kink, or perturb a different one. **Stochasticity**: if the forward pass contains dropout or any other randomness, the two evaluations use different random draws and the difference is dominated by noise. Fix the seed or put the model in `eval()` mode. A third, subtler one: if $\epsilon$ is too small, catastrophic cancellation in the subtraction destroys precision, so a failing check sometimes improves when you make $\epsilon$ *larger*.

## Part B — Reference solutions

### Manual backprop, batched

In the row-major layout, with `X` of shape $(B, n_0)$ and `W1` of shape $(n_1, n_0)$, the forward pass is `Z1 = X @ W1.T + b1`. Transposing the math version gives the batched gradients: the error signals become $(B, n_\ell)$ matrices, the outer product becomes `D.T @ A`, and the bias gradient becomes a sum over the batch axis. Cross-entropy with `reduction='mean'` divides by $B$, so that factor appears in $D_2$.

```python
import torch, torch.nn.functional as F
torch.manual_seed(0); torch.set_default_dtype(torch.float64)

B, n0, n1, n2 = 5, 4, 3, 3
X = torch.randn(B, n0)
y = torch.tensor([0, 2, 1, 1, 0])
W1 = torch.randn(n1, n0, requires_grad=True); b1 = torch.randn(n1, requires_grad=True)
W2 = torch.randn(n2, n1, requires_grad=True); b2 = torch.randn(n2, requires_grad=True)

Z1 = X @ W1.T + b1
A1 = torch.relu(Z1)
Z2 = A1 @ W2.T + b2
loss = F.cross_entropy(Z2, y)
loss.backward()

with torch.no_grad():                       # the manual version
    P = F.softmax(Z2, dim=1)
    Y = F.one_hot(y, n2).double()
    D2 = (P - Y) / B                        # dL/dZ2  — the whole module in one line
    gW2 = D2.T @ A1
    gb2 = D2.sum(0)
    D1 = (D2 @ W2) * (Z1 > 0).double()      # backward through W2, then through ReLU
    gW1 = D1.T @ X
    gb1 = D1.sum(0)

for name, mine, auto in [("W2", gW2, W2.grad), ("b2", gb2, b2.grad),
                         ("W1", gW1, W1.grad), ("b1", gb1, b1.grad)]:
    print(name, "max abs diff", (mine - auto).abs().max().item())
```
```
W2 max abs diff 2.220446049250313e-16
b2 max abs diff 5.551115123125783e-17
W1 max abs diff 2.220446049250313e-16
b1 max abs diff 1.1102230246251565e-16
```

Machine epsilon in `float64` is $2.2\times10^{-16}$, so these are *exact* to the last representable bit. That is what a correct backward pass looks like; a maximum difference of $10^{-8}$ would not be "close enough," it would be a bug.

Note `(Z1 > 0)` and not `(A1 > 0)` for the ReLU derivative. They agree here, but the pre-activation is the correct thing to test in general, and using the wrong one is a real source of bugs in layers where the activation can be zero for other reasons.

### Gradient check

```python
def loss_fn(W1v, b1v, W2v, b2v):
    z1 = X @ W1v.T + b1v
    return F.cross_entropy(torch.relu(z1) @ W2v.T + b2v, y)

eps = 1e-6
W1d = W1.detach().clone()
plus  = W1d.clone(); plus[0, 0]  += eps
minus = W1d.clone(); minus[0, 0] -= eps
num = (loss_fn(plus,  b1.detach(), W2.detach(), b2.detach())
     - loss_fn(minus, b1.detach(), W2.detach(), b2.detach())) / (2 * eps)
print("numeric", num.item(), "analytic", W1.grad[0, 0].item(),
      "rel", abs(num.item() - W1.grad[0, 0].item()) / abs(num.item()))
```
```
numeric 0.11473844...  analytic 0.11473844...  rel 1.9e-10
```

### The autograd engine

This is the engine quoted in [Module 05](../../05-backpropagation-and-autodiff.md). It is deliberately close to Karpathy's [micrograd](https://github.com/karpathy/micrograd), which is the canonical minimal version and worth reading alongside.

```python
import math

class Value:
    def __init__(self, data, _children=(), _op=""):
        self.data = data
        self.grad = 0.0
        self._backward = lambda: None
        self._prev = set(_children)
        self._op = _op

    def __add__(self, other):
        other = other if isinstance(other, Value) else Value(other)
        out = Value(self.data + other.data, (self, other), "+")
        def _backward():
            self.grad  += out.grad          # d(a+b)/da = 1
            other.grad += out.grad
        out._backward = _backward
        return out

    def __mul__(self, other):
        other = other if isinstance(other, Value) else Value(other)
        out = Value(self.data * other.data, (self, other), "*")
        def _backward():
            self.grad  += other.data * out.grad     # d(ab)/da = b
            other.grad += self.data  * out.grad
        out._backward = _backward
        return out

    def __pow__(self, k):
        out = Value(self.data ** k, (self,), f"**{k}")
        def _backward():
            self.grad += k * self.data ** (k - 1) * out.grad
        out._backward = _backward
        return out

    def relu(self):
        out = Value(self.data if self.data > 0 else 0.0, (self,), "relu")
        def _backward():
            self.grad += (out.data > 0) * out.grad
        out._backward = _backward
        return out

    def exp(self):
        out = Value(math.exp(self.data), (self,), "exp")
        def _backward():
            self.grad += out.data * out.grad        # d(e^x)/dx = e^x = out
        out._backward = _backward
        return out

    def log(self):
        out = Value(math.log(self.data), (self,), "log")
        def _backward():
            self.grad += (1.0 / self.data) * out.grad
        out._backward = _backward
        return out

    def __neg__(self):       return self * -1
    def __sub__(self, o):    return self + (-o)
    def __radd__(self, o):   return self + o
    def __rmul__(self, o):   return self * o
    def __truediv__(self, o):
        return self * (o ** -1 if isinstance(o, Value) else Value(o) ** -1)

    def backward(self):
        topo, visited = [], set()
        def build(v):
            if v not in visited:
                visited.add(v)
                for child in v._prev:
                    build(child)
                topo.append(v)                      # children appended before parents
        build(self)
        self.grad = 1.0
        for v in reversed(topo):
            v._backward()

    def __repr__(self):
        return f"Value(data={self.data:.4f}, grad={self.grad:.4f})"
```

Verification against PyTorch:

```python
a, b, c, f = Value(2.0), Value(-3.0), Value(10.0), Value(-2.0)
L = (a * b + c) * f
L.backward()
print("micrograd:", a.grad, b.grad, c.grad, f.grad)
```
```
micrograd: 6.0 -4.0 -2.0 4.0
torch    : 6.0 -4.0 -2.0 4.0
```

Exact on arithmetic, as it must be, since the operations are identical. On a nonlinear expression mixing `relu`, `exp` and a square, the two agree to nine decimal places — the residual is `math.exp` versus PyTorch's vectorized exponential, not a difference in the derivative.

Three implementation details are worth calling out. `self.grad += ...` rather than `=`, for the reason in A4. `Value(other)` wrapping so that `x * 2` works without a special case. And the `__radd__` definition, which is what makes Python's built-in `sum()` work on a list of `Value`s — it starts from the integer `0`, and without `__radd__` the very first addition raises.

### Training a network with it

```python
import random, math
random.seed(0)

class Neuron:
    def __init__(self, nin, nonlin=True):
        self.w = [Value(random.uniform(-1, 1) * math.sqrt(2 / nin)) for _ in range(nin)]
        self.b = Value(0.0)
        self.nonlin = nonlin
    def __call__(self, x):
        act = sum((wi * xi for wi, xi in zip(self.w, x)), self.b)
        return act.relu() if self.nonlin else act
    def parameters(self):
        return self.w + [self.b]

class Layer:
    def __init__(self, nin, nout, **kw):
        self.neurons = [Neuron(nin, **kw) for _ in range(nout)]
    def __call__(self, x):
        out = [n(x) for n in self.neurons]
        return out[0] if len(out) == 1 else out
    def parameters(self):
        return [p for n in self.neurons for p in n.parameters()]

class MLP:
    def __init__(self, nin, nouts):
        sizes = [nin] + nouts
        self.layers = [Layer(sizes[i], sizes[i+1], nonlin=i != len(nouts) - 1)
                       for i in range(len(nouts))]
    def __call__(self, x):
        for layer in self.layers:
            x = layer(x)
        return x
    def parameters(self):
        return [p for l in self.layers for p in l.parameters()]

def make_rings(n):                      # two concentric rings: not linearly separable
    X, Y = [], []
    for _ in range(n):
        angle = random.uniform(0, 2 * math.pi)
        r = random.choice([1.0, 2.0])
        X.append([r * math.cos(angle) + random.gauss(0, 0.1),
                  r * math.sin(angle) + random.gauss(0, 0.1)])
        Y.append(1.0 if r == 2.0 else -1.0)
    return X, Y

X, Y = make_rings(100)
model = MLP(2, [16, 16, 1])
print("parameters:", len(model.parameters()))

for step in range(200):
    outs = [model(x) for x in X]
    losses = [(1 + -yi * so).relu() for yi, so in zip(Y, outs)]   # hinge loss
    data_loss = sum(losses) * (1.0 / len(losses))
    reg = 1e-4 * sum((p * p for p in model.parameters()), Value(0.0))
    total = data_loss + reg

    for p in model.parameters():
        p.grad = 0.0
    total.backward()

    lr = 0.1 - 0.09 * step / 200                    # simple linear decay
    for p in model.parameters():
        p.data -= lr * p.grad

    if step % 50 == 0 or step == 199:
        acc = sum((yi > 0) == (so.data > 0) for yi, so in zip(Y, outs)) / len(Y)
        print(f"  step {step:3d}  loss {total.data:.4f}  acc {acc*100:.0f}%")
```
```
parameters: 337
  step   0  loss 1.0182  acc 53%
  step  50  loss 0.5352  acc 75%
  step 100  loss 0.0737  acc 100%
  step 150  loss 0.0184  acc 100%
  step 199  loss 0.0131  acc 100%
```

A 337-parameter neural network, trained to perfect accuracy on a problem no linear model can solve, using nothing but Python's `math` module and about seventy lines of your own code. There is no PyTorch anywhere in that training loop.

Notice what the loop is made of. Build a graph by doing arithmetic. Zero the gradients. Call `backward()`. Step the parameters. That is *exactly* the PyTorch training loop, because PyTorch is exactly this with tensors instead of scalars, C++ kernels instead of Python, and a GPU. Nothing conceptual is added between here and a frontier model — only scale, and the engineering that scale requires.

---

Back to [Set 05](../05-exercises.md) · Next solutions: [Set 06](./06-solutions.md)
