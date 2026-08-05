# 13 — Transfer Learning and Embeddings

Module 12 ended on a practical problem. You now understand the architecture that everything modern is built from, and you also know that GPT-3 cost several million dollars to train and that the models people actually deploy are trained on datasets you do not have and hardware you cannot rent. If the only way to use a Transformer were to train one, this book would end here with an interesting piece of theory and no way to apply it.

It does not, because the central practical fact of modern deep learning is that **nobody trains from scratch**. The dominant workflow is to take a model someone else trained on an enormous general corpus and adapt it to your specific problem with a tiny fraction of the data and compute. That workflow has a name — transfer learning — and it is not a trick or a shortcut. It reflects something true about what these networks learn: that the early and middle layers of a network trained on any sufficiently rich data converge on features that are broadly useful, and that only the last few layers are specialized to the original task.

This module makes that concrete. We will measure how transferable ImageNet features actually are, layer by layer, and see where the transferability runs out. We will compare training from scratch against linear probing against full fine-tuning on the same problem and the same budget, and the gap will be larger than you expect. We will look at the mechanics — freezing, discriminative learning rates, LoRA — and then step back to the more general idea underneath all of it, which is the embedding: the notion that a learned representation is a reusable asset in its own right, independent of the head that was trained on top of it.

The running example shifts to CIFAR-10, deliberately. It is a small dataset of low-resolution natural images, it is nothing like ImageNet in resolution or framing, and 5,000 examples is a realistic amount of labelled data for a problem someone actually has. That is exactly the regime where transfer learning either earns its reputation or does not.

> **Prerequisite:** [Module 10](./10-convolutional-networks.md) for the convolutional architectures being transferred, and [Module 12](./12-attention-and-transformers.md) for the language half of the story. [Module 09](./09-practical-training-and-debugging.md)'s training discipline is assumed throughout.

## Why transfer works at all

Start with the observation that made the field pay attention. When you visualize the filters a convolutional network learns in its first layer — whether it was trained on ImageNet, on faces, on medical scans, or on satellite imagery — you get essentially the same thing: oriented edge detectors, colour-opponent blobs, and small frequency patterns. They look like Gabor filters, which is also what the first stage of the mammalian visual cortex is understood to compute. The network was not told to learn edges. It learned them because edges are what the first stage of *any* useful decomposition of natural images looks like.

That generality does not hold all the way up. As you go deeper, features become progressively more specific to the training task: textures, then object parts, then whole-object detectors, then finally something close to a set of ImageNet-class-specific templates. Yosinski, Clune, Bengio and Lipson quantified this in 2014 with an experiment that is worth understanding precisely, because it is the empirical foundation for everything else in this module.[^m13-yosinski] They split ImageNet's 1,000 classes into two disjoint halves, trained a network on each half, and then measured how well the first $k$ layers from one network served as a feature extractor for the *other* task. Early layers transferred with essentially no loss. Later layers transferred progressively worse. And the drop-off had two distinct causes, which they separated: the features genuinely becoming task-specific, and — more subtly — the *co-adaptation* between adjacent layers being broken when you cut the network in the middle.

We can reproduce the shape of this result in a few minutes. Take a ResNet-18 pretrained on ImageNet, freeze it entirely, extract features from progressively deeper points in the network, and train nothing but a linear classifier on top of each. Any accuracy difference is attributable purely to the quality of the representation, since the classifier is the same trivial model in every case.

| representation | dimension | CIFAR-10 accuracy |
| --- | --- | --- |
| raw pixels | 49,152 | 26.80% |
| after `layer1` | 64 | 54.00% |
| after `layer2` | 128 | 69.25% |
| after `layer3` | 256 | 79.30% |
| after `layer4` | 512 | 79.55% |

Every row is the same linear classifier trained the same way on 5,000 CIFAR-10 images.[^m13-probe] Read it and three things come out.

**Depth buys enormous amounts of linear separability.** Raw pixels give 26.8% — barely better than the 10% of guessing, despite being by far the highest-dimensional representation in the table. A linear model on pixels cannot do this task, which is the same fact [Module 01](./01-what-is-a-neural-network.md) established on MNIST, restated on harder data. Push those pixels through four residual stages that have *never seen a CIFAR-10 image* and the same linear model reaches nearly 80%.

**The useful features are compressions, not expansions.** The 512-dimensional `layer4` representation beats the 49,152-dimensional pixel representation by 53 points while being 96 times smaller. This is the entire content of the phrase "learning a representation": the network has thrown away almost all of the information in the image and kept the part that matters, and what is left is small, dense, and linearly organized.

**And the gain stops.** `layer3` to `layer4` buys 0.25 points — nothing, within noise. That plateau is Yosinski's specificity effect appearing on our data. `layer4` is ResNet-18's last stage, the one immediately below the ImageNet classification head, and its features have been shaped to separate ImageNet's thousand fine-grained categories, including 120 breeds of dog. CIFAR-10 has ten coarse categories. The extra specialization is aimed at distinctions we do not need and does not help.

The practical rule that follows is the one worth memorizing: **the more your task differs from the pretraining task, the earlier you should cut.** For a target task close to ImageNet, use everything and replace only the head. For something distant — spectrograms, satellite imagery, medical scans in a modality with no natural-image analogue — cutting at an intermediate stage often works better, because you get the general visual primitives without the mismatched specialization.

## Three ways to use a pretrained model

There is a spectrum here, and it is worth being precise about the three points on it that matter.

**Training from scratch** ignores the pretrained weights entirely and initializes randomly. Every parameter is trainable. This is what you do when no relevant pretrained model exists, and — increasingly rarely — when you have enough data that pretraining adds nothing.

**Linear probing** freezes the entire pretrained network and trains only a new final layer. The frozen part is a fixed feature extractor; the only learned parameters are in the head. This is what produced the table above.

**Fine-tuning** initializes from the pretrained weights and then trains everything, usually at a much lower learning rate than you would use from scratch. The pretrained weights are a starting point rather than a constraint.

The comparison that matters is what each one buys on the same problem with the same budget. ResNet-18, 5,000 CIFAR-10 training images, 2,000 test images, three epochs each, AdamW:[^m13-transfer]

| approach | trainable parameters | test accuracy | time |
| --- | --- | --- | --- |
| from scratch | 11,181,642 | 46.60% | 17.1 s |
| linear probe | **5,130** | 74.50% | 5.1 s |
| fine-tune | 11,181,642 | **89.40%** | 13.6 s |

This table is the argument for the entire module, and the middle row is the one to sit with. **Training 5,130 parameters beats training 11.2 million by 28 percentage points.** That is a factor of 2,180 fewer trainable parameters, in a third of the wall-clock time, producing a model that is dramatically better. Nothing about scratch training is wrong here — 46.6% on 5,000 images in three epochs is roughly what an 11-million-parameter model should manage with that little data. The point is that the pretrained model is not really starting from 5,000 images. It is starting from 1.2 million.

Fine-tuning then adds another 15 points on top of the probe. Those points come from letting the features themselves adapt: CIFAR-10 images are $32\times32$ upsampled to 128, blurrier and lower-detail than ImageNet photographs, and the frozen ImageNet features are slightly wrong for that domain. Allowing them to move fixes the mismatch. The general pattern holds broadly — linear probing captures most of the benefit for very little cost, and fine-tuning captures the rest at full cost. The decision between them is mostly about how much target data you have, since fine-tuning eleven million parameters on a few hundred examples will simply overfit.

There is a useful diagnostic hiding in the gap between the two rows. If fine-tuning barely beats linear probing, your target domain is close to the pretraining domain and the features were already right. If fine-tuning beats it by a lot, as here, the domains differ and you should suspect that a model pretrained on something closer would do better still.

## Doing it correctly in PyTorch

The mechanics are short, and almost all of the mistakes are in the details around them.

```python
import torch, torch.nn as nn, torchvision as tv

# 1. Load with the modern weights= API, not the deprecated pretrained=True
model = tv.models.resnet18(weights=tv.models.ResNet18_Weights.IMAGENET1K_V1)

# 2. Replace the head. ResNet-18's final layer is `fc`, 512 -> 1000.
model.fc = nn.Linear(512, 10)          # new layer, randomly initialized, requires_grad=True

# 3. For a linear probe, freeze everything except the head
for name, p in model.named_parameters():
    if not name.startswith("fc"):
        p.requires_grad = False

# 4. Pass ONLY the trainable parameters to the optimizer
params = [p for p in model.parameters() if p.requires_grad]
optimizer = torch.optim.AdamW(params, lr=1e-3)
```

Four things go wrong here repeatedly.

**Use the preprocessing the model was trained with.** ImageNet models expect inputs normalized with `mean=[0.485, 0.456, 0.406]` and `std=[0.229, 0.224, 0.225]`, and they expect a certain input scale. Feeding them MNIST-style normalization, or raw $[0,1]$ pixels, silently degrades everything — the model still runs, the loss still decreases, and your accuracy is quietly several points below where it should be. `torchvision`'s weights enums carry the correct transform: `ResNet18_Weights.IMAGENET1K_V1.transforms()` returns it, and using that is strictly safer than hard-coding constants.

**Pass only trainable parameters to the optimizer.** Handing the optimizer parameters with `requires_grad=False` is not an error, and with plain SGD it is harmless. With AdamW it is not, because weight decay is applied to parameters independent of their gradients — so your frozen backbone will slowly decay toward zero while you believe it is frozen. The filter in step 4 is not optional.

**Freeze BatchNorm's statistics, not just its parameters.** Setting `requires_grad = False` stops $\gamma$ and $\beta$ from being updated, but BatchNorm's running mean and variance are *buffers*, not parameters, and they are updated in `train()` mode regardless of any gradient setting. A "frozen" backbone in `train()` mode will have its normalization statistics overwritten by your target data's statistics, which is a well-known and thoroughly confusing bug. If you want a genuinely frozen extractor, call `backbone.eval()` and keep it there, or freeze the BatchNorm modules explicitly.

**Use a lower learning rate than you would from scratch.** The pretrained weights are already good; large steps destroy them before the head has learned anything useful. In the experiment above, scratch training used $10^{-3}$ and fine-tuning used $10^{-4}$, and that order-of-magnitude gap is a reasonable default. The failure mode when you get this wrong even has a name — **catastrophic forgetting** — and the symptom is a fine-tuned model that performs *worse* than the linear probe, because the first few hundred large steps wrecked the representation before the randomly-initialized head produced a meaningful gradient direction.

Two refinements are worth knowing. A short **warmup with the backbone frozen** — train only the head for one epoch, then unfreeze — avoids exactly that failure, because it means the gradients reaching the backbone were computed through a head that already makes sense. And **discriminative learning rates** apply the layer-wise transferability result directly: since early layers are more general and later layers more task-specific, give early layers a smaller learning rate than late ones.

```python
optimizer = torch.optim.AdamW([
    {"params": model.layer1.parameters(), "lr": 1e-5},
    {"params": model.layer2.parameters(), "lr": 3e-5},
    {"params": model.layer3.parameters(), "lr": 1e-4},
    {"params": model.layer4.parameters(), "lr": 3e-4},
    {"params": model.fc.parameters(),     "lr": 1e-3},
])
```

This is standard practice in the fastai library, where it is called discriminative fine-tuning, and it consistently helps on small target datasets.[^m13-ulmfit]

## When the model is too large to fine-tune

Everything above assumes you can afford to compute and store gradients for the whole model. For an 11-million-parameter ResNet that is trivial. For a 7-billion-parameter language model it is not: full fine-tuning requires the weights, the gradients, and the optimizer state — for Adam, two additional tensors per parameter — which in 16-bit precision comes to roughly 16 bytes per parameter, or over 100 GB before you have stored a single activation.

**LoRA** — low-rank adaptation — solves this with an observation about the *update* rather than the model.[^m13-lora] When you fine-tune, the weight matrix changes from $W_0$ to $W_0 + \Delta W$. Hu et al. hypothesized that $\Delta W$, unlike $W_0$, has very low intrinsic rank: adapting a model to a new task is a small, structured change, not an arbitrary one. If that is true, $\Delta W$ can be factored as the product of two thin matrices,

$$W = W_0 + BA, \qquad B \in \mathbb{R}^{d\times r},\ A \in \mathbb{R}^{r\times k},\ r \ll \min(d,k)$$

and you train only $A$ and $B$ while $W_0$ stays frozen. For a $4096\times4096$ weight matrix, full fine-tuning trains 16.8 million parameters; LoRA at rank 8 trains $2 \times 4096 \times 8 = 65{,}536$, a reduction of 256×. Because $W_0$ is frozen, no optimizer state is needed for it, which is where most of the memory saving actually comes from.

Two details make it work in practice. $A$ is initialized randomly and $B$ is initialized to **zero**, so $BA = 0$ at the start and the adapted model is exactly the pretrained model — the same zero-initialization trick as the residual blocks in [Module 08](./08-initialization-and-normalization.md), for the same reason. And after training, $BA$ can be *merged* into $W_0$ by simple addition, so inference costs exactly what the original model cost. There is no runtime penalty, which distinguishes LoRA from adapter methods that insert extra layers.

The consequence is that fine-tuning a large model became something you can do on one consumer GPU, and that in turn is why there are tens of thousands of fine-tuned open-weight models rather than a handful. Note the shape of the argument, though: LoRA rests on an empirical hypothesis about the rank of $\Delta W$ that holds well for the adaptations people typically want and is not a theorem. For a target task genuinely distant from pretraining, low rank may be insufficient, and the honest answer is that you find out by trying it.

## Embeddings: the representation as the product

Step back from classification for a moment. In every setup above, the useful thing was not the model's output — it was the vector just before the output. That vector is an **embedding**, and treating it as the deliverable rather than a means to an end unlocks a large class of applications that have nothing to do with the task the model was trained on.

The idea appeared first, and most legibly, in language. A word is a discrete symbol, and the naive encoding is one-hot: a vocabulary of 50,000 words becomes 50,000 orthogonal vectors. That representation is enormous, sparse, and — the real problem — it makes every pair of distinct words exactly equally dissimilar. "Cat" and "kitten" are as unrelated as "cat" and "parliament."

Word2vec replaced it with a dense vector of a few hundred dimensions, learned by a self-supervised objective: predict a word from its neighbours, or its neighbours from it.[^m13-word2vec] The training signal is nothing but co-occurrence statistics from raw text, with no annotation at all. What emerged was a geometry in which distance encodes similarity and, famously, *direction* encodes relationship — the vector arithmetic $\text{king} - \text{man} + \text{woman}$ lands near $\text{queen}$. GloVe reached similar representations from a different direction, by factorizing a global co-occurrence matrix.[^m13-glove]

That analogy result deserves a caveat, because it is the most-repeated fact about embeddings and it is somewhat oversold. The standard evaluation excludes the three input words from the candidate answers; without that exclusion the nearest neighbour of $\text{king} - \text{man} + \text{woman}$ is frequently just $\text{king}$ again. Later analyses showed the effect is weaker and more dependent on the evaluation protocol than the original presentation suggested.[^m13-analogy] The underlying claim — that the space has meaningful linear structure — survives; the clean parallelogram picture is a simplification.

The more consequential limitation of word2vec is that it assigns **one vector per word type**. "Bank" gets a single vector that must simultaneously serve the riverside and the financial institution, so it lands somewhere unhelpfully in between. Contextual embeddings fix this: a Transformer encoder produces a *different* vector for each occurrence of a word, computed from the whole sentence.[^m13-bert] This is a direct consequence of the architecture in [Module 12](./12-attention-and-transformers.md) — self-attention means every position's representation is a function of every other position — and it is the reason BERT-style models replaced static embeddings for essentially every task.

Embeddings are useful precisely because vector spaces support operations that discrete symbols do not. Semantic search compares a query embedding against a corpus of document embeddings by cosine similarity, which finds documents that mean the same thing rather than documents containing the same words. Retrieval-augmented generation is that search feeding a language model. Recommendation systems embed users and items into a shared space so that a dot product predicts affinity. Clustering and visualization — t-SNE or UMAP over an embedding — reveal structure in unlabelled data. Deduplication, anomaly detection, and few-shot classification by nearest-neighbour all fall out of the same property.

This is why the 512-dimensional `layer4` row in the first table is worth more than its accuracy number suggests. It is not just a good input to a linear classifier; it is a coordinate system for images in which a great many questions become easy.

## A note on distillation

One more way to transfer, mechanically different from the rest. **Knowledge distillation** trains a small *student* model to reproduce the outputs of a large *teacher*, using the teacher's full probability distribution — softened by a temperature — rather than the hard labels.[^m13-distill]

The insight is about what the extra signal contains. A hard label says an image is a "7." The teacher's distribution says it is a 7 with probability 0.9, a 1 with probability 0.07, and a 9 with probability 0.02 — which encodes the teacher's learned knowledge about *which* classes resemble each other, information the one-hot label destroys. Hinton called these the "dark knowledge" in the model, and training against them transfers far more per example than training against labels alone.

The temperature $T$ in $\text{softmax}(z/T)$ controls how much of that structure is exposed: at $T=1$ you get the teacher's ordinary confident output, and at $T=3$ to $5$ the small probabilities are amplified into a usable signal. The gradient of the soft-target loss scales as $1/T^2$, so the convention is to multiply that term by $T^2$ to keep it commensurate with the hard-label loss when both are used.

DistilBERT is the canonical demonstration: 40% smaller and 60% faster than BERT while retaining about 97% of its performance on GLUE.[^m13-distilbert] The general principle — that a small model can reach accuracy it could not reach by training on the labels directly — is now standard practice for deployment, and it is worth noticing what it implies. If the student *can* represent the function but could not *find* it from labels alone, then the difficulty was optimization rather than capacity, which is the same distinction that ran through [Module 08](./08-initialization-and-normalization.md) and [Module 11](./11-sequence-models.md).

## Before you move on

The durable idea in this module is that a trained network's value is mostly in its intermediate representations, not its outputs. Early layers learn features that are general to a whole data modality; later layers specialize to the pretraining task, and the transition between the two is where you decide to cut. Linear probing, which trains a few thousand parameters, beat scratch training of eleven million by 28 points on the same data — and full fine-tuning at a reduced learning rate added fifteen more. When the model is too large to fine-tune whole, LoRA trains a low-rank update instead, at a hundredth of the trainable parameters and no inference cost. And the same vector that feeds a classifier head is an embedding: a coordinate system in which search, clustering, recommendation and retrieval become geometry.

The details that actually cost people accuracy are worth repeating because they are all silent failures. Use the pretraining normalization. Pass only trainable parameters to the optimizer, or AdamW will decay your frozen backbone. Put frozen BatchNorm modules in `eval()` mode, because their running statistics ignore `requires_grad`. And fine-tune at a learning rate roughly an order of magnitude below what you would use from scratch, or warm up with the backbone frozen, so that catastrophic forgetting does not destroy the representation you came for.

If you can explain why layer3 and layer4 gave nearly identical probe accuracy, why linear probing can beat scratch training so decisively on 5,000 images, what LoRA's rank hypothesis actually asserts, and why a teacher's soft distribution teaches more than a hard label, you have this module. [Exercise Set 13](./exercises/13-exercises.md) has you reproduce the layer-wise transferability curve, run the three-way comparison, implement LoRA on a linear layer from scratch, and use embeddings for nearest-neighbour retrieval.

Then [Module 14](./14-modern-landscape.md) takes the natural next question. Pretraining on 1.2 million *labelled* images works this well — what happens when you remove the labelling constraint entirely and pretrain on everything?

## Sources

[^m13-yosinski]: Jason Yosinski, Jeff Clune, Yoshua Bengio and Hod Lipson, ["How transferable are features in deep neural networks?"](https://arxiv.org/abs/1411.1792), NeurIPS 2014. The layer-by-layer transferability experiment, and the separation of specificity from co-adaptation, are both from this paper.

[^m13-probe]: Measured while writing this module: ResNet-18 with `IMAGENET1K_V1` weights, frozen and in `eval()` mode, features taken after each residual stage and globally average-pooled, then a single `nn.Linear` trained with AdamW at $10^{-2}$ (weight decay $10^{-3}$) for 60 epochs on standardized features. 5,000 CIFAR-10 training images resized to 128×128 with ImageNet normalization, evaluated on 2,000 held-out test images. The raw-pixel row uses the same flattened, standardized 128×128×3 input. Full script in the [Module 13 solutions](./exercises/solutions/13-solutions.md).

[^m13-transfer]: Measured: ResNet-18, `fc` replaced by `nn.Linear(512, 10)`, 5,000 CIFAR-10 training images resized to 128×128, 3 epochs, batch size 64, AdamW. Scratch and linear probe at $10^{-3}$, fine-tuning at $10^{-4}$. Evaluated on 2,000 test images. Times are on an Apple M-series GPU and are indicative only.

[^m13-ulmfit]: Jeremy Howard and Sebastian Ruder, ["Universal Language Model Fine-tuning for Text Classification"](https://arxiv.org/abs/1801.06146), ACL 2018, introduces discriminative fine-tuning and gradual unfreezing, and is also the paper that established the pretrain-then-fine-tune recipe for NLP shortly before BERT.

[^m13-lora]: Edward Hu et al., ["LoRA: Low-Rank Adaptation of Large Language Models"](https://arxiv.org/abs/2106.09685), ICLR 2022. Section 4.1 covers the zero-initialization of $B$ and the merge-at-inference property.

[^m13-word2vec]: Tomas Mikolov, Kai Chen, Greg Corrado and Jeffrey Dean, ["Efficient Estimation of Word Representations in Vector Space"](https://arxiv.org/abs/1301.3781), ICLR Workshop 2013, and the companion ["Distributed Representations of Words and Phrases and their Compositionality"](https://arxiv.org/abs/1310.4546), NeurIPS 2013, which adds negative sampling.

[^m13-glove]: Jeffrey Pennington, Richard Socher and Christopher Manning, ["GloVe: Global Vectors for Word Representation"](https://nlp.stanford.edu/pubs/glove.pdf), EMNLP 2014.

[^m13-analogy]: Tal Linzen, ["Issues in evaluating semantic spaces using word analogies"](https://aclanthology.org/W16-2503/), RepEval 2016, shows how much of the analogy result depends on excluding the input words from the candidate set. A useful corrective to the most-repeated claim about embeddings.

[^m13-bert]: Jacob Devlin, Ming-Wei Chang, Kenton Lee and Kristina Toutanova, ["BERT: Pre-training of Deep Bidirectional Transformers for Language Understanding"](https://arxiv.org/abs/1810.04805), NAACL 2019.

[^m13-distill]: Geoffrey Hinton, Oriol Vinyals and Jeff Dean, ["Distilling the Knowledge in a Neural Network"](https://arxiv.org/abs/1503.02531), NeurIPS Deep Learning Workshop 2014. Section 2 covers the temperature and the $T^2$ gradient-scaling correction.

[^m13-distilbert]: Victor Sanh, Lysandre Debut, Julien Chaumond and Thomas Wolf, ["DistilBERT, a distilled version of BERT"](https://arxiv.org/abs/1910.01108), 2019.

**Further reading.** PyTorch's [transfer learning tutorial](https://pytorch.org/tutorials/beginner/transfer_learning_tutorial.html) walks through fine-tuning and feature extraction with runnable code, and the [`torchvision.models` documentation](https://pytorch.org/vision/stable/models.html) is authoritative on the `weights=` API and the per-model preprocessing transforms. *Dive into Deep Learning* [Section 14.2](https://d2l.ai/chapter_computer-vision/fine-tuning.html) covers fine-tuning with a worked hot-dog-classification example, and [Chapter 15](https://d2l.ai/chapter_natural-language-processing-pretraining/) covers word2vec and GloVe in full derivation. The [CS224n notes on word vectors](https://web.stanford.edu/class/cs224n/readings/cs224n-2019-notes01-wordvecs1.pdf) are the clearest short treatment of the skip-gram objective and negative sampling. For LoRA and its successors, the HuggingFace [PEFT library documentation](https://huggingface.co/docs/peft/index) is both a good implementation and a good survey of the parameter-efficient fine-tuning landscape.
