using Xunit;

// ChronoTravelers.Core.Diagnostics.PassiveActivationTracker.Listener is a
// single global static (see its doc comment) — the harness that's its real
// consumer runs one battery at a time, single-threaded, so that's fine
// there. But xUnit parallelizes different test classes (collections) by
// default, and PassiveActivationTrackerTests attaches a listener for the
// duration of a test body: without this, an unrelated, concurrently-running
// test elsewhere in this assembly that happens to exercise the same
// PassiveHook (e.g. PassiveTraitTests' own Second Wind coverage) can fire
// while that listener is attached and pollute its count — an
// intermittent failure, reproducible under `dotnet test` for the whole
// project but not when PassiveActivationTrackerTests runs alone. This
// project's suite is small (under a second), so serializing it entirely
// is the standard, low-cost xUnit fix for tests sharing global state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
