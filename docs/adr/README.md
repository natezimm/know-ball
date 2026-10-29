# Architecture decision records

Short records of the decisions that shape Know Ball: the context, what was decided and what it costs.

| ADR | Decision |
| --- | --- |
| [0001](0001-layered-backend.md) | A layered backend with a narrow database interface |
| [0002](0002-answers-stay-on-the-server.md) | Answers stay on the server until a game is over |
| [0003](0003-anonymous-players.md) | Anonymous players instead of accounts |
| [0004](0004-idempotent-guesses.md) | Guesses are idempotent and safe across tabs |
| [0005](0005-defensive-nightly-import.md) | The nightly import refuses bad data rather than trusting it |
| [0006](0006-one-image-pulled-by-the-vm.md) | One image, built in CI and pulled by the VM |
