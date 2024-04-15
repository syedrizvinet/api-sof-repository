﻿using Terrajobst.UsageCrawling.Collectors;
using Terrajobst.UsageCrawling.Tests.Infra;

namespace Terrajobst.UsageCrawling.Tests.Collectors;

public class ApiUsageCollectorTests : CollectorTest<ApiUsageCollector>
{
    // Note: We don't need to deep here, most of the API specific tests are against the underlying AssemblyCrawler

    [Fact]
    public void ApiUsageCollector_Reports_MethodCalls()
    {
        var source =
            """
            using System;
            public class C {
                public static void M() {
                    Console.WriteLine("Hello");
                }
            }
            """;

        var expectedIds =
            """
            T:System.Object
            M:System.Object.#ctor
            M:System.Console.WriteLine(System.String)
            T:System.Void
            """;

        Check(source, expectedIds);
    }

    private void Check(string source, string expectedIds)
    {
        Check(source, expectedIds, UsageMetric.ForApi);
    }

    protected override bool Include(UsageMetric metric)
    {
        if (metric is not ApiFeatureUsage a)
            return true;

        return !Compiler.IsAutoGenerated(a.Api.DocumentationId);
    }
}