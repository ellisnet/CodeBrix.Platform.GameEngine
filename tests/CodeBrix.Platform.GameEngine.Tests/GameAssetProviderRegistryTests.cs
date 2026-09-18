using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the asset provider registry: registration and replacement semantics, key routing by
/// provider prefix, the union of provider catalogs, and the dispatchers that resolve a provider,
/// its capability and the asset kind before materializing anything.
/// </summary>
public class GameAssetProviderRegistryTests : IDisposable
{
    private const string FakeProviderId = "fake";
    private const string OtherProviderId = "other";

    private static readonly byte[] PcmBytes = [0x00, 0x00, 0xFF, 0x7F, 0x00, 0x80];

    private readonly GameAssetProviderRegistry _registry = GameAssetProviderRegistry.Instance;
    private readonly List<FakeGameAssetProvider> _providers = [];

    /// <summary>Starts from an empty registry; the assembly runs its tests serially.</summary>
    public GameAssetProviderRegistryTests()
    {
        _registry.Clear();
    }

    /// <summary>Clears the global registry and unloads every audio key the fakes materialized.</summary>
    public void Dispose()
    {
        _registry.Clear();

        foreach (var key in _providers.SelectMany(provider => provider.MaterializedKeys))
            AudioResourceManager.Instance.Unload(key);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Register_adds_the_provider_to_the_snapshot()
    {
        //Arrange
        var provider = CreateProvider(FakeProviderId, Audio(FakeProviderId, "Audio/laser.ogg"));

        //Act
        _registry.Register(provider);

        //Assert
        _registry.Count.Should().Be(1);
        _registry.Providers.Should().ContainSingle();
        _registry.Providers[0].Should().BeSameAs(provider);
    }

    [Fact]
    public void Register_is_idempotent_for_the_same_instance()
    {
        //Arrange
        var provider = CreateProvider(FakeProviderId, Audio(FakeProviderId, "Audio/laser.ogg"));

        //Act
        _registry.Register(provider);
        _registry.Register(provider);

        //Assert
        _registry.Count.Should().Be(1);
        provider.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void Register_replaces_and_disposes_a_provider_with_the_same_identifier()
    {
        //Arrange
        var first = CreateProvider(FakeProviderId, Audio(FakeProviderId, "Audio/laser.ogg"));
        var second = CreateProvider(FakeProviderId, Audio(FakeProviderId, "Audio/thud.ogg"));

        //Act
        _registry.Register(first);
        _registry.Register(second);

        //Assert
        _registry.Count.Should().Be(1);
        _registry.Providers[0].Should().BeSameAs(second);
        first.IsDisposed.Should().BeTrue();
        second.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void Register_rejects_an_identifier_that_contains_a_colon()
    {
        //Arrange
        var provider = CreateProvider("bad:id");

        //Act
        Action act = () => _registry.Register(provider);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unregister_removes_and_disposes_the_provider()
    {
        //Arrange
        var provider = CreateProvider(FakeProviderId, Audio(FakeProviderId, "Audio/laser.ogg"));
        _registry.Register(provider);

        //Act
        bool removed = _registry.Unregister(FakeProviderId);
        bool removedAgain = _registry.Unregister(FakeProviderId);

        //Assert
        removed.Should().BeTrue();
        removedAgain.Should().BeFalse();
        provider.IsDisposed.Should().BeTrue();
        _registry.Count.Should().Be(0);
    }

    [Fact]
    public void Unregister_can_keep_the_provider_alive()
    {
        //Arrange
        var provider = CreateProvider(FakeProviderId, Audio(FakeProviderId, "Audio/laser.ogg"));
        _registry.Register(provider);

        //Act
        bool removed = _registry.Unregister(FakeProviderId, dispose: false);

        //Assert
        removed.Should().BeTrue();
        provider.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void TryFind_routes_a_key_by_its_provider_prefix()
    {
        //Arrange
        var fake = CreateProvider(FakeProviderId, Audio(FakeProviderId, "Audio/laser.ogg"));
        var other = CreateProvider(OtherProviderId, Audio(OtherProviderId, "Audio/thud.ogg"));
        _registry.Register(fake);
        _registry.Register(other);

        //Act
        bool foundFake = _registry.TryFind($"{FakeProviderId}:Audio/laser", out var fakeProvider);
        bool foundOther = _registry.TryFind($"{OtherProviderId}:Audio/thud", out var otherProvider);
        bool foundUnknown = _registry.TryFind("missing:Audio/thud", out var unknownProvider);
        bool foundUnprefixed = _registry.TryFind("Audio/laser", out var unprefixedProvider);

        //Assert
        foundFake.Should().BeTrue();
        fakeProvider.Should().BeSameAs(fake);
        foundOther.Should().BeTrue();
        otherProvider.Should().BeSameAs(other);
        foundUnknown.Should().BeFalse();
        unknownProvider.Should().BeNull();
        foundUnprefixed.Should().BeFalse();
        unprefixedProvider.Should().BeNull();
    }

    [Fact]
    public void TryDescribe_returns_the_descriptor_of_the_owning_provider()
    {
        //Arrange
        var asset = Audio(FakeProviderId, "Audio/laser.ogg");
        _registry.Register(CreateProvider(FakeProviderId, asset));

        //Act
        bool found = _registry.TryDescribe(asset.Key, out var descriptor);
        bool missing = _registry.TryDescribe($"{FakeProviderId}:Audio/nothing", out var missingDescriptor);

        //Assert
        found.Should().BeTrue();
        descriptor.Should().Be(asset);
        missing.Should().BeFalse();
        missingDescriptor.Should().BeNull();
    }

    [Fact]
    public void Describe_returns_the_union_of_every_registered_provider()
    {
        //Arrange
        _registry.Register(CreateProvider(
            FakeProviderId,
            Audio(FakeProviderId, "Audio/laser.ogg"),
            Audio(FakeProviderId, "Audio/thud.ogg")));
        _registry.Register(CreateProvider(OtherProviderId, Audio(OtherProviderId, "Audio/chirp.ogg")));

        //Act
        var described = _registry.Describe();

        //Assert
        described.Count.Should().Be(3);
        described.Select(descriptor => descriptor.Key).Should().Contain($"{FakeProviderId}:Audio/laser");
        described.Select(descriptor => descriptor.Key).Should().Contain($"{OtherProviderId}:Audio/chirp");
    }

    [Fact]
    public void Describe_passes_the_query_to_each_provider()
    {
        //Arrange
        _registry.Register(CreateProvider(
            FakeProviderId,
            Audio(FakeProviderId, "Audio/laser.ogg"),
            Audio(FakeProviderId, "Audio/thud.ogg")));
        _registry.Register(CreateProvider(OtherProviderId, Audio(OtherProviderId, "Audio/laserBig.ogg")));

        //Act
        var byName = _registry.Describe(new GameAssetQuery { NameContains = "LASER" });
        var byKind = _registry.Describe(new GameAssetQuery { Kind = GameAssetKind.Font });

        //Assert
        byName.Count.Should().Be(2);
        byKind.Should().BeEmpty();
    }

    [Fact]
    public void LoadAudio_dispatches_to_the_provider_that_owns_the_key()
    {
        //Arrange
        var asset = Audio(FakeProviderId, "Audio/laser.ogg");
        var provider = CreateProvider(FakeProviderId, asset);
        _registry.Register(provider);

        //Act
        var resource = _registry.LoadAudio(asset.Key, volume: 0.5f, pan: -0.25f);

        //Assert
        resource.Should().NotBeNull();
        provider.MaterializedKeys.Should().ContainSingle();
        provider.MaterializedKeys[0].Should().Be(asset.Key);
        provider.LastVolume.Should().Be(0.5f);
        provider.LastPan.Should().Be(-0.25f);
    }

    [Fact]
    public void LoadAudio_throws_when_no_provider_owns_the_key()
    {
        //Arrange
        _registry.Register(CreateProvider(FakeProviderId, Audio(FakeProviderId, "Audio/laser.ogg")));

        //Act
        Action unknownProvider = () => _registry.LoadAudio("missing:Audio/laser");
        Action unknownAsset = () => _registry.LoadAudio($"{FakeProviderId}:Audio/nothing");

        //Assert
        unknownProvider.Should().Throw<KeyNotFoundException>();
        unknownAsset.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void LoadAudio_throws_when_the_provider_does_not_declare_the_kind()
    {
        //Arrange - the provider implements the audio capability but declares no supported kinds.
        var asset = Audio(FakeProviderId, "Audio/laser.ogg");
        _registry.Register(CreateProvider(FakeProviderId, supportedKinds: [], assets: asset));

        //Act
        Action act = () => _registry.LoadAudio(asset.Key);

        //Assert
        act.Should().Throw<UnsupportedGameAssetException>();
    }

    [Fact]
    public void LoadTilesheet_throws_when_the_provider_has_no_tilesheet_capability()
    {
        //Arrange - the fake implements only the audio capability.
        var asset = Image(FakeProviderId, "PNG/ballBlue.png");
        _registry.Register(CreateProvider(
            FakeProviderId,
            supportedKinds: [GameAssetKind.Audio, GameAssetKind.Image],
            assets: asset));

        //Act
        var thrown = Assert.Throws<UnsupportedGameAssetException>(() => _registry.LoadTilesheet(asset.Key));

        //Assert
        thrown.Message.Should().StartWith(UnsupportedGameAssetException.DefaultMessage);
        thrown.Kind.Should().Be(GameAssetKind.Image);
        thrown.Key.Should().Be(asset.Key);
    }

    [Fact]
    public void LoadFont_throws_for_an_asset_kind_the_engine_cannot_materialize()
    {
        //Arrange
        var asset = new GameAssetDescriptor
        {
            ProviderId = FakeProviderId,
            Key = $"{FakeProviderId}:Models/spaceship",
            Kind = GameAssetKind.Model3D,
            Name = "spaceship",
            Path = "Models/spaceship.glb"
        };
        _registry.Register(CreateProvider(FakeProviderId, asset));

        //Act
        var thrown = Assert.Throws<UnsupportedGameAssetException>(() => _registry.LoadFont(asset.Key));

        //Assert
        thrown.Message.Should().Be(
            $"{UnsupportedGameAssetException.DefaultMessage} (asset kind: Model3D; key: '{asset.Key}')");
        thrown.Kind.Should().Be(GameAssetKind.Model3D);
    }

    [Fact]
    public void Clear_disposes_every_registered_provider()
    {
        //Arrange
        var first = CreateProvider(FakeProviderId, Audio(FakeProviderId, "Audio/laser.ogg"));
        var second = CreateProvider(OtherProviderId, Audio(OtherProviderId, "Audio/thud.ogg"));
        _registry.Register(first);
        _registry.Register(second);

        //Act
        _registry.Clear();

        //Assert
        _registry.Count.Should().Be(0);
        _registry.Providers.Should().BeEmpty();
        first.IsDisposed.Should().BeTrue();
        second.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Engine_exposes_the_registry_through_its_managers()
    {
        //Arrange & Act
        var fromManagers = Engine.Instance.Managers.AssetProviders;

        //Assert
        fromManagers.Should().BeSameAs(_registry);
    }

    private FakeGameAssetProvider CreateProvider(string providerId, params GameAssetDescriptor[] assets)
    {
        return CreateProvider(providerId, [GameAssetKind.Audio], assets);
    }

    private FakeGameAssetProvider CreateProvider(
        string providerId,
        GameAssetKind[] supportedKinds,
        params GameAssetDescriptor[] assets)
    {
        var provider = new FakeGameAssetProvider(providerId, supportedKinds, assets);
        _providers.Add(provider);

        return provider;
    }

    private static GameAssetDescriptor Audio(string providerId, string path)
    {
        return Describe(providerId, path, GameAssetKind.Audio);
    }

    private static GameAssetDescriptor Image(string providerId, string path)
    {
        return Describe(providerId, path, GameAssetKind.Image);
    }

    private static GameAssetDescriptor Describe(string providerId, string path, GameAssetKind kind)
    {
        string stem = path[..path.LastIndexOf('.')];

        return new GameAssetDescriptor
        {
            ProviderId = providerId,
            Key = $"{providerId}:{stem}",
            Kind = kind,
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path,
            Pack = "fake-pack",
            SizeBytes = PcmBytes.Length
        };
    }

    /// <summary>
    /// A minimal provider that catalogs a fixed set of descriptors and implements exactly one
    /// capability, so that the registry's capability and kind gates can be exercised.
    /// </summary>
    private sealed class FakeGameAssetProvider : IGameAssetProvider, IAudioAssetSource
    {
        private readonly Dictionary<string, GameAssetDescriptor> _assets =
            new(StringComparer.OrdinalIgnoreCase);

        public FakeGameAssetProvider(
            string providerId,
            GameAssetKind[] supportedKinds,
            GameAssetDescriptor[] assets)
        {
            ProviderId = providerId;
            SupportedKinds = new HashSet<GameAssetKind>(supportedKinds);

            foreach (var asset in assets)
                _assets[asset.Key] = asset;
        }

        public string ProviderId { get; }

        public IReadOnlySet<GameAssetKind> SupportedKinds { get; }

        public bool IsDisposed { get; private set; }

        public List<string> MaterializedKeys { get; } = [];

        public float LastVolume { get; private set; }

        public float LastPan { get; private set; }

        public IReadOnlyList<GameAssetDescriptor> Describe(GameAssetQuery? query = null)
        {
            return _assets.Values
                .Where(asset => query is null || query.Matches(asset))
                .ToArray();
        }

        public bool TryDescribe(string key, [NotNullWhen(true)] out GameAssetDescriptor? descriptor)
        {
            return _assets.TryGetValue(key, out descriptor);
        }

        public Stream OpenRaw(GameAssetDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);

            if (!_assets.ContainsKey(descriptor.Key))
                throw new KeyNotFoundException(descriptor.Key);

            return new MemoryStream(PcmBytes, writable: false);
        }

        public AudioResource MaterializeAudio(GameAssetDescriptor descriptor, float volume = 1.0f, float pan = 0.0f)
        {
            ArgumentNullException.ThrowIfNull(descriptor);

            MaterializedKeys.Add(descriptor.Key);
            LastVolume = volume;
            LastPan = pan;

            return AudioResourceManager.Instance.LoadFromPcm(descriptor.Key, PcmBytes, 22050, 16);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
