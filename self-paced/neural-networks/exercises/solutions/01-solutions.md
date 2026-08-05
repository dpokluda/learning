# Solutions — Set 01

Worked answers for [Exercise Set 01](../01-exercises.md). Read only after attempting both parts.

## Part A — Answers

**1. The two bottlenecks.**

Between 1969 and 1986 the bottleneck was **the fitting procedure**, not the parameterization. Multi-layer networks were already conceivable — Minsky and Papert's critique was explicitly about the *single-layer* perceptron — but nobody had an efficient way to compute gradients through a multi-layer composition, so nobody could train one. Rumelhart, Hinton and Williams' 1986 paper did not invent a new kind of network; it popularized an efficient way to get the gradient, which turned an unusable model class into a usable one.

Between 1990 and 2012 the bottleneck was neither: backpropagation worked and networks were well understood. What was missing was **scale on both sides of the equation** — enough labelled data that a large model would not simply memorize, and enough compute that training one was a matter of days rather than months. ImageNet supplied the first and consumer GPUs the second, and AlexNet was the demonstration. A secondary contributor was a handful of technique fixes (ReLU, dropout, better initialization) that removed failure modes which had made deep networks seem fundamentally untrainable rather than merely slow.

The honest summary is that the first bottleneck was an idea and the second was resources.

**2. XOR and geometry.**

A perceptron computes $\mathbb{1}[\mathbf{w}\cdot\mathbf{x} + b > 0]$. The set of points where $\mathbf{w}\cdot\mathbf{x}+b = 0$ is a hyperplane — in two dimensions, a straight line — and the classifier assigns one label to everything on one side and the other label to everything on the other. So the *only* functions a perceptron can represent are those whose two classes can be separated by a single straight cut.

XOR's positive examples are $(0,1)$ and $(1,0)$; its negatives are $(0,0)$ and $(1,1)$. Those are the two diagonals of the unit square, and each diagonal's two points lie on opposite sides of any line you draw through the square. No single cut separates them, so no perceptron represents XOR. This is not a statement about how hard the function is; it is a statement about the shape of the available decision boundaries.

A hidden layer fixes it by changing the coordinate system before the cut is made. Each hidden unit draws its own line and reports which side of it the input fell on; the hidden layer's output is the input re-expressed in terms of those answers. Choose two hidden units — say one detecting $x_1 + x_2 > 0.5$ and one detecting $x_1 + x_2 > 1.5$ — and in that new two-dimensional space the four points are no longer arranged as two crossed diagonals, and a single line does separate them. The hidden layer is not adding "capacity" in some vague sense; it is performing a *change of representation* after which the problem is linearly separable. Module 03 gives the explicit weights.

**3. Why now and not in 1990.**

A defensible ranking, with the caveat that the question invites disagreement:

Data first. Deep networks are high-variance estimators, and without a training set large enough to constrain them they memorize. The jump from a few thousand labelled images to ImageNet's 1.2 million is what made large models statistically viable at all, and no amount of compute substitutes for it.

Compute second, and closely behind. The GPU turned a training run from "a graduate student's entire year" into "a week," and the effect of that is not linear — it changes how many ideas you can try, which changes how fast the technique improves. It is also what made the *data* usable, since a million images is only an asset if you can afford to iterate over it.

Technique third. ReLU, dropout, better initialization, batch normalization and Adam each removed a specific failure mode. They matter enormously in practice, but they are largely refinements that made a viable approach reliable rather than the thing that made it viable.

Software and community last but not zero. The gap between "possible in principle" and "an afternoon's work" is what determines how many people attempt it, and frameworks closed that gap.

My top choice is data, on the argument that compute without data gets you an overfitted model faster, whereas data without compute merely makes you wait. But the honest answer is that they are complements rather than substitutes and the ranking is somewhat artificial.

**4. What the MLP can represent.**

A linear model scores each class as a fixed weighted sum of pixel intensities, so its entire model of "this is a 7" is a single template — a 784-number image that it correlates against the input. It cannot express any *conditional* relationship between pixels: it cannot say "this pixel matters only if that other one is also on."

An MLP can. Concretely, consider distinguishing a 7 from a 9. Both have a horizontal stroke near the top and a descending stroke; the difference is whether the top stroke *closes into a loop*. Closure is a conjunction — the loop exists only if several specific stroke segments are all present together — and a conjunction is exactly what a linear function cannot compute, since it needs a nonlinearity to implement "all of these, not just some of them." A hidden unit can fire for one stroke segment, another for a second, and the output layer can then require the combination. The experimental confirmation is in Part B: 7-classified-as-9 is the linear model's single worst confusion.

**5. "Learns features automatically."**

Before deep learning, a vision system was a pipeline: a human designed a feature extractor (SIFT, HOG, edge histograms), which mapped raw pixels to a vector of hand-chosen measurements, and a learned classifier ran on that vector. The human chose the representation and the machine chose the boundary.

In a deep network, the hidden layers *are* the feature extractor, and their parameters are learned by the same gradient descent that trains the classifier. Formally, write the network as $f(\mathbf{x}) = g(h(\mathbf{x}))$ where $h$ is everything up to the last layer and $g$ is the final linear map. Classical pipelines fixed $h$ by hand and fitted $g$. Deep learning fits both, jointly, against the end task. "Automatic feature learning" means precisely that $h$ has parameters and receives gradient.

The consequence is that the representation is optimized for *this* task rather than for a human's general intuition about what matters — which is also why the learned features transfer well to related tasks, as Module 13 explores.

**6. "Firing" neurons.**

The analogy is good for one thing: it conveys that a unit integrates many weighted inputs and produces a single output that depends on whether that sum crosses some threshold-like regime. That much genuinely carries over, and it is why the vocabulary stuck.

Everything else breaks. A biological neuron communicates in discrete spikes whose *timing* carries information; an artificial unit emits a continuous real number with no temporal dimension. Real neurons have complex dendritic computation, dozens of neurotransmitters, and are not synchronized into layers. Most decisively, there is no biological mechanism resembling backpropagation — the brain does not appear to compute exact gradients of a global loss and propagate them backward through the same synapses used in the forward pass, a difficulty known as the weight transport problem.

Saying a unit "fires" is misleading mainly because it suggests a binary event where there is a real number, and because it imports a false sense that the model is a simplified brain rather than a piece of applied mathematics that borrowed some vocabulary. Calling it an *activation* is both more accurate and less romantic.

## Part B — Reference solution

The script below is what produced the numbers quoted in [Module 01](../../01-what-is-a-neural-network.md).

```python
import torch, torch.nn as nn
from torch.utils.data import DataLoader
from torchvision import datasets, transforms

device = torch.device("cuda" if torch.cuda.is_available()
                      else "mps" if torch.backends.mps.is_available() else "cpu")

tf = transforms.Compose([transforms.ToTensor(), transforms.Normalize((0.1307,), (0.3081,))])
train = datasets.MNIST("./data", train=True,  download=True, transform=tf)
test  = datasets.MNIST("./data", train=False, download=True, transform=tf)
train_loader = DataLoader(train, batch_size=64, shuffle=True)
test_loader  = DataLoader(test,  batch_size=1000)

def make_model(kind):
    if kind == "linear":
        return nn.Sequential(nn.Flatten(), nn.Linear(784, 10))
    if kind == "mlp":
        return nn.Sequential(nn.Flatten(), nn.Linear(784, 128), nn.ReLU(), nn.Linear(128, 10))
    if kind == "deep_linear":                      # same shape, no nonlinearity
        return nn.Sequential(nn.Flatten(), nn.Linear(784, 128), nn.Linear(128, 10))
    raise ValueError(kind)

criterion = nn.CrossEntropyLoss()

def train_and_eval(kind, epochs=10, lr=0.1):
    torch.manual_seed(0)                            # same init story for every model
    model = make_model(kind).to(device)
    opt = torch.optim.SGD(model.parameters(), lr=lr)

    for _ in range(epochs):
        model.train()
        for x, y in train_loader:
            x, y = x.to(device), y.to(device)
            opt.zero_grad()
            criterion(model(x), y).backward()
            opt.step()

    model.eval()
    confusion = torch.zeros(10, 10, dtype=torch.long)
    correct = total = 0
    with torch.no_grad():
        for x, y in test_loader:
            pred = model(x.to(device)).argmax(1).cpu()
            for t, p in zip(y, pred):
                confusion[t, p] += 1
            correct += (pred == y).sum().item()
            total += y.numel()

    n_params = sum(p.numel() for p in model.parameters())
    print(f"{kind:12s} params={n_params:>7,}  acc={100*correct/total:.2f}%")
    return confusion

C_linear = train_and_eval("linear")
C_mlp    = train_and_eval("mlp")
_        = train_and_eval("deep_linear")

for name, C in (("linear", C_linear), ("mlp", C_mlp)):
    off = C.clone(); off.fill_diagonal_(0)
    vals, idx = off.flatten().topk(3)
    print(f"  {name} worst confusions:",
          [(int(i // 10), int(i % 10), int(v)) for v, i in zip(vals, idx)])
```

### Measured output

```
linear       params=  7,850  acc=91.58%
mlp          params=101,770  acc=97.90%
deep_linear  params=101,770  acc=91.43%
  linear worst confusions: [(7, 9, 69), (5, 3, 54), (2, 8, 54)]
  mlp worst confusions:    [(8, 3, 10), (2, 7, 9), (5, 3, 9)]
```

Trust the first two digits. Accuracies move by up to a percentage point across devices, seeds and library versions — the module quotes 92.35% and 97.70% from a CPU run with a different data order — but the *ordering* and the *size of the gaps* are completely stable, and those are what the exercise is about.

### The deep-linear result, which is the point

The no-activation model has **101,770 parameters, exactly as many as the MLP**, and scores **91.43%** — statistically indistinguishable from the 7,850-parameter linear model and more than six points below the MLP. Thirteen times the parameters bought nothing at all.

The algebra says it must. Two stacked linear layers compute

$$W_2(W_1\mathbf{x} + \mathbf{b}_1) + \mathbf{b}_2 = (W_2W_1)\mathbf{x} + (W_2\mathbf{b}_1 + \mathbf{b}_2) = W'\mathbf{x} + \mathbf{b}'$$

and $W' = W_2W_1$ is a single $10\times784$ matrix. The composition is *exactly* a linear model — not approximately, not usually, but as an algebraic identity. The function class is identical; only the parameterization differs. Since $W_2W_1$ has rank at most 128, which exceeds the 10 rows it needs, the deep version cannot even represent a smaller set of functions; it just has a redundant coordinate system for the same ones.

If you predicted "about the same as linear," you had the right model of what is going on. If you predicted "somewhere in between," the useful correction is that depth without nonlinearity is not partial nonlinearity — it is no nonlinearity. The activation function is not a detail that improves a deep network; it is the thing that makes it deep at all.

### The confusion result

The linear model's worst error is **7 misclassified as 9, 69 times** — over a quarter of its total errors on those two classes. The MLP's worst single confusion is 10 instances, a seven-fold reduction.

Seven and nine share nearly all of their ink: a horizontal-ish stroke at the top and a stroke descending to the lower right. Under a template-matching model, which is precisely what a linear classifier is, they correlate strongly, and the linear model's "7" template and "9" template necessarily overlap heavily. The distinguishing feature is that the 9's upper stroke *closes into a loop* while the 7's does not — a property of how several strokes relate to one another rather than a property of any pixel or weighted pixel sum. Detecting closure requires a conjunction ("this segment AND that segment AND the gap between them filled"), and conjunctions are exactly what a linear function cannot express, since a weighted sum cannot represent "all of these together" distinctly from "several of these individually."

That is the entire argument of the module in one measured statistic, and it is the reason the rest of the book exists.

---

Back to [Set 01](../01-exercises.md) · Next solutions: [Set 02](./02-solutions.md)
