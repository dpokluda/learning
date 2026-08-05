# Exercise Set 08 — Initialization and Normalization

Companion to [Module 08](../08-initialization-and-normalization.md).

## Part A — Questionnaire

1. Derive the He initialization variance $\mathrm{Var}(w) = 2/n_{\text{in}}$ from the requirement that activation variance be preserved through a ReLU layer. Where exactly does the factor of 2 enter, and why does Xavier not have it?

2. Xavier initialization uses $2/(n_{\text{in}} + n_{\text{out}})$ rather than $1/n_{\text{in}}$. What is the second constraint being satisfied, and why is it a compromise rather than a solution?

3. Explain why initializing all weights to the same constant — zero or anything else — makes a layer permanently useless, using the backward pass to make the argument.

4. Write down what BatchNorm computes at training time and at inference time, and explain why they differ. Then name three consequences of the training-time behaviour that bite in practice.

5. LayerNorm normalizes over a different axis than BatchNorm. Say which, and explain why that difference makes LayerNorm the right choice for Transformers and BatchNorm the right choice for convolutional image models.

6. A residual connection is claimed to help gradients flow. Show why by differentiating $\mathbf{y} = \mathbf{x} + F(\mathbf{x})$, and explain what the resulting expression guarantees. Then state what the *degradation problem* is and why it is not overfitting.

## Part B — Coding

**The goal, in prose.** Make a deep network fail, then fix it three different ways, measuring the signal at every layer so you can see exactly what each fix does. This is the module where measurement beats intuition, because the failure modes are invisible in the loss curve — they show up as numbers that quietly go to zero.

**Specifics.**

*Probe the forward signal.* Build a stack of 50 fully-connected layers of width 256 with ReLU between them, feed in standard normal data, and record the standard deviation of the activations at each layer for four initializations: $\mathcal{N}(0,1)$; $\mathcal{N}(0,0.01^2)$; Xavier; He. Report layers 1, 5, 10, 25 and 50. Then repeat with $\tanh$ for Xavier and He, and explain why the ranking changes.

*Discover what PyTorch actually does.* `nn.Linear` does **not** use He initialization by default. Build a 10-hidden-layer ReLU MLP two ways — PyTorch's default and explicit He — and probe the per-layer activation standard deviation for both. Then train both on 10,000 MNIST examples for 8 epochs with SGD + momentum at learning rates 0.01, 0.1 and 0.5, alongside a third variant with BatchNorm after every hidden linear layer. Nine numbers. Predict them first; you will get some of them wrong.

*Explain a plateau.* In the default-init probe you will find the activation standard deviation stops decaying after about four layers and levels off. That is not the signal surviving. Work out what is holding it up, and design a two-line experiment that proves your explanation.

*Measure gradient flow through residual connections.* Build a 30-layer network with and without residual connections, run one backward pass, and report the ratio of gradient norm at layer 1 to gradient norm at layer 30 for each.

**Starter stub.**

```python
h = torch.randn(512, 256)
for layer in range(50):
    W = torch.empty(256, 256)
    nn.init.kaiming_normal_(W, nonlinearity="relu")     # or xavier_normal_, or .normal_(0, 1)
    h = torch.relu(h @ W.T)
    print(layer, h.std().item())
```

---

Solutions: [`solutions/08-solutions.md`](./solutions/08-solutions.md) · Next: [Set 09](./09-exercises.md)
