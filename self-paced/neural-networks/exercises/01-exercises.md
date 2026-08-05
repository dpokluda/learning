# Exercise Set 01 — What a neural network is

Companion to [Module 01](../01-what-is-a-neural-network.md). Answer Part A from memory before opening anything.

## Part A — Questionnaire

Write your answers in sentences before checking [the solutions](./solutions/01-solutions.md).

1. A neural network is described in the module as "a parameterized function fitted by gradient descent." Which of those three components — the parameterization, the fitting, the gradient — was the actual bottleneck between 1969 and 1986, and which was the bottleneck between 1990 and 2012? Be specific about what changed each time.

2. The perceptron cannot represent XOR. Explain *why* in terms of the geometry of the decision boundary, and then explain why adding a hidden layer fixes it. Your explanation should not use the phrase "more capacity."

3. Deep learning "works now and did not work in 1990." The module gives several reasons. Rank them by how much you think each contributed, and defend your top choice. There is no single correct ranking — the point is whether your argument holds together.

4. A linear model reaches 92.35% on MNIST and an MLP with one hidden layer reaches 97.70%. Both are trained the same way on the same data. What, concretely, can the MLP represent that the linear model cannot? Give an example of a specific visual pattern.

5. Someone tells you that a neural network "learns features automatically." What does that actually mean in terms of the function being computed, and what is the equivalent thing a human had to do before?

6. Why is it misleading to say a neuron "fires" when its activation is high? What is the biological analogy actually good for, and where does it break down?

## Part B — Coding

**The goal, in prose.** Establish the baseline the whole book builds on. Train two models on MNIST — one with no hidden layer and one with a single hidden layer — under an identical training procedure, and confirm for yourself that the gap between them is real and comes from the nonlinearity rather than from the extra parameters. Then find a case where the linear model's failure is visible rather than merely numerical.

**Specifics.**

Train `nn.Linear(784, 10)` and a `784 → 128 → ReLU → 10` MLP for 10 epochs with SGD at learning rate 0.1, batch size 64, on the standard MNIST split, reporting test accuracy for each. You should land near 92% and 98%.

Then do the part that matters. Add a third model: `784 → 128 → 10` with **no activation function** between the layers. It has the same parameter count as the MLP. Predict its accuracy before you run it, then run it. Explain the result.

Finally, look at *which* digits each model gets wrong. Build a confusion matrix for the linear model and for the MLP, and find the class pair where the linear model is worst. Argue for why that particular confusion is one a linear boundary cannot resolve.

**Starter stub.**

```python
import torch, torch.nn as nn
from torch.utils.data import DataLoader
from torchvision import datasets, transforms

torch.manual_seed(0)
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
    if kind == "deep_linear":
        ...   # your turn: same shape as the MLP, no activation
    raise ValueError(kind)

def train_and_eval(model, epochs=10, lr=0.1):
    ...   # SGD, cross-entropy, return test accuracy
```

**What you should be able to say afterwards.** Why two stacked linear layers are exactly one linear layer, in one sentence, with the matrix algebra to back it up.

---

Solutions: [`solutions/01-solutions.md`](./solutions/01-solutions.md) · Next: [Set 02](./02-exercises.md)
