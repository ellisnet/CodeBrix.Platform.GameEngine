using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using CodeBrix.Platform.GameEngine.CardsAndDice.Layout;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

public sealed partial class CardsAndDiceTable
{
    private sealed record DealRequest(Deck Deck, TableArea Destination, CardPose? Pose,
        bool FaceUp, DealAnimation Animation, DealSequence Sequence);
    private sealed class Flight(DealRequest request, Visual visual)
    {
        public readonly DealRequest Request = request;
        public readonly Visual Visual = visual;
        public readonly Vector2 From = visual.Position;
        public readonly float Angle = visual.Angle;
        public double Elapsed;
    }
    private readonly Queue<DealRequest> _dealQueue = new();
    private Flight? _flight;
    private double _dealPause;
    /// <summary>Whether scheduled deals or an in-flight card remain.</summary>
    public bool IsDealing => _flight != null || _dealQueue.Count > 0;
    /// <summary>Raised after landing and any face-up reveal animation finishes.</summary>
    public event Action<Card, TableArea>? CardDealt;

    /// <summary>
    /// Queues one card from the deck's top to a registered area. For an arbitrary center/angle,
    /// supply a pose and a Manual area. Waits for shuffles and earlier queued deals automatically.
    /// Scripted deals bypass drag/drop acceptance rules; both source and destination must be displayed.
    /// </summary>
    public DealSequence DealTo(Deck deck, TableArea destination, CardPose? pose = null,
        bool faceUp = true, DealAnimation? animation = null)
    {
        Check(); animation ??= new(); ValidateDeal(deck, destination, pose, animation);
        PrepareCards(deck.DrawPile.Cards);
        var sequence = new DealSequence(1);
        _dealQueue.Enqueue(new(deck, destination, pose, faceUp, animation, sequence));
        _wasBusy = true;
        return sequence;
    }
    /// <summary>Queues one card per player in order, repeated cardsPerPlayer times. No cards are reserved until launch.</summary>
    public DealSequence DealRoundRobin(Deck deck, IEnumerable<TableArea> players, int cardsPerPlayer,
        bool faceUp = true, DealAnimation? animation = null)
    {
        Check(); ArgumentNullException.ThrowIfNull(players); ArgumentNullException.ThrowIfNull(deck);
        ArgumentOutOfRangeException.ThrowIfNegative(cardsPerPlayer);
        var targets = players.ToArray();
        if (targets.Length == 0) throw new ArgumentException("At least one player area is required.", nameof(players));
        animation ??= new();
        foreach (var area in targets) ValidateDeal(deck, area, null, animation);
        int count = checked(targets.Length * cardsPerPlayer);
        if (count > 0) PrepareCards(deck.DrawPile.Cards);
        var sequence = new DealSequence(count);
        for (int round = 0; round < cardsPerPlayer; round++)
            foreach (var area in targets) _dealQueue.Enqueue(new(deck, area, null, faceUp, animation, sequence));
        _wasBusy |= count > 0;
        return sequence;
    }
    private void ValidateDeal(Deck deck, TableArea destination, CardPose? pose, DealAnimation animation)
    {
        ArgumentNullException.ThrowIfNull(deck); ArgumentNullException.ThrowIfNull(destination); animation.Validate();
        if (!_areas.Contains(destination) || !_areas.Any(a => ReferenceEquals(a.Pile, deck.DrawPile)) ||
            ReferenceEquals(deck.DrawPile, destination.Pile))
            throw new ArgumentException("Register distinct source and destination areas before dealing.", nameof(destination));
        if (pose is { } p && (destination.Layout != CardLayout.Manual || !float.IsFinite(p.Center.X) ||
            !float.IsFinite(p.Center.Y) || !float.IsFinite(p.Rotation)))
            throw new ArgumentException("Explicit poses require a Manual area and finite coordinates.", nameof(pose));
    }
    /// <summary>Cancels pending deals. A launched card stays in its destination; unlaunched cards remain in the deck.</summary>
    public void CancelDeals()
    {
        Check();
        foreach (var sequence in _dealQueue.Select(r => r.Sequence).Append(_flight?.Request.Sequence).OfType<DealSequence>().Distinct())
        { sequence.IsCanceled = true; sequence.Remaining = 0; }
        if (_flight is { } flight)
        {
            flight.Visual.Card.IsFaceUp = flight.Request.FaceUp;
            flight.Visual.Face = flight.Request.FaceUp;
            flight.Visual.FlipLeft = 0;
            flight.Visual.Position = flight.Visual.Target;
            flight.Visual.Angle = flight.Visual.TargetAngle;
        }
        _flight = null; _dealQueue.Clear(); _dealPause = 0;
    }
    private void AdvanceDeals(double seconds)
    {
        double budget = ReducedMotion ? double.PositiveInfinity : seconds;
        while (IsDealing)
        {
            if (_flight == null)
            {
                if (_dealPause > budget) { _dealPause -= budget; return; }
                budget -= _dealPause; _dealPause = 0;
                var request = _dealQueue.Dequeue();
                var card = request.Deck.DrawPile.Peek();
                if (card == null) { request.Sequence.Remaining--; continue; }
                Synchronize(false);
                var visual = _visuals[card.Id];
                // Every departure starts at the source top, even if layout is still settling.
                visual.Position = visual.Target;
                visual.Angle = visual.TargetAngle; visual.FlipLeft = 0;
                var flight = new Flight(request, visual);
                request.Deck.Draw(request.Destination.Pile, false);
                card.IsSelected = false; visual.Face = false;
                if (request.Pose is { } pose) request.Destination.ManualPoses[card.Id] = pose;
                request.Sequence.Launched(card);
                _flight = flight;
                Synchronize(false);
                Play("card-slide-1.ogg");
            }
            var current = _flight;
            if (!ReferenceEquals(current.Visual.Card.Pile, current.Request.Destination.Pile)) { CancelDeals(); return; }
            double total = current.Request.Animation.Duration + (current.Request.FaceUp ? .3 : 0);
            double step = Math.Min(budget, Math.Max(0, total - current.Elapsed));
            current.Elapsed += step; budget -= step;
            float t = (float)Math.Clamp(current.Elapsed / current.Request.Animation.Duration, 0, 1);
            float eased = Ease(t);
            var v = current.Visual; var options = current.Request.Animation;
            v.Position = Vector2.Lerp(current.From, v.Target, eased);
            if (options.Style != DealStyle.Slide) v.Position.Y -= MathF.Sin(t * MathF.PI) * options.ArcHeight;
            v.Angle = current.Angle + (v.TargetAngle - current.Angle) * eased + (options.Style == DealStyle.Toss ? 360 * eased : 0);
            if (t < 1) return;
            v.Position = v.Target; v.Angle = v.TargetAngle;
            v.Card.IsFaceUp = current.Request.FaceUp;
            v.FlipLeft = current.Request.FaceUp ? Math.Max(0, total - current.Elapsed) : 0;
            v.Face = current.Request.FaceUp && v.FlipLeft <= .15;
            if (current.Elapsed < total) return;
            current.Request.Sequence.Remaining--;
            _flight = null;
            _dealPause = options.Interval;
            CardDealt?.Invoke(v.Card, current.Request.Destination);
        }
        _dealPause = 0;
    }
}
