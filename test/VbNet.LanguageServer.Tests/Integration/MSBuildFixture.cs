using Microsoft.Build.Locator;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Shared fixture that ensures MSBuild is registered once for all integration tests.
/// </summary>
public class MSBuildFixture
{
    private static bool _initialized = false;
    private static readonly object _lockObject = new();

    public MSBuildFixture()
    {
        lock (_lockObject)
        {
            if (!_initialized)
            {
                // Only register if not already registered
                if (!MSBuildLocator.IsRegistered)
                {
                    MSBuildLocator.RegisterDefaults();
                }
                _initialized = true;
            }
        }
    }
}

/// <summary>
/// Collection definition for integration tests that need MSBuild.
/// All integration tests should use [Collection("MSBuild")] attribute.
/// </summary>
[CollectionDefinition("MSBuild")]
public class MSBuildCollection : ICollectionFixture<MSBuildFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
