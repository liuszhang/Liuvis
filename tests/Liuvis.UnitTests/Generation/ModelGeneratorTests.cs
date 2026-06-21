using Xunit;
using FluentAssertions;
using Liuvis.Core.Enums;
using Liuvis.Core.ValueObjects;
using Liuvis.Generation.Services;
using Liuvis.Generation.Geometry;
using Liuvis.Core.Interfaces;
using Liuvis.Infrastructure.Repositories;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liuvis.UnitTests.Generation;

public class ModelGeneratorTests
{
    [Fact]
    public async Task GenerateModel_FromDesignSpec_CreatesModel3D()
    {
        // Arrange
        var storageMock = new Mock<IObjectStorageService>();
        storageMock.Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/storage/test.glb");

        var llmMock = new Mock<ILlmClient>();
        var llmDesign = new LLMDesignService(llmMock.Object, NullLogger<LLMDesignService>.Instance);
        var geoBuilder = new ProceduralGeometryBuilder(NullLogger<ProceduralGeometryBuilder>.Instance);
        var modelRepoMock = new Mock<ModelRepository>(new object[] { null! });
        modelRepoMock.Setup(x => x.CreateAsync(It.IsAny<Liuvis.Core.Entities.Model3D>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Liuvis.Core.Entities.Model3D m, CancellationToken _) => m);
        var logger = NullLogger<ModelGenerator>.Instance;
        var generator = new ModelGenerator(storageMock.Object, llmDesign, geoBuilder, modelRepoMock.Object, logger);

        var spec = new DesignSpec
        {
            Strategy = DesignStrategy.New,
            Components = new List<ComponentSpec>
            {
                new()
                {
                    Name = "main",
                    GeometryType = "box",
                    Material = new MaterialSpec { Color = "#00ff00" }
                }
            }
        };

        // Act
        var model = await generator.GenerateModel(spec);

        // Assert
        model.Should().NotBeNull();
        model.Name.Should().NotBeEmpty();
        model.FilePath.Should().NotBeEmpty();
    }
}
