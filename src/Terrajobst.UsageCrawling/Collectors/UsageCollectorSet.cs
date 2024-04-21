﻿using System.Collections.Immutable;
using Microsoft.Cci;

namespace Terrajobst.UsageCrawling.Collectors;

public sealed class UsageCollectorSet
{
    public static int CurrentVersion { get; } = 3;

    public UsageCollectorSet()
    {
        var collectors = GetType().Assembly
                                  .GetTypes()
                                  .Where(t => !t.IsAbstract && typeof(UsageCollector).IsAssignableFrom(t))
                                  .Select(t => (UsageCollector?) Activator.CreateInstance(t))
                                  .Where(c => c is not null)
                                  .Select(c => c!)
                                  .ToImmutableArray();

        Collectors = collectors;
    }

    public ImmutableArray<UsageCollector> Collectors { get; }

    public void Collect(IAssembly assembly)
    {
        ThrowIfNull(assembly);

        foreach (var collector in Collectors)
            collector.Collect(assembly);
    }

    public CollectionSetResults GetResults()
    {
        var versionedSets = new List<VersionedFeatureSet>();

        foreach (var group in Collectors.GroupBy(c => c.VersionRequired))
        {
            var version = group.Key;
            var metrics = new HashSet<Guid>();
            foreach (var collector in group)
            {
                foreach (var featureUsage in collector.GetResults())
                    metrics.Add(featureUsage.FeatureId);
            }

            var versionedSet = new VersionedFeatureSet(version, metrics);
            versionedSets.Add(versionedSet);
        }

        return new CollectionSetResults(versionedSets);
    }
}