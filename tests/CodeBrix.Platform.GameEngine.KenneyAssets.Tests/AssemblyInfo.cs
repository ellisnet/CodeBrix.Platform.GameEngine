using Xunit.Sdk;   // ParallelMode
using Xunit.v3;    // ParallelizationAttribute

// Tests here open the same fixture bundles, extract them into shared folders under the test output
// directory, and will soon materialize assets into the engine's process-global registries. Any of
// those overlapping would have one test deleting a folder another is reading, so this assembly runs
// its collections serially.
[assembly: Parallelization(Mode = ParallelMode.None)]
