// Integration tests share one database, so running test classes in parallel makes them
// race over the same rows (role seeding, the same email address). Serialised on purpose.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
