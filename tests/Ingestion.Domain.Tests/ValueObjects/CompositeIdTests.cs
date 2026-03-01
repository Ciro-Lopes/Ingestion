using FluentAssertions;
using Ingestion.Domain.ValueObjects;

namespace Ingestion.Domain.Tests.ValueObjects;

public sealed class CompositeIdTests
{
    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        var compositeId = new CompositeId("ABC123", new DateOnly(2024, 3, 15), "SPOT");
        compositeId.ToString().Should().Be("ABC123_20240315_SPOT");
    }

    [Fact]
    public void ToString_ShouldPadDateCorrectly()
    {
        var compositeId = new CompositeId("X", new DateOnly(2024, 1, 5), "FWD");
        compositeId.ToString().Should().Be("X_20240105_FWD");
    }

    [Fact]
    public void Equality_SameValues_ShouldBeEqual()
    {
        var a = new CompositeId("ID1", new DateOnly(2024, 6, 1), "FUT");
        var b = new CompositeId("ID1", new DateOnly(2024, 6, 1), "FUT");
        a.Should().Be(b);
    }

    [Fact]
    public void Equality_DifferentId_ShouldNotBeEqual()
    {
        var a = new CompositeId("ID1", new DateOnly(2024, 6, 1), "FUT");
        var b = new CompositeId("ID2", new DateOnly(2024, 6, 1), "FUT");
        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_DifferentDate_ShouldNotBeEqual()
    {
        var a = new CompositeId("ID1", new DateOnly(2024, 6, 1), "FUT");
        var b = new CompositeId("ID1", new DateOnly(2024, 6, 2), "FUT");
        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_DifferentType_ShouldNotBeEqual()
    {
        var a = new CompositeId("ID1", new DateOnly(2024, 6, 1), "FUT");
        var b = new CompositeId("ID1", new DateOnly(2024, 6, 1), "SPOT");
        a.Should().NotBe(b);
    }

    [Fact]
    public void GetHashCode_SameValues_ShouldBeEqual()
    {
        var a = new CompositeId("ID1", new DateOnly(2024, 6, 1), "FUT");
        var b = new CompositeId("ID1", new DateOnly(2024, 6, 1), "FUT");
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Properties_ShouldBeAccessible()
    {
        var date = new DateOnly(2024, 3, 15);
        var compositeId = new CompositeId("ABC", date, "TYPE");

        compositeId.Id.Should().Be("ABC");
        compositeId.ReferenceDate.Should().Be(date);
        compositeId.Type.Should().Be("TYPE");
    }
}
