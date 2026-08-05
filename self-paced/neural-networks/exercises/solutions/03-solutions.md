# Solutions — Set 03

Worked answers for [Exercise Set 03](../03-exercises.md).

## Part A — Answers

**1. Universal approximation, stated and bounded.**

The theorem (Cybenko 1989 for sigmoidal activations, Hornik 1991 more generally): let $\phi$ be a non-polynomial continuous activation, $K \subset \mathbb{R}^n$ compact, $f: K \to \mathbb{R}$ continuous, and $\epsilon > 0$. Then there exist a finite $N$, weights $\mathbf{w}_i \in \mathbb{R}^n$, biases $b_i$ and coefficients $\alpha_i$ such that $g(\mathbf{x}) = \sum_{i=1}^{N}\alpha_i\phi(\mathbf{w}_i\cdot\mathbf{x} + b_i)$ satisfies $\sup_{\mathbf{x}\in K}|f(\mathbf{x}) - g(\mathbf{x})| < \epsilon$.

Three things it does not say. It gives **no bound on $N$** — the width may be exponential in the input dimension, so "possible" may mean "requires more units than there are atoms." It says nothing about **learnability**: it asserts that suitable weights *exist*, not that gradient descent from a random start will find them, and those are entirely different claims. And it is a statement about a **compact domain**, so it promises nothing about generalization to inputs outside the region you approximated on.

It is weaker than its reputation because it is an existence theorem about a *representation*, while every practical difficulty in this field is about *optimization* and *generalization*. A lookup table is also a universal approximator and nobody considers that a deep learning result. The theorem's real content is negative: it rules out "networks fundamentally cannot represent this" as an explanation for failure, which pushes all explanations toward optimization and data.

**2. Derivative bounds through depth.**

Backpropagation multiplies by $\phi'(z)$ once per layer. With sigmoid, each factor is at most $1/4$, so after ten layers the gradient is scaled by at most $(1/4)^{10} \approx 9.5\times10^{-7}$ — and that is the *best case*, achieved only if every pre-activation sits exactly at zero. Realistically most units are somewhere on the saturating tails where $\sigma'$ is far below $1/4$, so the true attenuation is worse by orders of magnitude. With tanh the bound is $1$ per layer, so ten layers give at most $1$ — no guaranteed decay, though away from zero tanh also saturates and shrinks the product.

The measurement in Part B makes this concrete: a five-hidden-layer sigmoid network has a first-layer gradient norm of $2.0\times10^{-4}$ against tanh's $1.9\times10^{-1}$, a factor of about a thousand, and it never learns anything at all.

ReLU is a different kind of fix, not a bigger constant. Its derivative is exactly $1$ where the unit is active and exactly $0$ where it is not — there is no *intermediate* regime that shrinks the gradient a little. So the gradient either passes through a unit completely unattenuated or does not pass through it at all. Multiplying by 1 many times gives 1, so there is no exponential decay along any active path, no matter how deep. The price is that some paths are cut entirely, which is the dead-unit problem of question 4. Saturating activations attenuate *every* path a little, which compounds; ReLU kills *some* paths entirely and leaves the rest pristine, which does not.

**3. Non-differentiability at zero.**

Three reasons it does not matter. The probability that a pre-activation lands *exactly* at 0.0 in floating point is essentially zero. ReLU has well-defined one-sided derivatives everywhere and a subgradient at zero — any value in $[0,1]$ is valid, so any choice is a legitimate subgradient step. And gradient descent is already an approximate method operating on a stochastic estimate; a measure-zero set of undefined points is far from the largest source of error in the procedure.

PyTorch returns **0** at exactly zero. The choice is arbitrary within $[0,1]$ but safe, and 0 is the conservative pick because it treats a zero pre-activation as inactive, matching the value ReLU returns there. If you write a custom activation with a kink, pick any subgradient and do not worry about it.

**4. Dead ReLUs.**

A unit is dead when its pre-activation is negative for every input in the dataset. Then its output is always zero, so its gradient is always zero, so its weights never update, so its pre-activation stays negative forever. The state is an absorbing one — the unit is permanently removed from the network, and no amount of further training recovers it.

The usual cause is a large gradient step pushing the bias sharply negative, most often from too high a learning rate. It is worst with a high learning rate, with poor initialization that starts many units already negative, and with unnormalized inputs on a large scale, since the resulting large gradients make the fatal step likely. It is also worse in a network without normalization layers, since BatchNorm's recentering tends to keep pre-activations straddling zero.

Leaky ReLU changes $\max(0, z)$ to $\max(\alpha z, z)$ with $\alpha \approx 0.01$, so the negative branch has a small nonzero slope. A unit that is negative everywhere still receives a small gradient, so it can climb back. In practice the effect is modest — Part B measures **0.0% dead units** for both plain and leaky ReLU at a sensible learning rate, and their accuracies differ by 0.18 points, which is noise. Dead ReLUs are real but they are largely a symptom of a learning rate that is too high, and the fix for that is to fix the learning rate.

**5. Why depth, without appealing to results.**

Because depth buys *exponentially* more expressive structure per parameter for compositional functions. A ReLU network's output is piecewise linear, and the number of linear regions it can carve out grows polynomially with width but **exponentially with depth**. Intuitively, each layer can fold the input space over itself, and folding a folded space multiplies the count rather than adding to it. There are explicit families of functions requiring exponentially many units in a shallow network but only polynomially many in a deep one.

There is a second, non-quantitative argument. Depth matches the compositional structure of the problems we care about. Pixels compose into edges, edges into motifs, motifs into parts, parts into objects — a hierarchy where each level is a simple function of the level below. A deep network's layered structure mirrors that hierarchy, so it can represent it with each layer doing a small amount of work. A shallow network must express the whole composition in one step, which is possible in principle and requires it to enumerate the combinations rather than build them up.

Note both arguments are about *representation efficiency*, not about what is representable at all. Universal approximation already settled that.

**6. A decision procedure for activations.**

Start with **ReLU** unless you have a reason not to. It is the default because it is cheap, it does not saturate on the positive side, and it works. If you are building a Transformer, start with **GELU** instead, because that is what the architecture was tuned with and its smoothness interacts better with the LayerNorm-heavy design; **SwiGLU** if you are following current large-model practice.

Change if you observe a specific symptom. Many dead units and a training loss that plateaus early suggests **LeakyReLU** — but check your learning rate first, because that is more often the real cause. A need for smooth gradients, for instance in a model you intend to differentiate twice, suggests **GELU**, **SiLU** or **Softplus**.

Reach for others only under specific conditions. **Tanh** when you need a bounded, zero-centred output — a gate or a recurrent state, as in Module 11's LSTM. **Sigmoid** only for gates and for the output of a binary classifier, never as a hidden activation in a deep network. And if you find yourself considering an exotic activation to fix a training problem, the problem is almost certainly initialization, normalization or learning rate instead; Modules 08 and 09 are the right places to look.

## Part B — Reference solution

### XOR by hand

```python
import torch, torch.nn.functional as F

X  = torch.tensor([[0., 0.], [0., 1.], [1., 0.], [1., 1.]])
W1 = torch.tensor([[1., 1.], [1., 1.]])     # both units see x1 + x2
b1 = torch.tensor([0., -1.])                # thresholds at 0 and 1
w2 = torch.tensor([1., -2.])
b2 = torch.tensor(0.)

H   = F.relu(X @ W1.T + b1)
out = H @ w2 + b2
print(H)
print(out)
```
```
tensor([[0., 0.],
        [1., 0.],
        [1., 0.],
        [2., 1.]])
tensor([0., 1., 1., 0.])
```

Exactly $[0,1,1,0]$, with no training. This is the construction from Goodfellow, Bengio and Courville §6.1.

What the hidden layer did: both units compute the *same* linear function $x_1 + x_2$, differing only in threshold. Unit 1 fires as $\max(0, s)$ and unit 2 as $\max(0, s-1)$ where $s = x_1+x_2$. The four inputs have $s \in \{0, 1, 1, 2\}$, so in the hidden space they land at $(0,0)$, $(1,0)$, $(1,0)$ and $(2,1)$ — and crucially **the two positive examples collapse onto the same point**. What were two crossed diagonals in the input are now three collinear points, and the output layer's job reduces to "high in the middle, low at both ends," which the single line $h_1 - 2h_2$ achieves.

That is the general mechanism stated concretely: the hidden layer is not adding capacity, it is *changing coordinates* so that a linear boundary suffices. Everything deep networks do is a more elaborate version of this.

### Activation comparison

```python
import torch, torch.nn as nn
from torch.utils.data import DataLoader
from torchvision import datasets, transforms

device = torch.device("cuda" if torch.cuda.is_available()
                      else "mps" if torch.backends.mps.is_available() else "cpu")
tf = transforms.Compose([transforms.ToTensor(), transforms.Normalize((0.1307,), (0.3081,))])
train = datasets.MNIST("./data", train=True,  download=True, transform=tf)
test  = datasets.MNIST("./data", train=False, download=True, transform=tf)
train_loader = DataLoader(train, 128, shuffle=True)
test_loader  = DataLoader(test, 1000)

ACTS = {"sigmoid": nn.Sigmoid, "tanh": nn.Tanh, "relu": nn.ReLU,
        "leaky_relu": nn.LeakyReLU, "gelu": nn.GELU}
criterion = nn.CrossEntropyLoss()

def build(act, depth=5, width=128):
    layers = [nn.Flatten(), nn.Linear(784, width), ACTS[act]()]
    for _ in range(depth - 1):
        layers += [nn.Linear(width, width), ACTS[act]()]
    layers += [nn.Linear(width, 10)]
    return nn.Sequential(*layers)

for act in ACTS:
    torch.manual_seed(0)
    model = build(act).to(device)
    opt = torch.optim.SGD(model.parameters(), lr=0.1)

    # first-layer gradient norm on the very first batch, before any update
    x, y = next(iter(train_loader)); x, y = x.to(device), y.to(device)
    criterion(model(x), y).backward()
    g1 = model[1].weight.grad.norm().item()
    model.zero_grad()

    for _ in range(3):
        for x, y in train_loader:
            x, y = x.to(device), y.to(device)
            opt.zero_grad(); criterion(model(x), y).backward(); opt.step()

    model.eval(); correct = total = 0
    with torch.no_grad():
        for x, y in test_loader:
            x, y = x.to(device), y.to(device)
            correct += (model(x).argmax(1) == y).sum().item(); total += y.numel()

    with torch.no_grad():                      # dead first-layer units
        x, _ = next(iter(test_loader))
        h = model[2](model[1](model[0](x.to(device))))
        dead = 100 * (h.abs().max(0).values == 0).float().mean().item()

    print(f"{act:11s} acc={100*correct/total:6.2f}%  first-layer |grad|={g1:.3e}  dead={dead:.1f}%")
```

### Measured output

| activation | test accuracy | first-layer gradient norm | dead units |
| --- | --- | --- | --- |
| sigmoid | **11.35%** | $2.0\times10^{-4}$ | — |
| tanh | 96.36% | $1.9\times10^{-1}$ | — |
| ReLU | **96.62%** | $4.0\times10^{-2}$ | 0.0% |
| LeakyReLU | 96.44% | $4.1\times10^{-2}$ | 0.0% |
| GELU | 96.22% | $1.1\times10^{-2}$ | — |

The sigmoid network scores **11.35%**, which is chance on MNIST — it did not learn anything in three epochs. Its first-layer gradient is a thousand times smaller than tanh's, and that is the entire explanation: at depth five, $(1/4)^5$ is already $10^{-3}$ before accounting for saturation, so the early layers receive essentially no signal and the network is effectively a random projection feeding a trainable output layer.

This is the exercise's real payload. Every one of these networks is identical apart from one function call, four of them work and one is catastrophically broken, and the reason is a bound on a derivative. Note also that the failure is *depth-dependent* — repeat the experiment with one hidden layer and sigmoid works fine, which is exactly why the field spent decades not noticing the problem.

Among the four that work, the spread is 0.4 points, which is within run-to-run noise. That is the honest and slightly deflating conclusion: **once an activation does not saturate, the choice among the survivors barely matters** on a problem like this. Choose ReLU by default and do not agonize. The dead-unit count of 0.0% for both ReLU variants at this learning rate is the other honest result — raise the learning rate to 1.0 and you will see it climb, which is the experiment worth running next.

---

Back to [Set 03](../03-exercises.md) · Next solutions: [Set 04](./04-solutions.md)
