# Learning

My ongoing learning projects, spanning multiple domains. Each top-level folder is a self-contained project with its own notes, code, and (where relevant) its own setup instructions.

## Structure

Most top-level folders are independent projects. The exception is `self-paced/`, which collects finished study books — each one a complete course in numbered markdown modules with its own exercise companion.

```
learning/
├── self-paced/           # Finished study books (one folder per course)
│   ├── neural-networks/
│   └── rust-for-csharp-engineers/
├── neural-networks/      # Neural network experiments and notes
├── artificial-intelligence/
├── data/                 # Shared datasets (MNIST, ...)
└── ...                   # Additional projects added over time
```

Planned/possible areas of exploration include:

- **AI & ML** — neural networks, classical machine learning, LLMs, agents, RAG
- **Languages** — exercises and small projects in Rust, Python, Go, etc.
- **Systems & tooling** — anything else worth learning by building

## Study books

Each book under `self-paced/` follows the same shape: a `README.md` front door, a `SETUP.md` with the exact toolchain, a `00-START-HERE.md` orientation, then numbered narrative modules and an `exercises/` companion with worked answers. Every module index carries per-module time estimates and a total.

| Book | Topic | Time |
| --- | --- | --- |
| [`self-paced/neural-networks/`](./self-paced/neural-networks) | Neural networks, from the perceptron to the Transformer, with a PyTorch companion. | ~55–60 h |
| [`self-paced/rust-for-csharp-engineers/`](./self-paced/rust-for-csharp-engineers) | Rust for senior .NET engineers — language core, ecosystem crates, and a shipped CLI capstone. | ~55–60 h |

## Current projects

| Folder | Description |
| --- | --- |
| [`neural-networks/`](./neural-networks) | Neural network experiments and notes (e.g., MNIST learning plan). |
| [`artificial-intelligence/`](./artificial-intelligence) | AI/LLM study plans and collected resources. |
| [`data/`](./data) | Datasets shared across projects. |

## Conventions

- Each project lives in its own top-level folder named in `kebab-case`.
- Each project is self-contained: dependencies, build/run instructions, and notes belong inside the project folder (typically in its own `README.md`).
- Folders may mix languages, frameworks, and tools — there is no shared build system at the repo root.

## License

See [LICENSE](./LICENSE).
