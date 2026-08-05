# Exercise Set 05 — Backpropagation and automatic differentiation

Companion to [Module 05](../05-backpropagation-and-autodiff.md). This is the most important set in the book. Give it a full sitting.

## Part A — Questionnaire

1. Backpropagation is often described as "the algorithm that trains neural networks." Correct that statement. What does backpropagation actually compute, and what does the training?

2. Explain why reverse-mode differentiation is the right choice for neural networks and forward-mode is not. Your answer should refer to the number of inputs and outputs and to the cost of a matrix product, and it should say when forward-mode *would* be preferable.

3. Write the four backpropagation equations from memory. Then explain, for each, what shape every object has and why the shapes work out.

4. In an autograd engine, why does the backward pass *accumulate* (`+=`) into gradients rather than assign? Give two distinct situations where assignment would give the wrong answer.

5. Why must the backward pass visit nodes in reverse topological order rather than, say, breadth-first from the output? Construct a small graph where a wrong order gives a wrong answer.

6. You write a custom layer and its backward pass. Describe the gradient check you would run, the formula you would use, what tolerance you would accept, and two ways the check can report a failure that is not actually a bug.

## Part B — Coding

**The goal, in prose.** Remove all mystery from autograd by building one. You will derive backprop for a two-layer network on paper, implement it with explicit tensor operations, and confirm it matches PyTorch to floating-point precision. Then you will write a scalar-valued automatic differentiation engine from nothing — no PyTorch, no NumPy — and train a real (small) neural network with it.

**Specifics.**

*Manual backprop.* For a network `x → Linear → ReLU → Linear → cross-entropy`, derive $\partial\mathcal{L}/\partial W_1, \mathbf{b}_1, W_2, \mathbf{b}_2$ on paper in the batched, row-major layout PyTorch uses. Implement them with tensor operations only, and compare against `autograd` in `float64`. You are aiming for a maximum absolute difference around $10^{-16}$; anything larger than about $10^{-12}$ is a real bug.

*Gradient check.* Verify one weight with a central finite difference, $(\mathcal{L}(\theta + \epsilon) - \mathcal{L}(\theta - \epsilon))/2\epsilon$ at $\epsilon = 10^{-6}$ in `float64`. Expect a relative error around $10^{-10}$.

*An autograd engine.* Implement a `Value` class holding a scalar, a gradient, and a closure that knows how to push gradient to its inputs. Support `+`, `*`, `**`, `relu`, `exp`, `log`, unary negation, subtraction and division. Implement `backward()` with a topological sort. Test it against PyTorch on an arithmetic expression and on a nonlinear one.

*Train something with it.* Build `Neuron`, `Layer` and `MLP` classes on top of `Value`, generate a two-dimensional dataset that is not linearly separable — two concentric rings works well — and train a `2 → 16 → 16 → 1` network to classify it, with a hinge loss and L2 regularization, using nothing but your own engine. Aim for 100% training accuracy in a couple of hundred steps.

**Starter stub.**

```python
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
            ...        # push out.grad to self and other
        out._backward = _backward
        return out

    def backward(self):
        ...            # topological sort, seed self.grad = 1.0, walk in reverse
```

**What you should be able to say afterwards.** How PyTorch computes a gradient, with no step left as magic.

---

Solutions: [`solutions/05-solutions.md`](./solutions/05-solutions.md) · Next: [Set 06](./06-exercises.md)
