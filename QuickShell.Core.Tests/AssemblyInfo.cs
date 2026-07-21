using Xunit;

// Phase remove-static-seams: terminal/profile discovery and list-icon caches are
// per-instance DI services. Collection-level parallelization is safe again for those
// former process-wide seams. Remaining GitRepoDiscovery.TestScope / AppDataRoot.TestScope
// users keep their own [Collection] isolation where needed.
