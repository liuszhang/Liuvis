using Xunit;
using FluentAssertions;
using Moq;
using Liuvis.Core.Interfaces;
using Liuvis.Design.Services;
using Liuvis.Core.ValueObjects;
using Liuvis.Core.Enums;
using Microsoft.Extensions.Logging;

namespace Liuvis.UnitTests.Design;

public class DesignEngineTests
{
    private readonly Mock<ILlmClient> _llmMock;
    private readonly Mock<ILogger<DesignEngine>> _loggerMock;
    private readonly DesignEngine _sut;

    public DesignEngineTests()
    {
        _llmMock = new Mock<ILlmClient>();
        _loggerMock = new Mock<ILogger<DesignEngine>>();
        _sut = new DesignEngine(_llmMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateDesignPlan_NoMatches_CreatesNewPlan()
    {
        // Arrange
        var intent = new IntentResult
        {
            IntentType = IntentType.Create,
            Entities = new List<EntityExtraction>
            {
                new() { Type = "object_type", Value = "cylinder" },
                new() { Type = "color", Value = "blue" }
            },
            OriginalText = "Create a blue cylinder"
        };

        // Act
        var plan = await _sut.CreateDesignPlan(intent, new List<ModelMatch>());

        // Assert
        plan.Strategy.Should().Be(DesignStrategy.New);
        plan.NewComponents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateDesignSpec_FromNewPlan_ReturnsValidSpec()
    {
        // Arrange
        var plan = new DesignPlan
        {
            Strategy = DesignStrategy.New,
            NewComponents = new List<ComponentSpec>
            {
                new() { Name = "cylinder", GeometryType = "cylinder", Material = new MaterialSpec { Color = "#00ff00" } }
            }
        };

        // Act
        var spec = await _sut.GenerateDesignSpec(plan);

        // Assert
        spec.Components.Should().HaveCount(1);
        spec.Strategy.Should().Be(DesignStrategy.New);
    }
}
