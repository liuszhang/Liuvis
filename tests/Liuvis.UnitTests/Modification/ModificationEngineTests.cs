using Xunit;
using FluentAssertions;
using Liuvis.Core.Entities;
using Liuvis.Core.Enums;
using Liuvis.Core.ValueObjects;
using Liuvis.Modification.Services;

namespace Liuvis.UnitTests.Modification;

public class ModificationEngineTests
{
    [Fact]
    public async Task ApplyModification_ColorChange_UpdatesComponentColor()
    {
        // Arrange
        var engine = new ModificationEngine(Microsoft.Extensions.Logging.Abstractions.NullLogger<ModificationEngine>.Instance);
        var model = new Model3D("test", "test model", ModelFormat.GLB);
        var component = new ModelComponent(model.ModelId, "body", "box");
        component.SetMaterial(new MaterialSpec { Color = "#ffffff" });
        model.AddComponent(component);

        var request = new ModificationRequest
        {
            ModelId = model.ModelId,
            SessionId = Guid.NewGuid(),
            ChangeType = ChangeType.Color,
            TargetComponent = "body",
            Parameters = new Dictionary<string, object> { ["color"] = "#ff0000" }
        };

        // Act
        var result = await engine.ApplyModification(model, request);

        // Assert
        result.Components[0].Material.Color.Should().Be("#ff0000");
        result.Version.Should().Be(2);
    }

    [Fact]
    public async Task ApplyModification_AllTarget_AffectsAllComponents()
    {
        // Arrange
        var engine = new ModificationEngine(Microsoft.Extensions.Logging.Abstractions.NullLogger<ModificationEngine>.Instance);
        var model = new Model3D("test", "multi-part", ModelFormat.GLB);
        model.AddComponent(new ModelComponent(model.ModelId, "part1", "box"));
        model.AddComponent(new ModelComponent(model.ModelId, "part2", "cylinder"));

        var request = new ModificationRequest
        {
            ModelId = model.ModelId,
            SessionId = Guid.NewGuid(),
            ChangeType = ChangeType.Color,
            TargetComponent = "all",
            Parameters = new Dictionary<string, object> { ["color"] = "#00ff00" }
        };

        // Act
        var result = await engine.ApplyModification(model, request);

        // Assert
        result.Components.Should().AllSatisfy(c => c.Material.Color.Should().Be("#00ff00"));
    }
}
