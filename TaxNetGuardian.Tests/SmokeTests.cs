using TaxNetGuardian.Tests.Fixtures;
using Xunit;

namespace TaxNetGuardian.Tests;

/// <summary>
/// Trivial smoke tests proving the test project builds, runs, and can construct
/// an isolated <see cref="TaxNetGuardian.Api.TaxNetState"/> via the shared fixture.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void TestProject_runs()
    {
        Assert.True(true);
    }

    [Fact]
    public void Fixture_constructs_isolated_state_with_synthetic_data()
    {
        using var fixture = new TaxNetStateFixture();

        // Constructed against an isolated temp App_Data and the deterministic seed.
        Assert.True(Directory.Exists(fixture.ContentRootPath));
        Assert.NotNull(fixture.State);

        // The default constructor seeds synthetic data when no snapshot exists.
        Assert.NotEmpty(fixture.State.People);
    }
}
