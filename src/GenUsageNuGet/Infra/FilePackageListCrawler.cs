using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace GenUsageNuGet.Infra;

public sealed class FilePackageListCrawler : PackageListCrawler
{
    private readonly string _fileName;

    public FilePackageListCrawler(string fileName)
    {
        ThrowIfNull(fileName);

        _fileName = fileName;
    }

    public override async Task<IReadOnlyList<PackageIdentity>> GetPackagesAsync()
    {
        var lines = await File.ReadAllLinesAsync(_fileName);
        var result = new List<PackageIdentity>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var parts = line.Split('\t');

            if (parts.Length == 2 &&
                NuGetVersion.TryParse(parts[1], out var version))
            {
                var packageId = new PackageIdentity(parts[0], version);
                result.Add(packageId);
            }
            else
            {
                Console.WriteLine($"warning: invalid package format in line {i + 1}: {line}");
            }
        }

        return result.ToArray();
    }
}