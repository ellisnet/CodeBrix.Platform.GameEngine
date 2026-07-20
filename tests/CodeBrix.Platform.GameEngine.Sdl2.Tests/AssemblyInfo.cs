using Xunit;

// SDL2 keeps its initialization state and its device list in process-global native state, and the
// tests here start and shut down that subsystem. Overlapping them would have one test calling
// SDL_Quit while another is mid-poll, so this assembly runs its collections serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
