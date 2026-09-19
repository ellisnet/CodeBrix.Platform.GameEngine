using CodeBrix.Platform.GameEngine.Assets.Models;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers one mesh of a model: the counts derived from its flat arrays, the parallel layout of
/// those arrays, and the default material reference.
/// </summary>
public class GameModelMeshTests
{
    [Fact]
    public void MaterialIndex_defaults_to_the_default_material()
    {
        //Arrange & Act
        var mesh = new GameModelMesh
        {
            Positions = [0.0f, 0.0f, 0.0f],
            Normals = [0.0f, 1.0f, 0.0f],
            TexCoords = [0.0f, 0.0f],
            Indices = []
        };

        //Assert
        mesh.MaterialIndex.Should().Be(-1);
    }

    [Fact]
    public void VertexCount_and_TriangleCount_are_derived_from_the_arrays()
    {
        //Arrange & Act
        var mesh = TestGameModels.Mesh(6);

        //Assert
        mesh.VertexCount.Should().Be(6);
        mesh.TriangleCount.Should().Be(2);
    }

    [Fact]
    public void The_vertex_arrays_are_parallel()
    {
        //Arrange & Act
        var mesh = TestGameModels.Mesh(9);

        //Assert
        mesh.Positions.Length.Should().Be(mesh.VertexCount * 3);
        mesh.Normals.Length.Should().Be(mesh.VertexCount * 3);
        mesh.TexCoords.Length.Should().Be(mesh.VertexCount * 2);
        mesh.Indices.Length.Should().Be(mesh.TriangleCount * 3);
    }

    [Fact]
    public void An_empty_mesh_counts_nothing()
    {
        //Arrange & Act
        var mesh = new GameModelMesh
        {
            Positions = [],
            Normals = [],
            TexCoords = [],
            Indices = []
        };

        //Assert
        mesh.VertexCount.Should().Be(0);
        mesh.TriangleCount.Should().Be(0);
    }

    [Fact]
    public void MaterialIndex_addresses_the_materials_of_the_model()
    {
        //Arrange
        var material = new GameModelMaterial { Name = "red" };
        var mesh = TestGameModels.Mesh(3, materialIndex: 0);

        //Act
        var model = TestGameModels.Model([mesh], [material]);

        //Assert
        model.Meshes[0].MaterialIndex.Should().Be(0);
        model.Materials[model.Meshes[0].MaterialIndex].Should().BeSameAs(material);
    }
}
