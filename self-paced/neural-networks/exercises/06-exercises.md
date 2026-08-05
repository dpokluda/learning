# Exercise Set 06 — Optimization

Companion to [Module 06](../06-optimization.md).

## Part A — Questionnaire

1. Explain momentum without using the word "momentum" or any physics analogy. What is the update actually computing, and why does it help?

2. Momentum with $\mu = 0.9$ is often said to give a "10× speedup." Derive where the 10 comes from, and state the condition under which the factor is actually achieved.

3. Adam maintains two running averages. Say what each one is estimating and what each one does to the update. Then explain why dividing by the second one makes Adam roughly scale-invariant, and what that buys you.

4. Adam's bias correction divides by $1-\beta_1^t$ and $1-\beta_2^t$. Why is it needed at all, why does it matter most early in training, and what would go wrong without it?

5. AdamW is described as "decoupled weight decay." Decoupled from what? Show why L2 regularization and weight decay are the same thing for SGD but not for Adam.

6. You are training a model and the loss decreases for 200 steps then jumps to `nan`. List the candidate causes in the order you would check them, and say what single experiment distinguishes the top two.

## Part B — Coding

**The goal, in prose.** Implement the optimizers from their equations and prove your implementations correct by matching PyTorch's to floating-point precision. Then measure what the differences between them are actually worth on a real task, and find a learning rate empirically rather than by guessing.

**Specifics.**

*Implement SGD with momentum and Adam from the update equations*, using only tensor arithmetic under `torch.no_grad()`. Run each alongside the corresponding `torch.optim` optimizer from an identical initialization on an identical sequence of batches, in `float64`, for 20 steps, and compare final parameters. You are aiming for a maximum difference around $10^{-16}$. Be careful with PyTorch's momentum convention — it is $v \leftarrow \mu v + g$, $\theta \leftarrow \theta - \eta v$, with $\eta$ *outside* the velocity, which differs from several textbooks.

*Sweep the optimizers* on MNIST with a `784 → 128 → ReLU → 10` MLP, 3 epochs, batch size 128, identical seed: SGD at 0.01 and 0.1, SGD with momentum 0.9 at 0.1, Adam at $10^{-3}$ and at $10^{-1}$. Predict the ordering before running. The last configuration is there to make a point.

*Run a learning-rate range test.* Starting from $10^{-6}$, multiply the learning rate by a constant factor every batch until it reaches $10$, recording the loss at each step. Plot loss against $\log(\text{lr})$ and identify the minimum. Compare what it recommends against what the sweep found.

**Starter stub.**

```python
import torch, torch.nn as nn
torch.set_default_dtype(torch.float64)

params = list(model.parameters())
m_buf = [torch.zeros_like(p) for p in params]
v_buf = [torch.zeros_like(p) for p in params]
beta1, beta2, eps, lr = 0.9, 0.999, 1e-8, 1e-2

for t in range(1, 21):
    model.zero_grad()
    criterion(model(X), y).backward()
    with torch.no_grad():
        for i, p in enumerate(params):
            g = p.grad
            ...        # the five lines of Adam
```

---

Solutions: [`solutions/06-solutions.md`](./solutions/06-solutions.md) · Next: [Set 07](./07-exercises.md)
