# 14 — The modern landscape and where to go next

You now have the whole classical stack. You can explain what a network computes, derive and implement backpropagation, choose and reason about an optimizer, diagnose a model that will not learn, and read the architecture of a convolutional network, a recurrent network and a Transformer. That is genuinely the foundation, and almost everything published since 2018 is a recombination of pieces you have already met.

This module is different in kind from the twelve before it. Those built up a body of settled technique with derivations you could check and numbers you could reproduce. This one is a map of the current landscape — the ideas that have reshaped the field since the Transformer, where they came from, which parts are solid and which are moving. It is unavoidably less certain than the rest of the book, and I will flag the uncertainty as we go rather than pretend it away. Read it as orientation, not as doctrine, and treat the further reading as the real content.

> **Prerequisite:** [Module 13](./13-transfer-learning-and-embeddings.md). This module is a survey; nothing later depends on it, so read it at whatever depth interests you.

## The pretraining revolution, stated plainly

The single most consequential change in deep learning since 2012 is not an architecture. It is the realization that **the bottleneck was never labels, and supervised learning was leaving almost all of the data on the floor**.

Module 13 showed you transfer learning with ImageNet: pretrain on a million *labelled* images, then adapt. That already worked well, but it inherited a hard ceiling, because a million hand-labelled images is roughly what humanity was willing to produce. Self-supervised learning removes the ceiling by manufacturing the supervision from the data's own structure. Hide part of the input and predict it from the rest. There is no annotation budget, so the training set becomes *everything*.

For text this is next-token prediction, and its elegance is worth restating from Module 12: a document of $n$ tokens supplies $n$ supervised examples, all computed in a single causally-masked forward pass, at zero labelling cost. BERT's masked-language-modelling variant does the same with bidirectional context. For images, the route was longer. **Contrastive** methods like SimCLR and MoCo learn by pulling two augmented views of the same image together in embedding space while pushing different images apart, which turns out to require either very large batches or a memory bank of negatives.[^m14-contrastive] Then **masked autoencoders** transplanted the BERT recipe directly: mask 75% of the image patches and reconstruct them, which works better than anyone expected and is far simpler.[^m14-mae] And **CLIP** trained on 400 million image-text pairs scraped from the web with a contrastive objective across the two modalities, producing a joint embedding space in which you can classify images into arbitrary categories you name in words, with no training examples at all.[^m14-clip]

If you take one idea from this module, take this one. The reason a modern model seems to know so much is not that its architecture is clever. It is that the objective was cheap enough to run on a corpus large enough to contain the knowledge.

## Scaling laws, and the correction

By 2020 the obvious question was how far the pattern goes. Kaplan et al. measured it and found something startling: language model loss falls as a smooth **power law** in model size, dataset size and compute, straight over more than seven orders of magnitude with no sign of a knee.[^m14-kaplan] Not a vague trend — a straight line on a log-log plot, precise enough to predict the loss of a model you have not trained yet. That predictability is why organizations became willing to spend nine figures on a single training run: the outcome was, in the relevant sense, forecastable.

Kaplan et al. also concluded that given more compute you should mostly make the model *bigger*, and for two years the field followed that advice into ever-larger, relatively under-trained models. In 2022 Hoffmann et al. at DeepMind redid the analysis more carefully — critically, letting the learning-rate schedule adapt to each run's length rather than holding it fixed — and reached a materially different conclusion: **parameters and training tokens should scale roughly in equal proportion**.[^m14-chinchilla] By that accounting the large models of the era were substantially under-trained. Their 70B-parameter Chinchilla, trained on 1.4 trillion tokens, outperformed the 280B-parameter Gopher trained on 300 billion — a quarter the size, four times the data, better results, and cheaper to serve.

I flag this episode deliberately, because it is the most instructive methodological story in recent deep learning. A quantitative result that everyone believed and that everyone was spending real money on was wrong in its practical recommendation, and the error came from a single seemingly-innocuous experimental choice. Current practice has drifted past Chinchilla too: because inference cost is paid forever while training cost is paid once, production models are now deliberately trained well past the compute-optimal point on far more tokens than Chinchilla prescribes. Llama 3's 8B model saw 15 trillion tokens, roughly a hundred times its Chinchilla-optimal allocation.[^m14-llama] The lesson is not "scaling laws are wrong"; it is that a law optimizes whatever objective you wrote down, and "training compute" was never the only thing anyone cared about.

Whether the power laws continue is the field's central open question. They must break eventually — loss is bounded below by the entropy of language itself — and the supply of high-quality human text is finite, which is why synthetic data and data quality have become the active frontier.[^m14-datalimit]

## How a language model becomes an assistant

A pretrained model predicts plausible continuations of internet text. That is not an assistant, and the gap between the two is worth understanding because it is entirely a training-procedure story, not an architecture story.

The first step is **instruction tuning**: ordinary supervised fine-tuning (Module 13, unchanged) on a curated set of instruction-response pairs, which teaches the model that the desired continuation of a question is an answer rather than more questions.[^m14-flan] It is a surprisingly small intervention with a large effect on usability.

The second is **preference optimization**, and its logic is that for most of what we want from an assistant — helpfulness, honesty, harmlessness — we cannot write down a loss function, but humans can reliably say which of two responses is better. **RLHF** turns those comparisons into a learned reward model and then optimizes the language model against it with reinforcement learning, typically PPO.[^m14-rlhf] The pipeline works and produced ChatGPT, but it is fragile: three models in play, a reward model that can be gamed by the policy it is training, and an RL loop that is notoriously sensitive. **DPO** showed that under the standard assumptions you can skip the reward model and the RL entirely, deriving a simple classification-style loss on preference pairs that optimizes the same objective — much simpler, competitive in quality, and now widely used.[^m14-dpo] **Constitutional AI** substitutes a written set of principles and model-generated critiques for much of the human labelling, reducing the human bottleneck.[^m14-cai]

Alongside these sit two techniques that changed what models can do without changing their weights at all. **In-context learning** — the observation that a sufficiently large model can learn a new task from examples placed in its prompt — was GPT-3's headline result and remains only partly explained.[^m14-gpt3] **Chain-of-thought prompting** — asking the model to reason step by step before answering — produces large gains on multi-step problems, and the current generation of reasoning models trains this behaviour in directly with reinforcement learning against verifiable answers rather than eliciting it by prompt.[^m14-cot]

Be appropriately sceptical about **emergent abilities**, the claim that certain capabilities appear abruptly past a scale threshold. It was widely reported and is genuinely contested: Schaeffer et al. argue persuasively that many apparent discontinuities are artifacts of discontinuous metrics like exact-match accuracy, and that the underlying continuous quantities improve smoothly.[^m14-emergent] Both papers are worth reading before forming a view.

## Generative modelling

Everything in this book so far has been *discriminative*: learn $p(y \mid \mathbf{x})$, map an input to a label. Generative modelling learns $p(\mathbf{x})$ itself and lets you sample from it. Four families, in rough historical order.

**Variational autoencoders** encode an input to a distribution over a latent code, sample from it, and decode, training against a reconstruction loss plus a KL term pulling the latent distribution toward a standard Gaussian.[^m14-vae] The reparameterization trick that makes the sampling differentiable is a genuinely clever piece of engineering and worth studying for its own sake. VAEs give you a usable latent space and stable training, at the cost of blurry samples — a direct consequence of the Gaussian likelihood, which is the pixel-space MSE of Module 04 and averages over ambiguity exactly as MSE always does.

**Generative adversarial networks** pit a generator against a discriminator in a minimax game, with the generator trained to fool a classifier that is simultaneously training to catch it.[^m14-gan] For roughly six years GANs produced the sharpest images anyone had seen, culminating in StyleGAN. They are also famously unstable — mode collapse, non-convergence, an equilibrium that is a saddle point rather than a minimum — and by 2022 diffusion had largely displaced them.

**Diffusion models** are the current answer for images, audio and video, and the idea is beautiful. Define a fixed forward process that destroys data by adding Gaussian noise over many steps until nothing is left, then train a network to reverse a single step of it. Sampling starts from pure noise and denoises repeatedly.[^m14-diffusion] The reason this beat GANs is worth stating: the training objective is a plain regression loss with no adversary, so it is *stable*, and the many-step generation process lets the model spend far more computation on a single sample than a one-shot generator can. Stable Diffusion made it practical by running the diffusion in a compressed latent space rather than pixel space.[^m14-ldm]

**Autoregressive models** are the fourth family and, for text, the winner. Factor $p(\mathbf{x}) = \prod_t p(x_t \mid x_{<t})$ and model each conditional — which is exactly the next-token prediction of Module 12. The factorization is exact, the likelihood is tractable, training is stable, and the causal mask makes it parallel. Its weakness is that sampling is inherently sequential, which is why it dominates text (where tokens are discrete and sequences are short) and lost to diffusion for images (where a million pixels in sequence is hopeless).

## Architectures after the Transformer

The Transformer's $O(n^2)$ attention cost, discussed at the end of Module 12, is the pressure driving most current architectural work.

**Mixture of Experts** attacks a different axis: replace the feedforward sublayer with many parallel experts and route each token to only one or two of them.[^m14-moe] Parameter count grows enormously while the FLOPs per token stay flat, which decouples capacity from compute — a model can *know* far more without *costing* more per token. Mixtral and several frontier models are built this way; the difficulties are load balancing across experts and the memory needed to hold all of them.

**State-space models** revive recurrence in a form that can be parallelized. Mamba's selective SSM achieves linear scaling in sequence length with a hardware-aware parallel scan, and is competitive with Transformers at moderate scale.[^m14-mamba] Whether it displaces attention at frontier scale is genuinely undecided as of this writing; the pragmatic answer so far has been hybrid stacks that interleave both.

And a great deal of the practical progress is not architectural at all. Grouped-query attention shrinks the KV cache that dominates inference memory; **quantization** to 8 or 4 bits makes large models fit on consumer hardware; **LoRA**, which Module 13 covered, made fine-tuning cheap enough to be routine; FlashAttention made exact attention memory-efficient. The unglamorous systems work is where much of the last few years' usable gains actually came from.

## What is genuinely unsolved

It is worth being clear about the limits, because the marketing rarely is.

We do not have a satisfying theory of **why deep networks generalize**. Module 07 covered this: classical capacity bounds predict that networks able to memorize random labels should not generalize, and they demonstrably do; double descent contradicts the U-shaped curve; the implicit regularization of SGD is real but not well characterized. There is progress, but no settled account.

**Interpretability** is improving fast and still immature. Mechanistic interpretability has produced real results — induction heads, sparse-autoencoder feature decomposition, identified circuits — but no one can give a complete account of why a frontier model produced a particular output.[^m14-interp]

**Robustness and calibration** remain weak. Adversarial examples have not been solved in the decade since they were found; models are overconfident out of distribution; and language models produce fluent falsehoods with the same confidence as facts, which is a direct consequence of an objective that rewards plausibility rather than truth.

**Data efficiency** is the starkest gap. A child learns a word from a handful of exposures. A frontier model needs trillions of tokens. Whatever humans are doing, it is not what we are doing.

**Alignment** — ensuring capable systems reliably do what we intend — is unsolved in a way that grows more consequential with capability. RLHF and its successors are engineering approximations, not solutions, and reward hacking is an observed phenomenon rather than a theoretical worry.

## Where to go next

Concretely, three routes from here, and I would pick one rather than sampling all three.

**Build something end to end.** The exercise capstone is a start; the real step is a project with data you care about, which will teach you the unglamorous 80% — data cleaning, label noise, evaluation design, the deployment gap — that no course covers well. Reimplementing a paper from scratch is the other high-yield version of this, and Karpathy's [nanoGPT](https://github.com/karpathy/nanoGPT) and [minGPT](https://github.com/karpathy/minGPT) are the best-designed on-ramps in existence.

**Go deeper on theory.** Work through the *Deep Learning* book properly, especially Part III on probabilistic models. Add Murphy's *Probabilistic Machine Learning* for the statistical foundations. Then read primary papers on a schedule — one a week, taking notes, checking the math — because the ability to read a paper critically is what separates practitioners who keep up from those who fall behind.

**Specialize.** Pick a domain — NLP, vision, speech, RL, graphs, scientific ML — and go deep. [CS224n](https://web.stanford.edu/class/cs224n/) and [CS231n](https://cs231n.github.io/) are the canonical entry points for the first two and both post materials publicly. Follow a small number of researchers rather than the firehose.

Whichever you choose, keep the habit this book has tried to build: **measure the claim**. Nearly every module here contained a number that came out differently from what the standard story predicted — the LSTM that failed where the RNN succeeded until its forget bias was fixed, the residual demo that did not work until the block's internal design was right, the regularization that improved calibration far more than accuracy, the scaling law that was correct and still gave the wrong advice. The field moves fast and its folklore is unreliable. Running the experiment takes twenty minutes and is the difference between knowing and repeating.

## Before you move on

The organizing idea of the last several years is that supervision was the bottleneck and self-supervised objectives removed it, letting models train on all the data rather than the labelled fraction. Scaling laws made the returns to that predictable enough to justify enormous investment, and the Chinchilla correction is a standing reminder that a precisely-measured law still only optimizes the objective you wrote down. Turning a pretrained model into an assistant is a training-procedure story — instruction tuning then preference optimization — not an architectural one. Generative modelling has four families whose relative fortunes were decided by training stability and the structure of the data: diffusion won images because its loss has no adversary and its many-step sampling buys computation, and autoregression won text because the factorization is exact and the causal mask makes it parallel.

If you can explain why self-supervised pretraining removed the labelling ceiling, what Chinchilla changed and why production models now deliberately ignore it, why diffusion displaced GANs, and what Mixture of Experts decouples from what — you have the map. The [reference module](./15-reference.md) is a glossary, formula sheet and consolidated source list to return to, and the [capstone exercise](./exercises/14-capstone.md) is where you put the whole book together.

## Sources

[^m14-contrastive]: Ting Chen et al., ["A Simple Framework for Contrastive Learning of Visual Representations" (SimCLR)](https://arxiv.org/abs/2002.05709), ICML 2020; Kaiming He et al., ["Momentum Contrast for Unsupervised Visual Representation Learning" (MoCo)](https://arxiv.org/abs/1911.05722), CVPR 2020; Jean-Bastien Grill et al., ["Bootstrap Your Own Latent" (BYOL)](https://arxiv.org/abs/2006.07733), NeurIPS 2020, which surprised everyone by working without negatives at all.

[^m14-mae]: Kaiming He et al., ["Masked Autoencoders Are Scalable Vision Learners"](https://arxiv.org/abs/2111.06377), CVPR 2022.

[^m14-clip]: Alec Radford et al., ["Learning Transferable Visual Models From Natural Language Supervision" (CLIP)](https://arxiv.org/abs/2103.00020), ICML 2021.

[^m14-kaplan]: Jared Kaplan et al., ["Scaling Laws for Neural Language Models"](https://arxiv.org/abs/2001.08361), 2020. Figure 1 is the three power laws.

[^m14-chinchilla]: Jordan Hoffmann et al., ["Training Compute-Optimal Large Language Models"](https://arxiv.org/abs/2203.15556), NeurIPS 2022. The methodological difference from Kaplan et al. — cosine schedules matched to each run's length — is discussed in their Section 3 and is the source of the disagreement.

[^m14-llama]: Aakanksha Dubey et al., ["The Llama 3 Herd of Models"](https://arxiv.org/abs/2407.21783), 2024. The 8B model's 15T-token budget is far past compute-optimal and is justified by inference economics.

[^m14-datalimit]: Pablo Villalobos et al., ["Will we run out of data? Limits of LLM scaling based on human-generated data"](https://arxiv.org/abs/2211.04325), 2022/2024.

[^m14-flan]: Jason Wei et al., ["Finetuned Language Models Are Zero-Shot Learners" (FLAN)](https://arxiv.org/abs/2109.01652), ICLR 2022; Long Ouyang et al., ["Training language models to follow instructions with human feedback" (InstructGPT)](https://arxiv.org/abs/2203.02155), NeurIPS 2022.

[^m14-rlhf]: Paul Christiano et al., ["Deep Reinforcement Learning from Human Preferences"](https://arxiv.org/abs/1706.03741), NeurIPS 2017, is the origin; Ouyang et al. 2022 (above) is the language-model application; John Schulman et al., ["Proximal Policy Optimization Algorithms"](https://arxiv.org/abs/1707.06347), 2017, is the RL algorithm used.

[^m14-dpo]: Rafael Rafailov et al., ["Direct Preference Optimization: Your Language Model is Secretly a Reward Model"](https://arxiv.org/abs/2305.18290), NeurIPS 2023.

[^m14-cai]: Yuntao Bai et al., ["Constitutional AI: Harmlessness from AI Feedback"](https://arxiv.org/abs/2212.08073), 2022.

[^m14-gpt3]: Tom Brown et al., ["Language Models are Few-Shot Learners"](https://arxiv.org/abs/2005.14165), NeurIPS 2020. For a mechanistic account of one contributing mechanism see Catherine Olsson et al., ["In-context Learning and Induction Heads"](https://transformer-circuits.pub/2022/in-context-learning-and-induction-heads/index.html), 2022.

[^m14-cot]: Jason Wei et al., ["Chain-of-Thought Prompting Elicits Reasoning in Large Language Models"](https://arxiv.org/abs/2201.11903), NeurIPS 2022; DeepSeek-AI, ["DeepSeek-R1: Incentivizing Reasoning Capability in LLMs via Reinforcement Learning"](https://arxiv.org/abs/2501.12948), 2025, for the RL-trained version.

[^m14-emergent]: Jason Wei et al., ["Emergent Abilities of Large Language Models"](https://arxiv.org/abs/2206.07682), TMLR 2022, versus Rylan Schaeffer, Brando Miranda and Sanmi Koyejo, ["Are Emergent Abilities of Large Language Models a Mirage?"](https://arxiv.org/abs/2304.15004), NeurIPS 2023. Read both.

[^m14-vae]: Diederik Kingma and Max Welling, ["Auto-Encoding Variational Bayes"](https://arxiv.org/abs/1312.6114), ICLR 2014.

[^m14-gan]: Ian Goodfellow et al., ["Generative Adversarial Networks"](https://arxiv.org/abs/1406.2661), NeurIPS 2014; Tero Karras, Samuli Laine and Timo Aila, ["A Style-Based Generator Architecture for GANs" (StyleGAN)](https://arxiv.org/abs/1812.04948), CVPR 2019.

[^m14-diffusion]: Jonathan Ho, Ajay Jain and Pieter Abbeel, ["Denoising Diffusion Probabilistic Models"](https://arxiv.org/abs/2006.11239), NeurIPS 2020, building on Jascha Sohl-Dickstein et al., ["Deep Unsupervised Learning using Nonequilibrium Thermodynamics"](https://arxiv.org/abs/1503.03585), ICML 2015. Lilian Weng's ["What are Diffusion Models?"](https://lilianweng.github.io/posts/2021-07-11-diffusion-models/) is the clearest derivation available.

[^m14-ldm]: Robin Rombach et al., ["High-Resolution Image Synthesis with Latent Diffusion Models"](https://arxiv.org/abs/2112.10752), CVPR 2022.

[^m14-moe]: Noam Shazeer et al., ["Outrageously Large Neural Networks: The Sparsely-Gated Mixture-of-Experts Layer"](https://arxiv.org/abs/1701.06538), ICLR 2017; William Fedus, Barret Zoph and Noam Shazeer, ["Switch Transformers"](https://arxiv.org/abs/2101.03961), JMLR 2022.

[^m14-mamba]: Albert Gu and Tri Dao, ["Mamba: Linear-Time Sequence Modeling with Selective State Spaces"](https://arxiv.org/abs/2312.00752), 2023.

[^m14-interp]: Nelson Elhage et al., ["A Mathematical Framework for Transformer Circuits"](https://transformer-circuits.pub/2021/framework/index.html), 2021; Trenton Bricken et al., ["Towards Monosemanticity: Decomposing Language Models With Dictionary Learning"](https://transformer-circuits.pub/2023/monosemantic-features/index.html), 2023.

**Further reading.** *Deep Learning* [Part III](https://www.deeplearningbook.org/) covers the probabilistic and generative material, though it predates diffusion and Transformers entirely. Kevin Murphy's [*Probabilistic Machine Learning: Advanced Topics*](https://probml.github.io/pml-book/book2.html) is free online and is the current best single reference for generative models and modern probabilistic methods. Lilian Weng's [Lil'Log](https://lilianweng.github.io/) is the most reliable technical blog in the field, with deep, correct posts on diffusion, RLHF and attention variants. [Transformer Circuits](https://transformer-circuits.pub/) is where mechanistic interpretability is published. For staying current, [Papers with Code](https://paperswithcode.com/), [Hugging Face](https://huggingface.co/papers) and the [Stanford AI Index](https://aiindex.stanford.edu/report/) are all better filters than raw arXiv. And [Sebastian Raschka's blog](https://magazine.sebastianraschka.com/) is unusually good at explaining recent LLM developments with working code.
