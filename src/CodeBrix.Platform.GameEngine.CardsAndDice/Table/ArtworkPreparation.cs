using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.CardsAndDice.Assets;
using CodeBrix.Platform.GameEngine.Drawing;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

/// <summary>A background artwork preparation operation. Read and cancel on the engine thread.</summary>
public sealed class ArtworkPreparation
{
    private readonly Queue<(string Key, string? Svg)> _remaining;
    private Task<SKImage>? _pending;
    private string? _key;
    internal ArtworkPreparation(IEnumerable<(string Key, string? Svg)> images)
    {
        _remaining = new(images); Total = _remaining.Count;
        StartNext();
    }
    /// <summary>Number of uncached images requested at the start.</summary>
    public int Total { get; }
    /// <summary>Number of images installed into the table cache.</summary>
    public int Completed { get; private set; }
    /// <summary>True once finished, canceled, or failed.</summary>
    public bool IsComplete => _pending == null;
    /// <summary>Whether preparation was canceled.</summary>
    public bool IsCanceled { get; private set; }
    /// <summary>A loading failure, if any. No action should start when this is non-null.</summary>
    public Exception? Error { get; private set; }
    /// <summary>Fraction prepared, from zero through one.</summary>
    public double Progress => Total == 0 ? 1 : (double)Completed / Total;
    private void StartNext()
    {
        if (!_remaining.TryDequeue(out var next)) { _pending = null; return; }
        _key = next.Key;
        // Only this isolated SVG and its raster image cross the worker boundary.
        // The worker never accesses table state or the published draw list.
        _pending = Task.Run(() =>
        {
            using var stream = next.Svg is { } svg
                ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svg)) : AssetCatalog.Open(next.Key);
            using var resource = SvgResource.Load(stream);
            return CardsAndDiceTable.Rasterize(resource, next.Key);
        });
    }
    internal void Pump(Dictionary<string, SKImage> cache)
    {
        if (_pending is not { IsCompleted: true } task) return;
        _pending = null;
        try
        {
            var image = task.GetAwaiter().GetResult();
            if (!cache.TryAdd(_key!, image)) image.Dispose();
            Completed++;
            StartNext();
        }
        catch (Exception error) { Error = error; _remaining.Clear(); }
    }
    /// <summary>Stops scheduling images without waiting for an in-progress rasterization.</summary>
    public void Cancel()
    {
        if (IsComplete) return;
        IsCanceled = true; _remaining.Clear();
        var task = _pending!; _pending = null;
        _ = task.ContinueWith(done =>
        {
            if (done.IsCompletedSuccessfully) done.Result.Dispose();
            else _ = done.Exception; // Observe failures from abandoned work.
        }, TaskScheduler.Default);
    }
}
