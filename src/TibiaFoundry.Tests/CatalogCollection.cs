namespace TibiaFoundry.Tests;

/// <summary>
/// Tests that run on their own, not at the same time as others: the item catalog tests (one
/// measures how quickly a large catalog loads, which the full sprite comparison running alongside
/// slowed beyond its limit).
/// </summary>
[CollectionDefinition(nameof(CatalogCollection), DisableParallelization = true)]
public sealed class CatalogCollection;
