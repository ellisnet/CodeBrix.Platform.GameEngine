using Xunit.Sdk;   // ParallelMode
using Xunit.v3;    // ParallelizationAttribute

// The engine under test is a process-global singleton machine (Engine.Instance plus the
// scene/sprite/cycle/tilesheet/audio registries). Tests that populate or clear that global
// state cannot overlap other tests, so this assembly runs its collections serially.
[assembly: Parallelization(Mode = ParallelMode.None)]
