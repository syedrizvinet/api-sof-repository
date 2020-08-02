﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Dapper;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.SqlClient;

using NuGet.Frameworks;
using NuGet.Packaging;

namespace PackageIndexing
{
    public static class Indexer
    {
        private static readonly string v3_flatContainer_nupkg_template = "https://api.nuget.org/v3-flatcontainer/{0}/{1}/{0}.{1}.nupkg";
        private static readonly HttpClient httpClient = new HttpClient();

        public static async Task Index(string id, string version, string connectionString)
        {
            using var database = await CatalogDatabase.OpenAsync(connectionString);
            await IndexPackage(id, version, database);
        }

        private static async Task IndexPackage(string id, string version, CatalogDatabase database)
        {
            var frameworkRows = await database.GetFrameworksAsync();
            var frameworkIdByName = frameworkRows.ToDictionary(r => r.FriendlyName, r => r.FrameworkId);

            var frameworks = frameworkRows.Select(r => NuGetFramework.Parse(r.FriendlyName));

            var frameworkGroups = frameworks.GroupBy(fx => fx.Framework)
                                            .Select(g => (Framework: g.Key, Versions: g.OrderByDescending(fx => fx.Version)))
                                            .ToArray();

            var dependencies = new List<PackageArchiveReader>();
            try
            {
                using (var root = await FetchPackageAsync(id, version))
                {
                    var targets = new List<NuGetFramework>();

                    foreach (var (framework, versions) in frameworkGroups)
                    {
                        foreach (var frameworkVersion in versions)
                        {
                            var versionReferenceGroup = GetReferenceItems(root, frameworkVersion);
                            if (versionReferenceGroup != null)
                            {
                                targets.Add(versionReferenceGroup.TargetFramework);
                                break;
                            }
                        }
                    }

                    if (!targets.Any())
                        return;

                    var packageId = 0;
                    var packageVersionId = 0;

                    foreach (var target in targets)
                    {
                        Console.WriteLine(target);

                        var referenceGroup = GetReferenceItems(root, target);

                        // Let's not index TFMs we don't care about.
                        if (!frameworkIdByName.ContainsKey(referenceGroup.TargetFramework.GetShortFolderName()))
                            continue;

                        Debug.Assert(referenceGroup != null);

                        if (packageId == 0 && referenceGroup.Items.Any())
                        {
                            packageId = await database.InsertPackageAsync(id);
                            packageVersionId = await database.InsertPackageVersionAsync(packageId, version);
                        }

                        await FetchDependenciesAsync(dependencies, root, target);

                        Console.WriteLine("Index:");

                        foreach (var path in referenceGroup.Items)
                            Console.WriteLine($"    {path,-70}{referenceGroup.TargetFramework.GetShortFolderName()}");

                        Console.WriteLine("Dependencies:");

                        foreach (var dependency in dependencies)
                        {
                            var dependencyReferences = GetReferenceItems(dependency, target);
                            if (dependencyReferences != null)
                            {
                                foreach (var path in dependencyReferences.Items)
                                    Console.WriteLine($"    {path,-70}{dependencyReferences.TargetFramework.GetShortFolderName()}");
                            }
                        }

                        // Add references to index

                        var compilation = CSharpCompilation.Create("dummy");
                        var toBeIndexed = new List<MetadataReference>();

                        foreach (var path in referenceGroup.Items)
                        {
                            var metadata = await AssemblyStream.CreateAsync(root.GetStream(path), path);
                            toBeIndexed.Add(metadata);
                            compilation = compilation.AddReferences(metadata);
                        }

                        // Add dependencies to index

                        foreach (var dependency in dependencies)
                        {
                            var dependencyReferences = GetReferenceItems(dependency, target);
                            if (dependencyReferences != null)
                            {
                                foreach (var path in dependencyReferences.Items)
                                {
                                    var metadata = await AssemblyStream.CreateAsync(dependency.GetStream(path), path);
                                    compilation = compilation.AddReferences(metadata);
                                }
                            }
                        }

                        // Add framework references

                        var plaformSet = await GetPlatformSet(target);
                        foreach (var (path, stream) in plaformSet.GetFiles())
                        {
                            var metadata = await AssemblyStream.CreateAsync(stream, path);
                            compilation = compilation.AddReferences(metadata);
                        }

                        foreach (var reference in toBeIndexed)
                        {
                            var assemblyOrModule = compilation.GetAssemblyOrModuleSymbol(reference);
                            if (assemblyOrModule is IAssemblySymbol a)
                            {
                                var frameworkId = frameworkIdByName[target.GetShortFolderName()];
                                await IndexAssemblyAsync(a, database, frameworkId, packageVersionId);
                            }
                        }
                    }
                }
            }
            finally
            {
                foreach (var package in dependencies)
                    package.Dispose();
            }
        }

        private static FrameworkSpecificGroup GetReferenceItems(PackageArchiveReader root, NuGetFramework current)
        {
            var referenceItems = root.GetReferenceItems();
            var referenceGroup = NuGetFrameworkUtility.GetNearest(referenceItems, current);
            return referenceGroup;
        }

        private static async Task FetchDependenciesAsync(List<PackageArchiveReader> packages, PackageArchiveReader root, NuGetFramework target)
        {
            var dependencies = root.GetPackageDependencies();
            var dependencyGroup = NuGetFrameworkUtility.GetNearest(dependencies, target);
            if (dependencyGroup != null)
            {
                foreach (var d in dependencyGroup.Packages)
                {
                    Console.WriteLine($"Discovered dependency {d}");
                    var dependency = await FetchPackageAsync(d.Id, d.VersionRange.MinVersion.ToNormalizedString());
                    packages.Add(dependency);
                    await FetchDependenciesAsync(packages, dependency, target);
                }
            }
        }

        private static async Task<PackageArchiveReader> FetchPackageAsync(string id, string version)
        {
            var url = GetFlatContainerNupkgUrl(id, version);
            var nupkgStream = await httpClient.GetStreamAsync(url);
            return new PackageArchiveReader(nupkgStream);
        }

        private static Uri GetFlatContainerNupkgUrl(string id, string version)
        {
            var url = string.Format(v3_flatContainer_nupkg_template, id, version);
            return new Uri(url);
        }

        private static Task<PlatformPackageSet> GetPlatformSet(NuGetFramework framework)
        {
            if (framework.Framework == ".NETCoreApp")
                return GetNetCore();
            else if (framework.Framework == ".NETStandard")
                return GetNetStandard();
            else if (framework.Framework == ".NETFramework")
                return GetNetFramework();
            else
                return Task.FromResult<PlatformPackageSet>(null);
        }

        private static async Task<PlatformPackageSet> GetNetCore()
        {
            return new PlatformPackageSet
            {
                Packages = new[]
                {
                    await FetchPackageAsync("Microsoft.AspNetCore.App.Ref", "3.1.0"),
                    await FetchPackageAsync("Microsoft.NETCore.App.Ref", "3.1.0"),
                    await FetchPackageAsync("Microsoft.WindowsDesktop.App.Ref", "3.1.0")
                },
                Selector = pr => pr.GetFiles()
                                   .Where(path => path.StartsWith("ref/", StringComparison.OrdinalIgnoreCase) &&
                                                  path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                   .Select(p => (p, pr.GetStream(p)))
            };
        }

        private static async Task<PlatformPackageSet> GetNetStandard()
        {
            return new PlatformPackageSet
            {
                Packages = new[]
                {
                    await FetchPackageAsync("NETStandard.Library.Ref", "2.1.0")
                },
                Selector = pr => pr.GetFiles()
                                   .Where(path => path.StartsWith("ref/", StringComparison.OrdinalIgnoreCase) &&
                                                  path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                   .Select(p => (p, pr.GetStream(p)))
            };
        }

        private static async Task<PlatformPackageSet> GetNetFramework()
        {
            return new PlatformPackageSet
            {
                Packages = new[]
                {
                    await FetchPackageAsync("Microsoft.NETFramework.ReferenceAssemblies.net48", "1.0.0")
                },
                Selector = pr => pr.GetFiles()
                                   .Where(path => path.StartsWith("build/.NETFramework/", StringComparison.OrdinalIgnoreCase) &&
                                                  path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                   .Select(p => (p, pr.GetStream(p)))
            };
        }

        private static async Task IndexAssemblyAsync(IAssemblySymbol a,
                                                     CatalogDatabase database,
                                                     int frameworkId,
                                                     int packageVersionId)
        {
            var apis = FlattenApis(GetApis(a));
            var assemblyGuid = CatalogSymbolExtensions.GetCatalogGuid(apis);
            var assemblyName = a.Name;
            var assemblyVersion = a.Identity.Version.ToString();
            var assemblyPublicKeyToken = a.Identity.GetPublicKeyTokenString();

            var (assemblyId, assemblyInserted) = await database.InsertAssemblyAsync(assemblyGuid, assemblyName, assemblyVersion, assemblyPublicKeyToken);
            await database.InsertPackageAssemblyAsync(packageVersionId, frameworkId, assemblyId);

            if (assemblyInserted)
            {
                var apiIdByEntry = new Dictionary<ApiEntry, int>();

                foreach (var api in apis)
                {
                    var parentApiId = api.Parent == null
                                        ? (int?)null
                                        : apiIdByEntry[api.Parent];
                    var apiGuid = api.Symbol.GetCatalogGuid();
                    var name = api.Symbol.GetCatalogName();
                    var apiId = await database.InsertApi(apiGuid, parentApiId, name);
                    apiIdByEntry.Add(api, apiId);

                    var syntax = api.Symbol.ToString();
                    await database.InsertDeclaration(apiId, assemblyId, syntax);
                }
            }
        }

        private static void DumpAllApis(IEnumerable<ApiEntry> entries, int indent = 0)
        {
            var x = new SymbolDisplayFormat(
                memberOptions: SymbolDisplayMemberOptions.IncludeParameters,
                parameterOptions: SymbolDisplayParameterOptions.IncludeType
            );

            var indentStr = new string(' ', indent * 2);
            foreach (var entry in entries)
            {
                var name = entry.Symbol.ToDisplayString(x);
                if (entry.Symbol is INamespaceSymbol ns)
                    name = ns.ToString();

                Console.WriteLine($"{indentStr}{name}");
                DumpAllApis(entry.Children, indent + 1);
            }
        }

        private static IEnumerable<INamedTypeSymbol> GetAllTypes(IAssemblySymbol symbol)
        {
            var stack = new Stack<INamespaceSymbol>();
            stack.Push(symbol.GlobalNamespace);

            while (stack.Count > 0)
            {
                var ns = stack.Pop();
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamespaceSymbol childNs)
                        stack.Push(childNs);
                    else if (member is INamedTypeSymbol type)
                        yield return type;
                }
            }
        }

        private static List<ApiEntry> GetApis(IAssemblySymbol symbol)
        {
            var result = new List<ApiEntry>();
            var types = GetAllTypes(symbol)
                         .Where(t => t.DeclaredAccessibility == Accessibility.Public)
                         .GroupBy(t => t.ContainingNamespace);

            foreach (var namespaceGroup in types)
            {
                var entry = new ApiEntry(namespaceGroup.Key);
                result.Add(entry);

                foreach (var type in namespaceGroup)
                    AddApi(entry, type);
            }

            return result;
        }

        private static List<ApiEntry> FlattenApis(List<ApiEntry> root)
        {
            var result = new List<ApiEntry>();
            FlattenApis(result, root);
            return result;
        }

        private static void FlattenApis(List<ApiEntry> target, List<ApiEntry> root)
        {
            target.AddRange(root);

            foreach (var child in root)
                FlattenApis(target, child.Children);
        }

        private static void AddApi(ApiEntry parent, ITypeSymbol symbol)
        {
            if (symbol.DeclaredAccessibility != Accessibility.Public &&
                symbol.DeclaredAccessibility != Accessibility.Protected)
                return;

            var apiEntry = new ApiEntry(symbol, parent);
            parent.Children.Add(apiEntry);

            foreach (var member in symbol.GetMembers())
                AddMember(apiEntry, member);
        }

        private static void AddMember(ApiEntry parent, ISymbol symbol)
        {
            if (symbol is INamedTypeSymbol type)
            {
                AddApi(parent, type);
                return;
            }

            if (symbol.DeclaredAccessibility != Accessibility.Public &&
                symbol.DeclaredAccessibility != Accessibility.Protected)
                return;

            var entry = new ApiEntry(symbol, parent);
            parent.Children.Add(entry);
        }
    }

    class FrameworkRow
    {
        public int FrameworkId { get; set; }
        public string FriendlyName { get; set; }
    }

    class PlatformPackageSet
    {
        public PackageArchiveReader[] Packages;
        public Func<PackageArchiveReader, IEnumerable<(string Path, Stream data)>> Selector;

        public IEnumerable<(string Path, Stream data)> GetFiles()
        {
            return Packages.Select(Selector).SelectMany(s => s);
        }
    }

    class CatalogDatabase : IDisposable
    {
        private readonly SqlConnection _connection;

        private CatalogDatabase(SqlConnection connection)
        {
            _connection = connection;
        }

        public void Dispose()
        {
            _connection.Dispose();
        }

        public static async Task<CatalogDatabase> OpenAsync(string connectionString)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return new CatalogDatabase(connection);
        }

        public async Task<IReadOnlyList<FrameworkRow>> GetFrameworksAsync()
        {
            var rows = await _connection.QueryAsync<FrameworkRow>(@"
                SELECT FrameworkId,
                       FriendlyName
                FROM Frameworks
            ");
            return rows.ToList();
        }

        public async Task<int> InsertPackageAsync(string name)
        {
            var packageId = await _connection.ExecuteScalarAsync<int>(@"
                MERGE Packages AS target
                USING (SELECT @Name) AS source (Name)
                ON    (target.Name = source.Name)
                WHEN NOT MATCHED THEN
                    INSERT (Name)
                    VALUES (@Name);
                SELECT  PackageId
                FROM    Packages
                WHERE   Name = @Name;
            ", new
            {
                Name = name
            });

            return packageId;
        }

        public async Task<int> InsertPackageVersionAsync(int packageId, string version)
        {
            var packageVersionId = await _connection.ExecuteScalarAsync<int>(@"
                INSERT INTO PackageVersions
                    (PackageId, Version)
                VALUES
                    (@PackageId, @Version);
                SELECT CAST(SCOPE_IDENTITY() AS INT)
            ", new
            {
                PackageId = packageId,
                Version = version
            });

            return packageVersionId;
        }

        public async Task<(int Id, bool Inserted)> InsertAssemblyAsync(Guid assemblyGuid,
                                                                       string assemblyName,
                                                                       string assemblyVersion,
                                                                       string assemblyPublicKeyToken)
        {
            using var reader = await _connection.ExecuteReaderAsync(@"
                MERGE Assemblies AS target
                USING (SELECT @AssemblyGuid) AS source (AssemblyGuid)
                ON    (target.AssemblyGuid = source.AssemblyGuid)
                WHEN NOT MATCHED THEN
                    INSERT  (AssemblyGuid, Name, Version, PublicKeyToken)
                    VALUES  (@AssemblyGuid, @Name, @Version, @PublicKeyToken)
                OUTPUT
                    inserted.AssemblyId;
                SELECT  AssemblyId
                FROM    Assemblies
                WHERE   AssemblyGuid = @AssemblyGuid;
            ", new
            {
                AssemblyGuid = assemblyGuid,
                Name = assemblyName,
                Version = assemblyVersion,
                PublicKeyToken = assemblyPublicKeyToken
            });

            var isInserted = await reader.ReadAsync();
            await reader.NextResultAsync();
            await reader.ReadAsync();

            var id = Convert.ToInt32(reader[0]);
            return (id, isInserted);
        }

        public async Task InsertPackageAssemblyAsync(int packageVersionId, int frameworkId, int assemblyId)
        {
            await _connection.ExecuteScalarAsync(@"
                INSERT INTO PackageAssemblies
                    (PackageVersionId, FrameworkId, AssemblyId)
                VALUES
                    (@PackageVersionId, @FrameworkId, @AssemblyId)
            ", new
            {
                PackageVersionId = packageVersionId,
                FrameworkId = frameworkId,
                AssemblyId = assemblyId
            });
        }

        public async Task<int> InsertApi(Guid apiGuid, int? parentApiId, string name)
        {
            var apiId = await _connection.ExecuteScalarAsync<int>(@"
                MERGE Apis AS target
                USING (SELECT @ApiGuid) AS source (ApiGuid)
                ON    (target.ApiGuid = source.ApiGuid)
                WHEN NOT MATCHED THEN
                    INSERT  (ApiGuid, ParentApiId, Name)
                    VALUES  (@ApiGuid, @ParentApiId, @Name);
                SELECT  ApiId
                FROM    Apis
                WHERE   ApiGuid = @ApiGuid;
            ", new
            {
                ApiGuid = apiGuid,
                ParentApiId = parentApiId,
                Name = name
            });

            return apiId;
        }

        public async Task<int> InsertDeclaration(int apiId, int assemblyId, string syntax)
        {
            var declartionId = await _connection.ExecuteScalarAsync<int>(@"
                INSERT INTO Declarations
                    (ApiId, AssemblyId, Syntax)
                VALUES
                    (@ApiId, @AssemblyId, @Syntax);
                SELECT CAST(SCOPE_IDENTITY() AS INT)
            ", new
            {
                ApiId = apiId,
                AssemblyId = assemblyId,
                Syntax = syntax
            });

            return declartionId;
        }
    }

    static class AssemblyStream
    {
        public static async Task<MetadataReference> CreateAsync(Stream stream, string path)
        {
            if (!stream.CanSeek)
            {
                var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                stream.Dispose();
                stream = memoryStream;
            }

            return MetadataReference.CreateFromStream(stream, filePath: path);
        }
    }

    class ApiEntry
    {
        public ApiEntry(ISymbol symbol, ApiEntry parent = null)
        {
            Symbol = symbol;
            Parent = parent;
        }

        public ApiEntry Parent { get; }
        public ISymbol Symbol { get; set; }
        public List<ApiEntry> Children { get; } = new List<ApiEntry>();
    }

    static class CatalogSymbolExtensions
    {
        private static SymbolDisplayFormat _nameFormat = new SymbolDisplayFormat(
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType
        );


        public static Guid GetCatalogGuid(this ISymbol symbol)
        {
            var id = symbol.GetDocumentationCommentId();
            var bytes = Encoding.UTF8.GetBytes(id);
            var md5 = MD5.Create();
            var hashBytes = md5.ComputeHash(bytes);
            return new Guid(hashBytes);
        }

        public static string GetCatalogName(this ISymbol symbol)
        {
            if (symbol is INamespaceSymbol)
                return symbol.ToString();

            return symbol.ToDisplayString(_nameFormat);
        }

        public static Guid GetCatalogGuid(List<ApiEntry> allApis)
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);

            foreach (var id in allApis.Select(e => e.Symbol.GetDocumentationCommentId()).OrderBy(i => i))
            {
                writer.WriteLine(id);
            }

            writer.Flush();

            stream.Position = 0;

            var md5 = MD5.Create();
            var hashBytes = md5.ComputeHash(stream);
            return new Guid(hashBytes);
        }

        public static string GetPublicKeyTokenString(this AssemblyIdentity identity)
        {
            return BitConverter.ToString(identity.PublicKeyToken.ToArray()).Replace("-", "").ToLower();
        }
    }
}
