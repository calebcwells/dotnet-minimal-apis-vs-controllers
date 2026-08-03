# Contributing

Corrections and focused improvements are welcome, especially when a .NET release changes one of
the documented behaviors.

## Before opening a pull request

Use the .NET SDK selected by `global.json`, then run these commands.

```bash
dotnet build ApiParity.slnx -warnaserror
dotnet format ApiParity.slnx --verify-no-changes --no-restore
dotnet test ApiParity.slnx --no-build
```

Keep changes scoped to the controller-versus-minimal-API comparison. Behavioral claims should be
backed by a test, and genuine differences between the styles should be documented rather than
normalized away.

## Benchmarks

Benchmark changes should be measured locally in Release configuration. Do not treat results from
shared CI runners as reliable microbenchmarks.

The performance table in the README is a checked-in snapshot with no test guarding it, so it goes
stale in silence. Re-run the benchmarks and update both the table and its measurement date when
you touch anything in the request pipeline: routing, filters, DI registration, or the shared
service. Do this even when the change looks unrelated to performance. Note that the controller row
is the baseline every ratio divides by, so a change affecting only controllers moves every number
in the table.
