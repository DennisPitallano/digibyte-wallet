# Contributing to DigiByte Wallet

Thank you for your interest in contributing! This project is open source and welcomes contributions from the community.

## Getting Started

### Prerequisites
- .NET 10 SDK
- Docker (for regtest/testnet node)
- A modern browser (Chrome, Edge, Firefox, Safari)

### Setup
```bash
git clone https://github.com/DennisPitallano/digibyte-wallet.git
cd digibyte-wallet
dotnet build
dotnet test
dotnet run --project src/DigiByte.Web/DigiByte.Web.csproj
```

### Running with Docker (regtest)
```bash
docker compose up -d
# Node API + Scalar docs at http://localhost:5260/scalar/v1
# Wallet PWA at http://localhost:5251
```

## How to Contribute

### Reporting Bugs
- Use the [GitHub Issues](https://github.com/DennisPitallano/digibyte-wallet/issues) page
- Include steps to reproduce, expected vs actual behavior
- Include browser/OS information
- Screenshots are helpful

### Suggesting Features
- Open a [GitHub Discussion](https://github.com/DennisPitallano/digibyte-wallet/discussions) or Issue
- Describe the use case and expected behavior
- Check the [Roadmap](docs/ROADMAP.md) first to see if it's planned

### Submitting Code

1. **Fork** the repository
2. **Create a branch**: `git checkout -b feature/my-feature`
3. **Make changes** and ensure tests pass: `dotnet test`
4. **Commit** with a descriptive message: `git commit -m "feat: add my feature"`
5. **Push** to your fork: `git push origin feature/my-feature`
6. **Open a Pull Request** against `main`

### Commit Convention
We follow [Conventional Commits](https://www.conventionalcommits.org/):
- `feat:` — new feature
- `fix:` — bug fix
- `docs:` — documentation only
- `refactor:` — code change that neither fixes a bug nor adds a feature
- `test:` — adding or updating tests
- `chore:` — maintenance tasks

### Code Style
- Follow existing patterns in the codebase
- Use Tailwind CSS utility classes for styling
- Keep Blazor components focused and small
- Write unit tests for crypto/wallet logic

## Project Structure
```
src/
  DigiByte.Crypto/     # Cryptography, keys, transactions
  DigiByte.Wallet/     # Wallet logic, storage, services
  DigiByte.Web/        # Blazor WASM PWA (the UI)
  DigiByte.Api/        # P2P marketplace backend
  DigiByte.NodeApi/    # Node RPC wrapper (87 methods)
  DigiByte.P2P.Shared/ # Shared P2P models
tests/
docker/
docs/
```

## Security

If you discover a security vulnerability, please **do not** open a public issue. Instead, email the maintainers directly or use GitHub's private vulnerability reporting.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
