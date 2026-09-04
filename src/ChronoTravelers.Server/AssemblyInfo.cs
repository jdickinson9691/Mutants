using System.Runtime.CompilerServices;

// Unlocks direct unit testing of this assembly's `internal` types
// (PasswordHash, TelnetConnection) from ChronoTravelers.Server.Tests
// without having to make them public just for the sake of tests.
[assembly: InternalsVisibleTo("ChronoTravelers.Server.Tests")]
