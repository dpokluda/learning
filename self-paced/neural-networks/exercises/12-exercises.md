# Exercise Set 12 — Attention and Transformers

Companion to [Module 12](../12-attention-and-transformers.md).

## Part A — Questionnaire

1. Explain attention as a soft dictionary lookup. What plays the role of the key, the query and the value, and what makes it *soft* rather than a hard lookup?

2. Derive why the scaling factor is $\sqrt{d_k}$ specifically. Compute $\mathrm{Var}(\mathbf{q}\cdot\mathbf{k})$ for independent zero-mean unit-variance components, then explain what goes wrong at large $d_k$ without the scaling — being precise about whether the problem is the forward pass or the backward pass.

3. $W^Q$ and $W^K$ could in principle be a single matrix, since only the product $\mathbf{q}^\top\mathbf{k} = \mathbf{x}_i^\top (W^Q)^\top W^K \mathbf{x}_j$ appears. Explain why they are kept separate anyway.

4. Multi-head attention with $h$ heads is often said to cost the same as single-head attention. Show why, and explain what the heads actually buy you if they are not buying capacity.

5. Dropping recurrence makes self-attention permutation-equivariant. Explain what that means, why it is a problem, and compare sinusoidal, learned and rotary position encodings on the axis of what each can extrapolate to.

6. Compare pre-norm and post-norm Transformer blocks. Which did the original paper use, which is used now, and what specifically does the change buy?

## Part B — Coding

**The goal, in prose.** Build scaled dot-product attention and multi-head attention from the equations, verify them against PyTorch to floating-point precision, measure the saturation effect that motivates the scaling factor, and then assemble the whole thing into a small decoder-only language model that actually generates text. By the end of this set you will have written every component of a GPT.

**Specifics.**

*Implement scaled dot-product attention* — including an optional causal mask — and verify against `F.scaled_dot_product_attention` in `float64`, both masked and unmasked.

*Measure the variance claim.* Draw $\mathbf{q}$ and $\mathbf{k}$ with i.i.d. standard normal components and report the empirical variance of $\mathbf{q}\cdot\mathbf{k}$ for $d_k \in \{4, 16, 64, 256\}$, before and after scaling. Then, for each $d_k$, report the maximum attention weight, the entropy of the attention distribution, and the magnitude of the gradient flowing back through the softmax — with and without the $1/\sqrt{d_k}$ factor. The gradient column is the one that matters.

*Implement multi-head attention* from scratch and verify against `nn.MultiheadAttention`. The trap: PyTorch packs $W^Q$, $W^K$ and $W^V$ into a single `in_proj_weight` of shape $(3E, E)$, stacked in that order.

*Verify the sinusoidal positional encoding's properties.* Show that all position vectors have the same norm, and that $P_{i} \cdot P_{i+k}$ depends only on $k$ and not on $i$ — the property that makes relative position learnable from an absolute encoding.

*Build a character-level GPT.* Decoder-only, four pre-norm blocks, four heads, embedding dimension 128, context 128 characters, weight tying between the token embedding and the output head, trained on the tiny Shakespeare corpus for 2,000 steps with AdamW and a one-cycle schedule. Before training, check the initial loss against $\ln V$. **You will fail that check the first time** — the model will report a loss around 82 rather than 4.17. Diagnose it, fix it, and quantify what the fix is worth in final validation loss.

**Starter stub.**

```python
def sdpa(q, k, v, mask=None):
    d_k = q.size(-1)
    scores = q @ k.transpose(-2, -1) / math.sqrt(d_k)
    if mask is not None:
        scores = scores.masked_fill(mask, float("-inf"))
    return scores.softmax(-1) @ v

# tiny Shakespeare, ~1.1 MB:
# curl -O https://raw.githubusercontent.com/karpathy/char-rnn/master/data/tinyshakespeare/input.txt
```

---

Solutions: [`solutions/12-solutions.md`](./solutions/12-solutions.md) · Next: [Set 13](./13-exercises.md)
