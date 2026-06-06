# 12-Week AI & LLM Engineering Plan

A hands-on, engineering-focused learning path to understand how AI and LLMs work and integrate them into production software.

---

## Overview

This plan is designed for a **principal software engineer** seeking to understand AI and LLMs from the ground up — from theory and algorithms to system integration and deployment.

It is divided into **three phases**:
1. **Foundations** (Weeks 1–4)
2. **LLMs & Practical Engineering** (Weeks 5–8)
3. **Integration & Enterprise Patterns** (Weeks 9–12)

---

## Detailed Plan

### **Phase 1 — Foundations (Weeks 1–4)**

Goal: Build a strong base in ML, neural networks, and transformers.

#### **Week 1 — AI & ML Fundamentals**

* **Topics**

  * Types of ML (supervised, unsupervised, RL)
  * Model training process, evaluation, overfitting
  * Feature engineering basics
* **Resources**

  * [Machine Learning Crash Course (Google)](https://developers.google.com/machine-learning/crash-course)
  * [CS229 Lecture Notes (Stanford)](https://cs229.stanford.edu/main_notes.pdf) — skim Sections 1–3
* **Hands-On**

  * Implement a linear regression and logistic regression from scratch (NumPy)

#### **Week 2 — Neural Networks & Optimization**

* **Topics**

  * Forward & backward propagation
  * Activation functions
  * Gradient descent variants (SGD, Adam)
* **Resources**

  * [3Blue1Brown — Neural Networks Playlist](https://www.youtube.com/playlist?list=PLZHQObOWTQDPD3MizzM2xVFitgF8hE_ab)
  * [PyTorch 60-Minute Blitz](https://pytorch.org/tutorials/beginner/deep_learning_60min_blitz.html)
* **Hands-On**

  * Build a simple feedforward network in PyTorch for MNIST

#### **Week 3 — Sequence Models & Word Embeddings**

* **Topics**

  * One-hot encoding, embeddings
  * word2vec, GloVe, contextual embeddings
* **Resources**

  * [The Illustrated Word2Vec](https://jalammar.github.io/illustrated-word2vec/)
  * [GloVe Project](https://nlp.stanford.edu/projects/glove/)
* **Hands-On**

  * Train a word2vec model with [gensim](https://radimrehurek.com/gensim/)

#### **Week 4 — Transformers Deep Dive**

* **Topics**

  * Self-attention, multi-head attention, positional encoding
  * Encoder–decoder vs. decoder-only architectures
* **Resources**

  * [The Illustrated Transformer](https://jalammar.github.io/illustrated-transformer/)
  * [Annotated Transformer (Harvard NLP)](http://nlp.seas.harvard.edu/2018/04/03/attention.html)
* **Hands-On**

  * Implement a small transformer in PyTorch
  * Use Hugging Face Transformers to run a pretrained GPT-2

---

### **Phase 2 — LLMs & Practical Engineering (Weeks 5–8)**

Goal: Understand LLM internals and start integrating them.

#### **Week 5 — Tokenization & LLM Pretraining**

* **Topics**

  * Byte Pair Encoding (BPE), SentencePiece
  * Causal language modeling
* **Resources**

  * [How GPT Tokenizer Works](https://platform.openai.com/tokenizer)
  * [SentencePiece GitHub](https://github.com/google/sentencepiece)
* **Hands-On**

  * Tokenize text with Hugging Face `tokenizers`

#### **Week 6 — Fine-Tuning & LoRA**

* **Topics**

  * Full fine-tuning vs. parameter-efficient tuning
  * Low-Rank Adaptation (LoRA)
* **Resources**

  * [Hugging Face Course: Fine-Tuning](https://huggingface.co/course/chapter3)
  * [PEFT Library](https://github.com/huggingface/peft)
* **Hands-On**

  * Fine-tune a small model on a domain dataset with LoRA

#### **Week 7 — RLHF & Model Alignment**

* **Topics**

  * Reinforcement Learning with Human Feedback
  * Preference modeling & policy optimization
* **Resources**

  * [OpenAI Blog — Fine-Tuning GPT Models](https://openai.com/research/learning-from-human-feedback)
  * [DeepLearning.AI RLHF Course](https://learn.deeplearning.ai/chatgpt-prompt-eng/rlhf)
* **Hands-On**

  * Implement a simple preference-based re-ranking

#### **Week 8 — Hosting & Scaling Inference**

* **Topics**

  * Deployment patterns (serverless, GPU nodes)
  * Quantization, distillation
* **Resources**

  * [Transformers for Production — Hugging Face](https://huggingface.co/docs/transformers/serialization)
  * [BitsAndBytes](https://github.com/TimDettmers/bitsandbytes) for quantization
* **Hands-On**

  * Deploy a small model to an API endpoint (FastAPI)

---

### **Phase 3 — LLM Integration & Enterprise Patterns (Weeks 9–12)**

Goal: Build production-grade LLM features and integrate them with real systems.

#### **Week 9 — Retrieval-Augmented Generation (RAG)**

* **Topics**

  * Vector databases (FAISS, Pinecone, Milvus)
  * Chunking and embedding strategies
* **Resources**

  * [Hugging Face RAG Tutorial](https://huggingface.co/docs/transformers/model_doc/rag)
  * [LangChain Docs: RAG](https://python.langchain.com/docs/use_cases/question_answering/)
* **Hands-On**

  * Build a document Q\&A system using FAISS + OpenAI API

#### **Week 10 — Function Calling & Orchestration**

* **Topics**

  * Tool-augmented LLMs
  * Agent architectures
* **Resources**

  * [LangChain Agents](https://python.langchain.com/docs/modules/agents/)
  * [Azure OpenAI Function Calling](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/function-calling)
* **Hands-On**

  * Create an LLM agent that calls real APIs (e.g., weather, finance)

#### **Week 11 — Guardrails, Testing & Monitoring**

* **Topics**

  * Output validation & filtering
  * AI-specific unit tests
  * Latency & cost monitoring
* **Resources**

  * [Guardrails AI](https://shreyar.github.io/guardrails/)
  * [DeepEval](https://docs.confident-ai.com/docs/evaluation)
* **Hands-On**

  * Build a test suite for your RAG system

#### **Week 12 — Capstone Project**

* **Goal**

  * Build and deploy a **full-stack AI-powered app** that uses:

    * RAG
    * Tool calling
    * Guardrails
* **Example**

  * “Internal Knowledge Assistant” — an LLM that answers engineering questions from company docs and triggers internal APIs
* **Deploy**

  * Containerize and deploy to Azure Kubernetes Service or similar

---

### **Recommended Ongoing Reading**

* *Deep Learning* — Goodfellow, Bengio, Courville
* *Designing Machine Learning Systems* — Chip Huyen
* [Hugging Face Course](https://huggingface.co/course/chapter1) (parallel reference)
* [Transformer Papers with Code](https://paperswithcode.com/method/transformer)

---

## Week-by-Week Plan

| **Week** | **Focus**                            | **Key Topics**                                               | **Primary Resources**                                        | **Hands-On Deliverables**                                    |
| -------- | ------------------------------------ | ------------------------------------------------------------ | ------------------------------------------------------------ | ------------------------------------------------------------ |
| **1**    | AI & ML Fundamentals                 | ML types (supervised/unsupervised/RL), training cycle, overfitting, evaluation metrics | [Google ML Crash Course](https://developers.google.com/machine-learning/crash-course), [CS229 Notes: Intro](https://cs229.stanford.edu/syllabus.html) | Implement linear & logistic regression from scratch in NumPy; run on toy dataset |
| **2**    | Neural Networks & Optimization       | Forward/backpropagation, activations, gradient descent (SGD, Adam) | [3Blue1Brown Neural Networks](https://www.youtube.com/playlist?list=PLZHQObOWTQDPD3MizzM2xVFitgF8hE_ab), [PyTorch Blitz](https://pytorch.org/tutorials/beginner/deep_learning_60min_blitz.html) | Build MNIST classifier in PyTorch with 1–2 hidden layers     |
| **3**    | Sequence Models & Embeddings         | One-hot, embeddings, word2vec, GloVe                         | [Illustrated Word2Vec](https://jalammar.github.io/illustrated-word2vec/), [GloVe Project](https://nlp.stanford.edu/projects/glove/) | Train small word2vec model using `gensim`; visualize embedding space |
| **4**    | Transformers Deep Dive               | Self-attention, multi-head attention, positional encoding, GPT vs BERT | [Illustrated Transformer](https://jalammar.github.io/illustrated-transformer/), [Annotated Transformer](http://nlp.seas.harvard.edu/2018/04/03/attention.html) | Implement minimal transformer block in PyTorch; run GPT-2 with Hugging Face |
| **5**    | Tokenization & LLM Pretraining       | BPE, SentencePiece, causal LM objective                      | [OpenAI Tokenizer Tool](https://platform.openai.com/tokenizer), [SentencePiece GitHub](https://github.com/google/sentencepiece) | Tokenize corpus with HF `tokenizers`; inspect token distribution |
| **6**    | Fine-Tuning & LoRA                   | Full vs parameter-efficient tuning, LoRA                     | [HF Course: Fine-Tuning](https://huggingface.co/course/chapter3), [PEFT Library](https://github.com/huggingface/peft) | Fine-tune small model (e.g., DistilGPT-2) on domain text with LoRA |
| **7**    | RLHF & Model Alignment               | RLHF process, reward modeling, PPO basics                    | [OpenAI Blog: Human Feedback](https://openai.com/research/learning-from-human-feedback), [DeepLearning.AI RLHF](https://learn.deeplearning.ai/chatgpt-prompt-eng/rlhf) | Implement simple preference re-ranking pipeline for generated outputs |
| **8**    | Hosting & Scaling Inference          | Model serving, quantization, distillation, GPUs              | [HF Deployment Docs](https://huggingface.co/docs/transformers/serialization), [BitsAndBytes](https://github.com/TimDettmers/bitsandbytes) | Deploy quantized model to FastAPI endpoint; test latency & throughput |
| **9**    | Retrieval-Augmented Generation (RAG) | Vector DBs (FAISS, Pinecone), chunking strategies            | [HF RAG Tutorial](https://huggingface.co/docs/transformers/model_doc/rag), [LangChain RAG](https://python.langchain.com/docs/use_cases/question_answering/) | Build doc Q&A system with FAISS + OpenAI API; test with your own PDF |
| **10**   | Function Calling & Orchestration     | Tool-augmented LLMs, agent frameworks                        | [LangChain Agents](https://python.langchain.com/docs/modules/agents/), [Azure OpenAI Function Calling](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/function-calling) | Create LLM agent that queries an external API (weather, finance, etc.) |
| **11**   | Guardrails, Testing & Monitoring     | Output validation, AI unit tests, latency/cost monitoring    | [Guardrails AI](https://shreyar.github.io/guardrails/), [DeepEval](https://docs.confident-ai.com/docs/evaluation) | Add guardrails & evaluation tests to your RAG system; monitor cost per request |
| **12**   | Capstone Project                     | End-to-end LLM app with RAG, tools, guardrails               | — combine resources from prior weeks —                       | Deploy **full-stack AI app** (e.g., internal knowledge assistant) to Azure or similar, with monitoring & documentation |

---

## Execution Tips

- **Time Budget**: ~6–8 hrs/week (mix of reading, coding, experimenting)
- **Environment**: Use **Jupyter Notebooks** for experiments, then move production work to your usual stack (Python, C#, etc.)
- **Version Control**: Keep a dedicated GitHub repo with week-by-week branches so you can track your learning evolution
- **Evaluation**: At the end of each phase, write a short design doc on what you learned and how you’d integrate it into a real service
