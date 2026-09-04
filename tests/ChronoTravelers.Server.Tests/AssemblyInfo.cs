using Xunit;

// These are integration tests: each spins up a real TcpListener / telnet
// session loop or a LiteDB-backed ServerStore. Run serially — sharing the
// machine with dozens of other socket-binding, DB-opening tests at once
// raced (instant, non-deterministic failures; a couple of tests flipping
// red on any given run). The whole assembly is ~1s serial, so there's
// nothing to gain from parallelising it anyway.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
