using Xunit.Sdk;   // ParallelMode
using Xunit.v3;    // ParallelizationAttribute

// Every test here works on process-wide state - the music generator and instrument library
// registries, the engine's streaming music registry and music manager - so this assembly runs its
// collections serially.
[assembly: Parallelization(Mode = ParallelMode.None)]
