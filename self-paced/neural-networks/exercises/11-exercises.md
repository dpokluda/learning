# Exercise Set 11 — Sequence Models

Companion to [Module 11](../11-sequence-models.md).

## Part A — Questionnaire

1. An RNN is described as "a feedforward network with tied weights, unrolled in time." Make that precise, and explain what backpropagation through time is actually doing that ordinary backpropagation is not.

2. Show why the gradient of an RNN loss with respect to an early hidden state involves a product of $T$ Jacobians, and state the condition on the recurrent weight matrix under which that product vanishes or explodes.

3. Gradient clipping addresses exploding gradients but not vanishing ones. Explain why the asymmetry is fundamental rather than a limitation of the technique.

4. Write down the LSTM equations and say, for each of the four gates, what it controls and what would break if you removed it. Then explain in one sentence why the cell state $\mathbf{c}_t$ is the part that solves the vanishing-gradient problem.

5. GRU merges two of the LSTM's gates and eliminates the separate cell state. Which merge, and what is the practical consequence — both what you gain and what you might lose?

6. Describe the seq2seq bottleneck. Why is compressing an arbitrarily long input into one fixed-size vector a structural problem rather than a capacity problem you could solve with a bigger hidden state?

## Part B — Coding

**The goal, in prose.** Build the recurrent cells from their equations and verify them against PyTorch, then run the long-dependency experiment that is supposed to show LSTMs beating plain RNNs. It will not show that — at least not at first — and chasing down why will teach you more about LSTMs than the equations do.

**Specifics.**

*Implement the RNN, LSTM and GRU cells* from their equations and check each against `nn.RNNCell`, `nn.LSTMCell` and `nn.GRUCell` in `float64`. Then unroll your RNN cell manually over a sequence and check the full trajectory against `nn.RNN`. Watch out for a detail that will cost you an hour if you miss it: PyTorch's recurrent layers carry **two** bias vectors, `bias_ih` and `bias_hh`, and both are added. Note also the gate orderings — LSTM packs $[i, f, g, o]$ and GRU packs $[r, z, n]$.

*Measure gradient decay empirically.* Feed a random sequence of length $T$ through an RNN and through an LSTM, backpropagate from the final output, and report the ratio of the gradient norm at $t=1$ to the gradient norm at $t=T$ for $T \in \{10, 30, 60\}$. Separately, verify the spectral-radius prediction: construct recurrent matrices with spectral radius 0.9, 1.0 and 1.1 and measure $\lVert W^{50}\mathbf{h}\rVert$.

*Run the long-dependency task.* Sequences of length $T$ drawn from a vocabulary of 8; the **first** token is one of two signal values and the rest are noise; the model must predict which signal it was from the hidden state at time $T$. This is the cleanest possible test of long-range memory, since the answer is at the maximum possible distance from the readout. Train an RNN, an LSTM and a GRU on $T = 50$ at 250, 500, 1000, 2000 and 4000 steps, and compare.

*Then explain what you find.* The result will contradict what you expect. Diagnose it — the fix is one line and it takes the LSTM from chance to perfect. State the fix, explain the mechanism, and confirm the analogous fix works for the GRU.

**Starter stub.**

```python
V = 8
def make_batch(n, T):
    x = torch.randint(3, V, (n, T))          # noise tokens are 3..7
    signal = torch.randint(1, 3, (n,))       # signal is 1 or 2
    x[:, 0] = signal                         # ...placed at the far end
    return x, signal - 1
```

---

Solutions: [`solutions/11-solutions.md`](./solutions/11-solutions.md) · Next: [Set 12](./12-exercises.md)
