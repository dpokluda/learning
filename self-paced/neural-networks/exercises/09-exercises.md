# Exercise Set 09 — Practical Training and Debugging

Companion to [Module 09](../09-practical-training-and-debugging.md).

## Part A — Questionnaire

1. Your ten-class classifier's loss sits at exactly 2.303 from step one and never moves. Enumerate the possible causes and give the diagnostic that distinguishes them.

2. Explain why "overfit one batch" is a *diagnostic* rather than merely an encouraging sign. What does passing it rule out, and — just as important — what does it not rule out?

3. Bergstra and Bengio showed random search beats grid search on the same budget. Reconstruct the argument. Under what condition would grid search be the better choice?

4. Why must normalization statistics be computed on the training split only? Give a concrete scenario where using full-dataset statistics produces a validation score you cannot reproduce in production.

5. `zero_grad`, `train`/`eval`, `no_grad` and `.item()`. State what each one does and what goes wrong when it is missing.

6. You have three splits and a fixed compute budget. Describe the discipline that keeps the test number honest, and say what has gone wrong the moment you find yourself checking test accuracy every epoch.

## Part B — Coding

**The goal, in prose.** This set is different: instead of building something, you will fix five programs that are subtly wrong. Each contains exactly one bug, each bug is one that experienced practitioners still ship, and — the point of the exercise — each produces a *distinctive symptom*. Your job is to run each script, read the symptom, name the bug from the symptom alone before looking at the code, then fix it and confirm the improvement.

Resist the urge to diff against a working version. The skill being trained is the inference from symptom to cause, because that is the only part of debugging that is transferable.

**The five scripts.** All train a `784 → 256 → ReLU → 10` MLP on 10,000 MNIST examples for 3 epochs. Below is the common scaffold; each bug is a small mutation of it.

```python
import torch, torch.nn as nn, torch.nn.functional as F
from torch.utils.data import DataLoader, Subset
from torchvision import datasets, transforms

tf = transforms.Compose([transforms.ToTensor(), transforms.Normalize((0.1307,), (0.3081,))])
train = Subset(datasets.MNIST("./data", train=True, download=True, transform=tf), range(10000))
test  = datasets.MNIST("./data", train=False, transform=tf)
train_loader = DataLoader(train, batch_size=128, shuffle=True)
test_loader  = DataLoader(test, batch_size=1000)
criterion = nn.CrossEntropyLoss()

def make(dropout=0.0):
    torch.manual_seed(0)
    layers = [nn.Flatten(), nn.Linear(784, 256), nn.ReLU()]
    if dropout: layers.append(nn.Dropout(dropout))
    return nn.Sequential(*layers, nn.Linear(256, 10))
```

**Script 1.**
```python
model, opt = make(), torch.optim.SGD(model.parameters(), lr=0.1)
for epoch in range(3):
    model.train()
    for x, y in train_loader:
        loss = criterion(model(x), y)
        loss.backward()
        opt.step()
```

**Script 2.**
```python
model, opt = make(), torch.optim.SGD(model.parameters(), lr=0.1, momentum=0.9)
for epoch in range(3):
    model.train()
    for x, y in train_loader:
        opt.zero_grad()
        loss = criterion(F.softmax(model(x), dim=1), y)
        loss.backward(); opt.step()
```

**Script 3.**
```python
model, opt = make(dropout=0.5), torch.optim.SGD(model.parameters(), lr=0.1, momentum=0.9)
for epoch in range(3):
    model.train()
    for x, y in train_loader:
        opt.zero_grad(); criterion(model(x), y).backward(); opt.step()

correct = total = 0                       # evaluation
with torch.no_grad():
    for x, y in test_loader:
        correct += (model(x).argmax(1) == y).sum().item(); total += y.numel()
print(f"test acc {100*correct/total:.2f}%")
```
Run the evaluation block three times and compare.

**Script 4.**
```python
X = torch.stack([train[i][0] for i in range(10000)])
y = torch.tensor([train[i][1] for i in range(10000)])
y = y[torch.randperm(len(y))]             # "shuffling the data"
loader = DataLoader(torch.utils.data.TensorDataset(X, y), batch_size=128, shuffle=True)
```

**Script 5.**
```python
model, opt = make(), torch.optim.SGD(model.parameters(), lr=10.0, momentum=0.9)
```
Print the loss every step for the first twenty steps.

**Then, having fixed all five**, write the training loop you would actually use: correct splits, an initial-loss sanity check against $\ln K$, an overfit-one-batch test, gradient clipping, a cosine learning-rate schedule, early stopping with best-checkpoint restore, and a final single evaluation on the sealed test set. Aim for 98%+ on full MNIST.

---

Solutions: [`solutions/09-solutions.md`](./solutions/09-solutions.md) · Next: [Set 10](./10-exercises.md)
