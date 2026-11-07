using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Padmon.Views;
using Windows.Graphics;

namespace Padmon.Services;

/// <summary>
/// Keeps a small stack of in-app notification windows. New cards appear at the
/// configured anchor and older cards animate down to make room.
/// </summary>
public sealed class NotificationDeck : IDisposable
{
    private const int MaxCards = 5;
    private const int CardHeight = 96;
    private const int CardGap = 8;
    private const double AnimationMilliseconds = 180;

    private readonly DispatcherQueueTimer _animationTimer;
    private readonly List<NotificationWindow> _cards = [];
    private readonly Dictionary<NotificationWindow, PointInt32> _targets = [];
    private readonly Dictionary<NotificationWindow, PointInt32> _starts = [];
    private int _elapsedMilliseconds;
    private bool _disposed;

    public NotificationDeck(DispatcherQueue dispatcher)
    {
        _animationTimer = dispatcher.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(15);
        _animationTimer.IsRepeating = true;
        _animationTimer.Tick += OnAnimationTick;
    }

    public void Show(string title, string body)
    {
        if (_disposed)
        {
            return;
        }

        var card = new NotificationWindow();
        card.Dismissed += OnCardDismissed;

        _cards.Insert(0, card);
        while (_cards.Count > MaxCards)
        {
            var oldest = _cards[^1];
            _cards.RemoveAt(_cards.Count - 1);
            oldest.Dismissed -= OnCardDismissed;
            oldest.CloseNow();
        }

        card.ShowMessage(title, body, card.GetStackBasePosition());
        Layout(animate: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _animationTimer.Stop();

        foreach (var card in _cards)
        {
            card.Dismissed -= OnCardDismissed;
            card.CloseNow();
        }

        _cards.Clear();
    }

    private void Layout(bool animate)
    {
        if (_cards.Count == 0)
        {
            return;
        }

        var basePosition = _cards[0].GetStackBasePosition();
        _targets.Clear();

        var display = DisplayArea.GetFromPoint(basePosition, DisplayAreaFallback.Nearest);
        var workArea = display.WorkArea;
        var stackHeight = (_cards.Count - 1) * (CardHeight + CardGap);
        var direction = basePosition.Y + stackHeight + CardHeight <= workArea.Y + workArea.Height ||
                        basePosition.Y - stackHeight < workArea.Y
            ? 1
            : -1;

        for (var index = 0; index < _cards.Count; index++)
        {
            var target = new PointInt32(
                basePosition.X,
                basePosition.Y + direction * index * (CardHeight + CardGap));

            var maxY = Math.Max(workArea.Y, workArea.Y + workArea.Height - CardHeight);
            target.Y = Math.Clamp(target.Y, workArea.Y, maxY);

            _targets[_cards[index]] = target;
        }

        if (!animate)
        {
            foreach (var (card, target) in _targets)
            {
                card.MoveTo(target);
            }

            return;
        }

        _starts.Clear();
        foreach (var card in _cards)
        {
            _starts[card] = card.Position;
        }

        _elapsedMilliseconds = 0;
        _animationTimer.Stop();
        _animationTimer.Start();
    }

    private void OnAnimationTick(DispatcherQueueTimer sender, object args)
    {
        _elapsedMilliseconds += 15;
        var progress = Math.Clamp(
            _elapsedMilliseconds / AnimationMilliseconds,
            0,
            1);
        var eased = 1 - Math.Pow(1 - progress, 3);

        foreach (var (card, target) in _targets)
        {
            if (!_starts.TryGetValue(card, out var start))
            {
                continue;
            }

            card.MoveTo(new PointInt32(
                (int)Math.Round(start.X + (target.X - start.X) * eased),
                (int)Math.Round(start.Y + (target.Y - start.Y) * eased)));
        }

        if (progress >= 1)
        {
            _animationTimer.Stop();
        }
    }

    private void OnCardDismissed(object? sender, EventArgs e)
    {
        if (sender is not NotificationWindow card)
        {
            return;
        }

        card.Dismissed -= OnCardDismissed;
        _cards.Remove(card);
        card.CloseNow();
        Layout(animate: true);
    }
}
