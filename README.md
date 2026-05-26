# Learning

My ongoing learning projects, spanning multiple domains. Each top-level folder is a self-contained project with its own notes, code, and (where relevant) its own setup instructions.

## Structure

The repository is organized as a flat collection of project folders. Each folder focuses on one topic or experiment so projects stay independent and easy to navigate.

```
learning/
├── neural-networks/      # Neural network experiments and notes
└── ...                   # Additional projects added over time
```

Planned/possible areas of exploration include:

- **AI & ML** — neural networks, classical machine learning, LLMs, agents, RAG
- **Languages** — exercises and small projects in Rust, Python, Go, etc.
- **Systems & tooling** — anything else worth learning by building

## Current projects

| Folder | Description |
| --- | --- |
| [`neural-networks/`](./neural-networks) | Neural network learning materials (e.g., MNIST learning plan). |

## Conventions

- Each project lives in its own top-level folder named in `kebab-case`.
- Each project is self-contained: dependencies, build/run instructions, and notes belong inside the project folder (typically in its own `README.md`).
- Folders may mix languages, frameworks, and tools — there is no shared build system at the repo root.

## License

See [LICENSE](./LICENSE).
