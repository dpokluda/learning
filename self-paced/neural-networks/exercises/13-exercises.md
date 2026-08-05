# Exercise Set 13 — Transfer Learning and Embeddings

Companion to [Module 13](../13-transfer-learning-and-embeddings.md).

## Part A — Questionnaire

1. Yosinski et al. found that transferability degrades with depth for two distinct reasons. Name both, explain how they differ, and say which one a longer fine-tuning run can repair.

2. You have 300 labelled examples for a new image task. Argue for linear probing over fine-tuning, then say what would change your mind.

3. Freezing a backbone with `requires_grad = False` is not sufficient to freeze it. Explain what still changes and how to actually stop it.

4. State LoRA's core hypothesis precisely. Why is $B$ initialized to zero rather than randomly, and why can the adapter be merged at inference with no runtime cost?

5. "Static word embeddings assign one vector per word type." Explain why that is a fundamental limitation, and what property of self-attention removes it.

6. Knowledge distillation trains a student on the teacher's soft distribution rather than hard labels. Explain what extra information the soft targets carry, why temperature is needed to expose it, and what the result implies about whether the student's failure to learn from labels alone was a capacity problem.

## Part B — Coding

**The goal, in prose.** Measure transferability rather than assuming it, then build the two mechanisms that make transfer practical — parameter-efficient adaptation, and embeddings as a standalone product.

**Specifics.**

*Reproduce the layer-wise transferability curve.* Take an ImageNet-pretrained ResNet-18, freeze it, and extract globally-average-pooled features after each of `layer1` through `layer4`. Train a single `nn.Linear` on each set of features using 5,000 CIFAR-10 images, and evaluate on 2,000. Include raw flattened pixels as a baseline. Plot accuracy against depth and explain the shape — particularly what happens between `layer3` and `layer4`.

*Run the three-way comparison.* Same backbone, same data, three epochs each: train from scratch, linear probe, and full fine-tune. Report trainable parameter counts alongside accuracy. Use $10^{-3}$ for scratch and probe, $10^{-4}$ for fine-tuning, and be able to explain the difference.

*Implement LoRA on a linear layer from scratch.* Your `LoRALinear` should wrap an `nn.Linear`, freeze it, and add a trainable rank-$r$ update. Verify two properties numerically: that at initialization the wrapped layer is *exactly* equivalent to the original, and that after training the adapter can be merged into a plain `nn.Linear` producing the same outputs. Tabulate trainable parameters against $r$.

*Use embeddings for retrieval.* Extract 512-dimensional penultimate features for 2,000 CIFAR-10 test images from the frozen backbone, L2-normalize them, and compute the cosine-similarity matrix. For each image, check whether its top-$k$ nearest neighbours share its class, for $k \in \{1, 5, 10\}$. Compare against the same procedure on raw pixels. Note that **no classifier is trained at any point** — this measures the representation alone.

**Starter stub.**

```python
class LoRALinear(nn.Module):
    def __init__(self, base: nn.Linear, r=8, alpha=16):
        super().__init__()
        self.base, self.scale = base, alpha / r
        for p in self.base.parameters():
            p.requires_grad = False
        self.A = nn.Parameter(torch.randn(r, base.in_features) * 0.01)
        self.B = nn.Parameter(torch.zeros(base.out_features, r))   # zero on purpose
    def forward(self, x):
        return self.base(x) + F.linear(F.linear(x, self.A), self.B) * self.scale
```

---

Solutions: [`solutions/13-solutions.md`](./solutions/13-solutions.md) · Next: [Capstone](./14-capstone.md)
