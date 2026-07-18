using Xunit;

// A number of tests (GitRepoDiscovery.IncludeDefaultSearchRoots/DefaultRootCandidatesOverride,
// LOCALAPPDATA via Environment.SetEnvironmentVariable, TerminalCatalog/WtProfilesService
// caches) reach into process-wide static state as their test seam. xUnit's default behavior
// runs different [Collection]s concurrently on separate threads even when an individual
// collection sets DisableParallelization=true, so two tests in different collections can
// observe each other's static overrides mid-run. Disabling collection parallelization for
// the whole assembly serializes every test, which is the only reliable way to make those
// seams safe without threading explicit dependency injection through every call site that
// currently relies on a static override (a much larger, riskier change).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
