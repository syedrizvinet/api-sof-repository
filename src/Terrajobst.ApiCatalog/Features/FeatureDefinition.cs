﻿using NuGet.Frameworks;

namespace Terrajobst.ApiCatalog.Features;

public abstract class FeatureDefinition
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    public static GlobalFeatureDefinition DefinesAnyRefStructs { get; } = new DefinesAnyRefStructsDefinition();
    public static GlobalFeatureDefinition DefinesAnyDefaultInterfaceMembers { get; } = new DefinesAnyDefaultInterfaceMembersDefinition();
    public static GlobalFeatureDefinition DefinesAnyVirtualStaticInterfaceMembers { get; } = new DefinesAnyVirtualStaticInterfaceMembersDefinition();
    public static GlobalFeatureDefinition DefinesAnyRefFields { get; } = new DefinesAnyRefFieldsDefinition();
    public static GlobalFeatureDefinition UsesNullableReferenceTypes { get; } = new UsesNullableReferenceTypesDefinition();

    public static IReadOnlyList<GlobalFeatureDefinition> GlobalFeatures { get; } = [
        DefinesAnyRefStructs,
        DefinesAnyDefaultInterfaceMembers,
        DefinesAnyVirtualStaticInterfaceMembers,
        DefinesAnyRefFields,
        UsesNullableReferenceTypes
    ];

    public static ParameterizedFeatureDefinition<Guid> ApiUsage { get; } = new ApiUsageDefinition();
    public static ParameterizedFeatureDefinition<Guid> DimUsage { get; } = new DimUsageDefinition();
    public static ParameterizedFeatureDefinition<Guid> DerivesFromUsage { get; } = new DerivesFromUsageDefinition();
    public static ParameterizedFeatureDefinition<Guid> FieldRead { get; } = new FieldReadDefinition();
    public static ParameterizedFeatureDefinition<Guid> FieldWrite { get; } = new FieldWriteDefinition();
    public static ParameterizedFeatureDefinition<Guid> ExceptionThrow { get; } = new ExceptionThrowDefinition();
    public static ParameterizedFeatureDefinition<Guid> ExceptionCatch { get; } = new ExceptionCatchDefinition();

    public static IReadOnlyList<ParameterizedFeatureDefinition<Guid>> ApiFeatures { get; } = [
        ApiUsage,
        DimUsage,
        DerivesFromUsage,
        FieldRead,
        FieldWrite,
        ExceptionThrow,
        ExceptionCatch
    ];

    public static ParameterizedFeatureDefinition<NuGetFramework> TargetFramework { get; } = new TargetFrameworkFeatureDefinition();

    public static Dictionary<Guid, FeatureDefinition> GetCatalogFeatures(ApiCatalogModel catalog)
    {
        ThrowIfNull(catalog);

        var result = new Dictionary<Guid, FeatureDefinition>();

        // Global features

        foreach (var globalFeature in GlobalFeatures)
            result.Add(globalFeature.FeatureId, globalFeature);

        // API features

        foreach (var api in catalog.AllApis)
        {
            foreach (var apiFeature in ApiFeatures)
                result.Add(apiFeature.GetFeatureId(api.Guid), apiFeature);
        }

        // Other parameterized features

        foreach (var framework in catalog.Frameworks)
            result.Add(TargetFramework.GetFeatureId(framework.NuGetFramework), TargetFramework);

        return result;
    }

    public static IEnumerable<(Guid ChildFeatureId, Guid ParentFeatureId)> GetParentFeatures(ApiCatalogModel catalog)
    {
        ThrowIfNull(catalog);

        // API features

        foreach (var child in catalog.AllApis)
        foreach (var parent in child.AncestorsAndSelf())
        foreach (var feature in ApiFeatures)
        {
            var childFeature = feature.GetFeatureId(child.Guid);
            var parentParent = feature.GetFeatureId(parent.Guid);
            yield return (childFeature, parentParent);
        }

        // Target frameworks

        foreach (var fx in catalog.Frameworks)
        {
            var child = fx.NuGetFramework;
            if (!child.IsRelevantForApisOfDotnet())
                continue;

            foreach (var parent in GetAncestorsAndSelf(child))
            {
                var childFeature = TargetFramework.GetFeatureId(child);
                var parentParent = TargetFramework.GetFeatureId(parent);
                yield return (childFeature, parentParent);
            }
        }
    }

    private static IEnumerable<NuGetFramework> GetAncestorsAndSelf(NuGetFramework framework)
    {
        yield return framework;

        if (framework.HasPlatform)
        {
            if (framework.PlatformVersion != FrameworkConstants.EmptyVersion)
                yield return new NuGetFramework(framework.Framework, framework.Version, framework.Platform, FrameworkConstants.EmptyVersion);

            yield return new NuGetFramework(framework.Framework, framework.Version);
        }

        var frameworkFamily = new NuGetFramework(framework.Framework, FrameworkConstants.EmptyVersion);
        yield return frameworkFamily;
    }

    private sealed class DefinesAnyRefStructsDefinition : GlobalFeatureDefinition
    {
        public override Guid FeatureId { get; } = Guid.Parse("740841a3-5c09-426a-b43b-750d21250c01");

        public override string Name => "Define any ref structs";

        public override string Description => "Percentage of applications/packages that defined their own ref structs";
    }

    private sealed class DefinesAnyDefaultInterfaceMembersDefinition : GlobalFeatureDefinition
    {
        public override Guid FeatureId { get; } = Guid.Parse("745807b1-d30a-405c-aa91-209bae5f5ea9");

        public override string Name => "Define any DIMs";

        public override string Description => "Percentage of applications/packages that defined any default interface members (DIMs)";
    }

    private sealed class DefinesAnyVirtualStaticInterfaceMembersDefinition : GlobalFeatureDefinition
    {
        public override Guid FeatureId { get; } = Guid.Parse("580c614a-45e8-4f91-a007-322377dd23a9");

        public override string Name => "Define any virtual static interface members";

        public override string Description => "Percentage of applications/packages that defined any virtual static interface members";
    }

    private sealed class DefinesAnyRefFieldsDefinition : GlobalFeatureDefinition
    {
        public override Guid FeatureId { get; } = Guid.Parse("acb7b28c-c88a-4bc6-b099-08c7e35cc1c1");

        public override string Name => "Defines any ref fields";

        public override string Description => "Percentage of applications/packages that defined any ref fields";
    }

    private sealed class UsesNullableReferenceTypesDefinition : GlobalFeatureDefinition
    {
        public override Guid FeatureId { get; } = Guid.Parse("b7977f35-478e-4fef-bc22-9a4984a69a48");

        public override string Name => "Compiles with nullable reference types";

        public override string Description => "Percentage of applications/packages that compiled with nullable reference types";
    }

    private sealed class ApiUsageDefinition : ParameterizedFeatureDefinition<Guid>
    {
        public override Guid GetFeatureId(Guid api)
        {
            return api;
        }

        public override string Name => "Reference this API";

        public override string Description => "Usage of an API in signatures or method bodies";
    }

    private sealed class DimUsageDefinition : ParameterizedFeatureDefinition<Guid>
    {
        public override Guid GetFeatureId(Guid api)
        {
            return FeatureId.Create(DefinesAnyDefaultInterfaceMembers.FeatureId, api);
        }

        public override string Name => "Declare a DIM for this API";

        public override string Description => "Definition of an interface member with a default implementation (DIM)";
    }

    private sealed class DerivesFromUsageDefinition : ParameterizedFeatureDefinition<Guid>
    {
        private static readonly Guid DerivesFromFeature = Guid.Parse("ee8100f2-6eed-4e31-b290-49941837f241");

        public override Guid GetFeatureId(Guid api)
        {
            return FeatureId.Create(DerivesFromFeature, api);
        }

        public override string Name => "Derive from this class or interface";

        public override string Description => "Subclassing or interface implementation";
    }

    private sealed class FieldReadDefinition : ParameterizedFeatureDefinition<Guid>
    {
        private static readonly Guid DerivesFromFeature = Guid.Parse("1cfae67f-df1c-42df-9a6e-f3b13bff730e");

        public override Guid GetFeatureId(Guid api)
        {
            return FeatureId.Create(DerivesFromFeature, api);
        }

        public override string Name => "Field read";

        public override string Description => "Reads from this field";
    }

    private sealed class FieldWriteDefinition : ParameterizedFeatureDefinition<Guid>
    {
        private static readonly Guid DerivesFromFeature = Guid.Parse("c014a47a-29d8-4d61-a538-dc7b6ce33ed4");

        public override Guid GetFeatureId(Guid api)
        {
            return FeatureId.Create(DerivesFromFeature, api);
        }

        public override string Name => "Field write";

        public override string Description => "Writes to this field";
    }

    private sealed class ExceptionThrowDefinition : ParameterizedFeatureDefinition<Guid>
    {
        private static readonly Guid DerivesFromFeature = Guid.Parse("eb23985b-8fe8-4230-9fa8-15c21827f5ee");

        public override Guid GetFeatureId(Guid api)
        {
            return FeatureId.Create(DerivesFromFeature, api);
        }

        public override string Name => "Exception throw";

        public override string Description => "Throwing of this exception type";
    }

    private sealed class ExceptionCatchDefinition : ParameterizedFeatureDefinition<Guid>
    {
        private static readonly Guid DerivesFromFeature = Guid.Parse("6b75066b-1e1e-47d7-854e-fe3da867ad0d");

        public override Guid GetFeatureId(Guid api)
        {
            return FeatureId.Create(DerivesFromFeature, api);
        }

        public override string Name => "Exception catch";

        public override string Description => "Catch handlers for this exception type";
    }

    private sealed class TargetFrameworkFeatureDefinition : ParameterizedFeatureDefinition<NuGetFramework>
    {
        private static readonly Guid TargetFrameworkFeature = Guid.Parse("8fe6904d-e83d-499c-929a-d9dd69fd0b05");

        public override Guid GetFeatureId(NuGetFramework framework)
        {
            var folderName = framework.GetShortFolderName();
            return FeatureId.Create(TargetFrameworkFeature, folderName);
        }

        public override string Name => "Target Framework Usage";

        public override string Description => "Indicates targeting of a specific framework";
    }
}