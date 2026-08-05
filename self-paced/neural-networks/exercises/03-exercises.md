# Exercise Set 03 — Feedforward networks and activations

Companion to [Module 03](../03-feedforward-networks-and-activations.md).

## Part A — Questionnaire

1. State the universal approximation theorem precisely enough that someone could check whether a given claim violates it. Then name three things it does *not* say. Why is it a much weaker result than it is usually presented as?

2. Sigmoid's derivative is at most $1/4$; tanh's is at most $1$. Explain what that difference does to a gradient after it has passed back through ten layers, with the arithmetic. Then explain why ReLU's derivative being exactly 1 on half its domain is a different kind of fix rather than just a bigger number.

3. ReLU is not differentiable at zero. Why is this not a problem in practice? What does PyTorch actually return for the derivative there, and why is the choice arbitrary but safe?

4. What is a "dead ReLU"? Describe the mechanism that creates one, why it is permanent, and what Leaky ReLU changes about it. Under what circumstance would you expect the problem to be worst?

5. If a wide-enough one-hidden-layer network can approximate any function, why does anyone build deep networks? Give an argument that does not appeal to empirical results.

6. You are choosing an activation for a new architecture. Give a decision procedure — what you would try first, what would make you change, and what you would only reach for under specific conditions.

## Part B — Coding

**The goal, in prose.** Two things. First, convince yourself that a hidden layer solves XOR by constructing the weights *by hand* — no training — so you can see the mechanism rather than trusting it. Second, measure activation functions against each other in the setting where the difference actually shows up, which is not a shallow network.

**Specifics.**

For the XOR part, implement the two-hidden-unit ReLU network with weights you write down yourself, and verify it produces exactly $[0, 1, 1, 0]$ on the four inputs. Then examine the hidden-layer activations for all four inputs and explain, in a sentence, what the hidden layer has done to the geometry. (Module 03 gives the construction; try to derive it before looking.)

For the activation comparison, build a deliberately deep MLP — `784 → 128 → 128 → 128 → 128 → 128 → 10`, five hidden layers — and train it on MNIST for 3 epochs with plain SGD at learning rate 0.1, batch size 128, once for each of sigmoid, tanh, ReLU, LeakyReLU and GELU, with an identical seed. Report test accuracy, and also record the gradient norm of the *first* layer's weights on the very first batch, before any training. Predict the ordering before you run it.

Finally, measure the dead-ReLU rate: after training, count what fraction of first-layer units produce zero for *every* example in a test batch.

**Starter stub.**

```python
import torch, torch.nn as nn, torch.nn.functional as F

# --- XOR by hand ---
X = torch.tensor([[0., 0.], [0., 1.], [1., 0.], [1., 1.]])
W1 = torch.tensor([[?, ?], [?, ?]])     # you fill these in
b1 = torch.tensor([?, ?])
w2 = torch.tensor([?, ?])
b2 = torch.tensor(0.)
H = F.relu(X @ W1.T + b1)
out = H @ w2 + b2
print(H, out)                            # target: [0, 1, 1, 0]

# --- activation comparison ---
ACTS = {"sigmoid": nn.Sigmoid, "tanh": nn.Tanh, "relu": nn.ReLU,
        "leaky_relu": nn.LeakyReLU, "gelu": nn.GELU}

def build(act, depth=5, width=128):
    ...   # Flatten, then `depth` blocks of Linear+act, then Linear(width, 10)
```

---

Solutions: [`solutions/03-solutions.md`](./solutions/03-solutions.md) · Next: [Set 04](./04-exercises.md)
